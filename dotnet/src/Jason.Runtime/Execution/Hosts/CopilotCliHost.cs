using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless GitHub Copilot CLI run, started for one attempt. The runtime composes the whole shape of the run and
/// leaves a profile only the program and whatever it adds at the end, so no profile can make the run interactive, widen
/// what the role may reach, or hand it a tool that writes a file.
/// <para>
/// Built from the host's documentation for version 1.0.105 and not yet run on an account: every form below is the
/// documented one, and where the documentation does not say what the host does, this says so rather than guess.
/// </para>
/// <para>
/// The prompt is piped on stdin, which the host reads when it is given none of its own, so the command carries no print
/// flag and no word of the brief. Everything that confines the run travels on the command line, in the host's own
/// options: nothing is written into the work directory, and nothing of the person's own configuration is touched. The
/// tool set is the confinement. A tool the host is not offered is unavailable even where a rule allows it, so the role is
/// offered what reads, runs the callback and loads a skill (any the host discovers, not the role's own alone), and none
/// of the tools that write, patch, ask a person or start another agent. The permission rules say what the role may then
/// do: run the callback by its command word, read the memory files the launch names, and use the web fetch tool where
/// the person granted it. A deny wins over an allow in the host, even over an allow-everything, so the web tool not
/// granted and every write are denied as well; the shell is held to the callback's command word by the allow rule alone.
/// </para>
/// <para>
/// Not measured, because the host's pages do not say: whether a rule naming one file lets the role read it outside the
/// work directory, whether a run in a folder the host does not trust stops to ask, whether the work directory's skills
/// load there, what shell redirection by the callback can write, whether version 1.0.105 accepts every flag composed
/// here, and which exit code and text a run that is not signed in ends with. A call no rule allows is denied and the
/// reason goes back to the role, which the host's reference says and no run has shown.
/// </para>
/// </summary>
/// <param name="windows">
/// Whether the paths this host is given are Windows paths and the host offers PowerShell beside Bash. The platform this
/// runs on, unless a test says otherwise.
/// </param>
public sealed class CopilotCliHost(bool windows) : IAgentHost
{
    /// <summary>
    /// The sentence an end of this host becomes when it says it is not signed in. Fixed: the tail it was read from can
    /// carry the person's login or address, and this becomes the attempt's error, which the journal carries.
    /// </summary>
    private const string NotSignedIn = "GitHub Copilot CLI is not signed in on this account: run `copilot login` as yourself.";

