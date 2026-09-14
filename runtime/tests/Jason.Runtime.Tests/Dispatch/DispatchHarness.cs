using System.Collections.Concurrent;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting.Modules;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>A command whose behaviour the test writes, and which remembers what it was told.</summary>
internal sealed class FakeCommand(WorkItemKind kind, Func<CommandContext, Task<CommandOutcome>> behaviour) : ICommand
{
    private readonly ConcurrentQueue<CommandContext> _contexts = new();

    public WorkItemKind Kind => kind;

    public IReadOnlyCollection<CommandContext> Contexts => _contexts;

    public static FakeCommand Returning(CommandOutcome outcome, WorkItemKind kind = WorkItemKind.AiRole) =>
        new(kind, _ => Task.FromResult(outcome));

    public Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _contexts.Enqueue(context);
        return behaviour(context);
    }
}

/// <summary>
/// The dispatcher's services over one temporary database, without a web host: what the runtime composes, with
/// the clock, the options and the commands in the test's hands.
/// </summary>
internal sealed class DispatchHarness : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly TempDataDir _dir = new();
    private readonly ServiceProvider _provider;
    private Action? _interfere;

    public DispatchHarness(DateTime now, Action<DispatcherOptions>? dispatcher = null, RolesOptions? roles = null, params ICommand[] commands)
    {
        Clock = new FixedClock(now);
        Options = TestOptions.Dispatcher(dispatcher);
        Registry = new RunningAttemptRegistry();
        Status = new DispatcherStatus { State = DispatcherState.Running };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IOptionsMonitor<DispatcherOptions>>(Options);
        services.AddSingleton<IOptions<DispatcherOptions>>(new OptionsWrapper<DispatcherOptions>(Options.CurrentValue));
        services.AddSingleton<IOptionsMonitor<RolesOptions>>(new TestOptionsMonitor<RolesOptions>(roles ?? new RolesOptions { DefaultEntryCommand = ["agent-host"] }));
        services.AddSingleton(_dir.Paths);
        services.AddSingleton(Registry);
        services.AddSingleton(Status);
        services.AddDbContext<JasonDbContext>(o =>
        {
            JasonDbContext.Configure(o, _database.File);
            o.AddInterceptors(new SaveHook(() => Interlocked.Exchange(ref _interfere, null)));
        });
        services.AddScoped<JournalWriter>();
        services.AddScoped<AttemptOutcomes>();
        services.AddDispatcherModule();
        foreach (var command in commands)
        {
            services.AddSingleton(command);
        }

        _provider = services.BuildServiceProvider();
    }

    public FixedClock Clock { get; }

    public TestOptionsMonitor<DispatcherOptions> Options { get; }

    public RunningAttemptRegistry Registry { get; }

    public DispatcherStatus Status { get; }

    public JasonPaths Paths => _dir.Paths;

    public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    public HandlerPool Pool => _provider.GetRequiredService<HandlerPool>();

    public ScanRunner Runner => _provider.GetRequiredService<ScanRunner>();

    public JasonDbContext Open() => _database.Open();

    /// <summary>
    /// Wedges another writer into the next save one of these services makes, which is how a test reproduces a
    /// race it could otherwise only hope for. Fires once, on the next save, and only for contexts the harness
    /// composed — a context the test opens itself is unaffected, so the interference can use one.
    /// </summary>
    public void InterfereOnceBeforeSaving(Action action) => _interfere = action;

    /// <summary>One active campaign holding one claimable ai_role item.</summary>
    public async Task<WorkItem> SeedClaimableAsync(CancellationToken ct, Action<WorkItem>? configure = null)
    {
        await using var db = Open();
        var campaign = WorkItemFactory.NewCampaign(now: Clock.Now.UtcDateTime);
        var item = WorkItemFactory.NewAiRole(campaign, now: Clock.Now.UtcDateTime, configure: configure);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>Claims whatever is ready and hands the claims back without dispatching them.</summary>
    public async Task<IReadOnlyList<ClaimedWork>> ClaimAsync(CancellationToken ct, int freeSlots = 4)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<Claimer>().ClaimAsync(db, freeSlots, ct);
    }

    public async Task<WorkItem> ReadItemAsync(string publicId, CancellationToken ct)
    {
        await using var db = Open();
        return await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.PublicId == publicId, ct);
    }

    public async Task<List<JournalEntry>> ReadJournalAsync(string workItemPublicId, CancellationToken ct)
    {
        await using var db = Open();
        return await db.Journal.AsNoTracking().Where(e => e.WorkItemId == workItemPublicId).OrderBy(e => e.Id).ToListAsync(ct);
    }

    /// <summary>
    /// Waits for the scan the loop runs the moment it starts, so a test that seeds work afterwards knows the
    /// next scan is its own.
    /// </summary>
    public static Task<bool> FirstScanDoneAsync(DispatcherStatus status, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(status);
        return EventuallyAsync(() => status.Scans >= 1, ct);
    }

    /// <summary>Polls until the condition holds or the deadline passes; never sleeps the thread.</summary>
    public static async Task<bool> EventuallyAsync(Func<bool> condition, CancellationToken ct, int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10, ct);
        }

        return condition();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _database.Dispose();
        _dir.Dispose();
    }

    /// <summary>Lets the test act in the instant between a service reading a row and writing it back.</summary>
    private sealed class SaveHook(Func<Action?> take) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            take()?.Invoke();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            take()?.Invoke();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
