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

/// <summary>The provider's answer as it gave it; every field it cannot say is null.</summary>
public sealed record AudienceAvailabilityResponse(
    string PluginId,
    bool Available,
    string? Reason,
    string? Detail,
    JsonObject? Credits,
    long? Headroom);
