using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One complete statement of how a host is launched, frozen at the moment it was written. Editing a profile
/// appends another of these rather than changing this one: an attempt records the revision it ran under, so a
/// revision that could be rewritten would let a later edit change what an earlier attempt says it did.
/// <para>
/// A revision is whole. An edit that names one field copies the rest from the current revision, so reading
/// revision 7 never means reconstructing it from six earlier rows.
/// </para>
/// </summary>
public sealed class ExecutionProfileRevision
{
    public int Id { get; set; }

    public int ProfileId { get; set; }

    public ExecutionProfile? Profile { get; set; }

    /// <summary>1 for the first revision of the profile, and up from there.</summary>
    public int Number { get; set; }

    /// <summary>Which host this describes. A closed vocabulary: an unknown value is refused when it is written.</summary>
    public AgentHostKind Host { get; set; }

    /// <summary>The program to start: an absolute path, or a name resolved on PATH.</summary>
    public required string Program { get; set; }

    /// <summary>What the profile adds after the arguments the runtime composes for the host.</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// What the launched agent may not do, in the host's own vocabulary. It travels in the per-attempt work
    /// directory, which is the only half of a policy a work directory can carry.
    /// </summary>
    public List<string> Deny { get; set; } = [];

    /// <summary>
    /// The bare command word the launched agent calls home with. Null means the runtime's own executable name,
    /// which is what the runtime puts within the child's reach.
    /// </summary>
    public string? CliCommand { get; set; }

    /// <summary>The host version this profile was verified against. Recorded and reported, never enforced.</summary>
    public string? HostVersionVerified { get; set; }

    public ActorType CreatedByType { get; set; }

    public string? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }
}
