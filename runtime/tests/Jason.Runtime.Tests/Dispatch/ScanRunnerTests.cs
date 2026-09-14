using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

public class ScanRunnerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
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

        Assert.Equal(new ScanReport(1, 1, 1), report);
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

    [Fact]
    public async Task A_dispatcher_that_is_not_running_reads_nothing_and_counts_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Status.State = DispatcherState.Draining;
        var item = await harness.SeedClaimableAsync(Ct);

        Assert.Equal(new ScanReport(0, 0, 0), await harness.Runner.ScanOnceAsync(Ct));

        Assert.Equal(0, harness.Status.Scans);
        Assert.Null(harness.Status.LastScanAt);
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
