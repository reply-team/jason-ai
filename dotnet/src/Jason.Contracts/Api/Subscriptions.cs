using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary><c>subscription.list</c>: every subscribed journal kind, a page at a time in kind order.</summary>
public sealed record SubscriptionListRequest(int? Limit, string? Cursor);

/// <summary>
/// Where one subscribed journal kind stands: its chain as the active load froze it, the line its cursor stands after and
/// the kind's newest line, how many of its lines are due, the members that keep the chain from starting, and the chain in
/// flight. Only a kind with a subscriber in the active load has a cursor, so only such a kind is listed.
/// </summary>
/// <param name="Chain">The subscribers of the kind, in the order the load froze, each handed the line in turn.</param>
/// <param name="Cursor">The id of the journal line the kind's cursor stands after, of any kind; null at the journal's start.</param>
/// <param name="Head">The id of the kind's newest line; null while the journal holds none.</param>
/// <param name="Lag">How many lines of the kind are after the cursor.</param>
/// <param name="WaitingOn">The members of the chain that cannot run on this machine now, so no chain of the kind starts; empty when none.</param>
/// <param name="InFlight">The kind's chain that has started and not ended; null when none.</param>
/// <param name="RestUntil">When a kind whose turn could not finish, for a reason of the runtime's own, is offered again; null while it does not rest, and after a restart, which forgets a rest. Not <see cref="SubscriptionInFlightDto.RetryAt"/>, which is a handler's own retry within a chain.</param>
public sealed record SubscriptionKindDto(
    string Kind,
    IReadOnlyList<string> Chain,
    string? Cursor,
    string? Head,
    int Lag,
    IReadOnlyList<string> WaitingOn,
    SubscriptionInFlightDto? InFlight,
    DateTimeOffset? RestUntil);

/// <summary>
/// The chain of a kind that has started and not ended: its run, its line, when it started, and when a handler that failed
/// in a way that may pass is tried again, null while a handler runs.
/// </summary>
public sealed record SubscriptionInFlightDto(string Run, string Line, DateTimeOffset StartedAt, DateTimeOffset? RetryAt);

/// <summary><c>subscription.runs</c>: the records of one kind's chains, a page at a time, the newest first.</summary>
public sealed record SubscriptionRunsRequest(string? Kind, int? Limit, string? Cursor);

/// <summary>
/// The record of the chain started for one line: how each handler ended, as the runner wrote it, how the chain ended,
/// and the envelope the last handler returned.
/// </summary>
/// <param name="Handlers">Each invocation of a handler for the line, in order: <c>plugin_id</c>, <c>started_at</c>, and once it ended <c>outcome</c>, <c>code</c> and <c>duration_ms</c>.</param>
/// <param name="Failure">Which handler ended the chain, and how; null unless the chain failed.</param>
/// <param name="Envelope">The envelope the last handler returned; null while the chain runs and where it did not succeed.</param>
public sealed record SubscriptionRunDto(
    string Id,
    string Kind,
    string Line,
    SubscriptionRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    JsonArray Handlers,
    SubscriptionFailureDto? Failure,
    JsonObject? Envelope);

/// <summary>The handler that ended a chain: the plugin, the class it ended with, and the code.</summary>
public sealed record SubscriptionFailureDto(string PluginId, string Class, string Code);

/// <summary>
/// <c>subscription.rewind</c>: move a subscribed kind's cursor back, so its chain hands its plugins again every line of
/// the kind after the new cursor. Exactly one of <c>line</c> (a line of the kind, handed again first) and <c>since</c>
/// (an ISO-8601 moment: every line of the kind from it on is handed again). A person's act only.
/// </summary>
public sealed record SubscriptionRewindRequest(string? Kind, string? Line, string? Since, ActorRef? Actor, string? Reason);
