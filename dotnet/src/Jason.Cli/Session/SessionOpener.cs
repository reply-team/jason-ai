using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Hosts;
using Jason.Contracts.Json;
using Jason.Contracts.Programs;

namespace Jason.Cli.Session;

/// <summary>
/// The act behind <c>jason session open</c> and the <c>…/session</c> links: one approval or question read by its kind's own
/// get, its folder resolved as <c>--prompt</c> resolves it, the host the named preference <c>agent.interactive_host</c>
/// names found on the search path, and one terminal started at the folder with that host and the item's brief as its
/// first input. Of the runtime it asks only those reads and <c>preference.list</c>, and it changes nothing there itself.
/// Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own
/// permission settings' to decide.
/// </summary>
/// <remarks>
/// Exit codes are the CLI's: 0 the terminal started (or, for a dry run, would be), 1 refused — by the runtime, or because
/// nothing waits on the item, the host is unknown or not installed, the recorded folder is not there, no folder is
/// recorded and no home folder is named, or no terminal is found or starts — and 3 no runtime to ask. Every refusal that
/// leaves the item waiting says how to do the same by hand.
/// Stdout is one compact object of the CLI's own, never the prompt.
/// </remarks>
public static class SessionOpener
{
    /// <summary>The preference that names the host, and the host it means when it is unset.</summary>
    public const string PreferenceKey = "agent.interactive_host";

    public const AgentHostKind DefaultHost = AgentHostKind.ClaudeCode;

    public static async Task<int> OpenAsync(CliEnvironment env, PromptKind kind, string id, bool dryRun, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentException.ThrowIfNullOrEmpty(id);

        var approval = kind == PromptKind.Approval;
        var noun = approval ? "approval" : "question";
        var body = RequestBody.Empty().Set(approval ? "approval_id" : "decision_id", id);
        var (exit, response) = await OperationRunner.SendAsync(env, approval ? Operations.ApprovalGet : Operations.DecisionGet, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exit;
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(OperationRunner.ErrorText(response));
            return ExitCodes.ApiError;
        }

        if (Read(response.Body, approval) is not { } item)
        {
            env.Error.WriteLine($"The runtime's answer names no {noun} to start a session on; nothing was started.");
            return ExitCodes.ApiError;
        }

        var byHand = $"To work on it by hand, run `jason {(approval ? "approval" : "decision")} get {item.Id} --prompt` in a terminal of your own and give the brief to your agent host.";
        if (item.DecidedState is { } decided)
        {
            env.Error.WriteLine($"Nothing waits on this {noun}: {item.Id} was already {decided}, so no session was started; `jason {(approval ? "approval" : "decision")} get {item.Id} --prompt` still prints its brief.");
            return ExitCodes.ApiError;
        }

        var folder = await PromptFolderResolver.ResolveAsync(env, item.CampaignId, cancellationToken).ConfigureAwait(false);
        var (preferenceExit, word, _) = await PreferredHostAsync(env, cancellationToken).ConfigureAwait(false);
        if (preferenceExit != ExitCodes.Success)
        {
            if (preferenceExit == ExitCodes.ApiError)
            {
                env.Error.WriteLine($"The preference {PreferenceKey} could not be read, so no host was chosen and nothing was started. {byHand}");
            }

            return preferenceExit;
        }

        if ((word is null ? DefaultHost : AgentHosts.Parse(word)) is not { } host)
        {
            env.Error.WriteLine($"The preference {PreferenceKey} names '{word}', which this jason does not know as a host; it knows {AgentHosts.Vocabulary}. Nothing was started.");
            env.Error.WriteLine(ChooseAnother);
            env.Error.WriteLine(byHand);
            return ExitCodes.ApiError;
        }

        var hostWord = AgentHosts.Word(host);
        var (program, found) = HostProgram(env, host);
        if (program is null)
        {
            env.Error.WriteLine(NotFound(hostWord, AgentHosts.Of(host).CommandWord, found));
            env.Error.WriteLine(ChooseAnother);
            env.Error.WriteLine(byHand);
            return ExitCodes.ApiError;
        }

        if (folder is not null && !Directory.Exists(folder.Path))
        {
            env.Error.WriteLine(Missing(folder));
            env.Error.WriteLine(byHand);
            return ExitCodes.ApiError;
        }

        var prompt = CopyPrompt.Compose(kind, item.Id, item.CampaignId, item.WorkItemId, folder);
        var where = folder?.Path ?? Home(env);
        if (string.IsNullOrWhiteSpace(where))
        {
            env.Error.WriteLine($"No folder is recorded for the {noun}'s campaign and this account names no home folder, so there is nowhere to start {hostWord} and nothing was started.");
            env.Error.WriteLine(byHand);
            return ExitCodes.ApiError;
        }

        var shown = folder?.Path ?? "your home folder";
        var terminals = env.Terminals;
        var terminal = terminals is null
            ? null
            : TerminalPlan.Find(terminals.Platform, terminals.SystemDirectory, Variable(env, "TERMINAL"), SearchPath(env));
        if (terminals is null || terminal is null)
        {
            env.Error.WriteLine($"No terminal was found to start {hostWord} in. In a terminal of your own, at {shown}, run `jason {(approval ? "approval" : "decision")} get {item.Id} --prompt` and give the brief to your host.");
            return ExitCodes.ApiError;
        }

        TerminalLaunch launch;
        try
        {
            launch = TerminalPlan.Compose(terminals.Platform, terminal, where, [.. program, .. AgentHosts.InteractiveArguments(host, prompt)], item.Id, DataDirectory(env));
        }
        catch (ArgumentException)
        {
            // macOS's script is named from the id, and only a well-formed one names a file.
            env.Error.WriteLine($"The runtime's answer names the {noun} by an id no script may be named from, so nothing was started.");
            return ExitCodes.ApiError;
        }

        if (!dryRun && !terminals.Start(launch, out var problem))
        {
            env.Error.WriteLine($"The terminal {launch.Terminal} did not start ({problem}). {byHand}");
            return ExitCodes.ApiError;
        }

        env.Out.WriteLine(human
            ? $"{(dryRun ? "Would start" : "Started")} {hostWord} in {launch.Terminal} at {shown}."
            : Printed(kind, item, folder, hostWord, program[0], launch, dryRun));
        return ExitCodes.Success;
    }

