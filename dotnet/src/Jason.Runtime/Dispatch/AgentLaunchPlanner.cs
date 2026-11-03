using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Programs;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Harness;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The per-item half of an agent launch, resolved at the claim: what the child is started with, what it may not
/// do where it runs, the word it calls home by, and the memory files it was given leave to read.
/// </summary>
/// <param name="MemoryFiles">The person's memory files as the claim found them, which the envelope names as they are.</param>
/// <param name="Unreadable">The levels whose file the claim named and the host could not write as a read rule.</param>
/// <param name="ProjectPublicId">The public id of the project whose folder the memory files were placed under; null for provider work, which has no launch decision.</param>
/// <param name="Host">The host the revision names, whose layout the attempt's work directory is given.</param>
public sealed record AgentPlan(
    IReadOnlyList<string> Command,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Allow,
    string CliCommand,
    AgentProvenanceDto Provenance,
    MemoryFileLocations MemoryFiles,
    IReadOnlyList<string> Unreadable,
    string? ProjectPublicId,
    AgentHostKind Host);

/// <summary>What the claim decided about launching one agent work item.</summary>
public abstract record AgentLaunchDecision
{
    public sealed record Ready(AgentPlan Plan) : AgentLaunchDecision;

    /// <summary>
    /// No profile anywhere: the role's own entry command runs it, and the attempt says that is what happened. The
    /// memory files are decided here all the same, so the envelope names what the claim found whoever composed the
    /// command.
    /// </summary>
    public sealed record Legacy(AgentProvenanceDto Provenance, MemoryFileLocations MemoryFiles, string? ProjectPublicId) : AgentLaunchDecision;

    /// <param name="Decided">
    /// How far the choice got before it was refused, where a level did name a profile. Null where nothing was
    /// chosen at all — which is itself the answer for work whose ancestry could not be read.
    /// </param>
    public sealed record Refused(string Code, string Message, AgentProvenanceDto? Decided = null) : AgentLaunchDecision;
}

/// <summary>
/// Everything that has to be true before an agent host is started, asked in one place and before a child exists:
/// which profile runs this work, whether it is still in service, whether the program it names is on this machine,
/// and what the command line then is.
/// <para>
/// Each answer is one reason, and the attempt keeps it. Nothing here is retried: a profile that does not exist
/// will not exist on the next scan either, and an item that quietly went back in the queue would look claimable
/// for ever while never running.
/// </para>
/// <para>
/// The person's memory files are decided here too, once: where the command that may read them is composed and the
/// provenance that records it is written. They travel from here to the envelope, so the files a role is told about
/// and the files its rules let it read are one decision, and the envelope never knows better than the provenance.
/// </para>
/// </summary>
public sealed class AgentLaunchPlanner(AgentPreflight preflight, ProgramResolver programs, IEnumerable<IAgentHost> hosts, HarnessFolders folders)
{
    /// <param name="globalDefault">The configured default profile, read once for the whole scan.</param>
    public async Task<AgentLaunchDecision> PlanAsync(
        JasonDbContext db,
        WorkItem item,
        string? globalDefault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);

