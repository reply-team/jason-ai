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

    /// <summary>The runtime's clock when the page that returned <see cref="Cursor"/> was stored.</summary>
    public DateTime? CursorAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
