using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Session;

/// <summary>
/// The act behind <c>jason session open --setup</c>: a new terminal window at this installation's role skills folder,
/// running the person's own agent host with the plugins folder beside it where the host takes a second folder, and the
/// setup brief (<see cref="SetupBrief"/>) as its first input. Of the runtime it asks only <c>preference.list</c>, and only
/// when no <c>--host</c> is given; it starts no runtime and writes nothing of Jason's.
/// </summary>
/// <remarks>
/// <para>
/// The folders are <see cref="SetupInfo.From"/>'s, the one place a reader takes the layout from, and nothing here composes
/// a path of it: a layout that moves a folder is followed with no edit. A role skills folder that is not there is refused,
/// and a plugins folder that is not there is not handed to the host; neither is made.
/// </para>
/// <para>
/// The brief names the skill <see cref="SetupBrief.SkillName"/> rather than carrying it, so the answer says whether that
/// skill is deployed for the host it opens: by the receipt <c>jason skills install</c> keeps in the one skills root the
/// host reads under the home folder, read locally. A receipt that cannot be read says nothing either way.
/// </para>
/// <para>
/// Exit codes are the CLI's: 0 the terminal started (or, for a dry run, would be), 1 refused — printed on stdout in the
/// API's error envelope and said on stderr — and 3 no runtime to ask for the preference.
/// </para>
/// </remarks>
public static class SetupSessionOpener
{
    /// <summary>The level the answer gives the folder the session opens at, and the name macOS's script takes.</summary>
    public const string Level = "role_skills";

    /// <summary>Why the plugins folder is not beside the session: the host takes no second folder, or it is not there.</summary>
    public const string HostTakesNoFolder = "host_takes_no_folder";

    /// <inheritdoc cref="HostTakesNoFolder"/>
    public const string FolderNotThere = "folder_missing";

    /// <summary>Opens the session.</summary>
    public static async Task<int> OpenAsync(CliEnvironment env, AgentHostKind? named, bool dryRun, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        // The data directory's layout is checked before this runs: LayoutGatedVerbs refuses `session open --setup` where
        // the stamp names a layout newer than this build runs or cannot be read, before the one read of the layout below,
        // so that no folder of a layout this build does not know is ever named.
        var setup = SetupInfo.From(env.Paths);
        var folder = setup.RoleSkillsDirectory;
        if (!Directory.Exists(folder))
        {
            return SessionOpener.Refuse(
                env,
                CliErrors.FolderMissing,
                $"The role skills folder {folder} is not there, so nothing was started; `jason skills install --roles-only` places the role skills there.");
        }

        var (chosenExit, chosen) = await SessionOpener.ChooseHostAsync(
            env,
            named,
            $"The preference {SessionOpener.PreferenceKey} could not be read, so no host was chosen and nothing was started; --host <host> names one.",
            refusedFor: null,
            cancellationToken).ConfigureAwait(false);
        if (chosen is null)
        {
            return chosenExit;
        }

        var host = chosen.Host;
        var hostWord = AgentHosts.Word(host);
        var plugins = setup.PluginsDirectory;
        var notAdded = AgentHosts.Of(host).InteractiveFolderFlag is null ? HostTakesNoFolder
            : !Directory.Exists(plugins) ? FolderNotThere
            : null;
        var skill = Skill(env, host);

        var terminals = env.Terminals;
        var terminal = terminals is null
            ? null
            : TerminalPlan.Find(terminals.Platform, terminals.SystemDirectory, SessionOpener.Variable(env, "TERMINAL"), SessionOpener.SearchPath(env));
        if (terminals is null || terminal is null)
        {
            return SessionOpener.Refuse(
                env,
                CliErrors.TerminalNotFound,
                $"No terminal was found to start {hostWord} in, so nothing was started; `jason session open --setup --dry-run` prints the command and its brief, to run in a terminal of your own.");
        }

        IReadOnlyList<string> program = [.. chosen.Program, .. AgentHosts.InteractiveArguments(host, SetupBrief.For(hostWord, skill.Deployed == true), notAdded is null ? plugins : null)];
        var launch = TerminalPlan.Compose(terminals.Platform, terminal, folder, program, "setup", SessionOpener.DataDirectory(env));
        if (!dryRun && !terminals.Start(launch, out var problem))
        {
            return SessionOpener.Refuse(env, CliErrors.TerminalStartFailed, $"The terminal {launch.Terminal} did not start ({problem}), so nothing was started.");
        }

        if (human)
        {
            var started = $"{(dryRun ? "Would start" : "Started")} {hostWord} in {launch.Terminal} at the role skills folder {folder}";
            env.Out.WriteLine(notAdded switch
            {
                null => $"{started}, with the plugins folder {plugins} beside it.",
                HostTakesNoFolder => $"{started}; {hostWord} takes no second folder, so the plugins folder {plugins} is not beside it.",
                _ => $"{started}; the plugins folder {plugins} is not there yet, so it is not beside it.",
            });
            switch (skill.Deployed)
            {
                case false:
                    env.Out.WriteLine($"jason skills install has not deployed the skill {SetupBrief.SkillName} for {hostWord}: `jason skills install --host {hostWord}` deploys it.");
                    break;
                case null:
                    env.Out.WriteLine($"Whether the skill {SetupBrief.SkillName} is deployed for {hostWord} could not be read; `jason skills install --host {hostWord}` deploys it.");
                    break;
            }
        }
        else
        {
            env.Out.WriteLine(Printed(folder, plugins, notAdded, hostWord, skill, chosen.Program[0], launch, dryRun));
        }

        return ExitCodes.Success;
    }

