using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The log files are read by agents and pasted into bug reports. The dispatcher must be traceable through them
/// — which item, which attempt — without any of what the work was actually about leaking out with it. Verbose
/// logging is deliberately on here: the rule has to hold at the noisiest level, not only at the default one.
/// </summary>
public class DispatcherLoggingTests
{
    private const string ContextKey = "canary_key_7f3a";
    private const string ContextValue = "canary-value-9c1d";
    private const string ResultValue = "canary-result-2b8e";
    private const string StderrTail = "canary-stderr-4d5c";

    private const string Settings = """
        {"Logging":{"MinimumLevel":"Debug"},
         "Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0},
         "Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_dispatcher_logs_ids_and_nothing_the_work_was_about()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(dir.Paths.UserSettingsFile, Settings, Ct);

        var command = new CanaryCommand();
        var runtime = await RuntimeHost.StartAsync(
            dir.Paths,
            TestRuntimeOptions.Quiet with
            {
                Clock = new FixedClock(Noon),

                // Replacing the hook rather than adding to it, which is why the transport that refuses the
                // release feed is a property of its own: this line cannot take it away.
                ConfigureServices = services => services.AddSingleton<ICommand>(command),
            },
            Ct);

        string itemId;
        string token = runtime.Token;
        try
        {
            // The loop scans the moment it starts; seeding after that scan keeps the next one the test's own.
            Assert.True(await DispatchHarness.FirstScanDoneAsync(runtime.Services.GetRequiredService<DispatcherStatus>(), Ct));
            itemId = Seed(dir.Paths);
            var runner = runtime.Services.GetRequiredService<ScanRunner>();
            Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
            Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(dir.Paths, itemId) == WorkItemStatus.Created, Ct));
            Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
            Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(dir.Paths, itemId) == WorkItemStatus.Failed, Ct));
        }
        finally
        {
            // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
            await runtime.StopAsync();
        }

        List<string> attemptIds;
        await using (var db = Open(dir.Paths))
        {
            attemptIds = await db.Attempts.AsNoTracking().Select(a => a.PublicId).ToListAsync(Ct);
        }

        var logs = string.Concat(await Task.WhenAll(
            Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(file => File.ReadAllTextAsync(file, Ct))));

        Assert.Contains(itemId, logs, StringComparison.Ordinal);
        Assert.Equal(2, attemptIds.Count);
        Assert.All(attemptIds, id => Assert.Contains(id, logs, StringComparison.Ordinal));

        Assert.DoesNotContain(ContextKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ContextValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ResultValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(StderrTail, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("context_snapshot", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
    }

    private static string Seed(JasonPaths paths)
    {
        using var db = Open(paths);
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.Context[ContextKey] = ContextValue;
            w.MaxAttempts = 2;
        });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item.PublicId;
    }

    private static WorkItemStatus StatusOf(JasonPaths paths, string publicId)
    {
        using var db = Open(paths);
        return db.WorkItems.AsNoTracking().Where(w => w.PublicId == publicId).Select(w => w.Status).Single();
    }

    private static JasonDbContext Open(JasonPaths paths) => new(JasonDbContext.CreateOptions(paths.DatabaseFile));

    /// <summary>Reports the loudest thing an executor can: a crash carrying its own output and a result.</summary>
    private sealed class CanaryCommand : ICommand
    {
        public WorkItemKind Kind => WorkItemKind.AiRole;

        public Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            Assert.Equal(ContextValue, (string?)context.ContextSnapshot[ContextKey]);
            return Task.FromResult<CommandOutcome>(new CommandOutcome.Exited(
                3,
                $"the executor said {StderrTail} and {ResultValue}",
                new AttemptLaunchDto(context.EntryCommand, context.WorkDir, 1234, 3)));
        }
    }
}
