using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// Gemini CLI, as its documentation and source describe version 0.66.0. Nothing here was run on an account: Jason
/// holds no key, and the sign-in is the person's own.
/// <para>
/// A non-interactive run with no prompt flag reads the whole of standard input as the prompt, so the brief goes there and
/// the command carries no prompt. What the role may do is a policy file at the host's highest tier, which a person's own
/// allow rules cannot widen: everything is denied first, then what the role needs is allowed, then what it may not do is
/// denied above the allows. A call the policy does not allow is refused in the default approval mode, where nobody can
/// be asked. The person's own servers, extensions and hooks are not part of the run.
/// </para>
/// <para>
/// One thing outranks that policy: the host ignores a policy given to a run when its system policy folder holds any file
/// whose name ends in <c>.toml</c>, and says so in a warning only. The run would then be held by the host's default rules
/// and the person's own, so <see cref="LaunchProblem"/> refuses the attempt before a child exists where such a file is.
/// </para>
/// </summary>
/// <param name="systemPoliciesDirectory">
/// The folder the host reads system policies from, as <see cref="SystemPoliciesDirectoryOf"/> gives it for this platform;
/// null where none is to be checked.
/// </param>
public sealed class GeminiCliHost(string? systemPoliciesDirectory = null) : IAgentHost
{
    /// <summary>The documented exit of a failed authentication; every other end is the role's own.</summary>
    private const int AuthenticationFailed = 41;

    /// <summary>
    /// That exit is also an enforced sign-in type that does not match, a sign-in missing a setting of its own, and a
    /// refresh that failed, so the sentence says the host could not authenticate and names what a person tries.
    /// </summary>
    private const string NotAuthenticated =
        "Gemini CLI could not authenticate; sign in as yourself, or use antigravity_cli where your plan is an individual one.";

    /// <summary>Where the policy is, relative to the work directory the host runs in: a directory, so the host reads it as the admin tier's.</summary>
    internal const string PolicyDirectory = ".jason-policy";

    private const string PolicyFile = "jason.toml";

    private const string ShellTool = "run_shell_command";

    private const string ShellAllowPrefix = ShellTool + ":";

    private const string ShellDenyPrefix = ShellTool + "(";

    /// <summary>
    /// A server name no server has, so the list of servers the run may use is a list of none. The run is also given no
    /// extension, and its project settings turn the person's hooks off.
    /// </summary>
    private const string NoServer = "jason-no-mcp-server";

    private const string IncludeFlag = "--include-directories=";

    /// <summary>Allowed to every role: reading what is in the workspace, keeping its own list, and loading the skill it was given.</summary>
    private static readonly string[] AlwaysAllowed =
        ["read_file", "read_many_files", "list_directory", "glob", "grep_search", "write_todos", "activate_skill"];

    /// <summary>
    /// Denied to every role above every allow: a role writes through the runtime's command and never to disk, and nobody is
    /// there to answer a question.
    /// </summary>
    private static readonly string[] AlwaysDenied = ["write_file", "replace", "ask_user"];

    /// <summary>
    /// The host's built-in tools, by the names its tool list gives them at 0.66.0 (the tracker's six, the topic and task
    /// tools, the agent tool, and the two built-in agents the policy also names), and the one alias of an older name.
    /// </summary>
    private static readonly HashSet<string> Tools = new(StringComparer.Ordinal)
    {
        "run_shell_command", "glob", "grep_search", "search_file_content", "list_directory", "read_file", "read_many_files",
        "replace", "write_file", "ask_user", "write_todos", "list_mcp_resources", "read_mcp_resource", "activate_skill",
        "get_internal_docs", "enter_plan_mode", "exit_plan_mode", "google_web_search", "web_fetch",
        "tracker_create_task", "tracker_update_task", "tracker_get_task", "tracker_list_tasks", "tracker_add_dependency",
        "tracker_visualize", "update_topic", "complete_task", "invoke_agent", "codebase_investigator", "cli_help",
    };

    public AgentHostKind Kind => AgentHostKind.GeminiCli;

    /// <summary>The folder checked by <see cref="LaunchProblem"/>; null where none is.</summary>
    internal string? SystemPoliciesDirectory => systemPoliciesDirectory;

    /// <summary>
    /// The system policy folder of a platform, where the host reads policies an organisation sets for every user: a folder
    /// of the host's own under the machine's shared application data on Windows and macOS, and under <c>/etc</c> elsewhere.
    /// </summary>
    internal static string SystemPoliciesDirectoryOf(bool windows, bool macOs) =>
        windows ? @"C:\ProgramData\gemini-cli\policies"
        : macOs ? "/Library/Application Support/GeminiCli/policies"
        : "/etc/gemini-cli/policies";

