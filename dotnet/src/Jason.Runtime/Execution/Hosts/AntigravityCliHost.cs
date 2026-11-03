using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// One headless Antigravity CLI run (the <c>agy</c> command), started for one attempt. The runtime composes the whole shape
/// of the run and leaves a profile only the program and whatever it adds at the end.
/// <para>
/// Built from the host's documentation for version 1.4.10 (2026-10-29) and not yet run on an account: every form below is
/// the documented one, and where the documentation does not say what the host does, this says so rather than guess.
/// </para>
/// <para>
/// The one way the host reads a prompt from standard input is its stream format: one JSON line per prompt, the run ending
/// when the stream does. So the brief travels as one such line (<see cref="FrameStdin"/>) and the command carries no print
/// flag and no word of the brief. The host has no limit of the attempt's kind that ends a run as a failure — its own print
/// timeout ends it with a success and partial output (the host's changelog says so) — so the command turns it off and the attempt's limit is the only one.
/// </para>
/// <para>
/// What the host may do is decided by permission rules, and the host reads those from the person's own files under their
/// home folder and from nowhere a run could carry them: no flag, no file of a work directory. The runtime never writes that
/// folder. What a work directory can carry is a workspace hook, <c>.agents/hooks.json</c>, whose pre-tool-use handler is
/// asked about each tool call and may answer that the call is approved, refused, or left to the person's own rules. So
/// this host writes one such hook, whose command is this executable's own <c>host-hook</c> mode with the rules of the
/// attempt in base64url (<see cref="AntigravityHook"/>): the role may call the runtime back, view the memory files the
/// launch names, and write its result file, and a tool of the profile that is not granted is refused. The person's own
/// deny and ask rules still win over an approval, and their allow rules still add.
/// </para>
/// <para>
/// Not measured, because the host's pages do not say and no run on an account has been made: whether the hook is honoured
/// in a headless run in a folder the host has not been told to trust, whether an approval lifts the headless refusal of a
/// call, whether the person's own deny still wins, whether a written file is seen by the hook, which shell runs the hook's
/// command, and an end to end run of a role on each operating system.
/// </para>
/// </summary>
public sealed class AntigravityCliHost : IAgentHost
{
    /// <summary>
    /// The sentence an end of this host becomes when it says it is not signed in. Fixed: the tail it was read from can
    /// carry the person's login or address, and this becomes the attempt's error, which the journal carries.
    /// </summary>
    public const string NotSignedIn = "Antigravity CLI is not signed in on this account: run `agy` and sign in as yourself.";

    /// <summary>
    /// The words read for a run with no sign-in, from the host's headless page ("authentication required"), and for one whose
    /// sign-in expired in the middle of it, from the host's changelog ("Authentication expired").
    /// Only these are looked for; an exit the pages give another meaning, a failure of the model service say, is not one.
    /// </summary>
    private static readonly string[] SignInWords = ["authentication required", "authentication expired"];

    private static readonly string[] Levels = ["client", "project", "campaign"];