    /// <summary>
    /// The command the host's own startup errors recommend to a person who is not signed in. The recommendation is
    /// documented; the exact text of the error around it is not, so only the command is looked for.
    /// </summary>
    private static readonly Regex LoginCommand = new(@"\bcopilot\s+login\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The characters that end or change a rule's argument in a list: the list is comma-separated, the argument is
    /// parenthesised, and a pattern's characters are read as one. A path holding one gets no rule rather than an escaped
    /// guess.
    /// </summary>
    private static readonly char[] RuleCharacters = [',', '(', ')', '*', '?', '[', ']'];

    public CopilotCliHost()
        : this(OperatingSystem.IsWindows())
    {
    }

    public AgentHostKind Kind => AgentHostKind.CopilotCli;

    /// <summary>
    /// Everything this host reads that would change the shape composed below, plus everything that would undo the
    /// confinement it rests on. A profile says which program and what to add; it does not say that the run is interactive,
    /// reports in another format, trusts its folder, allows every tool, path or address, adds a directory, a server or an
    /// agent, or carries a rule of its own in the arguments, where the check made when a profile is written never sees it.
    /// Which options stay open is said by the allowlist in ArgsProblem, not by what this set leaves out: the model, the effort, the credit limit, colour and the client telemetry.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-p", "--prompt",
        "-i", "--interactive",
        "-s", "--silent",
        "--output-format",
        "--stream",
        "--no-ask-user",
        "--no-auto-update",
        "--no-custom-instructions",
        "--disable-builtin-mcps",
        "--session-id",
        "-r", "--resume",
        "--continue",
        "--connect",
        "-n", "--name",

        // Trust and permission: each one either takes the person's confirmation away or allows what the tool set and the
        // rules below keep out.
        "--trust-folder",
        "--allow-all", "--yolo",
        "--allow-all-tools", "--allow-all-paths", "--allow-all-urls",
        "--allow-tool", "--deny-tool",
        "--allow-url", "--deny-url",
        "--available-tools", "--excluded-tools",
        "--add-dir",
        "--assisted-approval",
        "--attachment",

        // What else the run could load or become: servers, plugins, another agent, a plan or autopilot mode, a fleet.
        "--enable-mcp-server", "--additional-mcp-config",
        "--add-github-mcp-toolset", "--enable-all-github-mcp-tools",
        "--allow-all-mcp-server-instructions",
        "--agent", "--plugin-dir",
        "--autopilot", "--max-autopilot-continues",
        "--mode", "--plan",
        "--fleet",
        "--enable-memory",
        "--bash-env",

        // What the launch composes to close a thing the person's settings would leave open, and the documented flags that
        // widen what a run loads, reaches or says: a server, a tool set, a context, a secret, an extension, the screen.
        "--disallow-temp-dir",
        "--sync-session",
        "--no-bash-env",
        "--secret-env-vars",
        "--context",
        "--extension-sdk-path",
        "--disable-mcp-server",
        "--add-github-mcp-tool",
        "--mouse",
        "--banner", "--no-banner",
        "--experimental", "--no-experimental",

        // Where the run happens and where it is written down.
        "--share", "--share-gist",
        "--config-dir",
        "--sandbox", "--no-sandbox",
        "-C",
        "-w", "--worktree",
        "--remote",
        "--acp",
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

        // The runtime's own, so that an attempt can be found again by the id it ran under.
        var session = Guid.NewGuid().ToString("D");

        // The tools the role is offered. None of them writes a file. The shell tools carry the callback and their
        // read, list and stop companions let a long one be read back; the web fetch is there only where it was granted.
        // No search tool is ever offered: the host documents none.
        string[] tools =
            [
                "view", "glob", "grep", "skill",
                "bash", "read_bash", "list_bash", "stop_bash",
                .. windows ? new[] { "powershell", "read_powershell", "list_powershell", "stop_powershell" } : [],
                .. revision.Tools.Contains(AgentTool.WebFetch) ? new[] { "web_fetch" } : [],
            ];

        // What the role may do with them: run the callback by its command word followed by anything, which is one rule
        // for both shell tools; read each memory file the launch names; use the tools the person granted.
        var word = ProgramResolver.CliCommandFor(revision.CliCommand);
        var (reads, unreadable) = ReadRules(memoryFiles);
        string[] allow = [$"shell({word}:*)", .. reads, .. revision.Tools.Select(ToolRule).OfType<string>()];

        // What it may not: the profile's own words as written, then each tool of the vocabulary not granted, then every
        // write. A deny wins over an allow in the host, even over an allow-everything a profile's arguments carried, so
        // the last one costs nothing and holds where the tool set alone might not.
        string[] composed = [.. Enum.GetValues<AgentTool>().Except(revision.Tools).Select(ToolRule).OfType<string>()];
        string[] deny = [.. revision.Deny.Concat(composed).Append("write").Distinct(StringComparer.Ordinal)];

        return new HostLaunch(
            [
                .. launch,
                "--output-format",
                "json",

                // A run nobody is there to answer, that never replaces the person's installed host, and that does not
                // read the person's own instruction files: the memory files its launch names are read by rule instead.
                "--no-ask-user",
                "--no-auto-update",
                "--no-custom-instructions",
                "--disable-builtin-mcps",

                // What the person's own settings would otherwise decide for a run: the system temporary folder is not
                // opened to the role, the host's history of the run is not synced anywhere, the agent mode is the plain
                // one a setting cannot turn into autopilot, and no startup file named by BASH_ENV is read by the shell.
                "--disallow-temp-dir",
                "--sync-session=off",
                "--mode=interactive",
                "--no-bash-env",

                "--session-id",
                session,
                $"--available-tools={string.Join(',', tools)}",
                $"--allow-tool={string.Join(',', allow)}",
                $"--deny-tool={string.Join(',', deny)}",

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it.
                .. revision.Args,
            ],
            session,
            unreadable,
            composed,
            []);
    }

