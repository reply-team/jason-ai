using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>
/// Why an attempt failed. <c>Retriable</c> is the runtime's judgement, not the executor's: one rule set decides
/// whether the work is worth another attempt. <c>Trace</c> is diagnostic and never travels onto the work item.
/// </summary>
public sealed record AttemptErrorDto(
    string Code,
    string Message,
    bool Retriable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Trace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ErrorDetail>? Details = null);

/// <summary>How an attempt was actually run: provenance without secrets.</summary>
public sealed record AttemptLaunchDto(IReadOnlyList<string> EntryCommand, string WorkDir, int? Pid, int? ExitCode);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? ContextSnapshot);

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