    /// <summary>
    /// Everything this host reads that would change the shape composed below, plus everything that would undo it. A profile
    /// says which program and what to add; it does not say that the run is interactive, takes a prompt on its command line,
    /// reports in another format, ends on a limit of its own, acts without asking, widens its workspace, takes another
    /// project or conversation, or writes where it likes. What stays open is the choice of model and effort.
    /// </summary>
    public IReadOnlySet<string> ReservedFlags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-p", "--print", "--prompt",
        "-i", "--prompt-interactive",
        "--input-format", "--output-format",
        "--print-timeout",
        "--json-schema",
        // The modes page lists passing --permission-mode as a common mistake and says the host uses --mode; reserved as a guard.
        "--mode", "--permission-mode",
        "--sandbox",
        "--dangerously-skip-permissions",
        "--add-dir",
        "--project", "--new-project",
        "-c", "--continue", "--conversation",
        "--remote-control",
        "--log-file",
        "--agent",
    };

    public AgentHostKind Kind => AgentHostKind.AntigravityCli;

    /// <summary>The host mints its conversation's id itself and takes none: the provenance names no session.</summary>
    public bool TakesSessionId => false;

    public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(memoryFiles);
        if (launch.Count == 0)
        {
            throw new ArgumentException("A launch names at least the program to start.", nameof(launch));
        }

        // What the role may do goes through the work directory's hook: the command that calls this runtime back, each named
        // memory file as a file, and a tool the person granted. What it may not: a tool the profile does not grant. The
        // capability token and the envelope are in none of it.
        var allow = new List<string> { $"command({ProgramResolver.CliCommandFor(revision.CliCommand)})" };
        allow.AddRange(new[] { memoryFiles.Client, memoryFiles.Project, memoryFiles.Campaign }.OfType<string>().Select(file => $"{ReadPrefix}{file})"));
        allow.AddRange(revision.Tools.Select(ToolRule).OfType<string>());
        string[] deny = [.. Enum.GetValues<AgentTool>().Except(revision.Tools).Select(ToolRule).OfType<string>()];

        return new HostLaunch(
            [
                .. launch,

                // A stream of lines in and out, the only way the host reads a prompt from standard input; and no limit of
                // its own, which the pages disagree on and which would end a run as a success with partial output.
                "--input-format",
                "stream-json",
                "--output-format",
                "stream-json",
                "--print-timeout=0s",

                // Last, and nothing after them: a profile adds to the shape above and never rewrites it.
                .. revision.Args,
            ],
            null,
            [],
            deny,
            allow);
    }

    private const string ReadPrefix = "view_file(";

    private const string CommandPrefix = "command(";

    /// <summary>The tools whose call the hook always decides about, whether or not a profile names them.</summary>
    private static readonly string[] HookedTools =
    [
        "run_command", "view_file", "write_to_file", "replace_file_content", "multi_replace_file_content", "ask_question",
        "read_url_content", "search_web",
    ];

    /// <summary>
    /// Every tool the host's hooks page lists, which is the whole vocabulary a profile's deny may use here. The two a role
    /// cannot work without, the one it calls back with and the one it reads its memory files with, are in
    /// <see cref="Essential"/> and cannot be denied.
    /// </summary>
    private static readonly string[] HostTools =
    [
        "view_file", "write_to_file", "replace_file_content", "multi_replace_file_content", "list_dir", "find_by_name",
        "grep_search", "search_web", "read_url_content", "run_command", "manage_task", "schedule", "list_permissions",
        "ask_permission", "invoke_subagent", "define_subagent", "send_message", "manage_subagents", "ask_question", "generate_image",
    ];

    private static readonly string[] Essential = ["run_command", "view_file"];

    /// <summary>
    /// The workspace hook: <c>.agents/hooks.json</c> in the directory the host works in, one pre-tool-use handler whose
    /// matcher is the hooked tools and the ones the profile denies, and whose command is this executable's hook mode with the
    /// attempt's rules as one base64url word. Nothing else is written, and nothing under the person's home. The command
    /// word and the rules are bare words in every shell, so no path or quote of a person's reaches one.
    /// </summary>
    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);

        var cli = allow.Where(rule => rule.StartsWith(CommandPrefix, StringComparison.Ordinal) && rule.EndsWith(')')).Select(rule => rule[CommandPrefix.Length..^1]).FirstOrDefault()
            ?? ProgramResolver.DefaultCliCommand;
        var rules = new AntigravityHookRules(
            cli,
            [.. allow.Where(rule => rule.StartsWith(ReadPrefix, StringComparison.Ordinal) && rule.EndsWith(')')).Select(rule => rule[ReadPrefix.Length..^1])],
            [.. allow.Where(rule => HostTools.Contains(rule, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)],
            [.. deny.Where(rule => HostTools.Contains(rule, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)],
            Path.Combine(Path.GetFullPath(workDir), "result.json"));

        var names = HookedTools.Concat(rules.Deny).Distinct(StringComparer.Ordinal).Select(Regex.Escape).Append("browser_.*");
        var document = new JsonObject
        {
            ["jason-attempt"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray(
                    new JsonObject
                    {
                        ["matcher"] = $"^({string.Join('|', names)})$",
                        ["hooks"] = new JsonArray(
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = $"{cli} host-hook antigravity-cli {AntigravityHookRules.Encode(rules)}",
                                ["timeout"] = 10,
                            }),
                    }),
            },
        };

        var directory = Path.Combine(workDir, ".agents");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "hooks.json"), document.ToJsonString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// The host saying it has no usable sign-in: a non-zero end whose stderr tail holds one of the two phrases above.
    /// Nothing else of the tail is read as a signal and none of it is carried.
    /// </summary>
    public string? Unavailable(int exitCode, string? stderrTail) =>
        exitCode != 0 && !string.IsNullOrEmpty(stderrTail) && SignInWords.Any(word => stderrTail.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? NotSignedIn
            : null;

    /// <summary>
    /// The host's own name of the tool, which the hook is told to approve where the profile grants it and to refuse where it
    /// does not: a fetch of a page is <c>read_url_content</c> and a search is <c>search_web</c>.
    /// </summary>
    public string? ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebSearch => "search_web",
        AgentTool.WebFetch => "read_url_content",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    /// <summary>A grant is a tool the hook approves, so there is nothing a grant cannot make true here.</summary>
    public string? GrantProblem(AgentTool tool) => null;

    /// <summary>
    /// A deny is the host's name of a tool, as its hooks page lists them, and the hook refuses a call of it. A rule of the host's
    /// own form, <c>action(target)</c>, is not read, since matching its target would be a second copy of the host's own
    /// matcher; and the tool the role calls back with and the one it reads its memory files with cannot be denied, since
    /// neither can work without them.
    /// </summary>
    public string? DenyProblem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (Essential.Contains(word, StringComparer.Ordinal))
        {
            return $"'{word}' is the tool a role calls the runtime back with or reads its memory files with, so it cannot be denied: the callback and the memory files go through it.";
        }

        return HostTools.Contains(word, StringComparer.Ordinal)
            ? null
            : "only the host's own tool name is read, as its hooks page lists them (list_dir, grep_search, search_web, read_url_content and the rest), never a rule of the form action(target); a tool name is written exactly, in lower case.";
    }

    /// <summary>
    /// The options of the host's documentation that a person owns, each in long form with its value joined by an equals
    /// sign: the model and the reasoning effort (read from the host's command-line reference and headless pages at version
    /// 1.4.10). Nothing that changes the launch, the permissions, the workspace, the conversation, the output or where the
    /// prompt comes from is here. The effort's range is not checked, because the host's two pages disagree on it, and a value
    /// that holds a control character is refused, since no model or effort is named so and a NUL ends a Windows command line.
    /// </summary>
    private static readonly string[] OwnedOptions = ["--model", "--effort"];

    /// <summary>
    /// The host's parser could not be read: it is closed source and no copy was available to inspect. So no word is taken
    /// on the strength of what a parser might do with it. An argument is one of <see cref="OwnedOptions"/> in the form
    /// <c>--name=value</c> with a value, and every other word is refused, an undocumented flag, a switch shaped like one that
    /// skips permissions, a bare word, the double dash, a short flag, a cluster or a glued value alike. A long flag in
    /// <see cref="ReservedFlags"/> is left out here, since the profile rules refuse it by its own code, and the reserved list
    /// stays as the record of what the documentation calls dangerous, under the allowlist. A later release that reads the
    /// parser may turn this into a denylist.
    /// </summary>
    public string? ArgsProblem(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg == "--")
            {
                return Refused(arg, "it is not a long flag, its value, if any, joined by an equals sign");
            }

            var separator = arg.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? arg : arg[..separator];
            if (ReservedFlags.Contains(name))
            {
                continue;
            }

            if (separator < 0 || separator == arg.Length - 1 || !OwnedOptions.Contains(name, StringComparer.Ordinal)
                || arg.Skip(separator + 1).Any(char.IsControl))
            {
                return Refused(arg, "it is not one of the options the person owns");
            }
        }

        return null;
    }

    private static string Refused(string word, string why) =>
        $"'{word}' is refused: {why}; the host's parser is not read, so only the documented options the person owns are taken: " +
        string.Join(", ", OwnedOptions.Select(option => $"{option}=<value>")) + ".";

    /// <summary>
    /// One user event of the stream and the line feed that ends the line: the envelope is its content as the serializer
    /// escapes it, so a line break, a quote or a character outside ASCII in the envelope is never one in the line.
    /// </summary>
    public string FrameStdin(string envelopeJson)
    {
        ArgumentNullException.ThrowIfNull(envelopeJson);
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["event"] = "user", ["message"] = new Dictionary<string, string> { ["content"] = envelopeJson } }) + "\n";
    }
}
