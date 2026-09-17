using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Approvals;

/// <summary>One decision as a caller reads it. Nothing is assembled here: every field was written at the park.</summary>
public static class ApprovalMapper
{
    public static ApprovalDto ToDto(Approval approval, string workItemId, string campaignId)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return new ApprovalDto(
            approval.PublicId,
            workItemId,
            campaignId,
            approval.Operation,
            approval.OperationVersion,
            approval.Status,
            approval.Reason,
            approval.SubjectHash,
            approval.Subject,
            approval.Preview,
            approval.PluginId,
            approval.BindingIdentity,
            approval.RouteScope,
            approval.PluginSnapshotId,
            approval.RoutingSnapshotId,
            Utc(approval.RequestedAt),
            approval.DecidedAt is { } decided ? Utc(decided) : null,
            DecidedBy(approval),
            approval.DecisionReason);
    }

    public static ApprovalSummaryDto ToSummary(Approval approval, string workItemId, string campaignId)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return new ApprovalSummaryDto(
            approval.PublicId,
            workItemId,
            campaignId,
            approval.Operation,
            approval.Status,
            approval.Reason,
            approval.SubjectHash,
            approval.PluginId,
            Utc(approval.RequestedAt),
            approval.DecidedAt is { } decided ? Utc(decided) : null,
            DecidedBy(approval));
    }

    private static ActorRef? DecidedBy(Approval approval) =>
        approval.DecidedByType is { } type ? new ActorRef(type, approval.DecidedById) : null;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
