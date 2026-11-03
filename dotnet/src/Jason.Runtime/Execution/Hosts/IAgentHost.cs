using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
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
    /// What both halves of Jason know about this host: the program word, where it is installed, where it reads skills
    /// and which instruction file it reads. Its row of <see cref="AgentHosts"/>, never a second copy of it.
    /// </summary>
    AgentHostFacts Facts => AgentHosts.Of(Kind);

    /// <summary>
    /// The flags this host's shape is made of, which a profile may therefore not carry. They are refused where a
    /// profile is written rather than dropped where it is launched: a host parser takes the last value it is
    /// given, so an argument added at the end silently replaces what the runtime composed, and a profile that
    /// could do that could make a session interactive, unconfined or unable to report — the three things every
    /// attempt depends on.
    /// </summary>
    IReadOnlySet<string> ReservedFlags { get; }

    /// <summary>
    /// Why this host refuses a profile's arguments as a whole — a shape its own parser reads otherwise than the runtime
    /// composed it, say — or null where it takes them. Read where a profile is written, once its host is parsed, beside
    /// <see cref="ReservedFlags"/>; the refusal names the host, and this sentence says why.
    /// </summary>
    string? ArgsProblem(IReadOnlyList<string> args) => null;

    /// <summary>
    /// The host's own name for a tool: the allow rule a launch adds for a tool the person granted, and the deny it
    /// writes for one the revision does not grant. Null where the host does not have the tool: a grant of it is refused
    /// where a profile is written, naming the host and the tool, and a deny of it is kept as the person's word, since it
    /// holds anyway. A profile that grants a tool and denies it by this name is refused where it is written.
    /// </summary>
    string? ToolRule(AgentTool tool);

    /// <summary>
    /// Why a grant of a tool this host has is one Jason cannot make true on it — the host decides it from a setting of
    /// the person's own, say — or null where a grant is what it says. A grant this answers for is refused where a profile
    /// is written, naming the host and the tool.
    /// </summary>
    string? GrantProblem(AgentTool tool) => null;

    /// <summary>
    /// Why this host cannot deny what one entry of a profile's deny list names — a tool it has and has no way to take
    /// away, or a word it does not read as a rule — or null where it writes the entry as the person wrote it. A deny this
    /// answers for is refused where a profile is written, naming the host and the entry, never dropped at a launch.
    /// </summary>
    string? DenyProblem(string word) => null;

    /// <summary>
    /// Whether this host is told which session an attempt keeps. Then <see cref="Compose"/> mints a fresh, non-empty id
    /// for every attempt and the provenance records it; a host that mints its own is told none, and the provenance names
    /// no session rather than one the runtime did not give. No launch reads this: the planner records what
    /// <see cref="Compose"/> returned, and the seam's test holds every registered host's <see cref="Compose"/> to this
    /// answer, which is enough for a host that is registered.
    /// </summary>
    bool TakesSessionId => true;

    /// <summary>
    /// The constants this host's child runs with beside the runtime's own four: a fact of the host class that a launch
    /// cannot change, since no revision, memory file, setting or person is an input of it. Never a secret, never the
    /// token, and never a name of the runtime's own or one that starts with its prefix; <see cref="LaunchEnvironment"/>
    /// refuses such a name. The default adds nothing.
    /// </summary>
    IReadOnlyDictionary<string, string> Environment => EmptyEnvironment.Value;

    /// <summary>
    /// Why this host cannot start a run from where this installation stands, or null where it can: a fact about the
    /// machine, not the profile, which the claim decides before a child exists and records as the attempt's error. A
    /// sentence of the host's own that names the path it cannot use, never the host's own message about it.
    /// </summary>
    string? LaunchProblem() => null;

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
    /// <param name="memoryFiles">
    /// The person's memory files this launch names, which the session may read and nothing else outside its work
    /// directory. A level that is null is not readable; a host that cannot write a path as one file leaves it unreadable
    /// rather than guess at a wider rule.
    /// </param>
    HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles);

    /// <summary>
    /// Writes into an attempt's work directory what this host reads there before the child starts: the configuration
    /// that carries <paramref name="allow"/> and <paramref name="deny"/> in the host's own form, and whatever else confines
    /// a session of it. The role's skill is placed afterwards, under <see cref="AgentHostFacts.WorkSkills"/>, by the work
    /// directory and not here.
    /// </summary>
    /// <param name="workDir">The attempt's own directory, which exists; nothing is written outside it.</param>
    /// <param name="allow">
    /// What the launched agent may do, for a host that reads its allow rules from the work directory rather than its
    /// command line: the command word that calls the runtime back and the memory files' paths, never the capability token
    /// or the envelope. Empty for a host whose allow rules travel on the command line.
    /// </param>
    /// <param name="deny">What the launched agent may not do, in this host's own vocabulary.</param>
    void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny);

    /// <summary>
    /// Whether an end of this host without a report says the host cannot run on this machine — not signed in, say —
    /// rather than that an attempt went wrong; then a fixed sentence of this host's own, and otherwise null. The exit
    /// code and the tail of the child's stderr are read to decide and never repeated: a host's stderr can carry the
    /// person's login or address, and the sentence becomes the attempt's error, which the journal and a diagnostic
    /// bundle carry. An attempt ended this way is final, since a repeat would meet the same machine.
    /// </summary>
    string? Unavailable(int exitCode, string? stderrTail);
}

/// <summary>The environment of a host that declares no constants.</summary>
internal static class EmptyEnvironment
{
    public static IReadOnlyDictionary<string, string> Value { get; } = new Dictionary<string, string>();
}
