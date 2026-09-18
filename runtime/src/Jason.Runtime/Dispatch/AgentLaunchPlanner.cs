using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The per-item half of an agent launch, resolved at the claim: what the child is started with, what it may not
/// do where it runs, and the word it calls home by.
/// </summary>
public sealed record AgentPlan(
    IReadOnlyList<string> Command,
    IReadOnlyList<string> Deny,
    string CliCommand,
    AgentProvenanceDto Provenance);

/// <summary>What the claim decided about launching one agent work item.</summary>
public abstract record AgentLaunchDecision
{
    public sealed record Ready(AgentPlan Plan) : AgentLaunchDecision;

    /// <summary>No profile anywhere: the role's own entry command runs it, and the attempt says that is what happened.</summary>
    public sealed record Legacy(AgentProvenanceDto Provenance) : AgentLaunchDecision;

    public sealed record Refused(string Code, string Message) : AgentLaunchDecision;
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
/// </summary>
public sealed class AgentLaunchPlanner(AgentPreflight preflight, ProgramResolver programs, IEnumerable<IAgentHost> hosts)
{
    public async Task<AgentLaunchDecision> PlanAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);

        var verdict = await preflight.DecideAsync(db, item, cancellationToken).ConfigureAwait(false);
        switch (verdict)
        {
            case AgentVerdict.Refused refused:
                return new AgentLaunchDecision.Refused(refused.Code, refused.Message);

            case AgentVerdict.Legacy:
                return new AgentLaunchDecision.Legacy(new AgentProvenanceDto(ProfileResolutionSource.RoleEntryCommand));

            case AgentVerdict.Launch launch:
                return Compose(launch);

            default:
                throw new InvalidOperationException($"Unknown agent verdict '{verdict.GetType().Name}'.");
        }
    }

    private AgentLaunchDecision Compose(AgentVerdict.Launch launch)
    {
        var revision = launch.Revision;

        // A machine fact, decided here rather than by a child that fails to start: the profile names a program,
        // and either this machine has it or the work cannot run and says which program was missing.
        if (programs.Resolve(revision.Program) is not { } program)
        {
            return new AgentLaunchDecision.Refused(
                AttemptErrors.HostNotAvailable,
                $"The execution profile '{launch.Profile.Name}' runs '{revision.Program}', which is not on this machine.");
        }

        // One implementation per host, and no fallback: a profile written against a host this build does not
        // know is not something to guess about.
        var host = hosts.LastOrDefault(candidate => candidate.Kind == revision.Host);
        if (host is null)
        {
            return new AgentLaunchDecision.Refused(
                AttemptErrors.HostNotAvailable,
                $"The execution profile '{launch.Profile.Name}' names a host this build cannot start.");
        }

        var composed = host.Compose(revision, program);
        var provenance = new AgentProvenanceDto(
            launch.Source,
            launch.Profile.PublicId,
            launch.Profile.Name,
            revision.Number,
            launch.LineageRevision,
            revision.Host,
            program,
            composed.Command,
            revision.HostVersionVerified,
            composed.SessionId);

        return new AgentLaunchDecision.Ready(new AgentPlan(
            composed.Command,
            revision.Deny,
            ProgramResolver.CliCommandFor(revision.CliCommand),
            provenance));
    }
}
