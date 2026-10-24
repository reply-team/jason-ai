namespace Jason.Runtime.Persistence;

/// <summary>
/// One account a provider's events are read from: a plugin, and the identity of a binding a route names for it — the
/// <c>sha256:</c> of the binding's canonical form, or <see cref="NoBinding"/> for a route that names none. The cursor the
/// plugin last returned lives here, and it is the plugin's own string: the runtime stores it as it came, hands it back as
/// it was, and never reads it.
/// </summary>
/// <remarks>
/// The binding's value is not kept: it is the route's, read from the active snapshot at each read, and journaled where
/// the route was written. A source whose binding has left every route keeps its row and its cursor, and is read from
/// that cursor again once a route names the binding again.
/// </remarks>
public sealed class EventSource
{
    /// <summary>What a source of a route with no binding is known by: no binding is not the identity of an empty one.</summary>
    public const string NoBinding = "none";

    public int Id { get; set; }

    /// <summary><c>evs_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string PluginId { get; set; }

    public required string BindingIdentity { get; set; }

    /// <summary>Where the next read starts, exactly as the plugin returned it with the last page stored; null before any.</summary>
    public string? Cursor { get; set; }

    /// <summary>
    /// The runtime's clock when the page that returned <see cref="Cursor"/> was stored. A cursor the plugin no longer
    /// knows is cleared and this is kept: the next read starts by date from a day before it.
    /// </summary>
    public DateTime? CursorAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the last read of this source started, on the runtime's clock; null before any.</summary>
    public DateTime? ReadStartedAt { get; set; }

    /// <summary>When the last read of this source ended; earlier than <see cref="ReadStartedAt"/> while one is under way.</summary>
    public DateTime? ReadEndedAt { get; set; }

    /// <summary>
    /// When the last read that ended in success started: what the inbound read measures a source's health by. Null until
    /// a read of this source has succeeded.
    /// </summary>
    public DateTime? SucceededAt { get; set; }

    /// <summary>How many reads in a row ended in failure; a read that succeeds clears it.</summary>
    public int Failures { get; set; }

    /// <summary>The code the last read failed with; null after a read that succeeded.</summary>
    public string? LastCode { get; set; }

    /// <summary>
    /// The class of that failure, as the contract's word: <c>transient</c>, <c>permanent</c>, <c>validation</c> or
    /// <c>ambiguous</c>. Never <c>blocked</c>: the events family declares no block, so a blocked answer is one outside
    /// its contract and is kept as <c>result_invalid</c>, ambiguous.
    /// </summary>
    public string? LastClass { get; set; }

    /// <summary>Whether the last read stopped with more to read now — at the most pages a read takes — so the next is due at once.</summary>
    public bool More { get; set; }

    /// <summary>
    /// When somebody last asked for this account to be read sooner, on the runtime's clock; null before anyone has. A
    /// signal after the last read started brings the next forward, never to within a minute of that start.
    /// </summary>
    public DateTime? SignalAt { get; set; }
}