    /// <summary>
    /// How to name another host, in the form every shell passes whole: the value in a file, since Windows PowerShell 5.1
    /// and cmd.exe break an inline JSON value's quotes.
    /// </summary>
    internal const string ChooseAnother =
        "To choose another host, put {\"host\":\"<host>\"} in a file, host.json say, and run `jason preference set agent.interactive_host --value-file host.json --actor human:<your-name>`; "
        + "or go back to claude_code with `jason preference unset agent.interactive_host --actor human:<your-name>`.";

    private sealed record Item(string Id, string CampaignId, string WorkItemId, string? DecidedState);

    private static Item? Read(string json, bool approval)
    {
        if (approval)
        {
            var read = RenderText.Read<ApprovalDto>(json);
            return read is { Id: { Length: > 0 } id, CampaignId: { Length: > 0 } campaign, WorkItemId: { Length: > 0 } workItem }
                ? new Item(id, campaign, workItem, PromptRenderers.Decided(read.Status, ApprovalStatus.Pending))
                : null;
        }

        var decision = RenderText.Read<DecisionDto>(json);
        return decision is { Id: { Length: > 0 } decisionId, CampaignId: { Length: > 0 } decisionCampaign, WorkItemId: { Length: > 0 } decisionWorkItem }
            ? new Item(decisionId, decisionCampaign, decisionWorkItem, PromptRenderers.Decided(decision.Status, DecisionStatus.Pending))
            : null;
    }

