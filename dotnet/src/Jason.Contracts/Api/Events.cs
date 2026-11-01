using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary><c>events.sources</c>: every account whose events are read or were, a page at a time in the order each was first bound.</summary>
public sealed record EventsSourcesRequest(int? Limit, string? Cursor);

/// <summary>
/// One account whose events are read, as its record stands: the plugin and the binding a route names for it, whether a
/// route binds it now, how far its reading has got — whether it holds a cursor and when that was stored, never the
/// cursor itself — its last read and how that ended, whether the inbound read may leave its campaigns to it, when it is
/// read next, the latest span its own reading skipped (kept once a span asked for again has read it), and the spans a
/// person asked for again.
/// </summary>
/// <param name="Binding">The binding as the active route snapshot holds it, as <c>route.list</c> answers it; null when no route binds the account now, or names no binding.</param>
/// <param name="Healthy">Whether its last read that succeeded started within the shorter of two cadences and the inbound read's look-back, with no failure since: the inbound read's own question.</param>
/// <param name="NextReadAt">When its own reading is next due, or now when that is already so; null when no route binds it.</param>
/// <param name="Backfills">Its ten newest spans asked for again, the newest first: every one still waiting is among them.</param>
public sealed record EventSourceDto(
    string Id,
    string Plugin,
    string BindingIdentity,
    JsonObject? Binding,
    bool Bound,
    bool HasCursor,
    DateTimeOffset? CursorAt,
    DateTimeOffset? ReadStartedAt,
    DateTimeOffset? ReadEndedAt,
    DateTimeOffset? SucceededAt,
    int Failures,
    string? LastCode,
    string? LastClass,
    bool More,
    bool Healthy,
    DateTimeOffset? NextReadAt,
    DateTimeOffset? SignaledAt,
    EventGapDto? LatestGap,
    IReadOnlyList<EventBackfillDto> Backfills,
    DateTimeOffset CreatedAt);

/// <summary>A span of an account's events nobody will read, as its line said it, and when.</summary>
public sealed record EventGapDto(DateTimeOffset From, DateTimeOffset To, DateTimeOffset SaidAt);

/// <summary>
/// A span of an account asked for again: the span, where it stands, who asked and why, what its reads stored and found
/// already held, and how it ended — its code and class when a failure ended it, or the last one while it waits.
/// </summary>
public sealed record EventBackfillDto(
    string Id,
    string Source,
    DateTimeOffset From,
    DateTimeOffset To,
    EventBackfillStatus Status,
    DateTimeOffset RequestedAt,
    ActorRef Actor,
    string? Reason,
    int Stored,
    int Duplicates,
    int Failures,
    DateTimeOffset? AttemptedAt,
    DateTimeOffset? EndedAt,
    string? Code,
    string? Class);

/// <summary>
/// <c>events.backfill</c>: read a span of one account's events again, every event at or after <c>from</c> and before
/// <c>to</c>. It reads and sends nothing, and never moves the account's own cursor.
/// </summary>
public sealed record EventsBackfillRequest(string? Source, DateTimeOffset? From, DateTimeOffset? To, ActorRef? Actor, string? Reason);

/// <summary>
/// <c>events.signal</c>: read this account sooner. It names the account and nothing else — no actor, no reason, nothing a
/// webhook carried — because it is a nudge from whatever program receives a provider's webhooks, and what is read is
/// what the account's own read finds.
/// </summary>
public sealed record EventsSignalRequest(string? Source);

/// <summary>The receipt of a signal: when it came, and when the account's next read is due because of it.</summary>
public sealed record EventsSignalResponse(DateTimeOffset SignaledAt, DateTimeOffset? NextReadAt);
