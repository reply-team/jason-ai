using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One run of one work item. Its public id is the fencing token: every executor operation carries it, and a
/// stale executor is told to stop rather than allowed to write over the run that replaced it.
/// </summary>
public sealed class Attempt
{
    public int Id { get; set; }

    /// <summary><c>att_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public int WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    /// <summary>1 for the first attempt of the item, and up from there.</summary>
    public int Number { get; set; }

    /// <summary>Which command ran the attempt; copied from the item so the history survives a kind change.</summary>
    public WorkItemKind Command { get; set; }

    public AttemptStatus Status { get; set; }

    public string? ExecutionProfile { get; set; }

    /// <summary>The item's whole context as it stood at claim: what the executor was actually told.</summary>
    public JsonObject ContextSnapshot { get; set; } = new();

    public AttemptErrorDto? Error { get; set; }

    public AttemptLaunchDto? Launch { get; set; }

    public DateTime ClaimedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public DateTime? LastHeartbeatAt { get; set; }

    /// <summary>Claim plus the timeout, never extended: the attempt's total budget, not a sliding window.</summary>
    public DateTime LockUntil { get; set; }
}
