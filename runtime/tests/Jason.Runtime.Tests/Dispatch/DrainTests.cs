using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A drain is the dispatcher told to stop taking work, and to start again afterwards. It exists for an update:
/// a runtime that is about to be replaced should finish what it is doing and claim nothing new, and if the
/// update is abandoned halfway it should carry on as though nothing had happened.
/// </summary>
/// <remarks>
/// The word already existed for a different thing. <c>Draining</c> has meant "this process is stopping" since the
/// dispatcher was written, and a scan in that state did nothing at all — no claim, and no expiry, no lease
/// enforcement and no summon either, because a process on its way out has no business starting anything. A
/// reversible drain that inherited all of that would leave a hung child unnoticed for its whole bound: nothing
/// would mark it lost, the running count would never fall, and every drained update would wait its full two
/// minutes on an attempt that had already died. So the state now means one thing precisely — nothing new is
/// claimed — and everything else a scan does keeps happening.
/// </remarks>
public class DrainTests
{
    /// <summary>A real dispatcher that never ticks on its own: every scan here is one this test asked for.</summary>
    private const string Idle = """
        {"Dispatcher":{"TickSeconds":3600,"DrainSeconds":1,"RetryDelaySeconds":0,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":600,"MaxAttempts":2}},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The case the whole amendment is about: an attempt whose lease runs out while the dispatcher is draining is
    /// lost <em>during</em> the drain, and the running count falls with it.
    /// </summary>
    [Fact]
    public async Task An_attempt_whose_lease_runs_out_during_a_drain_is_lost_while_the_drain_is_still_on()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(
            WorkItemKind.AiRole,
            async context =>
            {
                // A real child that loses its lease is killed, and its handler returns a moment later; this
                // stands in for that, so the running count falls the way it would on a machine.
                using var killed = context.Kill.Register(() => gate.TrySetResult(new CommandOutcome.Completed(null)));
                return await gate.Task;
            });
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        await using var api = await StartAsync(command, clock);

        var item = await WorkingAsync(api, command);

        var drained = await api.PostOkAsync<DrainResponse>(Operations.SystemDrain, new { }, Ct);
        Assert.Equal(DispatcherState.Draining, drained.State);
        Assert.Equal(1, drained.RunningAttempts);

        // Past the lease, with the drain still on. The child is hung: it will never report, and nothing but the
        // enforcer is going to say so.
        clock.Advance(TimeSpan.FromSeconds(61));
        var scan = await api.Resolve<ScanRunner>().ScanOnceAsync(Ct);

        Assert.Equal(1, scan.Lost);
        Assert.Equal(0, scan.Claimed);

        var lost = await ReadAsync(api, item);
        Assert.Equal(WorkItemStatus.Created, lost.Status);
        Assert.Equal(AttemptErrors.LeaseExpired, lost.Attempts![0].Error!.Code);

        // And the number the applier polls has moved, which is the point: a drain that waits for this count would
        // otherwise wait its whole bound for an attempt that had already died.
        Assert.True(
            await DispatchHarness.EventuallyAsync(
                () => api.Resolve<RunningAttemptRegistry>().Count == 0,
                Ct),
            "the enforcer ended the attempt but the runtime still counts it as running, so a drain would wait for it");

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);
        Assert.Equal(DispatcherState.Draining, info.Dispatcher.State);
        Assert.Equal(0, info.Dispatcher.RunningAttempts);
    }

    /// <summary>A drain stops the claim, and a resume starts it again. Neither is a state anything else can see.</summary>
    [Fact]
    public async Task A_drain_stops_the_claim_and_a_resume_starts_it_again()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), clock);
        var campaign = await CampaignAsync(api);

        await api.PostOkAsync<DrainResponse>(Operations.SystemDrain, new { }, Ct);
        await CreateAsync(api, campaign);
        Assert.Equal(0, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);

        var resumed = await api.PostOkAsync<DrainResponse>(Operations.SystemResume, new { }, Ct);
        Assert.Equal(DispatcherState.Running, resumed.State);
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// Both are idempotent: an applier that is resuming an interrupted update repeats the step it was on, and a
    /// repeat must be an answer rather than an error.
    /// </summary>
    [Fact]
    public async Task Draining_twice_and_resuming_twice_are_each_one_answer()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), clock);

        Assert.Equal(DispatcherState.Draining, (await api.PostOkAsync<DrainResponse>(Operations.SystemDrain, new { }, Ct)).State);
        Assert.Equal(DispatcherState.Draining, (await api.PostOkAsync<DrainResponse>(Operations.SystemDrain, new { }, Ct)).State);
        Assert.Equal(DispatcherState.Running, (await api.PostOkAsync<DrainResponse>(Operations.SystemResume, new { }, Ct)).State);
        Assert.Equal(DispatcherState.Running, (await api.PostOkAsync<DrainResponse>(Operations.SystemResume, new { }, Ct)).State);
    }

    /// <summary>
    /// And neither is written down. A runtime that comes back after a drain comes back running: the drain
    /// belonged to an update that is over, one way or the other.
    /// </summary>
    [Fact]
    public async Task A_drain_does_not_survive_a_restart()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, Idle);
        var options = TestRuntimeOptions.Quiet with { Clock = clock };

        await using (var first = await RuntimeHost.StartAsync(dir.Paths, options, Ct))
        {
            Assert.Equal(
                DispatcherState.Draining,
                first.Services.GetRequiredService<DrainCoordinator>().Drain().State);
        }

        // The same data directory, a second runtime: there is nowhere for a drain to have been written down.
        await using var second = await RuntimeHost.StartAsync(dir.Paths, options, Ct);
        Assert.Equal(DispatcherState.Running, second.Services.GetRequiredService<DispatcherStatus>().State);
    }

    /// <summary>
    /// The split touches the reversible drain and nothing else: a runtime that is stopping still stops, inside
    /// the bound it was given, whether or not somebody drained it first.
    /// </summary>
    /// <remarks>
    /// This is the assertion that would fail if "Draining means nothing new is claimed" were implemented by
    /// letting a scan run after the loop has been told to stop. A shutdown is not a drain: the difference is
    /// that nothing comes after it.
    /// </remarks>
    [Fact]
    public async Task A_drained_runtime_still_shuts_down_and_scans_no_more()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), clock);
        var status = api.Resolve<DispatcherStatus>();

        await api.PostOkAsync<DrainResponse>(Operations.SystemDrain, new { }, Ct);
        var before = status.Scans;

        var stopping = System.Diagnostics.Stopwatch.StartNew();
        await api.Runtime.StopAsync();
        stopping.Stop();

        Assert.Equal(DispatcherState.Stopped, status.State);
        Assert.True(
            stopping.Elapsed < TimeSpan.FromSeconds(20),
            $"a drained runtime took {stopping.Elapsed.TotalSeconds:F1}s to stop, and its drain bound is one second");

        // Nothing scans after the loop is cancelled, drained or not.
        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        Assert.Equal(before, status.Scans);
    }

    /// <summary>
    /// A runtime whose loop never ticks on its own, handed back with its own startup scan already behind it — so
    /// everything a test seeds is there for the scan the test drives, and for no other.
    /// </summary>
    private static async Task<RuntimeApiFixture> StartAsync(ICommand command, FixedClock clock)
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, Idle),
            clock: clock,
            configureServices: services => services.AddSingleton<ICommand>(command));

        return await DispatchHarness.ScannedOnceAsync(fixture, Ct);
    }

    /// <summary>An item claimed and running, with the fake command holding it open.</summary>
    private static async Task<string> WorkingAsync(RuntimeApiFixture api, FakeCommand command)
    {
        var campaign = await CampaignAsync(api);
        var item = await CreateAsync(api, campaign);
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        return item;
    }

    /// <summary>A campaign that is running: work in a draft campaign is not claimable, and would prove nothing here.</summary>
    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Drain" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        return campaign.Id;
    }

    private static async Task<string> CreateAsync(RuntimeApiFixture api, string campaign) =>
        (await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "ai_role", role = "researcher" },
            Ct)).Id;

    private static Task<WorkItemDto> ReadAsync(RuntimeApiFixture api, string item) =>
        api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
}