    /// <summary>Whether the skill the brief names is deployed for the host, and the skills root that was read.</summary>
    private sealed record SkillState(bool? Deployed, string? Root);

    /// <summary>
    /// The skill in the one skills root the host reads under the home folder, by the receipt a deployment keeps there: a
    /// file of the receipt that names the skill, and that file on disk. Null where the receipt cannot be read or this
    /// environment knows no skills roots.
    /// </summary>
    private static SkillState Skill(CliEnvironment env, AgentHostKind host)
    {
        var root = env.Harnesses?.Recorded().FirstOrDefault(harness => harness.Hosts.Contains(host))?.Directory;
        if (root is null)
        {
            return new SkillState(null, null);
        }

        var file = SetupBrief.SkillName + "/SKILL.md";
        try
        {
            var record = SkillsRecord.Read(root);
            var deployed = record is not null
                && record.Packs.Any(pack => pack.Files.Any(written => string.Equals(written.Path, file, StringComparison.Ordinal)))
                && File.Exists(AgentHosts.Under(root, file));
            return new SkillState(deployed, root);
        }
        catch (Exception exception) when (exception is SkillsRecordUnreadable or IOException or UnauthorizedAccessException)
        {
            return new SkillState(null, root);
        }
    }

    /// <summary>
    /// The answer: the folder it opens at, the plugins folder and whether it was added (and if not, why), the host, the
    /// skill the brief names and whether it is deployed for that host, the program's path, the terminal, and on a dry run
    /// the whole command.
    /// </summary>
    private static string Printed(string folder, string plugins, string? notAdded, string host, SkillState skill, string program, TerminalLaunch launch, bool dryRun)
    {
        var pluginsFolder = new JsonObject { ["path"] = plugins, ["added"] = notAdded is null };
        if (notAdded is not null)
        {
            pluginsFolder["reason"] = notAdded;
        }

        var printed = new JsonObject
        {
            ["kind"] = "setup",
            ["folder"] = new JsonObject { ["level"] = Level, ["path"] = folder },
            ["plugins_folder"] = pluginsFolder,
            ["host"] = host,
            ["skill"] = new JsonObject { ["name"] = SetupBrief.SkillName, ["deployed"] = skill.Deployed, ["root"] = skill.Root },
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