    /// <summary>
    /// The word the preference holds, or null when it is unset; a read the runtime refused is its exit code. <c>Said</c> is
    /// true where the exit was already written on stdout by the read itself (no runtime, a request that timed out), so a
    /// caller writes no second envelope.
    /// </summary>
    internal static async Task<(int Exit, string? Word, bool Said)> PreferredHostAsync(CliEnvironment env, CancellationToken cancellationToken)
    {
        var (exit, response) = await OperationRunner.SendAsync(env, Operations.PreferenceList, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return (exit, null, true);
        }

        if (!response.IsSuccess)
        {
            return (ExitCodes.ApiError, null, false);
        }

        JsonNode? listed;
        try
        {
            listed = JsonNode.Parse(response.Body);
        }
        catch (JsonException)
        {
            return (ExitCodes.ApiError, null, false);
        }

        if (listed?["items"] is not JsonArray items)
        {
            return (ExitCodes.ApiError, null, false);
        }

        var entry = items.OfType<JsonObject>().FirstOrDefault(preference => preference["key"]?.GetValueKind() == JsonValueKind.String && preference["key"]!.GetValue<string>() == PreferenceKey);
        return entry?["value"]?["host"] is JsonValue host && host.GetValueKind() == JsonValueKind.String
            ? (ExitCodes.Success, host.GetValue<string>(), false)
            : (ExitCodes.Success, null, false);
    }

    /// <summary>A host chosen for a session at a folder or on Jason itself, and its program with what goes before its own arguments.</summary>
    internal sealed record ChosenHost(AgentHostKind Host, IReadOnlyList<string> Program);

    /// <summary>
    /// The host of a session with no item, and its program: <paramref name="named"/> where <c>--host</c> named one, asking
    /// the runtime nothing; else the one the preference names, read with <c>preference.list</c>, else Claude Code. A refusal
    /// is written here and its exit code returned with no host: no runtime (as the read wrote it), a preference that could
    /// not be read (<paramref name="unreadable"/>), a word no host has, <paramref name="refusedFor"/>'s own refusal of the
    /// host the preference named, and a program not found.
    /// </summary>
    internal static async Task<(int Exit, ChosenHost? Chosen)> ChooseHostAsync(
        CliEnvironment env,
        AgentHostKind? named,
        string unreadable,
        Func<AgentHostKind, int?>? refusedFor,
        CancellationToken cancellationToken)
    {
        var host = named;
        if (host is null)
        {
            var (preferenceExit, word, said) = await PreferredHostAsync(env, cancellationToken).ConfigureAwait(false);
            if (said)
            {
                return (preferenceExit, null);
            }

            if (preferenceExit == ExitCodes.ApiError)
            {
                return (Refuse(env, CliErrors.PreferenceUnreadable, unreadable), null);
            }

            host = word is null ? DefaultHost : AgentHosts.Parse(word);
            if (host is null)
            {
                return (Refuse(
                    env,
                    CliErrors.HostUnknown,
                    $"The preference {PreferenceKey} names '{word}', which this jason does not know as a host; it knows {AgentHosts.Vocabulary}. Nothing was started. {ChooseAnother}"), null);
            }

            if (refusedFor?.Invoke(host.Value) is { } refused)
            {
                return (refused, null);
            }
        }

        var (found, lookup) = HostProgram(env, host.Value);
        return found is null
            ? (Refuse(env, CliErrors.HostNotFound, NotFound(AgentHosts.Word(host.Value), AgentHosts.Of(host.Value).CommandWord, lookup)), null)
            : (ExitCodes.Success, new ChosenHost(host.Value, found));
    }

