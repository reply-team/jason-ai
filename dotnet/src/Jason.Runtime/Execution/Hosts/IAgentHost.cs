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
    /// The host's own name for a tool: the allow rule a launch adds for a tool the person granted, and the deny it
    /// writes for one the revision does not grant. A profile that grants a tool and denies it by this name is refused
    /// where it is written.
    /// </summary>
    string ToolRule(AgentTool tool);

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
    /// that carries <paramref name="deny"/> in the host's own form, and whatever else confines a session of it. The role's
    /// skill is placed afterwards, under <see cref="AgentHostFacts.WorkSkills"/>, by the work directory and not here.
    /// </summary>
    /// <param name="workDir">The attempt's own directory, which exists; nothing is written outside it.</param>
    /// <param name="deny">What the launched agent may not do, in this host's own vocabulary.</param>
    void WriteWorkDirectory(string workDir, IReadOnlyList<string> deny);

    /// <summary>
    /// Whether an end of this host without a report says the host cannot run on this machine — not signed in, say —
    /// rather than that an attempt went wrong; then a fixed sentence of this host's own, and otherwise null. The exit
    /// code and the tail of the child's stderr are read to decide and never repeated: a host's stderr can carry the
    /// person's login or address, and the sentence becomes the attempt's error, which the journal and a diagnostic
    /// bundle carry. An attempt ended this way is final, since a repeat would meet the same machine.
    /// </summary>
    string? Unavailable(int exitCode, string? stderrTail);

    /// <summary>
    /// Whether a file name is the launcher script this host's own installer writes, which a profile may name by its
    /// full path although it is a script: some hosts are installed as a script that starts an interpreter, and the host
    /// knows both which names those are and what they start. A name only, answered before it is known whether the file
    /// exists. No host has one by default, and a script is then refused as it always was.
    /// </summary>
    bool NamesItsLauncher(string fileName) => false;

    /// <summary>
    /// What this host's launcher script starts, as a command, when <paramref name="shimFile"/> is that script: the
    /// interpreter and the entry script it would run, found by the rule the script itself uses. Null where the file is
    /// not this host's launcher, where this is not a platform that has one, or where nothing is installed to start.
    /// The file system is the only thing read, and nothing is remembered between calls.
    /// </summary>
    IReadOnlyList<string>? ProgramLayout(string shimFile) => null;
}
