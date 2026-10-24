using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

public class ScanRunnerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.W6, "the scan order")]
    public async Task One_scan_expires_then_enforces_then_claims()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, _ => gate.Task);
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        string overdue;
        string lost;
        string ready;
        await using (var db = harness.Open())
        {
            var abandoned = WorkItemFactory.NewCampaign("Abandoned", now: Noon);
            var overdueItem = WorkItemFactory.NewAiRole(abandoned, now: Noon, configure: w => w.DueAt = Noon.AddMinutes(-1));

            // Paused, so the item the enforcer hands back is not claimed again in the same scan.
            var stalled = WorkItemFactory.NewCampaign("Stalled", CampaignStatus.Paused, now: Noon);
            var lostItem = WorkItemFactory.NewAiRole(stalled, now: Noon, configure: w => w.Status = WorkItemStatus.Processing);
            var lostAttempt = WorkItemFactory.NewAttempt(lostItem, 1, AttemptStatus.Running, Noon.AddHours(-2), timeoutSeconds: 60);
            var fresh = WorkItemFactory.NewCampaign("Fresh", now: Noon);
            var readyItem = WorkItemFactory.NewAiRole(fresh, now: Noon);
            db.Campaigns.AddRange(abandoned, stalled, fresh);
            db.WorkItems.AddRange(overdueItem, lostItem, readyItem);
            db.Attempts.Add(lostAttempt);
            await db.SaveChangesAsync(Ct);
            (overdue, lost, ready) = (overdueItem.PublicId, lostItem.PublicId, readyItem.PublicId);
        }

        var report = await harness.Runner.ScanOnceAsync(Ct);

        // One expired, one lease taken back, nothing summoned — these campaigns have never been live through
        // the service, so none of them has an anchor to be due from — and one claimed.
        Assert.Equal(new ScanReport(1, 1, 0, 1), report);
        Assert.Equal(WorkItemStatus.Expired, (await harness.ReadItemAsync(overdue, Ct)).Status);
        Assert.Equal(AttemptErrors.LeaseExpired, Assert.Single((await harness.ReadItemAsync(lost, Ct)).Attempts).Error!.Code);
        Assert.Equal(1, harness.Status.Scans);
        Assert.Equal(Noon, harness.Status.LastScanAt!.Value.UtcDateTime);

        // The claim went straight to a handler: the scan never waits for the work it hands out.
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        Assert.Equal(WorkItemStatus.Processing, (await harness.ReadItemAsync(ready, Ct)).Status);
        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// The release sits between the lease enforcer and the claim: an item whose last attempt is lost for good in a scan
    /// ends there, the item waiting on it is released by the same scan, and that scan claims it.
    /// </summary>
    [Fact]
    public async Task A_wait_whose_item_is_lost_for_good_is_released_and_claimed_in_the_same_scan()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, _ => gate.Task);
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        string planner;
        string researcher;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            var waiting = WorkItemFactory.NewAiRole(campaign, "planner", Noon, w => w.Status = WorkItemStatus.Waiting);
            var yielded = WorkItemFactory.NewAttempt(waiting, 1, AttemptStatus.Yielded, Noon.AddHours(-2));
            yielded.FinishedAt = Noon.AddHours(-2);
            var child = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
            {
                w.Status = WorkItemStatus.Processing;
                w.MaxAttempts = 1;
            });
            var lostAttempt = WorkItemFactory.NewAttempt(child, 1, AttemptStatus.Running, Noon.AddHours(-2), timeoutSeconds: 60);
            db.Campaigns.Add(campaign);
            db.WorkItems.AddRange(waiting, child);
            db.Attempts.AddRange(yielded, lostAttempt);
            db.WorkItemWaits.Add(new WorkItemWait
            {
                WorkItem = waiting,
                Attempt = yielded,
                Mode = WaitMode.All,
                Deadline = Noon.AddDays(14),
                OpenedAt = Noon.AddHours(-2),
                Items = [new WorkItemWaitItem { ChildWorkItem = child }],
            });
            await db.SaveChangesAsync(Ct);
            (planner, researcher) = (waiting.PublicId, child.PublicId);
        }

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.True(report is { Lost: 1, Resumed: 1, Claimed: 1 }, $"The scan said {report}.");
        Assert.Equal(WorkItemStatus.Failed, (await harness.ReadItemAsync(researcher, Ct)).Status);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        Assert.Equal(WorkItemStatus.Processing, (await harness.ReadItemAsync(planner, Ct)).Status);
        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Expiry comes before the release: an item whose due date passed while it waited, in a scan that also finds what it
    /// waited for done, expires there and its wait closes as expired. Nothing resumes work that was stopped.
    /// </summary>
    [Fact]
    public async Task A_wait_whose_item_is_overdue_expires_in_the_scan_that_finds_it_ready()
    {
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0);
        string planner;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            var waiting = WorkItemFactory.NewAiRole(campaign, "planner", Noon, w =>
            {
                w.Status = WorkItemStatus.Waiting;
                w.DueAt = Noon.AddMinutes(-1);
            });
            var yielded = WorkItemFactory.NewAttempt(waiting, 1, AttemptStatus.Yielded, Noon.AddHours(-2));
            yielded.FinishedAt = Noon.AddHours(-2);
            var child = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
            {
                w.Status = WorkItemStatus.Succeeded;
                w.FinishedAt = Noon.AddMinutes(-5);
            });
            db.Campaigns.Add(campaign);
            db.WorkItems.AddRange(waiting, child);
            db.Attempts.Add(yielded);
            db.WorkItemWaits.Add(new WorkItemWait
            {
                WorkItem = waiting,
                Attempt = yielded,
                Mode = WaitMode.All,
                Deadline = Noon.AddDays(14),
                OpenedAt = Noon.AddHours(-2),
                Items = [new WorkItemWaitItem { ChildWorkItem = child }],
            });
            await db.SaveChangesAsync(Ct);
            planner = waiting.PublicId;
        }

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.True(report is { Expired: 1, Resumed: 0, Claimed: 0 }, $"The scan said {report}.");
        Assert.Equal(WorkItemStatus.Expired, (await harness.ReadItemAsync(planner, Ct)).Status);
        await using var read = harness.Open();
        var wait = await read.WorkItemWaits.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WaitStatus.Expired, wait.Status);
        Assert.Null(wait.ReleaseReason);
        Assert.DoesNotContain(await read.Journal.AsNoTracking().ToListAsync(Ct), e => e.Kind == JournalKinds.WorkItemResumed);
    }

    /// <summary>
    /// A dispatcher with no loop — stopped, or disabled by its settings — reads nothing at all. There is no
    /// runtime behind it to do the work a scan hands out.
    /// </summary>
    [Theory]
    [InlineData(DispatcherState.Stopped)]
    [InlineData(DispatcherState.Disabled)]
    public async Task A_dispatcher_with_no_loop_reads_nothing_and_counts_nothing(DispatcherState state)
    {
        using var harness = new DispatchHarness(Noon);
        harness.Status.State = state;
        var item = await harness.SeedClaimableAsync(Ct);

        Assert.Equal(new ScanReport(0, 0, 0, 0), await harness.Runner.ScanOnceAsync(Ct));

        Assert.Equal(0, harness.Status.Scans);
        Assert.Null(harness.Status.LastScanAt);
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(item.PublicId, Ct)).Status);
    }

    /// <summary>
    /// A draining dispatcher is a different thing: it still has a loop and still has work in flight, so it reads
    /// everything and claims nothing. The claim is the only step a drain stops.
    /// </summary>
    /// <remarks>
    /// It used to stop all four, because <c>Draining</c> only ever meant "this process is stopping". A drain is
    /// now reversible and belongs to an update, so a scan during one must still expire what has expired and end
    /// what has lost its lease — otherwise the update waits its whole bound for an attempt that is already dead.
    /// </remarks>
    [Fact]
    public async Task A_draining_dispatcher_reads_everything_and_claims_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Status.State = DispatcherState.Draining;
        var item = await harness.SeedClaimableAsync(Ct);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(0, report.Claimed);
        Assert.Equal(1, harness.Status.Scans);
        Assert.Equal(Noon, harness.Status.LastScanAt!.Value.UtcDateTime);
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(item.PublicId, Ct)).Status);
    }

    [Fact]
    public async Task A_scan_claims_no_more_than_the_pool_can_hold()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, _ => gate.Task);
        using var harness = new DispatchHarness(Noon, o => o.MaxParallel = 1, commands: command);
        await using (var db = harness.Open())
        {
            foreach (var name in new[] { "One", "Two", "Three" })
            {
                var campaign = WorkItemFactory.NewCampaign(name, now: Noon);
                db.Campaigns.Add(campaign);
                db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
            }

            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(1, (await harness.Runner.ScanOnceAsync(Ct)).Claimed);

        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        Assert.Equal(0, harness.Pool.FreeSlots);
        Assert.Equal(0, (await harness.Runner.ScanOnceAsync(Ct)).Claimed);

        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Critical(Family.W6, "concurrent scans")]
    public async Task Two_scans_at_once_over_a_full_backlog_never_claim_more_than_the_pool_holds()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, _ => gate.Task);
        using var harness = new DispatchHarness(Noon, o => o.MaxParallel = 4, commands: command);
        await using (var db = harness.Open())
        {
            // One item each, so the per-campaign rule never limits what a scan may take: the pool must.
            for (var number = 0; number < 8; number++)
            {
                var campaign = WorkItemFactory.NewCampaign($"Campaign {number}", now: Noon);
                db.Campaigns.Add(campaign);
                db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
            }

            await db.SaveChangesAsync(Ct);
        }

        // Both scans read the free slots before either hands its claims out, so an unsynchronized runner
        // claims eight and then cannot place the second four.
        var reports = await Task.WhenAll(
            Task.Run(() => harness.Runner.ScanOnceAsync(Ct), Ct),
            Task.Run(() => harness.Runner.ScanOnceAsync(Ct), Ct));

        Assert.Equal(4, reports.Sum(r => r.Claimed));
        await using (var db = harness.Open())
        {
            Assert.Equal(4, await db.Attempts.AsNoTracking().CountAsync(Ct));
        }

        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 4, Ct));
        Assert.Equal(0, harness.Pool.FreeSlots);

        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));

        // The other four were never lost: the next scan takes them now that the slots are back.
        Assert.Equal(4, (await harness.Runner.ScanOnceAsync(Ct)).Claimed);
        await using (var db = harness.Open())
        {
            Assert.Equal(8, await db.Attempts.AsNoTracking().CountAsync(Ct));
        }

        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Two_scans_at_once_over_one_item_produce_one_attempt()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new DispatchHarness(Noon, commands: new FakeCommand(WorkItemKind.AiRole, _ => gate.Task));
        await harness.SeedClaimableAsync(Ct);

        var reports = await Task.WhenAll(
            Task.Run(() => harness.Runner.ScanOnceAsync(Ct), Ct),
            Task.Run(() => harness.Runner.ScanOnceAsync(Ct), Ct));

        Assert.Equal(1, reports.Sum(r => r.Claimed));
        await using (var db = harness.Open())
        {
            Assert.Single(await db.Attempts.AsNoTracking().ToListAsync(Ct));
        }

        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }
}