    /// <summary>
    /// A refusal of a session with no item: the API's error envelope on stdout with <paramref name="code"/>, the sentence
    /// on stderr, and exit 1.
    /// </summary>
    internal static int Refuse(CliEnvironment env, string code, string message)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: false));
        env.Error.WriteLine(message);
        return ExitCodes.ApiError;
    }

    /// <summary>
    /// The host's program and what goes before its own arguments, found by its command word as Jason finds every program it
    /// starts by name: an <c>.exe</c>, npm's shim read for what it starts (the interpreter and entry script it names, or
    /// the executable its package ships), or Cursor CLI's launcher read for what it starts; never a script through a
    /// shell. Null where none of those answers.
    /// </summary>
    internal static (IReadOnlyList<string>? Program, FoundProgram Found) HostProgram(CliEnvironment env, AgentHostKind host)
    {
        var pathExt = OperatingSystem.IsWindows() ? Variable(env, "PATHEXT") : null;
        var onPath = new ProgramSearchPath(SearchPath(env), pathExt);
        var found = ProgramLookup.Find(AgentHosts.Of(host).CommandWord, onPath, onPath);
        switch (found.Outcome)
        {
            case ProgramLookupOutcome.Program or ProgramLookupOutcome.NpmShim when found.Path is { } path:
                return ([path, .. found.Launch], found);
            case ProgramLookupOutcome.RefusedShim when host == AgentHostKind.CursorCli && found.File is { } file:
                return (new CursorCliLauncher(OperatingSystem.IsWindows()).ProgramLayout(file), found);
            default:
                return (null, found);
        }
    }

    /// <summary>
    /// Why no program was found for the host, by the lookup's outcome: nothing answered for the name, or only a file Jason
    /// refuses to start, named with the rule it broke as <c>jason status</c> names it.
    /// </summary>
    internal static string NotFound(string hostWord, string command, FoundProgram found) => found switch
    {
        { Outcome: ProgramLookupOutcome.ShellOnly, File: { } file } =>
            $"{hostWord}'s program '{command}' resolves only to {file}, {ProgramLookup.ShellScript}, so nothing was started: {ProgramLookup.ProgramsNotShells}.",
        { Outcome: ProgramLookupOutcome.RefusedShim, File: { } file } =>
            $"{hostWord}'s program '{command}' resolves only to {file}, which {found.Refusal}, so nothing was started: starting it would mean cmd.exe interprets the arguments.",
        { Outcome: ProgramLookupOutcome.NotFound, File: { } shim } =>
            $"{hostWord}'s program '{command}' resolves to {shim}, npm's shim, whose executable is not there, so nothing was started.",
        _ => $"{hostWord}'s program '{command}' is not on this machine's search path, so nothing was started.",
    };

    internal static string? SearchPath(CliEnvironment env) => env.SearchPath ?? Environment.GetEnvironmentVariable("PATH");

    internal static string? Variable(CliEnvironment env, string name) =>
        env.Variables is { } variables ? variables.GetValueOrDefault(name) : Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// The data directory this CLI runs on when <c>JASON_DATA_DIR</c> names one, else null: an empty or blank value names
    /// none, as it does where the data directory is resolved. The directory is the one the CLI uses, made absolute, since
    /// the terminal starts in another folder than the CLI.
    /// </summary>
    internal static string? DataDirectory(CliEnvironment env) =>
        string.IsNullOrWhiteSpace(Variable(env, JasonPaths.DataDirectoryVariable)) ? null : env.Paths.Root;

    /// <summary>The person's home folder, where a host starts when no folder is recorded: never the folder the CLI runs in.</summary>
    private static string Home(CliEnvironment env) => env.Identity?.Home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Printed(PromptKind kind, Item item, PromptFolder? folder, string host, string program, TerminalLaunch launch, bool dryRun)
    {
        var printed = new JsonObject
        {
            ["kind"] = kind == PromptKind.Approval ? "approval" : "question",
            ["id"] = item.Id,
            ["campaign_id"] = item.CampaignId,
            ["folder"] = folder is null ? null : new JsonObject { ["level"] = Level(folder.Level), ["path"] = folder.Path },
            ["host"] = host,
            ["program"] = program,
            ["terminal"] = launch.Terminal,
            ["started"] = !dryRun,
        };
        if (dryRun)
        {
            printed["argv"] = new JsonArray([.. launch.Argv.Select(word => (JsonNode?)JsonValue.Create(word))]);
            if (launch.Script is { } script)
            {
                printed["script"] = script;
            }
        }

        return printed.ToJsonString(JasonJson.Options);
    }

    /// <summary>
    /// Why a recorded folder that is not on disk starts nothing: a terminal started there would open somewhere else. A
    /// campaign's or a project's folder a person moved is recorded again by <c>jason project link</c>.
    /// </summary>
    internal static string Missing(PromptFolder folder) => folder.Level switch
    {
        PromptFolderLevel.HarnessRoot => $"The harness root at {folder.Path} is not there, so nothing was started.",
        _ => $"The {(folder.Level == PromptFolderLevel.Campaign ? "campaign's" : "project's")} folder recorded at {folder.Path} is not there, so nothing was started; "
            + "if it was moved, `jason project link --path <the project's folder, where it is now>` records it and its campaigns again.",
    };

    internal static string Level(PromptFolderLevel level) => level switch
    {
        PromptFolderLevel.Campaign => "campaign",
        PromptFolderLevel.Project => "project",
        _ => "harness_root",
    };
}
