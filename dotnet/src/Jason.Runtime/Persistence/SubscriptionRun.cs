using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// The record of the chain started for one journal line of a subscribed kind: which line, how each handler ended, the
/// envelope the last one returned, and, where a handler ended it, which one and how. A kind has at most one running.
/// </summary>
/// <remarks>
/// Records are kept to a bound per kind and pruned at each insert, a running one never; the permanent record of a
/// failure is the journal's <c>subscription_failed</c> line, never this row.
/// </remarks>
public sealed class SubscriptionRun
{
    public int Id { get; set; }

    /// <summary><c>sbr_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string Kind { get; set; }

    /// <summary>The line's journal id, and no reference to it.</summary>
    public int LineId { get; set; }

    /// <summary>The line's <c>jrn_</c> id, as the API names it.</summary>
    public required string LinePublicId { get; set; }

    public SubscriptionRunStatus Status { get; set; }

    /// <summary>
    /// Each invocation of a handler for this line, in order: <c>plugin_id</c> and <c>started_at</c>, written before the
    /// child starts, then <c>outcome</c> (<c>succeeded</c> or a class), <c>code</c> and <c>duration_ms</c> once it ends.
    /// An entry with no outcome is an invocation that may have acted.
    /// </summary>
    public JsonArray Handlers { get; set; } = [];

    /// <summary>The envelope the last handler returned; null while running and where a handler ended the chain.</summary>
    public JsonObject? Envelope { get; set; }

    public string? FailedPluginId { get; set; }

    /// <summary>The class the failing handler ended with, as the contract's word.</summary>
    public string? FailureClass { get; set; }

    public string? FailureCode { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Null while running.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>When a handler that failed in a way that may pass is tried again; null while a handler runs.</summary>
    public DateTime? RetryAt { get; set; }
}
