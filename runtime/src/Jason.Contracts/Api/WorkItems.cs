using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Api;

/// <summary>
/// Why an attempt failed. <c>Retriable</c> is the runtime's judgement, not the executor's: one rule set decides
/// whether the work is worth another attempt. <c>Trace</c> is diagnostic and never travels onto the work item.
/// <c>Class</c> is present only where something with the standing to classify the failure did so — a plugin
/// answering for a provider — so an attempt nobody classified serialises exactly as it always has. <c>Block</c> is
/// present only on a <c>blocked</c> failure whose block satisfies the published provider-block contract: what is
/// blocked, why, and where a person clears it.
/// </summary>
public sealed record AttemptErrorDto(
    string Code,
    string Message,
    bool Retriable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Trace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ErrorDetail>? Details = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FailureClass? Class = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? Block = null);

/// <summary>How an attempt was actually run: provenance without secrets.</summary>
/// <param name="StdoutTruncated">
/// True when the child said more than the configured maximum and its transcript was cut. Nothing about the
/// outcome depends on it — a result reaches the runtime through the API and never through standard output — so
/// this says only that the file is not the whole story.
/// </param>
/// <param name="OutputAbandoned">
/// True when the child ended and something it left behind still held its output pipes, so the launcher stopped
/// reading rather than waiting for a writer that may never let go. The attempt's own outcome is unaffected; the
/// files are simply not the whole story.
/// </param>
/// <param name="Deny">The deny rules written into the work directory for this attempt, as they were written.</param>
/// <param name="RoleSkill">
/// What the role was taught: the skill copied in for it, or that there was none to copy. Whether a launched role
/// had its instructions is otherwise unrecoverable after the fact.
/// </param>
public sealed record AttemptLaunchDto(
    IReadOnlyList<string> EntryCommand,
    string WorkDir,
    int? Pid,
    int? ExitCode,
    bool StdoutTruncated = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OutputAbandoned = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Deny = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RoleSkillDto? RoleSkill = null);

/// <summary>Whether the role that ran this attempt was given its skill, and how large the one it was given was.</summary>
public sealed record RoleSkillDto(bool Copied, string Name, long Bytes);

/// <summary>
/// What was resolved to run one agent attempt: which profile, chosen by which level of the published order, and
/// the arguments the host was actually started with. Written at the claim and never rewritten, so an edit to the
/// profile afterwards leaves this exactly as it stands — which is why it names a revision and not just a name.
/// </summary>
/// <param name="ProfileRevision">The revision in force when the attempt was claimed; the one that actually ran.</param>
/// <param name="LineageRevision">
/// What the item inherited, where the profile came from lineage. It may differ from <paramref name="ProfileRevision"/>,
/// because inherited work runs the profile as it is now rather than as it was when the ancestor ran; both
/// numbers are kept so that difference can be read rather than guessed.
/// </param>
/// <param name="Args">The whole argument list as launched: the runtime's own, then the profile's.</param>
public sealed record AgentProvenanceDto(
    ProfileResolutionSource ResolutionSource,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProfileId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProfileName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProfileRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? LineageRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentHostKind? Host = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Program = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Args = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HostVersionVerified = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SessionId = null);

/// <summary>
/// Where a work item's execution profile comes from when nothing more specific says. Materialized once, when the
/// item is created, from the run that caused it — so it is a fact about this item rather than a walk back
/// through a history that may since have changed.
/// </summary>
public sealed record LineageDto(
    LineageState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProfileName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProfileRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FromAttemptId = null);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApprovalId = null,

    /// <summary>
    /// The agent half: which execution profile ran this attempt and how it was chosen. Present on agent
    /// attempts, absent on provider ones, so there is still exactly one provenance record per attempt.
    /// </summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentProvenanceDto? Agent = null);

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
    DateTimeOffset? FinishedAt,

    /// <summary>Where this item's execution profile comes from when nothing more specific says.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LineageDto? Lineage = null,

    /// <summary>The key the item was created with, when its creator gave one; absent otherwise.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdempotencyKey = null,

    /// <summary>The campaign's name, so a reader need not resolve the id to know what this is part of.</summary>
    string CampaignName = "",

    /// <summary>The person's name, or null where the item is about nobody or the person has no name.</summary>
    string? ContactName = null,

    /// <summary>What a waiting item waits for; absent unless the item is waiting.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WaitDto? Wait = null);

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
    DateTimeOffset? FinishedAt,
    string CampaignName = "",
    string? ContactName = null,

    /// <summary>When the live attempt started, while the item is processing; null otherwise.</summary>
    DateTimeOffset? RunningSince = null,

    /// <summary>What a waiting item waits for, without the items themselves; absent unless the item is waiting.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WaitSummaryDto? Wait = null);

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
    string? Reason,

    /// <summary>
    /// A key the caller chose, so that sending the same request again answers with the item it made rather than making
    /// another. Scoped to the campaign; null means no key, and nothing ever makes one up.
    /// </summary>
    string? IdempotencyKey = null);

public sealed record WorkItemGetRequest(string? WorkItemId, bool? IncludeSnapshots);

public sealed record WorkItemListRequest(
    string? CampaignId,
    string? ContactId,
    IReadOnlyList<WorkItemStatus>? Status,
    WorkItemKind? Kind,
    string? Role,
    bool? Eligible,
    int? Limit,
    string? Cursor,

    /// <summary>The start of the window, inclusive, on the time the order sorts by.</summary>
    DateTimeOffset? Since = null,

    /// <summary>The end of the window, exclusive, on the same time.</summary>
    DateTimeOffset? Until = null,
    WorkItemListOrder? Order = null);

