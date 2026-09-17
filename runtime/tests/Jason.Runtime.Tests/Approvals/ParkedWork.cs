using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// One parked item and the decision it is waiting for, written the way the claim writes them. The claim's own
/// half of this is proven where the claim is; what these tests are about is everything that happens next.
/// </summary>
internal static class ParkedWork
{
    public static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    public static async Task<(WorkItem Item, Approval Approval)> WriteAsync(
        JasonDbContext db,
        CancellationToken ct,
        DateTime? dueAt = null,
        string hash = "sha256:subject",
        int priority = 0)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        item.Status = WorkItemStatus.AwaitingApproval;
        item.DueAt = dueAt;
        item.Priority = priority;
        db.WorkItems.Add(item);

        var approval = new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = "campaign.enroll",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "campaign.enroll", ["work_item"] = item.PublicId },
            SubjectHash = hash,
            Preview = new JsonObject
            {
                ["intent"] = "Put one person into a campaign at the provider.",
                ["contact"] = new JsonObject { ["name"] = "Ada", ["value"] = "ada@example.test" },
            },
            PluginId = "stand-in-provider",
            BindingIdentity = "sha256:account",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_one",
            RoutingSnapshotId = "rts_one",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
        };

        db.Approvals.Add(approval);
        await db.SaveChangesAsync(ct);
        return (item, approval);
    }
}
