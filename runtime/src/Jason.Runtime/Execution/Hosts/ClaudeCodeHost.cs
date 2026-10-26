using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless Claude Code session, started for one attempt. The runtime composes the whole of the session's
/// shape and leaves a profile only the program and whatever it wants to add at the end, so no profile can take
/// away the three things every attempt depends on: the session is non-interactive, it says what it is doing
/// while it does it, and it may call the runtime back, read the person's memory files, use the tools the person
/// granted by name, and nothing else; a web tool not granted is denied by name.
/// <para>
/// The allow rules were measured against Claude Code 2.1.309 on Windows. The callback as a bare command word runs
/// through the host's Bash tool and through its PowerShell tool alike. A rule naming one file by its absolute path,
/// in the form <c>Read(//c/Users/…/AGENTS.md)</c> — the drive letter lower-cased, every separator a forward slash —
/// makes exactly that file readable, a space or a comma in the path included, while the file beside it and another
/// campaign's file of the same name stay refused: without one, the mode refuses every read outside the work directory. The
/// host does not escape a rule written by hand, and it splits its allow list at a space or a comma that follows a
/// closing parenthesis, so in this version a path holding <c>[</c>, <c>]</c>, <c>*</c>, <c>?</c>, <c>(</c> or
/// <c>)</c>, or off Windows a backslash, and on Windows a path that does not start at a drive letter, gets no rule:
/// the envelope still names it, the role's read is refused, and the role reports the refusal as it reports any
/// refused tool. Off Windows the form is the host's documented one for an absolute path, not measured there.
/// </para>
/// <para>
/// A granted tool is a rule in the host's own name for it, the bare word: <c>WebSearch</c> and <c>WebFetch</c>, and a
/// tool not granted is a deny of that same word. Run against Claude Code 2.1.310 on Windows, in a work directory
/// prepared as the runtime prepares one, both ran with the two allow words. Without them and without a deny, the mode
/// refused a search and a fetch of an ordinary page, but let through a fetch of a page on a documentation list of its
/// own; with the bare deny in the work directory the host did not offer the tool at all.
/// </para>
/// <para>
/// The version a profile was checked against is recorded on the revision and reported, never enforced: a host is
/// installed and updated by a person, not by this runtime.
/// </para>
/// </summary>
/// <param name="windows">
/// Whether the paths this host is given are Windows paths, which decides how a path is written as a rule. The
/// platform this runs on, unless a test says otherwise.
/// </param>
public sealed class ClaudeCodeHost(bool windows) : IAgentHost
{
    /// <summary>
    /// The characters the host reads in a path rule as something other than themselves: a pattern's, and the
    /// parentheses its allow list is split by — after a closing one, a space or a comma ends the rule, so a folder named
    /// <c>Acme (EU) Ltd</c> becomes a rule for part of its name, and one named <c>) notes</c> a rule for the folder above
    /// it. Off Windows the backslash, which escapes a pattern's characters, is one more; on Windows it is the separator
    /// and becomes a forward slash.
    /// </summary>
    private static readonly char[] PatternCharacters = ['*', '?', '[', ']', '(', ')'];

    public ClaudeCodeHost()
        : this(OperatingSystem.IsWindows())
    {
    }

    public AgentHostKind Kind => AgentHostKind.ClaudeCode;

    /// <summary>
    /// Everything this host reads that would change the shape composed below, plus the ones that would undo the
    /// confinement it rests on. A profile says which program and what to add; it does not get to say that the
    /// session is interactive, that it reports in another format, that it may do anything it likes, or that the
    /// per-attempt work directory is inert. Verified against Claude Code 2.1.275, and <c>--attach-serve</c> against
    /// 2.1.309.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-p", "--print",
        "--output-format",
        "--include-partial-messages",
        "--verbose",
        "--allowed-tools", "--allowedTools",
        // The host's own deny flag: a deny in the arguments would take a grant away where the check made when a
        // profile is written never sees it, so the profile's deny is the one way a deny arrives.
        "--disallowed-tools", "--disallowedTools",
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

        // The confinement below is only as good as what cannot be added after it: a profile that could name a
        // settings file, a plugin directory or source, another MCP configuration, a second working directory, or
        // turn the slash commands off, could hand a launched session the reach these flags exist to take away.
        "--setting-sources",
        "--strict-mcp-config",
        "--mcp-config",
        "--plugin-dir",
        "--plugin-url",
        "--settings",
        "--add-dir",
        "--disable-slash-commands",

