using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The count of a campaign's automatic releases of the day is read inside the claim's write transaction for every send the
/// automatic mode would release, and the approvals table only grows. It is answered from the partial index of automatic
/// releases by campaign and moment, and visits no row of another campaign, of an earlier day or of another reason. The
/// plan and the steps are read from the very query the claim runs.
/// </summary>
public class DailyCeilingPlanTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_count_is_a_search_of_the_partial_index_and_never_a_scan()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(db, DailyCeiling.Released(db, 3, Noon.Date).ToQueryString(), Ct);

        Assert.True(
            plan.Any(detail => detail.Contains(
                $"SEARCH approvals USING COVERING INDEX {DailyCeiling.Index} (campaign_id=? AND decided_at>?)",
                StringComparison.Ordinal)),
            $"The plan is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN approvals", StringComparison.Ordinal));
    }

    /// <summary>
    /// Forty rows more that the count is not about — another campaign's releases of today, this campaign's of yesterday, and its
    /// rows of today under every other reason — leave the steps the count takes exactly as they were, and none is a full
    /// scan's. The steps are counted, not timed.
    /// </summary>
    [Fact]
    public async Task The_count_visits_no_row_of_another_campaign_another_day_or_another_reason()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (mine, other) = await CampaignsAsync(db);
        for (var release = 0; release < 3; release++)
        {
            await RowAsync(db, mine, ApprovalGate.AutomaticMode, Noon.AddMinutes(-release));
        }

        // A seek reads the key just past its range to know the range has ended, so a row on either side of it is there in
        // both counts.
        await RowAsync(db, other, ApprovalGate.AutomaticMode, Noon);
        await RowAsync(db, mine, ApprovalGate.AutomaticMode, Noon.Date.AddTicks(-1));
        var query = DailyCeiling.Released(db, mine.CampaignId, Noon.Date).ToQueryString();
        var before = await QueryPlans.StepsAsync(db, query, Ct);

        for (var n = 0; n < 10; n++)
        {
            await RowAsync(db, other, ApprovalGate.AutomaticMode, Noon);
            await RowAsync(db, mine, ApprovalGate.AutomaticMode, Noon.Date.AddDays(-1).AddHours(n));
            await RowAsync(db, mine, n % 2 == 0 ? ApprovalGate.StandingApproval : ApprovalGate.ApprovalRequired, Noon);
            await RowAsync(db, mine, n % 2 == 0 ? ApprovalGate.InputChanged : "daily_ceiling", Noon);
        }

        var after = await QueryPlans.StepsAsync(db, query, Ct);

        Assert.Equal(before, after);
        Assert.Equal(0, after.FullScanSteps);
        Assert.Equal(3, await DailyCeiling.Released(db, mine.CampaignId, Noon.Date).SingleAsync(Ct));
    }

    private static async Task<(WorkItem Mine, WorkItem Other)> CampaignsAsync(JasonDbContext db)
    {
        var mine = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(now: Noon), "message.send", Noon);
        var other = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(name: "Other", now: Noon), "message.send", Noon);
        db.WorkItems.AddRange(mine, other);
        await db.SaveChangesAsync(Ct);
        return (mine, other);
    }

    /// <summary>A decided row of <paramref name="item"/>'s campaign under <paramref name="reason"/>, decided at <paramref name="at"/>.</summary>
    internal static async Task RowAsync(JasonDbContext db, WorkItem item, string reason, DateTime at, ApprovalStatus status = ApprovalStatus.Approved)
    {
        db.Approvals.Add(new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItemId = item.Id,
            CampaignId = item.CampaignId,
            Operation = "message.send",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "message.send" },
            SubjectHash = "sha256:" + new string('c', 64),
            Preview = [],
            PluginId = "stand-in-provider",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "pls_01K52JR0000000000000000001",
            RoutingSnapshotId = "rts_01K52JR0000000000000000001",
            Status = status,
            Reason = reason,
            RequestedAt = at,
            DecidedAt = at,
        });
        await db.SaveChangesAsync(Ct);
    }
}
