using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One unit of durable work inside a campaign: what is to be done, when it may run, and — once it has run —
/// what came of it. The row survives restarts, so an agent that dies mid-thought loses nothing but its turn.
/// </summary>
public sealed class WorkItem
{
    public int Id { get; set; }

    /// <summary><c>wi_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public int? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public WorkItemKind Kind { get; set; }

    /// <summary>Set for <c>ai_role</c> work: the name of the role that does the job.</summary>
    public string? Role { get; set; }

    /// <summary>Set for <c>provider_op</c> work: the vendor-neutral operation a plugin will perform.</summary>
    public string? Operation { get; set; }

    /// <summary>
    /// The execution profile this item asks for by name: the most specific level of the resolution order, and
    /// the one repair for work whose lineage cannot be resolved. Validated against the registry when it is
    /// written, so a name nothing answers is refused where it is typed rather than an hour later.
    /// </summary>
    public string? ExecutionProfile { get; set; }

    /// <summary>
    /// What the run that caused this item can hand down. Materialized once, when the item is created, so a
    /// later edit anywhere in the ancestry changes nothing here — and <see cref="LineageState.Unresolved"/>
    /// blocks the claim rather than falling through to the global default.
    /// </summary>
    public LineageState LineageState { get; set; } = LineageState.Root;

    public string? LineageProfileName { get; set; }

    public int? LineageProfileRevision { get; set; }

    /// <summary>The attempt this was inherited from, so a chain can be read back.</summary>
    public string? LineageFromAttemptId { get; set; }

    /// <summary>The concurrency token: two writers racing over one item cannot both win.</summary>
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Created;

    public int Priority { get; set; }

    public DateTime? NotBefore { get; set; }

    public DateTime? DueAt { get; set; }

    /// <summary>Owned by the dispatcher: when a retriable failure may be tried again. Never touches <see cref="NotBefore"/>, which belongs to the planner.</summary>
    public DateTime? RetryAfter { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? HeartbeatSeconds { get; set; }

    public int? MaxAttempts { get; set; }

    public ActorType CreatedByType { get; set; }

    public string? CreatedById { get; set; }

    /// <summary>What the executor is told about the job; snapshotted onto every attempt at claim.</summary>
    public JsonObject Context { get; set; } = new();

    public JsonNode? ResultFormat { get; set; }

    public JsonNode? Result { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>The last attempt's error, without its trace: the item carries the verdict, the attempt carries the evidence.</summary>
    public AttemptErrorDto? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public List<Attempt> Attempts { get; } = [];
}
