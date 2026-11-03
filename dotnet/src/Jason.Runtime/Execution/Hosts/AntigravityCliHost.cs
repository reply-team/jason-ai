using System.Text.Json;
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
/// timeout ends it with a success and partial output — so the command turns it off and the attempt's limit is the only one.
/// </para>
/// <para>
/// What the host may do is decided by permission rules, and the host reads those from the person's own files under their
/// home folder and from nowhere a run could carry: no flag, no file of a work directory. The runtime never writes that
/// folder. So this host composes no allow and no deny, refuses every grant and every deny where a profile is written, and
/// says every memory file outside the work directory unreadable. The person states two rules of their own, for the command
/// word that calls the runtime back and for the harness folder, and the documentation of this host says how. A headless run
/// the person's rules do not allow is denied without stopping, and ends without reporting.
/// </para>
/// <para>
/// Not measured, because the host's pages do not say: whether a run in a folder the host does not know stops to ask,
/// whether an allowed command runs unattended, whether the work directory's skills load there, and the exit code and text
/// of a run that is not signed in beyond the two phrases the pages quote.
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
    /// The words the host's pages quote for a run with no sign-in, and for one whose sign-in expired in the middle of it.
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

        // A file outside the work directory is read only where the person's own rule allows it, which this cannot write.
        string[] unreadable =
            [.. Levels.Zip([memoryFiles.Client, memoryFiles.Project, memoryFiles.Campaign]).Where(level => level.Second is not null).Select(level => level.First)];

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
            unreadable,
            [],
            []);
    }

    /// <summary>
    /// Nothing: the host takes its permission rules from the person's own files and none from a work directory, and the
    /// role's skill is placed by the work directory where the host reads skills.
    /// </summary>
    public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);
    }

    /// <summary>
    /// The host saying it has no usable sign-in: a non-zero end whose stderr tail holds one of the phrases the pages quote.
    /// Nothing else of the tail is read as a signal and none of it is carried.
    /// </summary>
    public string? Unavailable(int exitCode, string? stderrTail) =>
        exitCode != 0 && !string.IsNullOrEmpty(stderrTail) && SignInWords.Any(word => stderrTail.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? NotSignedIn
            : null;

    /// <summary>
    /// The host's rule word for a fetch of a page, <c>read_url(*)</c>, which Jason can neither grant nor deny; a search has
    /// no rule in the host's table at all.
    /// </summary>
    public string? ToolRule(AgentTool tool) => tool switch
    {
        AgentTool.WebSearch => null,
        AgentTool.WebFetch => "read_url(*)",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "A tool outside the vocabulary has no rule."),
    };

    public string? GrantProblem(AgentTool tool) =>
        "the host reads its permission rules from the person's own settings files and from nowhere a run can carry them, so a grant of a tool is a rule the person adds there; see the host's section of the execution-profiles document.";

    public string? DenyProblem(string word) =>
        "the host reads its permission rules from the person's own settings files and from nowhere a run can carry them, so a deny is a rule the person adds there; see the host's section of the execution-profiles document.";

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
