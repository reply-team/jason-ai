using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// One decision about one work item, as a caller reads it. The subject is what is being approved and the preview
/// is what a person reads before deciding; both were written when the work was parked, so what is answered here
/// is what the claim saw rather than a fresh assembly against rows that may have moved since.
/// </summary>
public sealed record ApprovalDto(
    string Id,
    string WorkItemId,
    string CampaignId,
    string Operation,
    int OperationVersion,
    ApprovalStatus Status,

    /// <summary>Why the work was parked: <c>approval_required</c>, or <c>input_changed</c> after a decision.</summary>
    string Reason,
    string SubjectHash,
    JsonObject Subject,
    JsonObject Preview,
    string PluginId,
    string? BindingIdentity,
    RouteScope RouteScope,
    string PluginSnapshotId,
    string RoutingSnapshotId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    ActorRef? DecidedBy,
    string? DecisionReason);

/// <summary>A listing stays small: what it is about, and how far it has got. The subject is read one at a time.</summary>
public sealed record ApprovalSummaryDto(
    string Id,
    string WorkItemId,
    string CampaignId,
    string Operation,
    ApprovalStatus Status,
    string Reason,
    string SubjectHash,
    string PluginId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    ActorRef? DecidedBy);

/// <summary>Pending by default: the question a person opens this with is "what am I holding up?".</summary>
public sealed record ApprovalListRequest(
    ApprovalStatus? Status,
    string? CampaignId,
    string? WorkItemId,
    int? Limit,
    string? Cursor);

public sealed record ApprovalGetRequest(string? ApprovalId);

/// <summary>
/// Approve and reject take the same three things, and the actor is not optional: an absent one would be an
/// anonymous human, which is right for creating work and wrong for deciding it.
/// </summary>
public sealed record ApprovalDecisionRequest(string? ApprovalId, ActorRef? Actor, string? Reason);
