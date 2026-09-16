using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>A campaign without its context: what <c>campaign.list</c> returns, so a listing stays small.</summary>
public sealed record CampaignSummaryDto(
    string Id,
    string Name,
    CampaignStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// A campaign with its context: the shared knowledge every role working the campaign reads. <c>external_ids</c>
/// is always a list, possibly empty, for the same reason a contact's is.
/// </summary>
public sealed record CampaignDto(
    string Id,
    string Name,
    CampaignStatus Status,
    JsonObject Context,
    IReadOnlyList<ExternalIdDto> ExternalIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record CampaignCreateRequest(string? Name, JsonObject? Context, ActorRef? Actor, string? Reason);

public sealed record CampaignGetRequest(string? CampaignId);

public sealed record CampaignListRequest(CampaignStatus? Status, int? Limit, string? Cursor);

/// <summary>A partial patch: an absent <c>name</c> leaves the campaign's name alone.</summary>
public sealed record CampaignUpdateRequest(
    string? CampaignId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Name,
    ActorRef? Actor,
    string? Reason);

/// <summary>The body of <c>campaign.start</c>, <c>campaign.pause</c> and <c>campaign.archive</c>.</summary>
public sealed record CampaignTransitionRequest(string? CampaignId, ActorRef? Actor, string? Reason);

/// <summary>Top-level edits to the campaign context. Null is a legitimate value, so clearing a key means listing it in <c>unset</c>.</summary>
public sealed record CampaignUpdateContextRequest(
    string? CampaignId,
    JsonObject? Set,
    IReadOnlyList<string>? Unset,
    ActorRef? Actor,
    string? Reason);
