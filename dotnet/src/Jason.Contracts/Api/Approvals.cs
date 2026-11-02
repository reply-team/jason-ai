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

    /// <summary>
    /// Why the work was parked: <c>approval_required</c>, or <c>input_changed</c> after a decision; or
    /// <c>standing_approval</c> where it was never parked, because a standing approval a person set released it.
    /// </summary>
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
    string? DecisionReason,
    string CampaignName,
    string? ContactName);

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
    ActorRef? DecidedBy,
    string CampaignName,
    string? ContactName);

/// <summary>
/// Pending by default: the question a person opens this with is "what am I holding up?". <c>statuses</c> names several
/// at once — "what was decided" — and wins over <c>status</c> when both are given.
/// </summary>
public sealed record ApprovalListRequest(
    ApprovalStatus? Status,
    string? CampaignId,
    string? WorkItemId,
    int? Limit,
    string? Cursor,
    IReadOnlyList<ApprovalStatus>? Statuses = null);

public sealed record ApprovalGetRequest(string? ApprovalId);

/// <summary>
/// Approve and reject take the same three things, and the actor is not optional: an absent one would be an
/// anonymous human, which is right for creating work and wrong for deciding it.
/// </summary>
public sealed record ApprovalDecisionRequest(string? ApprovalId, ActorRef? Actor, string? Reason);

/// <summary>
/// A person's word, given in advance, that the campaign's drafted replies of one category may go without a decision each
/// time. <c>operations</c> is a non-empty set of <c>conversation.reply</c> and <c>message.send</c>, and
/// <c>conversation.reply</c> alone when absent; <c>expires_at</c> and <c>max_releases</c> are optional limits. Only a person
/// sets one, and the request names them.
/// </summary>
public sealed record ApprovalSetStandingRequest(
    string? CampaignId,
    string? Category,
    IReadOnlyList<string>? Operations = null,
    DateTimeOffset? ExpiresAt = null,
    int? MaxReleases = null,
    ActorRef? Actor = null,
    string? Reason = null);

/// <summary>Takes a campaign's standing approval for one category back. A person's act, as setting it is.</summary>
public sealed record ApprovalUnsetStandingRequest(string? CampaignId, string? Category, ActorRef? Actor = null, string? Reason = null);

/// <summary>The campaign's standing approvals that are set, in category order, those past a limit included.</summary>
public sealed record ApprovalListStandingRequest(string? CampaignId, int? Limit = null, string? Cursor = null);

/// <summary>
/// One campaign's standing approval for one category, as it stands. <c>released</c> counts the work it has let go since
/// it was set; <c>in_force</c> is false once it has expired or reached <c>max_releases</c>, and for a category that is not
/// set, which answers with every other member null. <c>line_id</c> is the chronicle line that set it, which each line of a
/// release under it names.
/// </summary>
public sealed record StandingApprovalDto(
    string CampaignId,
    string Category,
    IReadOnlyList<string>? Operations,
    DateTimeOffset? ExpiresAt,
    int? MaxReleases,
    int Released,
    bool InForce,
    ActorRef? SetBy,
    DateTimeOffset? SetAt,
    string? LineId);
