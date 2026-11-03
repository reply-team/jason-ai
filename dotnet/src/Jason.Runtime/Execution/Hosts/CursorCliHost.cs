using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Programs;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless Cursor CLI session, started for one attempt. The runtime composes the whole of the session's shape and
/// leaves a profile only the program and whatever it wants to add at the end.
/// <para>
/// What is written here was read from Cursor's documentation and from the bundle of the installed version
/// 2026.10.22-27dcf5f, and was not run on a signed-in account: no key is held by Jason, and the sign-in is the
/// person's own, made as themselves with <c>agent login</c>. In print mode the host reads its prompt from the whole of
/// stdin when no word for one stands on the command line and stdin is not a terminal, and ignores stdin when a word
/// does, so the brief goes on stdin and no composed argument is a bare word. <c>--new-session-id</c> is not in the
/// host's help text; the bundle validates it as a version-4 UUID, and a profile may not name another one.
/// </para>
/// <para>
/// On Windows the word on the search path is a <c>.cmd</c> that runs a PowerShell script, which in turn runs the newest
/// version's <c>node.exe</c> with its <c>index.js</c>. A script is never started through a shell, so
/// <see cref="ProgramLayout"/> answers what the launcher script itself would start, by the rule that script uses.
/// </para>
/// </summary>
/// <param name="windows">
/// Whether this machine is Windows, which decides whether a launcher script has a layout. The platform this runs on,
/// unless a test says otherwise.
/// </param>
public sealed partial class CursorCliHost(bool windows) : IAgentHost
{
    private readonly CursorCliLauncher _launcher = new(windows);

    public CursorCliHost()
        : this(OperatingSystem.IsWindows())
    {
    }

    /// <summary>The rule that denies every write, which the permission file carries whatever the launch denies.</summary>
    private const string DenyEveryWrite = "Write(**)";

    public AgentHostKind Kind => AgentHostKind.CursorCli;

    /// <summary>
    /// Everything this host reads that would change the shape composed below, plus the ones that would undo the
    /// confinement it rests on. The rule for the second kind: anything that hands the session another account, provider,
    /// endpoint, transport security setting, tool set, prompt, conversation history or directory than the ones the
    /// runtime composed. Read from the help text and the bundle of 2026.10.22-27dcf5f.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "-p", "--print",
        "--output-format",
        "--stream-partial-output",

        // Every one of these makes a session act without asking, which the work directory's deny rules and the
        // allow list were written to make unnecessary, or runs it somewhere the host's own sandbox decides.
        "-f", "--force",
        "--yolo",
        "--auto-review",
        "--trust",
        "--sandbox",
        "--approve-mcps",

        // Where the session works and what else it reads.
        "--workspace",
        "--add-dir",
        "--mode",
        "--plan",
        "--resume",
        "--continue",
        "--new-session-id",
        "--exclude-tools",
        "-w", "--worktree",
        "--worktree-base",
        "--skip-worktree-setup",
        "--plugin-dir",
        "--disable-project-configs",
        "--statsig-overrides",

        // Whose account it is and where it calls: no key is held by Jason, and the sign-in is the person's own.
        "--api-key",
        "--auth-token",
        "-H", "--header",
        "-e", "--endpoint",
        "--base-url",
        "--local-agent-api-key",
        "--enable-bedrock",
        "--enable-local-bedrock",
        "--authless",
        "--agent-endpoint",
        "--website-url",
        "--repo-endpoint",

        // Whether the host checks the certificate of the address it calls.
        "-k", "--insecure",
        "--repo-insecure",

        // Another tool set, prompt, harness, conversation or data directory than the composed ones.
        "--allowed-tools",
        "--system-prompt",
        "--harness",
        "--exclude-workspace-context",
        "--conversation-history-file",
        "--data-dir",
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

        var session = Guid.NewGuid().ToString("D");

        // What the agent may do: run the command that calls this runtime back, read the person's memory files the
        // launch names, one rule per file, and use the tools the person granted that the host has a rule for. It is
        // written into the work directory, beside the deny.
        var (reads, unreadable) = ReadRules(memoryFiles);
        string[] allow =
        [
            $"Shell({ProgramResolver.CliCommandFor(revision.CliCommand)})",
            .. reads,
            .. revision.Tools.Select(ToolRule).OfType<string>(),
        ];

        // Written into the work directory, where a deny is honoured: a tool of the vocabulary the revision does not grant is
        // denied by its rule, where the host has one. A search has none. Writing is denied wholly in the file itself,
        // whatever the revision grants.
        string[] deny = [.. Enum.GetValues<AgentTool>().Except(revision.Tools).Select(ToolRule).OfType<string>()];

