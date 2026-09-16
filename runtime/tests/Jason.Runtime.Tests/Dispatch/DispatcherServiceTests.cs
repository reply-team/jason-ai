using System.Diagnostics;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Tests.Dispatch;

public class DispatcherServiceTests
{
    private const string Launchable = """
        {"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0,"DrainSeconds":1},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_started_runtime_is_scanning()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings("""{"Dispatcher":{"TickSeconds":3600}}"""));

        var status = fixture.Resolve<DispatcherStatus>();
        Assert.True(await DispatchHarness.EventuallyAsync(() => status.Scans >= 1, Ct));

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal(DispatcherState.Running, info.Dispatcher.State);
        Assert.Equal(3600, info.Dispatcher.TickSeconds);
        Assert.Equal(4, info.Dispatcher.MaxParallel);
        Assert.Equal(0, info.Dispatcher.RunningAttempts);
        Assert.NotNull(info.Dispatcher.LastScanAt);
        Assert.True(info.Dispatcher.Scans >= 1);
    }

    [Fact]
    public async Task A_disabled_dispatcher_leaves_the_work_where_it_is()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings("""{"Dispatcher":{"Enabled":false}}"""));
        var item = SeedClaimable(fixture.Paths);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal(DispatcherState.Disabled, info.Dispatcher.State);

        Assert.Equal(new ScanReport(0, 0, 0), await fixture.Resolve<ScanRunner>().ScanOnceAsync(Ct));
        Assert.Equal(0, fixture.Resolve<DispatcherStatus>().Scans);
        Assert.Equal(WorkItemStatus.Created, await StatusOfAsync(fixture.Paths, item, Ct));
    }

    [Fact]
    public async Task A_command_that_throws_does_not_stop_the_loop()
    {
        var command = new FakeCommand(WorkItemKind.AiRole, _ => throw new InvalidOperationException("the host blew up"));
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings(Launchable),
            configureServices: services => services.AddSingleton<ICommand>(command));
        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var item = SeedClaimable(fixture.Paths);

        var runner = fixture.Resolve<ScanRunner>();
        Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        Assert.True(await fixture.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(WorkItemStatus.Failed, await StatusOfAsync(fixture.Paths, item, Ct));

        // The loop is unharmed: the next scan still counts.
        var before = fixture.Resolve<DispatcherStatus>().Scans;
        await runner.ScanOnceAsync(Ct);
        Assert.Equal(before + 1, fixture.Resolve<DispatcherStatus>().Scans);
    }

    [Fact]
    public async Task A_clean_drain_stops_without_a_word_about_leftovers()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Completed(null));
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings(Launchable),
            configureServices: services => services.AddSingleton<ICommand>(command));
        await using (fixture)
        {
            Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
            SeedClaimable(fixture.Paths);
            Assert.Equal(1, (await fixture.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
            Assert.True(await fixture.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(5)));

            await fixture.Runtime.StopAsync();

            Assert.DoesNotContain("attempts still running", ReadLogs(fixture.Paths), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_drain_that_runs_out_of_time_says_what_is_still_running()
    {
        // Never completed on purpose: the child outlives the runtime, which is exactly what the drain gives up on.
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, _ => gate.Task);
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings(Launchable),
            configureServices: services => services.AddSingleton<ICommand>(command));
        await using (fixture)
        {
            Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
            SeedClaimable(fixture.Paths);
            Assert.Equal(1, (await fixture.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
            Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));

            var stopping = Stopwatch.StartNew();
            await fixture.Runtime.StopAsync();
            stopping.Stop();

            Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(10), $"stopping took {stopping.Elapsed}");
            Assert.Contains("attempts still running", ReadLogs(fixture.Paths), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A settings file edited while the runtime runs is the operator's path — to a tick, to a budget, to a
    /// route — and an operator mistypes. A value the validator refuses is a fact about the file and not about
    /// the process: the runtime keeps the last settings that did validate, says once what it refused, is still
    /// there to answer, and takes the file the moment it is put right.
    /// </summary>
    [Fact]
    public async Task A_live_edit_the_validator_refuses_costs_the_edit_and_not_the_runtime()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings("""{"Dispatcher":{"TickSeconds":1,"DrainSeconds":1}}"""));
        await using (fixture)
        {
            var status = fixture.Resolve<DispatcherStatus>();
            var settings = fixture.Resolve<DispatcherSettings>();
            var lifetime = fixture.Resolve<IHostApplicationLifetime>();
            Assert.True(await DispatchHarness.FirstScanDoneAsync(status, Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":0,"DrainSeconds":1}}""");

            // Nothing else has read the settings since the edit, so the refusals counted here are the loop's.
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);

            // And the runtime answers for itself with what it is working from, which is the tick it had.
            var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
            Assert.Equal(DispatcherState.Running, info.Dispatcher.State);
            Assert.Equal(1, info.Dispatcher.TickSeconds);

            // The loop was there all along to see the file put right: it scans again without a restart.
            var scans = status.Scans;
            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":2,"DrainSeconds":1}}""");
            Assert.True(await DispatchHarness.EventuallyAsync(() => status.Scans > scans, Ct));
            Assert.Equal(2, settings.Current.TickSeconds);

            await fixture.Runtime.StopAsync();

            // The validator's own sentence, said once for the edit and not once for every tick that read it.
            var logs = ReadLogs(fixture.Paths);
            Assert.Contains("Dispatcher:TickSeconds must be between 1 and 3600; got 0.", logs, StringComparison.Ordinal);
            Assert.Equal(1, Occurrences(logs, "Edited settings were refused"));
        }
    }

    private static Action<JasonPaths> Settings(string json) => paths => File.WriteAllText(paths.UserSettingsFile, json);

    private static string SeedClaimable(JasonPaths paths)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item.PublicId;
    }

    private static async Task<WorkItemStatus> StatusOfAsync(JasonPaths paths, string publicId, CancellationToken ct)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        return await db.WorkItems.AsNoTracking().Where(w => w.PublicId == publicId).Select(w => w.Status).SingleAsync(ct);
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReadLogs(JasonPaths paths) =>
        string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(File.ReadAllText));
}