        // A session served for attaching reads the work directory's settings without claudeMdExcludes.
        "--attach-serve",
    };

    public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(memoryFiles);
        if (launch.Count == 0)
        {
            throw new ArgumentException("A launch names at least the program to start.", nameof(launch));
        }

        // The runtime's own, so that an attempt can be found again by the session it ran in. A host that minted
        // its own would leave the runtime with a session id it could only learn by reading the transcript.
        var session = Guid.NewGuid().ToString("D");

        // What the agent may do: run the command that calls this runtime back, through whichever shell tool it
        // reaches for — on Windows the host offers PowerShell beside Bash, and a callback refused there ends the
        // attempt without its result; where the host has no such tool the rule approves nothing — and read the
        // person's memory files the launch names, one rule per file. It travels on the command line because a work
        // directory the runtime created is not a workspace the host trusts, and an allow rule written into an
        // untrusted directory is ignored without being reported. What the agent may not do travels the other way,
        // in that same directory — see WorkDirectory. Last come the tools the person granted on the revision, one
        // bare word each in the host's own name, and only those.
        var word = ProgramResolver.CliCommandFor(revision.CliCommand);
        var (reads, unreadable) = ReadRules(memoryFiles);
        string[] allow = [$"Bash({word} *)", $"PowerShell({word} *)", .. reads, .. revision.Tools.Select(ToolRule)];

        // The grant is closed by default: a tool of the vocabulary the revision does not grant is denied by the same
        // bare name, which the host reads as not offering the tool at all. Leaving it unlisted is not enough — the
        // mode still let a fetch of the host's own documentation list through without a rule. The deny travels with
        // the profile's own, in the work directory, where a bare name was seen to take the tool away.
        string[] deny = [.. Enum.GetValues<AgentTool>().Except(revision.Tools).Select(ToolRule)];

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

                // A session that says what it is doing while it does it, which this version refuses to do in
                // print mode unless it is also told to be verbose: without the second flag the first is an
                // argument error and the session never starts.
                "--include-partial-messages",
                "--verbose",

                // The callback and the reads, as one list: the host reads the flag as a list that the next flag
                // ends, which is why it is never last. A rule for the file-editing tools was tried against
                // 2.1.275 and granted neither writing nor editing, so it is not composed: an unproven rule on
                // the command line reads afterwards as a permission the role had.
                "--allowed-tools",
                .. allow,
                "--permission-mode",
                "dontAsk",
                "--session-id",
                session,

                // The settings of the person who installed the host are not this role's settings. Without these
                // two the session inherits that person's whole configuration — their plugins, their hooks and
                // their MCP servers — none of which the work directory's deny rules reach, and none of which
                // anybody chose for this work item. With them the session keeps the built-in tools and the work
                // directory's own skill, which is exactly what the runtime put there.
                "--setting-sources",
                "project",
                "--strict-mcp-config",

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it.
                .. revision.Args,
            ],
            session,
            unreadable,
            deny);
    }

    /// <summary>
    /// The host's own name for a tool, which is the whole rule and carries no pattern: the allow of a granted tool and
    /// the deny of one the revision does not grant.
    /// </summary>
    public string ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebSearch => "WebSearch",
        AgentTool.WebFetch => "WebFetch",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    /// <summary>
    /// One rule for each memory file the launch names, in the order of the levels, and one only for a file named
    /// twice; and the levels whose file was named and could not be written as a rule. A level the launch does not know
    /// is null and gets nothing: never a folder, never a pattern.
    /// </summary>
    private (List<string> Rules, List<string> Unreadable) ReadRules(MemoryFileLocations memoryFiles)
    {
        var rules = new List<string>();
        var unreadable = new List<string>();
        foreach (var (level, path) in new[] { ("client", memoryFiles.Client), ("project", memoryFiles.Project), ("campaign", memoryFiles.Campaign) })
        {
            if (path is null)
            {
                continue;
            }

            if (ReadRule(path) is not { } rule)
            {
                unreadable.Add(level);
            }
            else if (!rules.Contains(rule, StringComparer.Ordinal))
            {
                rules.Add(rule);
            }
        }

        return (rules, unreadable);
    }

    /// <summary>
    /// The host's form of "this one file, by its absolute path", or null where the path cannot be written as one file:
    /// it holds a character the host would read as a pattern, or it is not a path the form covers.
    /// </summary>
    private string? ReadRule(string path)
    {
        if (path.IndexOfAny(PatternCharacters) >= 0)
        {
            return null;
        }

        if (windows)
        {
            // The host matches a Windows path in its POSIX form, C:\Users becoming /c/Users, so the rule is written
            // that way: two slashes for "from the root", the drive letter lower-cased whichever way the path spells
            // it. A path that does not start at a drive — a share, a device path, a relative one — has no form known.
            var drive = path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
            return drive ? $"Read(//{char.ToLowerInvariant(path[0])}/{path[3..].Replace('\\', '/')})" : null;
        }

        return path.StartsWith('/') && !path.Contains('\\', StringComparison.Ordinal)
            ? $"Read(//{path.TrimStart('/')})"
            : null;
    }
}