    /// <summary>The folder this process's platform keeps system policies in.</summary>
    public static string PlatformSystemPoliciesDirectory() => SystemPoliciesDirectoryOf(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    /// <summary>
    /// A system policy folder that holds an entry whose name ends in <c>.toml</c>: the host then drops the policy this
    /// runtime gives the run, and holds it to the default rules and the person's own instead, which is not what a profile
    /// asked for. The host reads the folder once, and goes on without the check where it cannot (no folder, no access), as
    /// this does.
    /// </summary>
    public string? LaunchProblem()
    {
        if (systemPoliciesDirectory is null)
        {
            return null;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(systemPoliciesDirectory).Any(entry => entry.EndsWith(".toml", StringComparison.Ordinal)))
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return $"gemini cannot be held to the policy this runtime composes: '{systemPoliciesDirectory}', the host's system policy folder, holds a .toml file, "
            + "and then the host ignores the policy it is given and applies its own default rules and the person's. "
            + "Move or remove the file there, or run this role on another host.";
    }

    /// <summary>
    /// A signed-out run reads a line of standard input, which would be the first line of the brief, and may wait on a
    /// browser; this variable, which the host reads and has no flag for, makes it fail with its authentication exit at
    /// once instead. The other is the variable an editor's companion sets to name the folders of its workspace, which the
    /// host adds to the run's own; an empty value is read as none, so a value in the person's environment adds no folder
    /// the launch never named. The third turns the host's telemetry off: the host starts its exporters from the person's own
    /// settings and variables and logs prompts by default, so a role's run would send the brief wherever the person points
    /// it; the variable outranks the setting, and a value other than true or 1 is read as off.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string>
    {
        ["NO_BROWSER"] = "true",
        ["GEMINI_CLI_IDE_WORKSPACE_PATH"] = string.Empty,
        ["GEMINI_TELEMETRY_ENABLED"] = "false",
    };

    /// <summary>
    /// Every spelling the host's parser knows for an option that would widen or replace what is composed. Read from the
    /// declarations in the host's source at 0.66.0, never from its help: the long names and the short ones, the hidden
    /// internal option, and the positional <c>query</c>, which an option of that name also sets. The parser sets one option
    /// under its kebab-case name and its camel-case twin alike, and a <c>--no-</c> form of either negates it, so
    /// <see cref="Spellings"/> adds those to each long name.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = Spellings(
        ShortReserved,
        [
            "prompt", "prompt-interactive", "output-format", "raw-output", "accept-raw-output-risk",
            "approval-mode", "yolo", "skip-trust", "query", "is-command",

            // A session of this attempt's own, and no other: a run that resumed or named a file would be another attempt's.
            "session-id", "session-file", "resume", "list-sessions", "delete-session",

            // The confinement is only as good as what cannot be added after it: another policy, a server, an extension, a
            // second directory to read, a sandbox that changes where the run happens, or a protocol that is not this one.
            "policy", "admin-policy", "allowed-mcp-server-names", "allowed-tools", "extensions", "list-extensions",
            "include-directories", "sandbox", "worktree", "acp", "experimental-acp",
            "fake-responses", "fake-responses-non-strict", "record-responses",
        ]);

    private static readonly string[] ShortReserved = ["-p", "-i", "-o", "-y", "-r", "-e", "-l", "-s", "-w"];

    private static HashSet<string> Spellings(string[] shorts, string[] longs)
    {
        var all = new HashSet<string>(shorts, StringComparer.OrdinalIgnoreCase);

        // The parser takes the key of a long word from after its dashes, and an option's short letter is an alias of its
        // key, so --y is -y and --no-y negates it: each short option's letter is also a long spelling, and a negation.
        foreach (var letter in shorts.Select(@short => @short[1..]))
        {
            all.Add("--" + letter);
            all.Add("--no-" + letter);
        }

        foreach (var kebab in longs)
        {
            var parts = kebab.Split('-');
            var camel = parts[0] + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            foreach (var name in new[] { kebab, camel })
            {
                all.Add("--" + name);
                all.Add("--no-" + name);
            }
        }

        return all;
    }

    public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(memoryFiles);
        if (launch.Count == 0)
        {
            throw new ArgumentException("A launch names at least the program to start.", nameof(launch));
        }