        var verdict = await preflight.DecideAsync(db, item, globalDefault, cancellationToken).ConfigureAwait(false);
        switch (verdict)
        {
            case AgentVerdict.Refused refused:
                return new AgentLaunchDecision.Refused(refused.Code, refused.Message, Decided(refused));

            case AgentVerdict.Legacy:
            {
                var (legacyFiles, legacyProject) = await MemoryFilesAsync(db, item, cancellationToken).ConfigureAwait(false);
                return new AgentLaunchDecision.Legacy(
                    new AgentProvenanceDto(ProfileResolutionSource.RoleEntryCommand),
                    legacyFiles,
                    legacyProject);
            }

            case AgentVerdict.Launch launch:
            {
                var (files, project) = await MemoryFilesAsync(db, item, cancellationToken).ConfigureAwait(false);
                return Compose(launch, files, project);
            }

            default:
                throw new InvalidOperationException($"Unknown agent verdict '{verdict.GetType().Name}'.");
        }
    }

    /// <summary>
    /// Where the person's memory files are, as this claim finds the root and the records: the campaign's folder and its
    /// project's, as recorded now, and the root by its marker. Never a word of what the files say.
    /// <para>
    /// The project's public id comes back beside the files: it is the project these folders were placed under, so
    /// whoever needs the project for this attempt takes it from this decision and not from a navigation the claim
    /// happens to hold.
    /// </para>
    /// </summary>
    private async Task<(MemoryFileLocations Files, string? ProjectPublicId)> MemoryFilesAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        var campaign = item.Campaign ?? throw new InvalidOperationException($"Work item '{item.PublicId}' was claimed without its campaign.");
        var project = db.Entry(campaign).Reference(c => c.Project);
        if (!project.IsLoaded)
        {
            await project.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        return (folders.MemoryFiles(campaign.Project?.FolderPath, campaign.FolderPath), campaign.Project?.PublicId);
    }

    private AgentLaunchDecision Compose(AgentVerdict.Launch launch, MemoryFileLocations memoryFiles, string? projectPublicId)
    {
        var revision = launch.Revision;

        // One implementation per host, and no fallback: a profile written against a host this build does not
        // know is not something to guess about. It is decided first, because the program is resolved for the host:
        // a host that is installed as a launcher script says what that script starts.
        var host = hosts.LastOrDefault(candidate => candidate.Kind == revision.Host);
        if (host is null)
        {
            return new AgentLaunchDecision.Refused(
                AttemptErrors.HostNotAvailable,
                $"The execution profile '{launch.Profile.Name}' names a host this build cannot start.",
                Chosen(launch));
        }

        // A machine fact, decided here rather than by a child that fails to start: the profile names a program,
        // and either this machine has it or the work cannot run and says which program was missing.
        // A script is the other way to have no program: a profile stored before the rule keeps its program, and the
        // refusal names what it is rather than calling it missing.
        // A host answers what its own install starts, and its code is not the claim's: nothing it throws leaves the
        // claim. It ends this item as a program that cannot be started does, in a sentence that carries no word of the
        // exception, and the scan goes on to the next candidate.
        IReadOnlyList<string>? resolved;
        try
        {
            resolved = programs.Resolve(revision.Program, host);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AgentLaunchDecision.Refused(
                AttemptErrors.HostNotAvailable,
                $"The execution profile '{launch.Profile.Name}' runs '{revision.Program}', whose installation could not be read on this machine.",
                Chosen(launch));
        }

        if (resolved is not { Count: > 0 } startedAs)
        {
            return new AgentLaunchDecision.Refused(
                AttemptErrors.HostNotAvailable,
                NotStartable(launch.Profile.Name, revision.Program, host),
                Chosen(launch));
        }

        var composed = host.Compose(revision, startedAs, memoryFiles);
        var provenance = new AgentProvenanceDto(
            launch.Source,
            launch.Profile.PublicId,
            launch.Profile.Name,
            revision.Number,
            launch.LineageRevision,
            revision.Host,

            // The file this machine actually starts, which is the shim's interpreter where a name resolved to
            // one: the whole command is in the arguments beside it.
            startedAs[0],
            composed.Command,
            revision.HostVersionVerified,

            // As the host composed it, never checked here against TakesSessionId: only the seam's test reads that
            // member, holding every registered host's Compose to it, which is enough for a host that is registered.
            composed.SessionId,
            revision.Tools);

        // The person's deny rules first, then what the host denies on the revision's account, each rule once.
        return new AgentLaunchDecision.Ready(new AgentPlan(
            composed.Command,
            [.. revision.Deny.Concat(composed.Deny).Distinct(StringComparer.Ordinal)],

            // What the host allows through the work directory, beside the deny and carried where it is.
            composed.Allow,
            ProgramResolver.CliCommandFor(revision.CliCommand),
            provenance,
            memoryFiles,
            composed.Unreadable,
            projectPublicId,
            revision.Host));
    }

    /// <summary>
    /// Why a profile's program cannot be started: the script it names, the launcher script whose interpreter was not
    /// found, or nothing at all by that name. A host installed as a launcher script is told how to point at it.
    /// </summary>
    private static string NotStartable(string profile, string program, IAgentHost host)
    {
        var launcher = host.Facts.CommandWord + ".cmd";
        var named = $"The execution profile '{profile}' runs '{program}'";

        // The launcher sentence is for a program that is the launcher: its bare word, or a path to one of its files. A script
        // that is something else is a script a shell would run, whichever host it is under.
        var isTheLauncher = host.NamesItsLauncher(launcher)
            && (host.NamesItsLauncher(Path.GetFileName(program.Trim()))
                || (!program.Contains('/', StringComparison.Ordinal) && !program.Contains('\\', StringComparison.Ordinal)
                    && host.NamesItsLauncher(program.Trim() + ".cmd")));
        if (isTheLauncher)
        {
            return $"{named}, which this machine cannot start: it is not installed, or nothing was found to start. "
                + "This host is installed as a launcher script, and Jason starts what that script starts, a node.exe and an index.js in its newest version directory, never the script itself. "
                + $"If it is installed, name the launcher by its full path in the profile: --program <full path of {launcher}>, "
                + "which also starts an installation the search path of this runtime does not reach.";
        }

        return ProgramResolver.NamesShellScript(program)
            ? $"{named}, {ProgramLookup.ShellScript}: {ProgramLookup.ProgramsNotShells}. "
              + "Name the program itself in the profile, or a bare name without its extension, which Jason looks up on the search path (on Windows as an .exe, a .com or npm's own shim)."
            : $"{named}, which is not on this machine.";
    }

    /// <summary>What the pre-flight had decided when it refused: the level and the profile, and no more.</summary>
    private static AgentProvenanceDto? Decided(AgentVerdict.Refused refused) =>
        refused.Source is { } source
            ? new AgentProvenanceDto(source, refused.ProfileId, refused.ProfileName, refused.ProfileRevision)
            : null;

    /// <summary>
    /// The same for a refusal of this class: the profile resolved and the machine or the build refused it, so
    /// everything but the launch itself is known and worth keeping, the tools the revision granted among it.
    /// </summary>
    private static AgentProvenanceDto Chosen(AgentVerdict.Launch launch) =>
        new(
            launch.Source,
            launch.Profile.PublicId,
            launch.Profile.Name,
            launch.Revision.Number,
            launch.LineageRevision,
            launch.Revision.Host,
            launch.Revision.Program,
            Tools: launch.Revision.Tools);
}
