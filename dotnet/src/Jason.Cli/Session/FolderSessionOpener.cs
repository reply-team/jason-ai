using System.Text.Json.Nodes;
using Jason.Cli.Folders;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Contracts.Json;

namespace Jason.Cli.Session;

/// <summary>
/// The act behind <c>jason session open</c> with no item: a new terminal window at a folder the installation records, a
/// campaign's, a project's or the harness root, running the person's own agent host or the system's plain shell, with no
/// first input. Of the runtime it asks only reads — <c>campaign.get</c>, <c>project.get</c>, <c>system.info</c> and, when
/// no program is named, <c>preference.list</c> — and it writes nothing there or into any folder.
/// </summary>
/// <remarks>
/// <para>
/// The folder: <c>--campaign</c> is the campaign's recorded folder, else its project's, else the harness root, as the
/// item form resolves a campaign; <c>--project</c>, by its id (a <c>prj_</c> value) or its slug, is the project's, else
/// the root; neither is the project of the folder the command runs in, found by its nearest marker as every verb that
/// takes a project finds it (<see cref="WorkingFolder.FillProjectAsync"/>, said on stderr, never a campaign), else the
/// root. The root is a place only when it is a folder with the root's marker. Nothing falls back to the home folder: a
/// folder named and not recorded is said, never replaced by another.
/// </para>
/// <para>
/// The program: <c>--host</c> names a host, else the preference <c>agent.interactive_host</c> does, else Claude Code, found
/// on the search path as the item form finds it; <c>--shell</c> is the system's plain shell (<see cref="TerminalPlan.Shell"/>).
/// The terminal is the one the item form starts, through the same seam, so the data directory reaches it as it reaches
/// the item form's: inherited on Windows and Linux, exported by the script on macOS.
/// </para>
/// <para>
/// Exit codes are the CLI's: 0 the terminal started (or, for a dry run, would be), 1 refused — by the runtime, printed as
/// it came, or by this verb, printed on stdout in the API's error envelope with one of the codes <see cref="CliErrors"/> names for this form, and said on stderr
/// — and 3 no runtime to ask.
/// </para>
/// </remarks>
public static class FolderSessionOpener
{
    /// <summary>Opens the session; <paramref name="host"/> null and <paramref name="shell"/> false mean the preference names the host.</summary>
    public static async Task<int> OpenAsync(
        CliEnvironment env, string? campaignId, string? project, AgentHostKind? host, bool shell, bool dryRun, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var (resolved, folder, scriptName) = await ResolveAsync(env, campaignId, project, cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return resolved;
        }

        IReadOnlyList<string>? program = null;
        string? hostWord = null;
        if (!shell)
        {
            if (host is null)
            {
                var (preferenceExit, word, said) = await SessionOpener.PreferredHostAsync(env, cancellationToken).ConfigureAwait(false);
                if (said)
                {
                    return preferenceExit;
                }

                if (preferenceExit == ExitCodes.ApiError)
                {
                    return Refuse(env, CliErrors.PreferenceUnreadable, $"The preference {SessionOpener.PreferenceKey} could not be read, so no host was chosen and nothing was started; --host <host> names one, or --shell opens a plain shell.");
                }

                host = word is null ? SessionOpener.DefaultHost : AgentHosts.Parse(word);
                if (host is null)
                {
                    return Refuse(
                        env,
                        CliErrors.HostUnknown,
                        $"The preference {SessionOpener.PreferenceKey} names '{word}', which this jason does not know as a host; it knows {AgentHosts.Vocabulary}. Nothing was started. {SessionOpener.ChooseAnother}");
                }
            }

            hostWord = AgentHosts.Word(host.Value);
            var (found, lookup) = SessionOpener.HostProgram(env, host.Value);
            if (found is null)
            {
                return Refuse(env, CliErrors.HostNotFound, SessionOpener.NotFound(hostWord, AgentHosts.Of(host.Value).CommandWord, lookup));
            }

            program = found;
        }

        if (!Directory.Exists(folder.Path))
        {
            return Refuse(env, CliErrors.FolderMissing, SessionOpener.Missing(folder));
        }

        var terminals = env.Terminals;
        var terminal = terminals is null
            ? null
            : TerminalPlan.Find(terminals.Platform, terminals.SystemDirectory, SessionOpener.Variable(env, "TERMINAL"), SessionOpener.SearchPath(env));
        var what = hostWord ?? "a shell";
        if (terminals is null || terminal is null)
        {
            return Refuse(env, CliErrors.TerminalNotFound, $"No terminal was found to start {what} in, so nothing was started; in a terminal of your own, go to {folder.Path}.");
        }

        if (shell)
        {
            program = TerminalPlan.Shell(terminals.Platform, terminals.SystemDirectory, SessionOpener.Variable(env, "SHELL"));
            if (program is null)
            {
                return Refuse(env, CliErrors.ShellNotFound, "Windows PowerShell is not in the system folder, where it is always looked for, so nothing was started.");
            }
        }

        var launch = TerminalPlan.Compose(terminals.Platform, terminal, folder.Path, program!, scriptName, SessionOpener.DataDirectory(env));
        if (!dryRun && !terminals.Start(launch, out var problem))
        {
            return Refuse(env, CliErrors.TerminalStartFailed, $"The terminal {launch.Terminal} did not start ({problem}), so nothing was started.");
        }

        env.Out.WriteLine(human
            ? $"{(dryRun ? "Would start" : "Started")} {what} in {launch.Terminal} at {folder.Path}."
            : Printed(folder, hostWord, launch, dryRun, program![0]));
        return ExitCodes.Success;
    }

