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
/// anonymous human, which is right for creating work and wrong for deciding it. An approval of a search may also carry
/// <c>allow</c>: the person approves it and, in the same act, allows up to that many more people under its audience.
/// </summary>
public sealed record ApprovalDecisionRequest(string? ApprovalId, ActorRef? Actor, string? Reason, AllowanceRequest? Allow = null);

/// <summary>
/// The allowance given with an approval of a search, for that search's audience: the ceiling in people, required, and the
/// optional cap per search and expiry, as <c>approval.set_standing</c> takes them.
/// </summary>
public sealed record AllowanceRequest(int? MaxPeople, int? MaxPerSearch = null, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// A person's word, given in advance. For the campaign's drafted replies of one category: <c>operations</c> is a non-empty
/// set of <c>conversation.reply</c> and <c>message.send</c>, and <c>conversation.reply</c> alone when absent;
/// <c>expires_at</c> and <c>max_releases</c> are optional limits. For the campaign's searches, the allowance:
/// <c>operations</c> is <c>audience.search</c> alone, with no category, <c>audience</c> names one of the campaign's
/// audiences by its number, <c>max_people</c> is the ceiling in people the searches under it may ask for, and
/// <c>max_per_search</c> and <c>expires_at</c> are optional limits. Only a person sets one, and the request names them.
/// </summary>
public sealed record ApprovalSetStandingRequest(
    string? CampaignId,
    string? Category,
    IReadOnlyList<string>? Operations = null,
    DateTimeOffset? ExpiresAt = null,
    int? MaxReleases = null,
    ActorRef? Actor = null,
    string? Reason = null,
    int? Audience = null,
    int? MaxPeople = null,
    int? MaxPerSearch = null);

/// <summary>
/// Takes a campaign's standing approval back: the one for a category of reply, or the allowance, by its
/// <c>operation</c>. Exactly one of the two. A person's act, as setting it is.
/// </summary>
public sealed record ApprovalUnsetStandingRequest(string? CampaignId, string? Category, ActorRef? Actor = null, string? Reason = null, string? Operation = null);

/// <summary>The campaign's standing approvals that are set, in category order, those past a limit included.</summary>
public sealed record ApprovalListStandingRequest(string? CampaignId, int? Limit = null, string? Cursor = null);

/// <summary>
/// One campaign's standing approval, as it stands: for one category of reply, or the allowance for its searches, which
/// has no category. <c>released</c> counts the work it has let go since it was set; for the allowance <c>people</c> adds
/// up the people those searches asked for, and is null for a category. <c>in_force</c> is false once it has expired or
/// reached <c>max_releases</c> or <c>max_people</c>, when its campaign is archived, and for one that is not set, which
/// answers with every other member null. <c>line_id</c> is the chronicle line that set it, which each line of a release
/// under it names.
/// </summary>
public sealed record StandingApprovalDto(
    string CampaignId,
    string? Category,
    IReadOnlyList<string>? Operations,
    AudienceRef? Audience,
    DateTimeOffset? ExpiresAt,
    int? MaxReleases,
    int? MaxPeople,
    int? MaxPerSearch,
    int Released,
    int? People,
    bool InForce,
    ActorRef? SetBy,
    DateTimeOffset? SetAt,
    string? LineId);

/// <summary>One of a campaign's audiences, by its id and the number the campaign knows it by.</summary>
public sealed record AudienceRef(string Id, int Number);
