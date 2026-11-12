using System.Collections.Concurrent;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Audiences;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting.Modules;
using Jason.Runtime.Domain;
using Jason.Runtime.Events;
using Jason.Runtime.Tests.Events;
using Jason.Runtime.Tests.Plugins.Subscriptions;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private Interference? _interfere;

    public DispatchHarness(
        DateTime now,
        Action<DispatcherOptions>? dispatcher = null,
        RolesOptions? roles = null,
        ManagerOptions? manager = null,
        params ICommand[] commands)
    {
        Clock = new FixedClock(now);
        Options = TestOptions.Dispatcher(dispatcher);
        Registry = new RunningAttemptRegistry();
        Status = new DispatcherStatus { State = DispatcherState.Running };

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(Logs));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IOptionsMonitor<DispatcherOptions>>(Options);
        services.AddSingleton<IOptions<DispatcherOptions>>(new OptionsWrapper<DispatcherOptions>(Options.CurrentValue));
        services.AddSingleton<IOptionsMonitor<RolesOptions>>(new TestOptionsMonitor<RolesOptions>(roles ?? new RolesOptions { DefaultEntryCommand = ["agent-host"] }));
        // The loop's own settings, and the plugin section the work-item service reads for a provider item's
        // timeout floor. Both are seams the dispatcher module now names, so a test that composes the module
        // composes them too.
        services.AddSingleton<IOptionsMonitor<ManagerOptions>>(new TestOptionsMonitor<ManagerOptions>(manager ?? new ManagerOptions()));
        services.AddSingleton<IOptionsMonitor<PluginsOptions>>(new TestOptionsMonitor<PluginsOptions>(new PluginsOptions()));

        // The inbound read, as the runtime ships it: on. With nothing routed it reads nothing, and says so once.
        services.AddSingleton<IOptionsMonitor<InboundOptions>>(Inbound);

        // The respond step, as the runtime ships it: on, so a reply the read says wakes a responder where one can launch.
        services.AddSingleton<IOptionsMonitor<ResponderOptions>>(Responder);
        services.AddSingleton<IOptionsMonitor<LaunchesOptions>>(Launches);

        // The subscription runner's numbers, as the runtime ships them unless a test changes them between turns.
        services.AddSingleton<IOptionsMonitor<SubscriptionsOptions>>(Subscriptions);
        services.AddSingleton(_dir.Paths);
        services.AddSingleton(Registry);
        services.AddSingleton(Status);
        services.AddDbContext<JasonDbContext>(o =>
        {
            JasonDbContext.Configure(o, _database.File, JournalLines.Strict);
            o.AddInterceptors(new SaveHook(Take), Commands);
        });

        // The notifier's half of the scan: off unless a test turns it on, a state that lives as long as this
        // "runtime" does, and a queue that keeps what it was handed instead of sending anything.
        services.AddSingleton<IOptionsMonitor<NotificationsOptions>>(Notifications);
        services.AddSingleton(DispatcherModule.Seam<NotificationsOptions>(NotificationsOptions.Section));
        services.AddSingleton(NotifierState);
        services.AddSingleton<INotificationQueue>(Queue);
        services.AddSingleton(NotificationStatus);
        services.AddScoped<JournalWriter>();
        services.AddScoped<AttemptOutcomes>();
        services.AddScoped<SentFacts>();
        services.AddScoped<SearchLanding>();
        services.AddScoped<ProviderOutcomeRecorder>();

        // What the claim reads before it hands out provider work. The registries are empty here — these tests
        // are about agent work — and an empty route snapshot is exactly what "nothing is routed" looks like.
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<RouteRegistry>();
        services.AddScoped<ExternalIdStore>();
        services.AddScoped<SuppressionService>();
        services.AddScoped<ISuppressionCheck>(provider => provider.GetRequiredService<SuppressionService>());
        // The accounts' events are read through pages the test hands over rather than through a child.
        services.AddSingleton<IEventsInvoker>(Events);

        // The subscribed kinds' handlers answer as the test says rather than through a child.
        services.AddSingleton<ISubscriptionInvoker>(Handlers);

        // Where the claim finds the person's memory files: a root under this test's own home, which no test here marks,
        // so every root a role is told of is one a test made on purpose.
        services.AddSingleton(Harness.TestHarness.Folders(_dir.Home));
        services.AddDispatcherModule();
        foreach (var command in commands)
        {
            services.AddSingleton(command);
        }

        _provider = services.BuildServiceProvider();
    }

    public FixedClock Clock { get; }

    /// <summary>What a read of an account's events is answered with, page by page, and what each page was asked for.</summary>
    public FakeEventsInvoker Events { get; } = new();

    /// <summary>The reader the scan hands reads of accounts' events to, as the runtime's singleton holds it.</summary>
    public EventReader Reader => _provider.GetRequiredService<EventReader>();

    /// <summary>What each subscriber's handler answers, line by line, and what each was handed.</summary>
    public FakeSubscriptionInvoker Handlers { get; } = new();

    /// <summary>The runner the scan offers subscribed kinds to, as the runtime's singleton holds it.</summary>
    public SubscriptionRunner SubscriptionRunner => _provider.GetRequiredService<SubscriptionRunner>();

    /// <summary>The subscription runner's settings, which a test may change between turns.</summary>
    public TestOptionsMonitor<SubscriptionsOptions> Subscriptions { get; } = new(new SubscriptionsOptions());

    /// <summary>Runs one subscription step, in a scope of its own, the way the scan runs it.</summary>
    public async Task<int> SubscriptionScanAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<SubscriptionScan>().ScanAsync(db, ct);
    }

    /// <summary>Runs one events step, in a scope of its own, the way the scan runs it.</summary>
    public async Task<int> EventScanAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<EventScan>().ScanAsync(db, ct);
    }

    /// <summary>
    /// The inbound read's settings, which a test may change between scans. The look-back is an hour here rather than
    /// the shipped default, so the moments a test writes read in hours: what the tests hold is the arithmetic, which
    /// the default does not change, and the default is held by the settings' own tests.
    /// </summary>
    public TestOptionsMonitor<InboundOptions> Inbound { get; } = new(new InboundOptions { LookbackSeconds = 3_600 });

    /// <summary>The notification settings the scan reads. Off until a test turns them on.</summary>
    public TestOptionsMonitor<ResponderOptions> Responder { get; } = new(new ResponderOptions());

    public TestOptionsMonitor<LaunchesOptions> Launches { get; } = new(new LaunchesOptions());

    public TestOptionsMonitor<NotificationsOptions> Notifications { get; } = new(new NotificationsOptions { Enabled = false });

    /// <summary>What the notifier knows between scans, as the runtime's singleton holds it.</summary>
    public NotifierState NotifierState { get; } = new();

    /// <summary>What the last notifications did, as <c>system.info</c> reads it.</summary>
    public NotificationStatus NotificationStatus { get; } = new();

    /// <summary>A service the harness composed, for a test that builds one of the runtime's parts by hand.</summary>
    public T Resolve<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Where the notifier hands what it decided to send.</summary>
    public RecordingQueue Queue { get; } = new();

    /// <summary>Every command the harness's contexts sent, and a way to make the next journal read fail.</summary>
    public CommandLog Commands { get; } = new();

    /// <summary>Turns the notifier on with the shipped defaults, adjusted by the test, and dates this runtime's start.</summary>
    public void NotificationsOn(Action<NotificationsOptions>? configure = null)
    {
        var options = new NotificationsOptions();
        configure?.Invoke(options);
        Notifications.CurrentValue = options;
        Status.RuntimeStartedAt ??= Clock.Now.UtcDateTime;
    }

    /// <summary>
    /// The notifier run once over a state the test holds, the way a runtime that has just started runs it: the same
    /// services, the same contexts, a state that has read nothing yet.
    /// </summary>
    public async Task<int> NotifyAsync(NotifierState state, CancellationToken ct)
    {
        await using var scope = _provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var notifier = new Notifier(
            state,
            Queue,
            NotificationStatus,
            services.GetRequiredService<LiveSettings<NotificationsOptions>>(),
            services.GetRequiredService<LiveSettings<DispatcherOptions>>(),
            Status,
            Clock,
            services.GetRequiredService<ILogger<Notifier>>());
        return await notifier.NotifyAsync(services.GetRequiredService<JasonDbContext>(), ct);
    }

    /// <summary>
    /// One journal line, written by the runtime at the harness clock's time, as a step of the runtime would: in the shape
    /// its kind's document states, with the members of <paramref name="updated"/> over the documented ones, and naming
    /// stand-ins where the document names a campaign, a work item or an attempt on every line.
    /// </summary>
    public async Task<JournalEntry> WriteLineAsync(string kind, CancellationToken ct, System.Text.Json.Nodes.JsonObject? updated = null)
    {
        await using var db = Open();
        var (campaign, item, attempt) = await Tests.Journal.DocumentedLines.StandInsAsync(db, kind, Clock.GetUtcNow().UtcDateTime, ct);
        var entry = Tests.Journal.DocumentedLines.AppendDocumented(
            new JournalWriter(Clock),
            db,
            Actors.Runtime,
            kind,
            campaign,
            with: updated,
            workItem: item,
            attempt: attempt);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>What these services logged, for the decisions whose only trace is a diagnostic.</summary>
    public RecordingLogs Logs { get; } = new();

    public TestOptionsMonitor<DispatcherOptions> Options { get; }

    public RunningAttemptRegistry Registry { get; }

    public DispatcherStatus Status { get; }

    public JasonPaths Paths => _dir.Paths;

    public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    public HandlerPool Pool => _provider.GetRequiredService<HandlerPool>();

    public ScanRunner Runner => _provider.GetRequiredService<ScanRunner>();

    public JasonDbContext Open() => _database.Open();

    /// <summary>The file the harness's database lives in, for a context a test composes with interceptors of its own.</summary>
    public string DatabaseFile => _database.File;

    /// <summary>
    /// Wedges another writer into the next save one of these services makes, which is how a test reproduces a
    /// race it could otherwise only hope for. Fires once, on the next save, and only for contexts the harness
    /// composed — a context the test opens itself is unaffected, so the interference can use one.
    /// </summary>
    public void InterfereOnceBeforeSaving(Action action) => InterfereOnceBeforeSaving(_ => true, action);

    /// <summary>
    /// The same, for the first save whose context <paramref name="when"/> recognises. A race between two writers
    /// that both save is staged on one of them, and which one reaches its save first is the very thing the race
    /// is about, so the interference has to be able to say whose save it is waiting for.
    /// </summary>
    public void InterfereOnceBeforeSaving(Func<DbContext, bool> when, Action action) => _interfere = new Interference(when, action);

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

    /// <summary>Runs one summon, in a scope of its own, the way the scan runs it.</summary>
    public async Task<int> SummonAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<Summoner>().SummonAsync(db, ct);
    }

    /// <summary>Runs one resume step, in a scope of its own, the way the scan runs it.</summary>
    public async Task<int> ResumeAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<Resumer>().ResumeAsync(db, ct);
    }

    /// <summary>Runs one respond step, in a scope of its own, the way the scan runs it.</summary>
    public async Task<int> RespondAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<Responder>().RespondAsync(db, ct);
    }

    /// <summary>Runs one reserve count, in a scope of its own, the way the scan runs it.</summary>
    public async Task<int> ReserveAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<ReserveCounter>().CountAsync(db, ct);
    }

    /// <summary>Runs one poll of the searches, in a scope of its own, the way the scan runs it.</summary>
    public async Task<SearchPollReport> PollAsync(CancellationToken ct)
    {
        await using var scope = Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<SearchPoll>().PollAsync(db, ct);
    }

    /// <summary>Runs one inbound read step, in a scope of its own, the way the scan runs it.</summary>
    public async Task<InboundReport> SyncAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<InboundSync>().SyncAsync(db, ct);
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

    /// <summary>
    /// A started fixture handed back only once the loop's own first scan is behind it, so that everything a test
    /// seeds afterwards is there for the scan the test drives and for no other.
    /// </summary>
    /// <remarks>
    /// The window is short — the loop scans the moment it starts and then sleeps a tick — but on a loaded machine
    /// a test's two or three seeding calls fit inside it, and then the loop claims what the test seeded, the
    /// test's own scan finds nothing left to claim, and a count that is right on every developer's machine fails
    /// on somebody else's CPU. Waiting here, before a test has seeded anything, closes the window. Waiting inside
    /// a scan helper cannot: it runs after the seeding it would have to protect.
    /// </remarks>
    public static async Task<RuntimeApiFixture> ScannedOnceAsync(RuntimeApiFixture fixture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var scanned = await FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), ct);
        if (!scanned)
        {
            // This runtime is no use to the test now, and leaving it up would leave its database open with it.
            await fixture.DisposeAsync();
        }

        Assert.True(scanned, "The dispatcher never ran its first scan, so nothing here can tell a test's scan from the loop's.");
        return fixture;
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

    /// <summary>
    /// A queue that keeps every request it was handed, with the time it was handed over, and can be told to refuse
    /// the next ones. It never releases: the test plays the sender's part with <see cref="NotifierState.Release"/>.
    /// </summary>
    internal sealed class RecordingQueue : INotificationQueue
    {
        private readonly ConcurrentQueue<NotificationRequest> _requests = new();

        public IReadOnlyList<NotificationRequest> Requests => [.. _requests];

        public bool Refuse { get; set; }

        public void Enqueue(NotificationRequest request)
        {
            if (Refuse)
            {
                throw new InvalidOperationException("The queue was told to refuse.");
            }

            _requests.Enqueue(request);
        }
    }

    /// <summary>Every command text the harness's contexts sent, and a read of the journal made to fail on request.</summary>
    internal sealed class CommandLog : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _texts = new();
        private int _failNextJournalRead;
        private int _failNextWorkItemRead;
        private Action? _beforeJournalRead;

        public IReadOnlyList<string> Texts => [.. _texts];

        public void Clear() => _texts.Clear();

        /// <summary>The next query that reads the journal throws, as a database that went away would.</summary>
        public void FailNextJournalRead() => Interlocked.Exchange(ref _failNextJournalRead, 1);

        /// <summary>
        /// Runs <paramref name="action"/> once, just before the next query that reads the journal is sent: another writer
        /// arriving between a step's look at what it has said and its write, by construction rather than by timing.
        /// </summary>
        public void BeforeNextJournalRead(Action action) => Interlocked.Exchange(ref _beforeJournalRead, action);

        /// <summary>The next query that reads work items throws, and the one after it does not.</summary>
        public void FailNextWorkItemRead() => Interlocked.Exchange(ref _failNextWorkItemRead, 1);

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(System.Data.Common.DbCommand command)
        {
            _texts.Enqueue(command.CommandText);
            if (command.CommandText.Contains("FROM \"journal\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _beforeJournalRead, null) is { } before)
            {
                before();
            }

            if (command.CommandText.Contains("FROM \"journal\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _failNextJournalRead, 0) == 1)
            {
                throw new InvalidOperationException("The journal could not be read.");
            }

            if (command.CommandText.Contains("FROM \"work_items\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _failNextWorkItemRead, 0) == 1)
            {
                throw new InvalidOperationException("The work items could not be read.");
            }
        }
    }

    /// <summary>Lets the test act in the instant between a service reading a row and writing it back.</summary>
    /// <summary>The pending interference, taken by the first save it recognises and by no other.</summary>
    private Action? Take(DbContext context)
    {
        var pending = Volatile.Read(ref _interfere);
        return pending is not null && pending.When(context) && Interlocked.CompareExchange(ref _interfere, null, pending) == pending
            ? pending.Action
            : null;
    }

    private sealed record Interference(Func<DbContext, bool> When, Action Action);

    private sealed class SaveHook(Func<DbContext, Action?> take) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Take(eventData)?.Invoke();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Take(eventData)?.Invoke();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private Action? Take(DbContextEventData eventData) => eventData.Context is { } context ? take(context) : null;
    }
}
