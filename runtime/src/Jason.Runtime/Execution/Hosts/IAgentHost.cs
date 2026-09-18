using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// How one kind of agent host is started. It is the only place that knows a host's own flags: the launcher
/// starts a program, writes one envelope and watches it end, and a profile says which program and what to add at
/// the end. Everything between the two is composed here, so every attempt of a host is confined and reports in
/// the same way whatever profile ran it.
/// </summary>
public interface IAgentHost
{
    /// <summary>The host this composes for. One implementation per value of the enum, and no fallback.</summary>
    AgentHostKind Kind { get; }

    /// <summary>
    /// The command one attempt of <paramref name="revision"/> is started with. Called once per attempt: the
    /// session it names is minted here and belongs to that attempt alone.
    /// </summary>
    /// <param name="revision">The profile as it was frozen — the arguments, the command word, nothing secret.</param>
    /// <param name="program">
    /// The revision's program as this machine answered for it. Resolving it is a separate step on purpose: a
    /// host that is not installed is decided before a child exists, and is a fact about the machine rather than
    /// a failed run.
    /// </param>
    HostLaunch Compose(ExecutionProfileRevision revision, string program);
}