/// <summary>
/// How many rows the same listing would walk through to its end: the list's filters, its window and its order (which
/// decides the time the window measures and, for <c>finished</c>, that open work is left out), and no page.
/// </summary>
public sealed record WorkItemCountRequest(
    string? CampaignId,
    string? ContactId,
    IReadOnlyList<WorkItemStatus>? Status,
    WorkItemKind? Kind,
    string? Role,
    bool? Eligible,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    WorkItemListOrder? Order);

public sealed record WorkItemCountResponse(int Total);

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

    /// <summary>
    /// Which profile runs this work. Patchable, unlike the campaign, contact, kind, role and operation that say
    /// what the item <em>is</em>: this is how the work runs, and it belongs beside the timeout, the heartbeat,
    /// the attempt limit and the result format, which are patchable for the same reason.
    /// <para>
    /// It is the repair for an item whose ancestry cannot be resolved, and the repair has to be made
    /// <em>before</em> the work is claimed. A refused attempt fails the item, and a finished item is not
    /// changed — so an item that has already been refused is asked for again rather than reopened, exactly as
    /// every other fail-closed refusal in this runtime behaves.
    /// </para>
    /// </summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> ExecutionProfile,
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

/// <summary>
/// The attempt ends by waiting, and its item waits: for the work items named in <paramref name="On"/> to end — all of
/// them, or any one — for the moment <paramref name="Until"/>, or for whichever of the two comes first. The next run of
/// the item is a new attempt, told in its launch envelope what it waited for.
/// </summary>
/// <param name="On">The work items to wait for, in the order the next run is told about them; each one created by an attempt of this item.</param>
/// <param name="Mode">Whether every named item must end, or any one; <c>all</c> when items are named and no mode is, and never given without items.</param>
/// <param name="Until">The moment to wait for, or the latest one to wait until when items are named; later than now.</param>
public sealed record WorkItemWaitRequest(
    string? WorkItemId,
    string? AttemptId,
    IReadOnlyList<string>? On,
    WaitMode? Mode,
    DateTimeOffset? Until,
    string? Reason)
{
    /// <summary>The most work items one wait may name.</summary>
    public const int MaxWaitItems = 100;
}

/// <summary>A person, or any named actor, ends a wait by hand: the item goes back to the queue as if what it waited for had happened.</summary>
public sealed record WorkItemResumeRequest(string? WorkItemId, ActorRef? Actor, string? Reason);

/// <summary>
/// The open wait of a waiting item: what it waits for and until when. Identifiers, statuses and moments only, so it
/// reads the same whatever the items are about.
/// </summary>
/// <param name="AttemptId">The attempt that ended by waiting; every wait a role makes has one.</param>
/// <param name="On">The named work items in the order they were named, each with where it stands now.</param>
/// <param name="Mode">Whether every named item must end, or any one; null for a wait on a moment alone.</param>
/// <param name="Until">The moment the wait named; null where it named none.</param>
/// <param name="Deadline">When the wait ends whatever happens: the moment it named, or the ceiling; every wait a role makes has one.</param>
/// <param name="OpenedAt">When the item began to wait.</param>
/// <param name="BlockedBy">The provider block a parked item waits on; null for a role's wait.</param>
public sealed record WaitDto(
    string? AttemptId,
    IReadOnlyList<WaitItemDto> On,
    WaitMode? Mode,
    DateTimeOffset? Until,
    DateTimeOffset? Deadline,
    DateTimeOffset OpenedAt,
    WaitBlockedByDto? BlockedBy = null);

/// <summary>
/// The open wait of a waiting item as a listing carries it: how many items it names and how many of them have ended, not
/// which or how each stands, so a page stays small; <c>workitem.get</c> has the items.
/// </summary>
/// <param name="AttemptId">The attempt that ended by waiting.</param>
/// <param name="Mode">Whether every named item must end, or any one; null for a wait on a moment alone.</param>
/// <param name="Until">The moment the wait named; null where it named none.</param>
/// <param name="Deadline">When the wait ends whatever happens.</param>
/// <param name="OpenedAt">When the item began to wait.</param>
/// <param name="OnCount">How many work items the wait names; 0 for a wait on a moment alone.</param>
/// <param name="EndedCount">How many of them have ended — succeeded, failed, cancelled or expired.</param>
/// <param name="BlockedBy">The provider block a parked item waits on; null for a role's wait.</param>
public sealed record WaitSummaryDto(
    string? AttemptId,
    WaitMode? Mode,
    DateTimeOffset? Until,
    DateTimeOffset? Deadline,
    DateTimeOffset OpenedAt,
    int OnCount,
    int EndedCount,
    WaitBlockedByDto? BlockedBy = null);

/// <summary>
/// The provider block a parked item waits on: which block, what it holds and why, in the contract's words. Never where
/// a person goes to clear it — the block's link stays with the block.
/// </summary>
/// <param name="ProviderBlockId">The block's public id.</param>
/// <param name="Scope">What it holds: every operation it holds through the binding, or those from one sending account.</param>
/// <param name="Reason">Why, in the contract's words: <c>billing</c>, <c>limit</c>, <c>credential</c>, <c>channel</c> or <c>other</c>.</param>
public sealed record WaitBlockedByDto(string ProviderBlockId, ProviderBlockScope Scope, string Reason);

/// <summary>One work item a wait names, and where it stands.</summary>
public sealed record WaitItemDto(string WorkItemId, WorkItemStatus Status);
