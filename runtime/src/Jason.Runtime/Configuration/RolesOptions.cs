namespace Jason.Runtime.Configuration;

/// <summary>
/// How a role is launched when nothing more specific says: the execution profile every role's work falls back
/// to, the command used where no profile is configured at all, and the two bounds the launcher holds a child to.
/// </summary>
public sealed class RolesOptions
{
    public const string Section = "Roles";

    /// <summary>
    /// The execution profile agent work uses when neither the item, nor its campaign or role, nor its causal
    /// lineage names one. A name, resolved when work is claimed: settings cannot see the database, so a name
    /// nothing answers is a visible refusal on the work item rather than a refusal to start.
    /// </summary>
    public string? DefaultExecutionProfile { get; set; }

    /// <summary>The command used for any role without one of its own, where no profile is configured anywhere.</summary>
    public List<string> DefaultEntryCommand { get; set; } = [];

    /// <summary>
    /// How much of a child's standard output is kept. A host that streams its reasoning writes a great deal, and
    /// nothing cleans a work directory up; past this the transcript is cut and says so. No outcome depends on
    /// it — a result reaches the runtime through the API, never through standard output.
    /// </summary>
    public int MaxStdoutBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// How large a role's skill may be. Past it the attempt is refused rather than run: a role that was given a
    /// skill and did not receive it would do the job untaught, at the price of a real launch, and the only trace
    /// would be a log line nobody is reading at the time. A role with no skill at all is a different thing and
    /// runs normally — nothing was configured, so nothing is missing.
    /// </summary>
    public int MaxSkillBytes { get; set; } = 1024 * 1024;
}