    /// <summary>
    /// The folder, by the reads the flags call for, and the name macOS's script takes from its level. A refusal the runtime
    /// gave is printed as it came; no runtime is the exit code the read ended with.
    /// </summary>
    private static async Task<(int Exit, PromptFolder? Folder, string ScriptName)> ResolveAsync(CliEnvironment env, string? campaignId, string? project, CancellationToken cancellationToken)
    {
        string? projectId = null;
        string? slug = null;
        if (campaignId is not null)
        {
            var (exit, campaign) = await ReadAsync<CampaignDto>(env, Operations.CampaignGet, RequestBody.Empty().Set("campaign_id", campaignId), cancellationToken).ConfigureAwait(false);
            if (campaign is null)
            {
                return (exit, null, string.Empty);
            }

            if (campaign.FolderPath is { Length: > 0 } campaignFolder)
            {
                return (ExitCodes.Success, new PromptFolder(PromptFolderLevel.Campaign, campaignFolder), "campaign");
            }

            projectId = campaign.ProjectId is { Length: > 0 } inProject ? inProject : null;
        }
        else if (project is not null)
        {
            (projectId, slug) = project.StartsWith("prj_", StringComparison.Ordinal) ? (project, null) : ((string?)null, project);
        }
        else
        {
            // Named by neither flag: the project of the folder the command runs in, as every verb that takes a project and
            // is given none takes it, said on stderr; a campaign's folder gives the campaign's project, never the campaign.
            var fromFolder = RequestBody.Empty();
            if ((await WorkingFolder.FillProjectAsync(env, fromFolder, WorkingFolder.ByProjectOrCampaign, cancellationToken).ConfigureAwait(false)).ExitCode is { } stopped)
            {
                return (stopped, null, string.Empty);
            }

            projectId = fromFolder["project_id"]?.GetValue<string>();
        }

        if (projectId is not null || slug is not null)
        {
            var (exit, read) = await ReadAsync<ProjectDto>(env, Operations.ProjectGet, RequestBody.Empty().Set("project_id", projectId).Set("slug", slug), cancellationToken).ConfigureAwait(false);
            if (read is null)
            {
                return (exit, null, string.Empty);
            }

            if (read.FolderPath is { Length: > 0 } projectFolder)
            {
                return (ExitCodes.Success, new PromptFolder(PromptFolderLevel.Project, projectFolder), "project");
            }
        }

        var (infoExit, info) = await ReadAsync<SystemInfoResponse>(env, Operations.SystemInfo, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return (infoExit, null, string.Empty);
        }

        if (info.Harness is { RootState: HarnessRootState.Marked, Root: { Length: > 0 } root })
        {
            return (ExitCodes.Success, new PromptFolder(PromptFolderLevel.HarnessRoot, root), "harness_root");
        }

        var named = campaignId is not null ? "The campaign and its project have no folder recorded, and " : projectId is not null || slug is not null ? "The project has no folder recorded, and " : string.Empty;
        var why = RootNotAPlace(info.Harness);
        return (Refuse(env, CliErrors.FolderNotRecorded, $"{named}{(named.Length == 0 ? char.ToUpperInvariant(why[0]) + why[1..] : why)}, so there is nowhere to start and nothing was started."), null, string.Empty);
    }

    /// <summary>
    /// Why the harness root is not a place to start, by what stands there: only where nothing, or an unmarked folder, is
    /// there does the first project or campaign made make it one, so only there is that said.
    /// </summary>
    private static string RootNotAPlace(HarnessInfo? harness) => harness switch
    {
        null => "this runtime does not say where the harness root is",
        { RootState: HarnessRootState.Absent } => $"nothing is at the harness root {harness.Root} yet (the first project or campaign made makes it)",
        { RootState: HarnessRootState.Unmarked } => $"the harness root {harness.Root} is a folder with no harness marker yet (the first project or campaign made marks it)",
        { RootState: HarnessRootState.Taken } => $"the folder at the harness root {harness.Root} holds a marker that names something other than a root",
        { RootState: HarnessRootState.NotAFolder } => $"a file stands at the harness root {harness.Root}, where its folder belongs",
        { RootState: HarnessRootState.Unreadable } => $"the harness root {harness.Root} cannot be listed, or its marker read, by this account",
        _ => $"the harness root {harness.Root} is not a folder with the harness's marker",
    };

    /// <summary>One read: the answer, or null with the exit code, the runtime's own refusal printed as it came.</summary>
    private static async Task<(int Exit, T? Answer)> ReadAsync<T>(CliEnvironment env, string operation, JsonObject body, CancellationToken cancellationToken)
        where T : class
    {
        var (exit, response) = await OperationRunner.SendAsync(env, operation, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return (exit, null);
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(OperationRunner.ErrorText(response));
            return (ExitCodes.ApiError, null);
        }

        if (RenderText.Read<T>(response.Body) is { } answer)
        {
            return (ExitCodes.Success, answer);
        }

        return (Refuse(env, CliErrors.RuntimeError, $"The runtime's answer to {operation} could not be read, so nothing was started."), null);
    }

    private static int Refuse(CliEnvironment env, string code, string message)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: false));
        env.Error.WriteLine(message);
        return ExitCodes.ApiError;
    }

    /// <summary>
    /// The answer: <c>program</c> is the program's path, as in the item form's answer (the shell's under <c>--shell</c>),
    /// and <c>shell</c> says which of the two was started.
    /// </summary>
    private static string Printed(PromptFolder folder, string? host, TerminalLaunch launch, bool dryRun, string program)
    {
        var printed = new JsonObject
        {
            ["kind"] = "folder",
            ["folder"] = new JsonObject { ["level"] = SessionOpener.Level(folder.Level), ["path"] = folder.Path },
            ["host"] = host,
            ["shell"] = host is null,
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
}
