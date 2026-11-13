using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>
/// A campaign without its context: what <c>campaign.list</c> returns, so a listing stays small. <c>project_id</c> and
/// <c>project_slug</c> name the project it is in, so a listing can be shown by project without asking for each one;
/// <c>folder_path</c> is the full path of its folder under the harness root, null where none is recorded.
/// </summary>
public sealed record CampaignSummaryDto(
    string Id,
    string Name,
    CampaignStatus Status,
    string ProjectId,
    string ProjectSlug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,
    string? FolderPath = null);

/// <summary>
/// A campaign with its context: the shared knowledge every role working the campaign reads. <c>external_ids</c>
/// is always a list, possibly empty, for the same reason a contact's is. <c>execution_profile</c> is the
/// campaign's policy on which host its agent work uses, and null means it has none of its own. <c>project_id</c> and
/// <c>project_slug</c> name the project it is in. <c>folder_path</c> is the full path of its folder under the harness root,
/// null where none is recorded — a campaign made before folders were. <c>audience_threshold</c> is how few untouched
/// members the campaign keeps before its manager is told the reserve is low; absent, nobody counts them.
/// </summary>
public sealed record CampaignDto(
    string Id,
    string Name,
    CampaignStatus Status,
    string ProjectId,
    string ProjectSlug,
    JsonObject Context,
    IReadOnlyList<ExternalIdDto> ExternalIds,
    string? ExecutionProfile,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ReviewSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,

    /// <summary>The key the campaign was created with, when its creator gave one; absent otherwise.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdempotencyKey = null,
    string? FolderPath = null,
    int? AudienceThreshold = null);

/// <summary>
/// <c>idempotency_key</c> is a key the caller chose, so that sending the same request again answers with the campaign
/// it made rather than making another. It is unique in the installation; null means no key, and nothing ever makes one up.
/// <c>project_id</c> names the project the campaign is in; absent, it is the default project.
/// </summary>
public sealed record CampaignCreateRequest(
    string? Name,
    JsonObject? Context,
    ActorRef? Actor,
    string? Reason,
    string? IdempotencyKey = null,
    string? ProjectId = null);

public sealed record CampaignGetRequest(string? CampaignId);

/// <summary>
/// <c>query</c> finds campaigns by any part of their name, whatever its case or accents. Archived campaigns are hidden
/// unless <c>include_archived</c> is true or <c>status</c> asks for them; <c>project_id</c> keeps the campaigns of one
/// project.
/// </summary>
public sealed record CampaignListRequest(
    CampaignStatus? Status,
    int? Limit,
    string? Cursor,
    string? Query = null,
    bool? IncludeArchived = null,
    string? ProjectId = null);

/// <summary>
/// A partial patch: an absent <c>name</c> leaves the campaign's name alone, and an absent
/// <c>execution_profile</c> leaves its policy alone. An explicit null clears the policy; a value has to name a
/// profile this runtime knows. <c>review_seconds</c> behaves the same way, and null there means "however often
/// the installation reviews everything else". <c>project_id</c> moves the campaign to another project; a campaign is
/// always in one, so null is refused. <c>audience_threshold</c> behaves as <c>review_seconds</c> does, and null there
/// means nobody counts the campaign's untouched members.
/// </summary>
public sealed record CampaignUpdateRequest(
    string? CampaignId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> ExecutionProfile,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int?> ReviewSeconds,
    ActorRef? Actor,
    string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> ProjectId = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int?> AudienceThreshold = default);

/// <summary>The body of <c>campaign.start</c>, <c>campaign.pause</c> and <c>campaign.archive</c>.</summary>
public sealed record CampaignTransitionRequest(string? CampaignId, ActorRef? Actor, string? Reason);

/// <summary>Top-level edits to the campaign context. Null is a legitimate value, so clearing a key means listing it in <c>unset</c>.</summary>
public sealed record CampaignUpdateContextRequest(
    string? CampaignId,
    JsonObject? Set,
    IReadOnlyList<string>? Unset,
    ActorRef? Actor,
    string? Reason);

