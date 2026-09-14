using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// One line of the chronicle: what happened, who claims to have done it, and what changed. Entries are append-only —
/// there is no operation that rewrites or removes one.
/// </summary>
public sealed record JournalEntryDto(
    string Id,
    DateTimeOffset Ts,
    ActorRef Actor,
    string Kind,
    string? CampaignId,
    string? WorkItemId,
    string? AttemptId,
    string? Key,
    JsonNode? Old,
    JsonNode? New,
    string? Reason);

/// <summary>Kinds the runtime writes itself are refused here; roles append their own vocabulary.</summary>
public sealed record JournalAppendRequest(string? CampaignId, string? Kind, string? Key, JsonNode? New, ActorRef? Actor, string? Reason);

public sealed record JournalListRequest(string? CampaignId, string? WorkItemId, string? Kind, DateTimeOffset? Since, int? Limit, string? Cursor);
