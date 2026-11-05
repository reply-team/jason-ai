namespace Jason.Runtime.Persistence;

/// <summary>
/// Where one subscribed journal kind's chain stands: every line of the kind with an id above <see cref="AfterLine"/> is
/// due. A kind has a row while a plugin in the active snapshot is granted it, made at the load that gave it its first
/// subscriber, at the journal's head, and removed at the load that left it none.
/// </summary>
/// <remarks>
/// Every move bumps <see cref="Revision"/>, and a move is written only where the revision is still the one read: a
/// chain whose position moved under it — a rewind, a removal — learns so from the update that matched nothing.
/// </remarks>
public sealed class SubscriptionCursor
{
    public int Id { get; set; }

    /// <summary>The journal kind, one row per kind.</summary>
    public required string Kind { get; set; }

    /// <summary>A journal id, and no reference to it: lines above it are due.</summary>
    public int AfterLine { get; set; }

    public long Revision { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
