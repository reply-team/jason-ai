using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The decision still about a work item, as the claim, the expirer and a cancellation read it: the newest of the item's
/// approvals that is pending or approved, or none. It is read through the campaign's approvals, because each of those
/// callers asks it and the table only grows; read by the item alone, nothing bounds it but the whole table. The plan is
/// read from the very query those callers run.
/// </summary>
public class LiveApprovalTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LiveApproval_ReadsTheCampaignsApprovals()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        // Take(1) is what FirstOrDefaultAsync composes over the read: the inner query becomes a subquery, then LIMIT.
        var plan = await QueryPlans.PlanAsync(db, ApprovalGate.Live(db, 3, 7).Take(1).ToQueryString(), Ct);

        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH approvals USING INDEX ix_approvals_campaign_id (campaign_id=?)", StringComparison.Ordinal)),
            $"The plan is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN approvals", StringComparison.Ordinal));
    }

    /// <summary>
    /// Of an item's decisions, the one still about it is the newest pending or approved; an older one, a decision ended
    /// since, or one about another item of the same campaign is never it.
    /// </summary>
    [Fact]
    public async Task LiveApproval_IsTheItemsNewestPendingOrApproved()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var first = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        var second = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        var quiet = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        db.WorkItems.AddRange(first, second, quiet);
        await db.SaveChangesAsync(Ct);

        db.Approvals.Add(Row(first, ApprovalStatus.Superseded));
        db.Approvals.Add(Row(first, ApprovalStatus.Approved));
        await db.SaveChangesAsync(Ct);
        var approved = Row(first, ApprovalStatus.Approved);
        db.Approvals.Add(approved);
        await db.SaveChangesAsync(Ct);
        db.Approvals.Add(Row(first, ApprovalStatus.Cancelled));
        var pending = Row(second, ApprovalStatus.Pending);
        db.Approvals.Add(pending);
        db.Approvals.Add(Row(quiet, ApprovalStatus.Rejected));
        await db.SaveChangesAsync(Ct);

        await using var fresh = database.Open();
        Assert.Equal(approved.PublicId, (await ApprovalGate.LiveAsync(fresh, first.CampaignId, first.Id, Ct))?.PublicId);
        Assert.Equal(pending.PublicId, (await ApprovalGate.LiveAsync(fresh, second.CampaignId, second.Id, Ct))?.PublicId);
        Assert.Null(await ApprovalGate.LiveAsync(fresh, quiet.CampaignId, quiet.Id, Ct));
    }

    private static Approval Row(WorkItem item, ApprovalStatus status) => new()
    {
        PublicId = PublicId.New(ApprovalGate.IdPrefix),
        WorkItemId = item.Id,
        CampaignId = item.CampaignId,
        Operation = "campaign.enroll",
        OperationVersion = 1,
        Subject = new JsonObject { ["operation"] = "campaign.enroll", ["work_item"] = item.PublicId },
        SubjectHash = "sha256:subject",
        Preview = new JsonObject { ["intent"] = "Put one person into a campaign at the provider." },
        PluginId = "stand-in-provider",
        PluginSnapshotId = "snp_one",
        RoutingSnapshotId = "rts_one",
        Status = status,
        Reason = ApprovalGate.ApprovalRequired,
        RequestedAt = Noon,
    };
}