        // The runtime's own, so that an attempt can be found again by the session it ran in.
        var session = Guid.NewGuid().ToString("D");

        // What the role may do beyond what every role may: run the command that calls this runtime back, and use the
        // tools the person granted, each by the host's own name. What it may not: each tool of the vocabulary that was
        // not granted. Both travel in the work directory, where the policy is.
        var word = ProgramResolver.CliCommandFor(revision.CliCommand);
        string[] allow = [ShellAllowPrefix + word, .. revision.Tools.Select(ToolRule).OfType<string>()];
        string[] deny = [.. Enum.GetValues<AgentTool>().Except(revision.Tools).Select(ToolRule).OfType<string>()];

        // The host reads files of its workspace only, and a memory file lives outside the work directory, so the folder
        // that holds each file the launch names is made part of the workspace: those folders, whole, and no other. A file
        // whose folder cannot be named exactly is not readable, and is reported so.
        var (folders, unreadable) = Folders(memoryFiles);

        return new HostLaunch(
            [
                .. launch,
                "--output-format=stream-json",

                // Nobody is there to answer, so a call the policy does not allow is refused. The work directory is a new
                // folder for every attempt, which the host would not trust, and an untrusted folder's policy and settings
                // are ignored without a word.
                "--approval-mode=default",
                "--skip-trust",
                $"--session-id={session}",
                $"--admin-policy={PolicyDirectory}",
                $"--allowed-mcp-server-names={NoServer}",
                "--extensions=none",
                .. folders.Select(folder => IncludeFlag + folder),

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it.
                .. revision.Args,
            ],
            session,
            unreadable,
            deny,
            allow);
    }

    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);

        var policy = Directory.CreateDirectory(Path.Combine(workDir, PolicyDirectory)).FullName;
        File.WriteAllText(Path.Combine(policy, PolicyFile), Policy(allow, deny), new UTF8Encoding(false));

        // The project's settings win over the person's for the keys they name. The instruction file is the one the harness
        // folders carry, so no file name the person configured is read, and no hook of theirs runs.
        var settings = new JsonObject
        {
            ["context"] = new JsonObject { ["fileName"] = new JsonArray("GEMINI.md") },
            ["hooksConfig"] = new JsonObject { ["enabled"] = false },
        };
        var directory = Directory.CreateDirectory(Path.Combine(workDir, ".gemini")).FullName;
        File.WriteAllText(
            Path.Combine(directory, "settings.json"),
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    public string? Unavailable(int exitCode, string? stderrTail) => exitCode == AuthenticationFailed ? NotAuthenticated : null;

    public string? ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebSearch => "google_web_search",
        AgentTool.WebFetch => "web_fetch",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    public string? DenyProblem(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return "an empty word is not a rule.";
        }

        if (Tools.Contains(word))
        {
            return null;
        }

        if (word.StartsWith(ShellDenyPrefix, StringComparison.Ordinal) && word.EndsWith(')'))
        {
            var prefix = word[ShellDenyPrefix.Length..^1];
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return "a shell command to deny has a prefix between its brackets.";
            }

            return prefix.Any(char.IsControl) ? "a shell command's prefix holds no control character." : null;
        }

        return $"the host reads a tool by its own name, such as web_fetch or write_file, or a shell command as {ShellDenyPrefix}<prefix>), and this is neither.";
    }

    public string? ArgsProblem(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.Length == 0 || arg[0] != '-')
            {
                return $"'{arg}' is not a flag: the host reads a word that is not one as part of the prompt, and a flag's value after it as a word of its own; write each as --flag=value.";
            }

            if (arg == "--")
            {
                return "'--' ends the host's flags and the words after it are read as the prompt.";
            }

            // The bare dash stands for the standard input, which is already the prompt.
            if (arg == "-")
            {
                return "'-' stands for standard input, which the runtime already gives the host as the prompt.";
            }

            // A single dash is a short group: the parser reads -yo as -y and -o, and -pfoo as -p with the value foo, so only
            // a short option alone is a word of its own.
            if (arg[1] != '-' && arg.Length > 2)
            {
                return $"'{arg}' is a short option with more after it, which the host reads as several options or as the option and its value; write a long option as --flag=value.";
            }

            // The parser normalises a name before it matches one: a dot is a path into an object assigned to the option of the
            // part before it, and a leading or trailing dash, a doubled one and an underscore are dropped or folded, so each
            // would be a reserved option under a spelling the reserved list does not hold. A long flag's name is therefore
            // letters, digits and single dashes between them, and nothing else is a flag a profile may carry.
            var name = arg.Split('=', 2)[0];
            if (arg[1] == '-' && !LongName(name))
            {
                return $"'{arg}' is not a flag name the host reads as written: a long flag's name is letters and digits with single dashes between them, since the host's parser reads other spellings as an option it may not be given.";
            }

            // A value is any text, a space included, but not a control character: one would not reach the host as written.
            if (arg.Any(char.IsControl))
            {
                return $"A flag holds a control character, which would not reach the host as written; the value of {name} is plain text.";
            }
        }

        return null;
    }

    /// <summary>Two dashes and then letters and digits, with a dash only between two of them.</summary>
    private static bool LongName(string name)
    {
        var body = name.AsSpan(2);
        if (body.IsEmpty || body[0] == '-' || body[^1] == '-')
        {
            return false;
        }

        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            var plain = char.IsAsciiLetterOrDigit(character);
            if (!plain && !(character == '-' && body[index - 1] != '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The folder of each memory file the launch names, once each, and the levels whose folder cannot be named to the host exactly.</summary>
    private static (List<string> Folders, List<string> Unreadable) Folders(MemoryFileLocations memoryFiles)
    {
        var folders = new List<string>();
        var unreadable = new List<string>();
        foreach (var (level, path) in new[] { ("client", memoryFiles.Client), ("project", memoryFiles.Project), ("campaign", memoryFiles.Campaign) })
        {
            if (path is null)
            {
                continue;
            }

            // The host takes a list as one flag split on commas, or as one flag each; one flag each, so only a comma in
            // the name itself is a problem, and a control character would not survive a command line. The host also trims
            // each entry, so a name with a space at either end would name another folder; and a path that is not fully
            // qualified, such as a drive-relative one, names a folder by where the host happens to be.
            var folder = Path.IsPathFullyQualified(path) ? Path.GetDirectoryName(path) : null;
            if (string.IsNullOrEmpty(folder) || folder.Contains(',', StringComparison.Ordinal) || folder.Any(char.IsControl)
                || char.IsWhiteSpace(folder[0]) || char.IsWhiteSpace(folder[^1]))
            {
                unreadable.Add(level);
            }
            else if (!folders.Contains(folder, StringComparer.Ordinal))
            {
                folders.Add(folder);
            }
        }

        return (folders, unreadable);
    }

    /// <summary>
    /// The policy as TOML, in the order that reads as it is decided: the lowest rule first. The admin tier's priority is
    /// the tier plus the rule's own, so every rule here sits above anything a person or a project wrote, and within it a
    /// higher number wins.
    /// </summary>
    private static string Policy(IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        var text = new StringBuilder();
        text.Append("# Written by Jason for this attempt alone. Admin tier: a person's own allow rules cannot widen it.\n");

        Rule(text, "*", null, "deny", 1);
        foreach (var tool in AlwaysAllowed)
        {
            Rule(text, tool, null, "allow", 500);
        }

        foreach (var word in allow.Distinct(StringComparer.Ordinal))
        {
            if (word.StartsWith(ShellAllowPrefix, StringComparison.Ordinal))
            {
                Rule(text, ShellTool, word[ShellAllowPrefix.Length..], "allow", 500);
            }
            else
            {
                Rule(text, word, null, "allow", 500);
            }
        }

        foreach (var word in AlwaysDenied.Concat(deny).Distinct(StringComparer.Ordinal))
        {
            if (word.StartsWith(ShellDenyPrefix, StringComparison.Ordinal) && word.EndsWith(')'))
            {
                Rule(text, ShellTool, word[ShellDenyPrefix.Length..^1], "deny", 900);
            }
            else
            {
                Rule(text, word, null, "deny", 900);
            }
        }

        return text.ToString();
    }

    private static void Rule(StringBuilder text, string tool, string? commandPrefix, string decision, int priority)
    {
        text.Append("\n[[rule]]\n");
        text.Append("toolName = ").Append(Quoted(tool)).Append('\n');
        if (commandPrefix is not null)
        {
            text.Append("commandPrefix = ").Append(Quoted(commandPrefix)).Append('\n');
        }

        text.Append("decision = ").Append(Quoted(decision)).Append('\n');
        text.Append("priority = ").Append(priority.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    /// <summary>
    /// A TOML basic string: the quote and the backslash escaped, so a word cannot end the string it is in, and every
    /// control character written as its escape, since a basic string may not hold one raw.
    /// </summary>
    private static string Quoted(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case < ' ' or '\u007F':
                    text.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
