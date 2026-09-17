using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Api;

/// <summary>
/// Why an attempt failed. <c>Retriable</c> is the runtime's judgement, not the executor's: one rule set decides
/// whether the work is worth another attempt. <c>Trace</c> is diagnostic and never travels onto the work item.
/// <c>Class</c> is present only where something with the standing to classify the failure did so — a plugin
/// answering for a provider — so an attempt nobody classified serialises exactly as it always has.
/// </summary>
public sealed record AttemptErrorDto(
    string Code,
    string Message,
    bool Retriable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Trace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ErrorDetail>? Details = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FailureClass? Class = null);

/// <summary>How an attempt was actually run: provenance without secrets.</summary>
public sealed record AttemptLaunchDto(IReadOnlyList<string> EntryCommand, string WorkDir, int? Pid, int? ExitCode);

/// <summary>
/// What was resolved to run one provider attempt, and what the invocation then added. Written at claim as far
/// as resolution got and never edited afterwards: an attempt is a record of what happened, so a route changed,
/// a plugin reloaded or a package edited later leaves it exactly as it stands.
/// </summary>
/// <remarks>
/// Every field is nullable because the record is honest about how far the decision reached: an item refused
/// before anything was routed names no plugin, and one refused because its plugin was unavailable still names
/// the plugin it would have used, which is what a manager acts on. The binding is named by its identity rather
/// than by its value — the same identity across attempts is what answers "was this retried against a different
/// account?", while the value itself stays in the route.
/// </remarks>
/// <param name="CorrelationId">The attempt's own id, which is what ties an invocation back to the work.</param>
/// <param name="ExternalIdsReturned">
/// The identifiers the plugin answered with, keyed as it keyed them. The kinds the contract declares are pinned
/// and can be read from the entity; this is the only place an undeclared one survives at all, so its spelling is
/// kept out of the dialect's naming policy — the key here matches the pointer that refused it.
/// </param>
/// <param name="RejectedResult">
/// The answer a shape error refused, so a plugin author can see what was actually sent. Present only where an
/// otherwise well-formed answer failed the operation's own schema, and only where the read asked for snapshots:
/// it is bounded by what the invoker will read back, which is not a size every read of a work item should carry.
/// </param>
public sealed record AttemptProvenanceDto(
    string? PluginId,
    string? PluginVersion,
    string? PluginDigest,
    int? ProtocolVersion,
    int? OperationContractVersion,
    string? Operation,
    int? OperationVersion,
    string? PluginSnapshotId,
    string? RoutingSnapshotId,
    RouteScope? RouteScope,
    string? BindingIdentity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InvocationId = null,
    string? CorrelationId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OutcomeDiagnostics? Diagnostics = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonConverter(typeof(VerbatimKeysConverter))]
    IReadOnlyDictionary<string, string>? ExternalIdsReturned = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? RejectedResult = null,

    /// <summary>The decision this attempt ran under, where a person had to make one.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApprovalId = null);

/// <summary>One run of one work item. The id is also the fencing token every executor operation must carry.</summary>
public sealed record AttemptDto(
    string Id,
    int Number,
    AttemptStatus Status,
    WorkItemKind Command,
    string? ExecutionProfile,
    AttemptErrorDto? Error,
    AttemptLaunchDto? Launch,
    DateTimeOffset ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset LockUntil,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? ContextSnapshot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AttemptProvenanceDto? Provenance = null);

