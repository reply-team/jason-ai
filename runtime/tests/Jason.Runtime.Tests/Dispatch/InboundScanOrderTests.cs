using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Where the inbound read sits in a scan: after the lease enforcer and before the summon, so a reply it says wakes the
/// manager in the same scan, and before the claim, so a read it starts is claimed in the same scan.
/// </summary>
public class InboundScanOrderTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reply_said_by_a_scan_wakes_the_manager_in_that_scan()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-5))));

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Observed);
        Assert.Equal(1, report.Summoned);
        await using var db = harness.Open();
        var line = await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.InboundReplyObserved, Ct);
        var checkIn = await db.WorkItems.SingleAsync(item => item.Role == ManagerCheckIn.Role, Ct);
        Assert.Contains(line.PublicId, checkIn.Context.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(JournalKinds.InboundReplyObserved, checkIn.Context.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_started_by_a_scan_is_claimed_in_that_scan()
    {
        using var harness = new DispatchHarness(Noon);
        await ReadableAsync(harness);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Synced);
        await using var db = harness.Open();
        var read = await db.WorkItems.Include(item => item.Attempts).SingleAsync(item => item.Operation == InboundSync.Operation, Ct);
        Assert.NotEmpty(read.Attempts);
    }

    /// <summary>A drain stops claims, not bookkeeping: the read is started and accounted for, and nobody claims it.</summary>
    [Fact]
    public async Task A_draining_dispatcher_starts_reads_and_claims_none()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Status.State = DispatcherState.Draining;
        await ReadableAsync(harness);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Synced);
        Assert.Equal(0, report.Claimed);
        await using var db = harness.Open();
        var read = await db.WorkItems.Include(item => item.Attempts).SingleAsync(item => item.Operation == InboundSync.Operation, Ct);
        Assert.Empty(read.Attempts);
    }

    [Theory]
    [InlineData(DispatcherState.Stopped)]
    [InlineData(DispatcherState.Disabled)]
    public async Task A_dispatcher_with_no_loop_reads_no_conversations(DispatcherState state)
    {
        using var harness = new DispatchHarness(Noon);
        harness.Status.State = state;
        await ReadableAsync(harness);

        Assert.Equal(new ScanReport(0, 0, 0, 0), await harness.Runner.ScanOnceAsync(Ct));

        await using var db = harness.Open();
        Assert.Empty(await db.WorkItems.ToListAsync(Ct));
    }

    /// <summary>A live campaign routed to a plugin that reads conversations, linked there, with no read outstanding.</summary>
    private static async Task ReadableAsync(DispatchHarness harness)
    {
        Load(harness, TestPlugins.Loaded("reader", operations: ["conversation.list"]));
        RouteAll(harness, "reader");
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
        campaign.InboundWatermark = Noon.AddHours(-2);
        db.Campaigns.Add(campaign);
        db.ExternalIds.Add(new ExternalId
        {
            Campaign = campaign,
            PluginId = "reader",
            Kind = "campaign",
            Value = "seq-1",
            RecordedAt = Noon.AddDays(-1),
            RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        });
        await db.SaveChangesAsync(Ct);
    }
}
