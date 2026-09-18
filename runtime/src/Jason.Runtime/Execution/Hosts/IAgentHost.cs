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
    /// The flags this host's shape is made of, which a profile may therefore not carry. They are refused where a
    /// profile is written rather than dropped where it is launched: a host parser takes the last value it is
    /// given, so an argument added at the end silently replaces what the runtime composed, and a profile that
    /// could do that could make a session interactive, unconfined or unable to report — the three things every
    /// attempt depends on.
    /// </summary>
    IReadOnlySet<string> ReservedFlags { get; }

    /// <summary>
    /// The command one attempt of <paramref name="revision"/> is started with. Called once per attempt: the
    /// session it names is minted here and belongs to that attempt alone.
    /// </summary>
    /// <param name="revision">The profile as it was frozen — the arguments, the command word, nothing secret.</param>
    /// <param name="launch">
    /// How this machine starts the revision's program: usually one path, and two where the name resolved to a
    /// shim whose interpreter and entry script are what actually run. Resolving it is a separate step on
    /// purpose — a host that is not installed is a fact about the machine, decided before a child exists rather
    /// than discovered by failing to start one.
    /// </param>
    HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch);
}
