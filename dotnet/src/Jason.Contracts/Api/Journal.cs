using System.Text.Json.Nodes;
using Jason.Contracts.Journal;

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
    string? Reason,
    string? ContactId = null,
    string? CampaignName = null,
    string? ContactName = null,
    WorkItemKind? WorkItemKind = null,
    string? WorkItemRole = null,
    string? WorkItemOperation = null,
    string? ActorRole = null);

/// <summary>Kinds the runtime writes itself are refused here; roles append their own vocabulary.</summary>
public sealed record JournalAppendRequest(string? CampaignId, string? Kind, string? Key, JsonNode? New, ActorRef? Actor, string? Reason);

public sealed record JournalListRequest(
    string? CampaignId,
    string? WorkItemId,
    string? Kind,
    DateTimeOffset? Since,
    int? Limit,
    string? Cursor,
    string? ContactId = null,
    IReadOnlyList<string>? Kinds = null);

public sealed record JournalKindsRequest(int? Limit, string? Cursor);

/// <summary>
/// One kind the runtime reserves for its own journal lines, as its document under <c>docs/contracts/journal/</c>
/// publishes it: the release that first wrote it, the one sentence of what a line of it records, whether a plugin may
/// subscribe to it, the entities its lines name and where, the release each later field of its body arrived in, and the
/// body's schema exactly as the document writes it.
/// </summary>
public sealed record JournalKindDto(
    string Kind,
    int Version,
    string Since,
    string Summary,
    bool Subscribable,
    IReadOnlyDictionary<string, JournalKindEntityDto> Entities,
    IReadOnlyDictionary<string, string> FieldSince,
    JsonObject Body);

/// <summary>
/// Where a line of a kind names one entity: every place, as a list of JSON pointers into the line as
/// <c>journal.list</c> answers it, even where the document writes one; whether every line names it; and whether a place
/// names many of them.
/// </summary>
public sealed record JournalKindEntityDto(IReadOnlyList<string> At, JournalEntityPresence Presence, bool Each);