/// <summary>
/// One unit of durable work. <c>Eligible</c> is computed, never stored: it says whether the dispatcher would
/// claim the item right now, which is the question every caller actually asks.
/// </summary>
public sealed record WorkItemDto(
    string Id,
    string CampaignId,
    string? ContactId,
    WorkItemKind Kind,
    string? Role,
    string? Operation,
    string? ExecutionProfile,
    WorkItemStatus Status,
    bool Eligible,
    int Priority,
    DateTimeOffset? NotBefore,
    DateTimeOffset? DueAt,
    DateTimeOffset? RetryAfter,
    int? TimeoutSeconds,
    int? HeartbeatSeconds,
    int? MaxAttempts,
    ActorRef CreatedBy,
    JsonObject Context,
    JsonNode? ResultFormat,
    JsonNode? Result,
    int AttemptCount,
    string? CurrentAttemptId,
    AttemptErrorDto? LastError,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AttemptDto>? Attempts,

    /// <summary>
    /// Effects somebody performed outside this runtime and reported into it afterwards, newest first. They are
    /// a list of their own so that nobody can mistake one for work: an attempt is something this runtime
    /// claimed, ran and stands behind, a report is somebody's word about something it never touched. Sent when
    /// one item is fetched, and capped — past the cap the report listing is where the rest of them are.
    /// </summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ReportSummaryDto>? ExternalReports,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt);

/// <summary>A listing stays small: no context, no result, no attempts.</summary>
public sealed record WorkItemSummaryDto(
    string Id,
    string CampaignId,
    string? ContactId,
    WorkItemKind Kind,
    string? Role,
    string? Operation,
    string? ExecutionProfile,
    WorkItemStatus Status,
    bool Eligible,
    int Priority,
    DateTimeOffset? NotBefore,
    DateTimeOffset? DueAt,
    DateTimeOffset? RetryAfter,
    ActorRef CreatedBy,
    int AttemptCount,
    string? CurrentAttemptId,
    AttemptErrorDto? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt);

public sealed record WorkItemCreateRequest(
    string? CampaignId,
    WorkItemKind? Kind,
    string? Role,
    string? Operation,
    string? ContactId,
    string? ExecutionProfile,
    int? Priority,
    DateTimeOffset? NotBefore,
    DateTimeOffset? DueAt,
    int? TimeoutSeconds,
    int? HeartbeatSeconds,
    int? MaxAttempts,
    JsonObject? Context,
    JsonNode? ResultFormat,
    ActorRef? Actor,
    string? Reason);

public sealed record WorkItemGetRequest(string? WorkItemId, bool? IncludeSnapshots);

public sealed record WorkItemListRequest(
    string? CampaignId,
    string? ContactId,
    IReadOnlyList<WorkItemStatus>? Status,
    WorkItemKind? Kind,
    string? Role,
    bool? Eligible,
    int? Limit,
    string? Cursor);

/// <summary>Partial patch: absent fields leave the item alone; set/unset edit top-level context keys like the campaign context.</summary>
public sealed record WorkItemUpdateRequest(
    string? WorkItemId,
    JsonObject? Set,
    IReadOnlyList<string>? Unset,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<DateTimeOffset?> NotBefore,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<DateTimeOffset?> DueAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int> Priority,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int?> TimeoutSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int?> HeartbeatSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<int?> MaxAttempts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<JsonNode?> ResultFormat,
    ActorRef? Actor,
    string? Reason);

public sealed record WorkItemCancelRequest(string? WorkItemId, ActorRef? Actor, string? Reason);

public sealed record WorkItemHeartbeatRequest(string? WorkItemId, string? AttemptId);

/// <summary>The lease is never extended; the heartbeat only proves liveness, and the answer says when the next one is due.</summary>
public sealed record HeartbeatResponse(string WorkItemId, string AttemptId, DateTimeOffset LockUntil, DateTimeOffset? HeartbeatDueBy);

public sealed record WorkItemSetResultRequest(string? WorkItemId, string? AttemptId, JsonNode? Result);

public sealed record CompletionErrorDto(string? Code, string? Message, IReadOnlyList<ErrorDetail>? Details);

public sealed record WorkItemCompleteRequest(
    string? WorkItemId,
    string? AttemptId,
    CompletionStatus? Status,
    JsonNode? Result,
    CompletionErrorDto? Error,
    string? Reason);