        return new HostLaunch(
            [
                .. launch,
                "-p",
                "--output-format",
                "stream-json",

                // The brief is on stdin. Trusting the work directory is what lets the host read the project's
                // permission file there at all; the marker it leaves is one directory of the person's own for
                // each attempt.
                "--trust",
                "--new-session-id",
                session,

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it. And no word
                // of it may be a prompt, which would make the host ignore stdin.
                .. revision.Args,
            ],
            session,
            unreadable,
            deny,
            allow);
    }

    /// <summary>
    /// The host reads a project's permissions from <c>.cursor/cli.json</c> in the directory it works in, and takes the
    /// union of the allow and deny lists of every layer it finds, a deny winning over an allow. So this adds to the
    /// person's own rules and never replaces them. Every write is denied in it, whatever <paramref name="deny"/> holds. The file carries the permissions, the file's version key and the editor mode set off, nothing else.
    /// </summary>
    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);

        var document = new JsonObject
        {
            ["version"] = 1,
            ["editor"] = new JsonObject { ["vimMode"] = false },
            ["permissions"] = new JsonObject
            {
                ["allow"] = new JsonArray([.. allow.Select(rule => (JsonNode)JsonValue.Create(rule)!)]),
                ["deny"] = new JsonArray([.. deny.Prepend(DenyEveryWrite).Distinct(StringComparer.Ordinal).Select(rule => (JsonNode)JsonValue.Create(rule)!)]),
            },
        };

        var directory = Path.Combine(workDir, ".cursor");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "cli.json"), document.ToJsonString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// A fixed sentence when the host ended because the account is not signed in, which it says before it reads stdin,
    /// either as no sign-in or as a stored one the server refused; null for every other end. Nothing of the tail is
    /// carried: it can hold the person's address.
    /// </summary>
    public string? Unavailable(int exitCode, string? stderrTail)
    {
        if (exitCode == 0 || string.IsNullOrEmpty(stderrTail))
        {
            return null;
        }

        // The bundle prints the sign-in command as `agent login`, `cursor agent login` or `agent-local login`, by how
        // the host was started, and a stored sign-in the server refused in its own words.
        return stderrTail.Contains("agent login", StringComparison.OrdinalIgnoreCase)
            || stderrTail.Contains("agent-local login", StringComparison.OrdinalIgnoreCase)
            || stderrTail.Contains("stored authentication is invalid", StringComparison.OrdinalIgnoreCase)
            ? "Cursor CLI is not signed in on this account: run `agent login` as yourself."
            : null;
    }

    /// <summary>
    /// A fetch of any address, which is how the host writes the tool. A search has no rule at all: the host has no token
    /// for it in either list, and whether it runs is the person's own setting.
    /// </summary>
    public string? ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebFetch => "WebFetch(*)",
        AgentTool.WebSearch => null,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    private const string SearchSetting =
        "Cursor CLI has no rule for web search in either list: the person's own autoAcceptWebSearch setting decides whether it runs";

    /// <summary>A search is the one tool this host has and Jason cannot grant, since the person's setting decides it.</summary>
    public string? GrantProblem(AgentTool tool) => tool == AgentTool.WebSearch ? SearchSetting + ", so a grant of it cannot be made true." : null;

    /// <summary>
    /// A deny is written into the permission file as the person wrote it, and the host reads five heads — a shell
    /// command, a read, a write, a fetch and an MCP tool, each with or without what it applies to in parentheses. Web
    /// search in either spelling cannot be denied, and anything else is a word the host would not read as a rule.
    /// </summary>
    public string? DenyProblem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (SearchSpelling().IsMatch(word))
        {
            return SearchSetting + ", so it cannot be denied here.";
        }

        return PermissionRule().IsMatch(word)
            ? null
            : "Cursor CLI reads a rule as Shell(command), Read(path), Write(path), WebFetch(domain) or Mcp(server:tool), with the part in parentheses optional.";
    }

    /// <summary>
    /// The host takes its prompt from stdin only when no word for one stands on its command line, so an argument that is
    /// a bare word, or a value apart from its flag, would be read as the prompt and stdin would be ignored. A flag takes
    /// its value joined by an equals sign, and no double dash is taken. The host's parser also expands a word of one dash
    /// and several letters into a flag per letter and reads the text after a short flag as its value, and reads a lone
    /// dash as the prompt, so a single-dash word is taken only as one letter; a reserved short flag cannot be written
    /// inside another word.
    /// </summary>
    public string? ArgsProblem(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Any(arg => !arg.StartsWith('-') || arg == "--" || arg == "-" || (arg[1] != '-' && arg.Length > 2))
            ? "an argument that is a bare word, a lone dash, a flag's value apart from the flag, or a short flag with more letters or a value after it would be read as the prompt or as other flags and stdin would be ignored: write a flag with its value joined by an equals sign, as --model=name, and no double dash"
            : null;
    }

    [GeneratedRegex(@"^(Shell|Read|Write|WebFetch|Mcp)(\(.+\))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionRule();

    [GeneratedRegex(@"^web[ _-]?search(\(.*\))?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SearchSpelling();

    private static readonly char[] PatternCharacters = ['*', '?', '[', ']', '(', ')', '{', '}'];

    /// <summary>
    /// One rule for each memory file the launch names, in the order of the levels, and one only for a file named twice;
    /// and the levels whose file was named and could not be written as one rule. A level the launch does not know is null
    /// and gets nothing: never a folder, never a pattern.
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
    /// it holds a character the host would read as a pattern, or it is not an absolute path of this machine's kind. On
    /// Windows the drive form is written with forward slashes, which is how the rule was read in the bundle and has not
    /// been run on an account.
    /// </summary>
    private string? ReadRule(string path)
    {
        if (path.IndexOfAny(PatternCharacters) >= 0)
        {
            return null;
        }

        if (windows)
        {
            var drive = path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
            return drive ? $"Read({path.Replace('\\', '/')})" : null;
        }

        return path.StartsWith('/') && !path.Contains('\\', StringComparison.Ordinal) ? $"Read({path})" : null;
    }

    /// <summary>
    /// Whether a file name is one of the two this host's installer gives its launcher script. A name only: a profile
    /// is written before it is known whether the file is there. <see cref="CursorCliLauncher"/> holds the rule, which the
    /// CLI reads too.
    /// </summary>
    public bool NamesItsLauncher(string fileName) => _launcher.NamesItsLauncher(fileName);

    /// <summary>
    /// What the launcher script starts, as a command: the interpreter, then the entry script, by the rule
    /// <see cref="CursorCliLauncher.ProgramLayout"/> states. Null where there is no such layout.
    /// </summary>
    public IReadOnlyList<string>? ProgramLayout(string shimFile) => _launcher.ProgramLayout(shimFile);
}
