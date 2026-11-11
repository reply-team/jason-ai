using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// How big an audience is and a sample of who is in it, read now from the plugin the campaign's route for
/// <c>audience.preview</c> names. Exactly one of <c>audience</c> (an audience the campaign holds, by its number) and
/// <c>definition</c> (the provider's own shape, checked against the plugin's declared schema before anything runs).
/// Nothing is recorded.
/// </summary>
public sealed record AudiencePreviewRequest(string? CampaignId, int? Audience, JsonObject? Definition, int? PeoplePerCompany);

/// <summary>The provider's estimate as it answered it: <c>sample</c> holds the operation's own people, untouched.</summary>
public sealed record AudiencePreviewResponse(string PluginId, long? Count, long? Companies, JsonArray Sample);

/// <summary>
/// The values one of the provider's filters may take, starting with <c>query</c>, read now through the campaign's route
/// for <c>audience.filter_values</c>. Nothing is recorded.
/// </summary>
public sealed record AudienceFilterValuesRequest(string? CampaignId, string? Filter, string? Query, int? Limit);

/// <summary>The first matches in the provider's order, as it answered them.</summary>
public sealed record AudienceFilterValuesResponse(string PluginId, JsonArray Values);

/// <summary>
/// Whether the account the campaign's route for <c>audience.availability</c> binds may search now, and what it has left.
/// Nothing is recorded.
/// </summary>
public sealed record AudienceAvailabilityRequest(string? CampaignId);

/// <summary>
/// Where one campaign's audience stands, read from Jason's own records: the campaign <c>campaign_id</c> names, and a page
/// of its audiences, <c>limit</c> and <c>cursor</c> as any list takes them. <c>search_limit</c> is how many of each
/// audience's searches are listed, the newest first: 1 to 100, and 10 when absent.
/// </summary>
public sealed record CampaignAudienceRequest(string? CampaignId, int? Limit = null, string? Cursor = null, int? SearchLimit = null);

/// <summary>
/// The campaign's reserve, its allowance with how much of it is used, and a page of its audiences, the newest first, each
/// with its latest searches. <c>allowance</c> is the campaign's allowance as <c>approval.list_standing</c> answers it, null
/// where none is set. The reserve and the allowance are said on every page; <c>next_cursor</c> reads the audiences that
/// came before.
/// </summary>
public sealed record CampaignAudienceResponse(
    string CampaignId,
    AudienceReserve Reserve,
    StandingApprovalDto? Allowance,
    IReadOnlyList<CampaignAudienceDto> Audiences,
    string? NextCursor);

/// <summary>
/// How many members nobody has started on yet (<c>untouched</c>, the summary's <c>new</c>), and the threshold under which the
/// reserve is low, null where the campaign keeps none.
/// </summary>
public sealed record AudienceReserve(int Untouched, int? Threshold);

/// <summary>
/// One of the campaign's audiences: its id and number, when the claim that made it ran, the definition as that claim held
/// it, how many memberships its searches landed (<c>landed</c>, every search's, the memberships <c>audience</c> on a
/// people read keeps to, a removed one included), how many searches the provider took under it (<c>search_count</c>), and
/// the latest of them, the newest first.
/// </summary>
public sealed record CampaignAudienceDto(
    string Id,
    int Number,
    DateTimeOffset CreatedAt,
    JsonObject Definition,
    int Landed,
    int SearchCount,
    IReadOnlyList<AudienceSearchDto> Searches);

/// <summary>
/// One search as the runtime follows it: its work item (<c>search</c>), the provider's latest word for it (<c>state</c>, null
/// until a read answered), how many people it asked for, how many the provider said it found (null until it said), how
/// many memberships it landed, when the provider took it, and how and when it ended, both null while it is followed.
/// </summary>
public sealed record AudienceSearchDto(
    string Search,
    string? State,
    int Asked,
    int? Found,
    int Landed,
    DateTimeOffset BeganAt,
    string? EndedAs,
    DateTimeOffset? EndedAt);

/// <summary>The provider's answer as it gave it; every field it cannot say is null.</summary>
public sealed record AudienceAvailabilityResponse(
    string PluginId,
    bool Available,
    string? Reason,
    string? Detail,
    JsonObject? Credits,
    long? Headroom);
