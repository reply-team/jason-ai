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
/// </summary>
public sealed class GeminiCliHost : IAgentHost
{
    /// <summary>The documented exit of a failed authentication; every other end is the role's own.</summary>
    private const int AuthenticationFailed = 41;

    private const string NotSignedIn =
        "Gemini CLI is not signed in to a Code Assist account; sign in as yourself, or use antigravity_cli where your plan is an individual one.";

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

    /// <summary>The host's built-in tools, by the names its tools reference gives them, and the one alias of an older name.</summary>
    private static readonly HashSet<string> Tools = new(StringComparer.Ordinal)
    {
        "run_shell_command", "glob", "grep_search", "search_file_content", "list_directory", "read_file", "read_many_files",
        "replace", "write_file", "ask_user", "write_todos", "list_mcp_resources", "read_mcp_resource", "activate_skill",
        "get_internal_docs", "enter_plan_mode", "exit_plan_mode", "google_web_search", "web_fetch",
    };

    public AgentHostKind Kind => AgentHostKind.GeminiCli;

    /// <summary>
    /// A signed-out run reads a line of standard input, which would be the first line of the brief, and may wait on a
    /// browser; this variable, which the host reads and has no flag for, makes it fail with its authentication exit at
    /// once instead.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string> { ["NO_BROWSER"] = "true" };

    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-p", "--prompt",
        "-i", "--prompt-interactive",
        "-o", "--output-format",
        "--raw-output", "--accept-raw-output-risk",
        "--approval-mode",
        "-y", "--yolo",
        "--skip-trust",

        // A session of this attempt's own, and no other: a run that resumed or named a file would be another attempt's.
        "--session-id", "--session-file",
        "-r", "--resume",
        "--list-sessions", "--delete-session",

        // The confinement is only as good as what cannot be added after it: another policy, a server, an extension, a
        // second directory to read, a sandbox that changes where the run happens, or a protocol that is not this one.
        "--policy", "--admin-policy",
        "--allowed-mcp-server-names", "--allowed-tools",
        "-e", "--extensions", "-l", "--list-extensions",
        "--include-directories",
        "-s", "--sandbox",
        "-w", "--worktree",
        "--acp", "--experimental-acp",
        "--fake-responses", "--fake-responses-non-strict", "--record-responses",
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

    public string? Unavailable(int exitCode, string? stderrTail) => exitCode == AuthenticationFailed ? NotSignedIn : null;

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

            if (arg.Length > 2 && arg[1] != '-' && char.IsAsciiLetter(arg[1]) && arg[2] != '=')
            {
                return $"'{arg}' glues a short option to its value, which the host reads as the option it starts with; write it as a long option or as -x=value.";
            }
        }

        return null;
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
            // the name itself is a problem, and a control character would not survive a command line.
            var folder = Path.IsPathRooted(path) ? Path.GetDirectoryName(path) : null;
            if (string.IsNullOrEmpty(folder) || folder.Contains(',', StringComparison.Ordinal) || folder.Any(char.IsControl))
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

    /// <summary>A TOML basic string: the quote and the backslash escaped, so a word cannot end the string it is in.</summary>
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
                default:
                    text.Append(character);
                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