    /// <summary>
    /// Nothing: every rule travels on the command line, and the role's skill is placed by the work directory where the
    /// host reads skills. No file of the host's configuration is written, here or in the person's home.
    /// </summary>
    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);
    }

    /// <summary>
    /// The host saying it cannot run on this machine: a non-zero end whose stderr tail names the command the host
    /// recommends to a person who is not signed in. The recommendation is documented and the error text around it is not,
    /// so it is the command that is looked for, and nothing of the tail is carried.
    /// </summary>
    public string? Unavailable(int exitCode, string? stderrTail) =>
        exitCode != 0 && !string.IsNullOrEmpty(stderrTail) && LoginCommand.IsMatch(stderrTail) ? NotSignedIn : null;

    /// <summary>
    /// The host's permission word for a tool, which allows and denies it alike: <c>url</c> for a fetch of a page. Null for a
    /// search, since there is no search tool in the host's documented tool table: a grant of it is refused where a profile
    /// is written, and a deny of it holds anyway.
    /// </summary>
    public string? ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebSearch => null,
        AgentTool.WebFetch => "url",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    /// <summary>
    /// A deny word is one rule, a kind and an argument in parentheses as the person wrote them, and travels in a
    /// comma-separated list. A word holding a comma is two rules at the host, and a blank word is none. The host reads a
    /// rule's head as one of five kinds, memory, read, shell, url and write, in exactly that case; any other head is an
    /// MCP server's name, which denies nothing the role is offered, so it is refused here rather than stored as a rule
    /// that holds nothing. That covers another host's word for a tool, a kind in another case, and the vocabulary word of
    /// a tool this host has: a deny of the fetch tool by its own name would leave the fetch allowed, so the message names
    /// the word the host reads. The two search words are kept as the person's own: the host has no search tool, so the
    /// deny holds whatever its spelling.
    /// </summary>
    public string? DenyProblem(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return "an empty word is not a rule.";
        }

        if (word.Contains(',', StringComparison.Ordinal))
        {
            return "the host's list is comma-separated, so a word holding a comma is not one rule; give each rule as its own entry.";
        }

        if (word is "web_search" or "WebSearch")
        {
            return null;
        }

        if (word is "web_fetch" or "WebFetch")
        {
            return "the host reads a rule by its kind, and the kind for a fetch is 'url': write 'url' or 'url(<address>)'.";
        }

        var open = word.IndexOf('(', StringComparison.Ordinal);
        var head = open < 0 ? word : word[..open];
        return DenyKinds.Contains(head, StringComparer.Ordinal)
            ? null
            : "the host reads a rule's head as one of memory, read, shell, url or write, in that case, with or without a part in parentheses; any other head is read as an MCP server's name and denies nothing the role is offered.";
    }

    private static readonly string[] DenyKinds = ["memory", "read", "shell", "url", "write"];

    /// <summary>
    /// The options a profile may carry that take a value, each documented for the host and each the person's own: which
    /// model, how hard it reasons, and a soft limit on credits. None changes what the role may touch, how the host is told
    /// to run or where the brief goes. The log level and the log directory are left out: they change what the host writes
    /// outside the work directory, where at a high level the brief and the tool output land in the person's own logs.
    /// </summary>
    private static readonly string[] ValueOptions = ["--model", "--effort", "--reasoning-effort", "--max-ai-credits"];

    /// <summary>The documented options a profile may carry that take no value.</summary>
    private static readonly string[] FlagOptions = ["--no-color", "--disable-client-telemetry"];

    /// <summary>
    /// The host's parser is closed and was not read, so a hidden alias of a flag the runtime composes, a cluster of short
    /// options or a value glued to one cannot be ruled out by a list of what is reserved. A profile's arguments are an
    /// allowlist instead: only the documented options a person owns, each as one argument, <c>--name=value</c> for the ones
    /// that take a value and the bare flag for the two that do not. Anything else is refused, however it is spelled.
    /// </summary>
    public string? ArgsProblem(IReadOnlyList<string> args) =>
        args.FirstOrDefault(arg => !IsOwnedOption(arg)) is { } refused
            ? $"'{refused}' is refused: the host's parser is not read, so only the documented options the person owns are taken: {string.Join(", ", ValueOptions.Select(option => option + "=<value>"))}, {string.Join(", ", FlagOptions)}, each as one argument."
            : null;

    /// <summary>
    /// An option the person owns: the bare flag, or a documented name joined by an equals sign to a value that is neither
    /// empty nor holds a control character. A value may hold a space, which travels as one argument; a control character
    /// ends a line or, as a NUL on Windows, a command line, so none is taken.
    /// </summary>
    private static bool IsOwnedOption(string arg)
    {
        if (Array.IndexOf(FlagOptions, arg) >= 0)
        {
            return true;
        }

        var equals = arg.IndexOf('=', StringComparison.Ordinal);
        return equals > 0
            && equals < arg.Length - 1
            && Array.IndexOf(ValueOptions, arg[..equals]) >= 0
            && !arg.Any(char.IsControl);
    }

    /// <summary>
    /// One rule for each memory file the launch names, in the order of the levels, and one only for a file named twice;
    /// and the levels whose file was named and could not be written as a rule. A level the launch does not know is null
    /// and gets nothing: never a folder, never a pattern.
    /// </summary>
    private (List<string> Rules, List<string> Unreadable) ReadRules(MemoryFileLocations memoryFiles)
    {
        var rules = new List<string>();
        var unreadable = new List<string>();
        foreach (var (level, path) in memoryFiles.NamedLevels())
        {
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
    /// The host's form of "this one file", the <c>read</c> kind with the absolute path as its argument, forward slashes on
    /// Windows; or null where the path cannot be written as one file: it holds a character the list or the argument would
    /// read as something else, or it is not a path the form covers.
    /// </summary>
    private string? ReadRule(string path)
    {
        if (path.IndexOfAny(RuleCharacters) >= 0)
        {
            return null;
        }

        if (windows)
        {
            var drive = path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            return drive ? $"read({path.Replace('\\', '/')})" : null;
        }

        return path.StartsWith('/') && !path.Contains('\\', StringComparison.Ordinal) ? $"read({path})" : null;
    }
}
