namespace Jason.Runtime.Persistence;

/// <summary>
/// A named, non-secret description of a launchable agent host: which program starts it, what it may not do, and
/// what the runtime tells it to call home with. It holds no credential and has nowhere to put one — the host the
/// profile names is already authenticated by the person who installed it.
/// <para>
/// The row carries only what is meant to move: which revision is current, and whether the profile is disabled.
/// Everything behavioural lives in <see cref="ExecutionProfileRevision"/>, which never changes, because an
/// attempt names the revision it ran under for ever.
/// </para>
/// </summary>
public sealed class ExecutionProfile
{
    public int Id { get; set; }

    /// <summary><c>prf_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    /// <summary>How work names this profile: unique, and the same shape as a role's name.</summary>
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>The revision a claim resolves to now. An edit appends a revision and moves this number.</summary>
    public int CurrentRevision { get; set; }

    /// <summary>
    /// Set when the profile is taken out of service. Profiles are never deleted: attempts name their revisions
    /// for ever, and a row somebody's history points at is not the runtime's to remove.
    /// </summary>
    public DateTime? DisabledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ExecutionProfileRevision> Revisions { get; } = [];
}