/// <summary>
/// <c>campaign_ids</c> names one to a hundred campaigns, archived ones included, answered in the order asked; absent, it
/// means every campaign <c>campaign.list</c> shows by default, in its order. <c>audience</c>, the number of one of the
/// campaign's audiences, counts only the people a search landed under it, each count by its own definition; an audience is
/// one campaign's, so <c>campaign_ids</c> then names that campaign alone. <c>exclude_archived_contacts</c> true leaves
/// out every membership of a contact whose own record is archived, so each count agrees with <c>contact.list</c> without
/// <c>include_archived</c>; absent, null or false, they are counted. A campaign's status plays no part in it.
/// </summary>
public sealed record CampaignSummaryRequest(IReadOnlyList<string>? CampaignIds, int? Audience = null, bool? ExcludeArchivedContacts = null);

/// <summary>
/// Where one campaign's people stand now, each count a number of people: one person may be in several, so they need not
/// add up to <c>people</c>. <c>sequence_linked</c> is false while the campaign's provider campaign is not pinned, and
/// nothing is known about a sequence there.
/// </summary>
public sealed record CampaignSummaryRow(
    string CampaignId,
    int People,
    int Waiting,
    int Failed,
    int Working,
    int New,
    int Idle,
    int Sequence,
    int Out,
    int Removed,
    bool SequenceLinked);

/// <summary>
/// The same counts across every campaign asked, each person counted once however many of them count the person; a person
/// may still be in two counts, as in one campaign. There is no <c>sequence_linked</c>: a campaign not linked counts nobody
/// in the sequence.
/// </summary>
public sealed record CampaignSummaryTotal(int People, int Waiting, int Failed, int Working, int New, int Idle, int Sequence, int Out, int Removed);

/// <summary>A row per campaign asked, and <c>total</c> across them.</summary>
public sealed record CampaignSummaryResponse(IReadOnlyList<CampaignSummaryRow> Campaigns, CampaignSummaryTotal Total);

/// <summary>
/// The campaigns, the audience and <c>exclude_archived_contacts</c> as <see cref="CampaignSummaryRequest"/> names them,
/// and the period: <c>since</c> included, <c>until</c> not, both required.
/// </summary>
public sealed record CampaignActivityRequest(
    IReadOnlyList<string>? CampaignIds, DateTimeOffset? Since, DateTimeOffset? Until, int? Audience = null, bool? ExcludeArchivedContacts = null);

/// <summary>
/// What happened to one campaign's people in the period, each count a number of people, removed ones included:
/// <c>people</c> is everyone anything happened to.
/// </summary>
public sealed record CampaignActivityRow(string CampaignId, int People, int Added, int WorkedOn, int Contacted, int Replied, int OptedOut, int Removed);

/// <summary>The period's counts across every campaign asked, each person counted once however many of them count the person.</summary>
public sealed record CampaignActivityTotal(int People, int Added, int WorkedOn, int Contacted, int Replied, int OptedOut, int Removed);

/// <summary>A row per campaign asked, and <c>total</c> across them.</summary>
public sealed record CampaignActivityResponse(IReadOnlyList<CampaignActivityRow> Campaigns, CampaignActivityTotal Total);

/// <summary>
/// One Activity count over time: the campaigns, the audience and <c>exclude_archived_contacts</c> as
/// <see cref="CampaignSummaryRequest"/> names them, the count, the period each bucket is (an hour, a day or a week), the
/// window (<c>since</c> included, <c>until</c> not) and the IANA zone the buckets are cut in, UTC when absent.
/// </summary>
public sealed record CampaignActivityByPeriodRequest(
    IReadOnlyList<string>? CampaignIds,
    CampaignActivityFilter? Count,
    ActivityPeriod? Period,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    string? Zone = null,
    int? Audience = null,
    bool? ExcludeArchivedContacts = null);

/// <summary>
/// The count with this bucket as its period, people counted once within it across the campaigns asked: a person counted
/// on two days stands in two buckets and once in <c>campaign.activity</c>'s total over the window.
/// </summary>
public sealed record ActivityBucket(DateTimeOffset Start, DateTimeOffset End, int Value);

/// <summary>Every bucket of the window, in order, zeros included; the first and the last are clipped to the window.</summary>
public sealed record CampaignActivityByPeriodResponse(ActivityPeriod Period, string Zone, IReadOnlyList<ActivityBucket> Buckets);
