using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless Claude Code session, started for one attempt. The runtime composes the whole of the session's
/// shape and leaves a profile only the program and whatever it wants to add at the end, so no profile can take
/// away the three things every attempt depends on: the session is non-interactive, it says what it is doing
/// while it does it, and it may call the runtime back and nothing else.
/// <para>
/// Verified against Claude Code 2.1.275 with a bare command word in the allow rule; a path inside an allow
/// pattern has not been verified. The version a profile was checked against is recorded on the revision and
/// reported, never enforced: a host is installed and updated by a person, not by this runtime.
/// </para>
/// </summary>
public sealed class ClaudeCodeHost : IAgentHost
{
    public AgentHostKind Kind => AgentHostKind.ClaudeCode;

    /// <summary>
    /// Everything this host reads that would change the shape composed below, plus the ones that would undo the
    /// confinement it rests on. A profile says which program and what to add; it does not get to say that the
    /// session is interactive, that it reports in another format, that it may do anything it likes, or that the
    /// per-attempt work directory is inert. Verified against Claude Code 2.1.275.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-p", "--print",
        "--output-format",
        "--include-partial-messages",
        "--allowed-tools", "--allowedTools",
        "--permission-mode",
        "--session-id",
        "--restricted",
        "--tools",
        "--dangerously-skip-permissions",
        "--allow-dangerously-skip-permissions",
        "-r", "--resume",
        "-c", "--continue",
        "--fork-session",
        "--bare",
        "--safe-mode",
    };

    public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(launch);
        if (launch.Count == 0)
        {
            throw new ArgumentException("A launch names at least the program to start.", nameof(launch));
        }

        // The runtime's own, so that an attempt can be found again by the session it ran in. A host that minted
        // its own would leave the runtime with a session id it could only learn by reading the transcript.
        var session = Guid.NewGuid().ToString("D");

        // What the agent may do, as one rule: run the command that calls this runtime back. It travels on the
        // command line because a work directory the runtime created is not a workspace the host trusts, and an
        // allow rule written into an untrusted directory is ignored without being reported. What the agent may
        // not do travels the other way, in that same directory — see WorkDirectory.
        var allow = $"Bash({ProgramResolver.CliCommandFor(revision.CliCommand)} *)";

        // Two flags are deliberately absent. --restricted would make the per-attempt work directory inert: it
        // reads no project settings file and loads no project skill, so neither the deny rules nor the role's
        // skill would reach the session. --tools would narrow the session to the tools it names, and a skill is
        // reached through a tool, so naming tools costs the role its skills.
        return new HostLaunch(
            [
                .. launch,
                "-p",
                "--output-format",
                "stream-json",
                "--include-partial-messages",
                "--allowed-tools",
                allow,
                "--permission-mode",
                "dontAsk",
                "--session-id",
                session,

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it.
                .. revision.Args,
            ],
            session);
    }
}
