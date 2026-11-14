using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Autostart;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Human;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Contracts.Programs;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;

namespace Jason.Cli.Status;

/// <summary>
/// The checks themselves, one method each. Every one of them answers a fact and, where there is one, the
/// command that repairs it.
/// </summary>
/// <remarks>
/// <para>
/// Half of what this verb reports is not the runtime's to know — an executable on PATH, another vendor's CLI,
/// files in a folder in the operator's home directory — which is why no API operation can answer "am I ready
/// to work?" and why this is the one command that is not one-to-one with one.
/// </para>
/// <para>
/// Nothing here starts a process except through <see cref="IProgramRunner"/>, and nothing here reads a log.
/// Both are held by guards, because a status verb that grows a probe has become a diagnostics verb.
/// </para>
/// </remarks>
internal static class StatusChecks
{
    /// <summary>
    /// Every repair a check can print, composed in one place — and there are two kinds of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most are <c>jason</c> commands, and a guard types every one: a check whose repair is a command this
    /// build does not answer teaches a person the tool is broken at the moment they most need it not to be.
    /// They were null until the verb existed, deliberately, so that the commit shipping a verb is the commit
    /// that turns its repairs on and proves them.
    /// </para>
    /// <para>
    /// One of them is not a <c>jason</c> command and cannot be. Putting this executable on the PATH is the
    /// shell's own act and no verb of this product's performs it; inventing <c>jason path add</c> so that a
    /// guard's sentence could stay short would be a verb that exists for a test. So a repair may also be a
    /// line for this machine's own shell, and the guard checks that kind by rules of its own: it is not
    /// empty, it names a directory that is really there, and it is written for the platform it was composed
    /// on.
    /// </para>
    /// <para>
    /// What holds of both kinds is that every repair is composed <em>here</em>. That sentence was written
    /// before it was true: the PATH repair arrived as a method further down this file, outside the class the
    /// guard slices, and three checks were passing their line as a literal that happened to duplicate a
    /// constant beside it. Neither was visible to a guard claiming to reach every repair, which is the defect
    /// that guard exists to prevent — so a second guard now reads every <c>new StatusCheck</c> in these
    /// sources and refuses a repair spelled anywhere but here.
    /// </para>
    /// </remarks>
    private static class Repair
    {
        public const string Start = "jason runtime start";

        public const string Restart = "jason runtime restart";

        /// <summary>
        /// The repair for an update that stopped part-way: the apply carries it on from the step its ledger names, starts the
        /// runtime itself and finishes what the update still owes. Never <see cref="Start"/>, which would run the new build
        /// outside the update.
        /// </summary>
        public const string FinishUpdate = "jason update apply";

        public const string ReloadPlugins = "jason plugin reload";

        /// <summary>
        /// Printed in a fact, not as a repair: where an unavailable plugin's problems are read whole, the program each one
        /// is about among them, since the fact quotes their codes alone.
        /// </summary>
        public const string ListPlugins = "jason plugin list";

        public const string EnableAutostart = "jason runtime autostart enable";

        /// <summary>The harness half as well as the role half: the repair for an agent harness nothing was deployed to.</summary>
        public const string Install = "jason skills install";

        /// <summary>
        /// The role skills alone, into the runtime's own directory — the repair for the required check, which
        /// must never cost the optional half.
        /// </summary>
        /// <remarks>
        /// It was <see cref="Install"/>, which also writes the interactive and business packs into the agent's
        /// own configuration. So an operator who would not let Jason write there — an agent told to change
        /// nothing outside the install and data directories, which is what the README's prompt tells it —
        /// could not repair a required check without breaking that rule, and stopped rather than reach ready.
        /// </remarks>
        public const string InstallRoles = "jason skills install --roles-only";

        /// <summary>
        /// <see cref="Install"/> into the one root a host reads: printed in a fact, not as a repair, for a detected root no
        /// deployment was made in, which <see cref="UpdateSkills"/> does not carry.
        /// </summary>
        public static string InstallForHost(string host) => $"{Install} --host {host}";

        /// <summary>
        /// The release's UI, placed as the default build: the repair where none is installed, and where the default one is
        /// another release's. A build of the person's own in <c>ui/custom</c> is never one this names.
        /// </summary>
        public const string InstallUi = "jason ui install";

        /// <summary>
        /// Deliberately the same as <see cref="InstallRoles"/>. Overwriting is not what repairs a role whose
        /// directory is missing a SKILL.md or is over the cap -- a plain install replaces it -- and --force
        /// overwrites every edited file in every root of the plan, so printing it as the repair for any role
        /// problem would cost an operator unrelated work for a problem that did not need it.
        /// </summary>
        public const string Reinstall = InstallRoles;

        /// <summary>
        /// <see cref="InstallRoles"/>, from the source a deployment came from wherever that is not this build's
        /// default, or the one builds before 0.33.0 had: the repair for role skills at neither the runtime's tag nor the one
        /// this build installs.
        /// </summary>
        /// <remarks>
        /// Bare, it would move a deployment taught from a mirror, or from a repository on this machine, to the default
        /// repository — or fail there, where the tag it asks for may not exist yet — and its record would then say it
        /// had always come from there. The record names its source, and that is the one value a repair here is
        /// composed from: a guard reads this as the one member handed anything but the environment, and types what it
        /// composes.
        /// </remarks>
        public static string InstallRolesFrom(string source) =>
            SkillsSource.IsDefault(source) ? InstallRoles : $"{InstallRoles} --source {source}";

        /// <summary>
        /// Every deployment Jason made, carried to the ref this build carries: the repair for an agent's packs not at
        /// this build's tag.
        /// </summary>
        /// <remarks>
        /// Printed for the person and never run by an agent. The check is optional, and the roots it rewrites are the
        /// agent's own configuration, which is the person's to allow.
        /// </remarks>
        public const string UpdateSkills = "jason skills update";

        /// <summary>
        /// The repair for a profile whose current revision this build's rules for its host refuse: read what is refused. The
        /// act is <c>jason profile update</c>, said in the fact.
        /// </summary>
        public static string ReadProfile(string profile) => $"jason profile get {profile}";

        /// <summary>
        /// The repair for a profile that names a host this machine does not have: the profile, moved to a host it has, with
        /// that host's own program. The runtime refuses a move that keeps the program written for another host, and a
        /// profile made as the handbook says carries one. A bundle's report names no profile, so there it is
        /// <c>&lt;name&gt;</c>.
        /// </summary>
        public static string MoveProfile(string profile, AgentHostKind host) =>
            $"jason profile update {profile} --host {AgentHosts.Word(host)} --program {AgentHosts.Of(host).CommandWord}";

        /// <summary>
        /// <see cref="Install"/> for one pack, from the source a deployment came from wherever that is not this build's
        /// default, or the one builds before 0.33.0 had: the line that moves a harness deployment its record pins to this build's tag.
        /// </summary>
        /// <remarks>
        /// Printed in a fact, not as a repair, because the check passes: the record pins its ref, made with --ref or
        /// marked so by an earlier <c>skills update</c>, and <see cref="UpdateSkills"/> keeps a pinned ref. An install
        /// that names no ref writes the pack again at this build's tag, and its record no longer pins it. Its reach is
        /// the pack's: for the runtime pack that includes the role half in the runtime's own root, which no flag
        /// separates.
        /// </remarks>
        public static string InstallPackFrom(string pack, string source) =>
            SkillsSource.IsDefault(source) ? $"{Install} --pack {pack}" : $"{Install} --pack {pack} --source {source}";

        /// <summary>
        /// The repair that is not a <c>jason</c> command: the line putting this executable's directory on
        /// this account's PATH, spelled as this product's own installer spells it.
        /// </summary>
        /// <remarks>
        /// Null where nothing named an executable, or where this account has no home to write into. There is
        /// then no directory to name, and a repair reading "add  to your PATH" is worse than none.
        /// </remarks>
        public static string? OnPath(CliEnvironment env)
        {
            // Never `Environment.ProcessPath`: under `dotnet jason.dll` that is the muxer, and this would
            // print "put C:\Program Files\dotnet on your PATH" as the way to make `jason` typeable -- and under
            // `dotnet run` it is the build's own launcher, and this printed the build directory.
            var executable = env.InstallPath ?? SelfExecutable.InstalledImage;
            if (executable is null || System.IO.Path.GetDirectoryName(executable) is not { Length: > 0 } directory)
            {
                return null;
            }

            if (OperatingSystem.IsWindows())
            {
                return PathEntry.RegistryCommand(directory);
            }

            // Into the profile this shell reads at login, and exported for the shell it is typed in. The bare
            // export line lasts exactly one shell: printed as a repair it is advice that appears to have
            // worked, and it left the two platforms' repairs differing in kind with neither saying so.
            // And into the file an interactive shell reads, where the shell has one, as the installer writes it: a
            // desktop's terminal starts no login shell, and the login profile alone left it without `jason`.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var shell = Environment.GetEnvironmentVariable("SHELL");
            return home.Length == 0
                ? null
                : PathEntry.AppendCommand(directory, PathEntry.LoginProfile(home, shell), PathEntry.InteractiveProfile(home, shell));
        }
    }

    /// <summary>
    /// The one argument the provider check passes. The <em>program</em> is named by whoever runs this and is
    /// never named here: A7(a) makes vendor neutrality a property of the sources, and a guard reads it off
    /// them. Jason knowing the name of somebody's provider CLI is exactly the coupling that rule forbids.
    /// </summary>
    public static IReadOnlyList<string> ProviderArguments => ["--version"];

    /// <summary>
    /// How long the provider is given to answer. A check that hangs is a status verb that never answers, which
    /// is worse than one that says it could not tell.
    /// </summary>
    public static TimeSpan ProviderTimeout => TimeSpan.FromSeconds(10);

    public static Task<StatusReport> ComposeAsync(CliEnvironment env, string? providerCli, CancellationToken cancellationToken) =>
        ComposeAsync(env, providerCli, profileNames: true, cancellationToken);

    /// <summary>
    /// The report, with the profiles the <c>agent_hosts</c> check reads named by their names, or, without
    /// <paramref name="profileNames"/>, counted by host: a profile's name is the person's, and the doctor's bundle never
    /// carries one.
    /// </summary>
    public static async Task<StatusReport> ComposeAsync(CliEnvironment env, string? providerCli, bool profileNames, CancellationToken cancellationToken)
    {
        var checks = new List<StatusCheck>();

        var descriptor = new DescriptorReader(env.Paths).Read();
        SystemInfoResponse? info = null;
        RuntimeClient? client = null;
        try
        {
            if (descriptor is not null)
            {
                client = new RuntimeClient(descriptor, env.AttemptFromEnvironment(), env.Error, env.HttpHandler, env.Clock, confined: env.Confined());
                info = await ReadAsync<SystemInfoResponse>(client, Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
            }

            var unfinished = Contracts.Update.UnfinishedUpdate.Read(env.Paths);
            checks.Add(Runtime(descriptor, info, unfinished));
            checks.Add(RuntimeVersion(info, SkillsSource.BuildVersion(env)));
            checks.Add(Migrations(info));
            checks.Add(Plugins(info));
            checks.Add(await RoleSkillsAsync(env, client, info, cancellationToken).ConfigureAwait(false));
            checks.Add(Path(env));
            checks.Add(UpdateInFlight(unfinished));

            checks.Add(ProviderPlugin(info));
            checks.Add(PluginPackage(info));
            // One plugin.list answer, read once and passed on: the two checks it serves describe the same snapshot.
            var asked = client is not null && info is not null;
            var registry = asked ? await ReadAsync<PluginRegistryDto>(client!, Operations.PluginList, cancellationToken).ConfigureAwait(false) : null;
            checks.Add(PluginAvailability(asked, registry));
            checks.Add(PluginSubscriptions(asked, registry));
            checks.Add(PluginGrants(asked, registry));
            var routes = client is null ? null : await ReadAsync<RoutesDto>(client, Operations.RouteList, cancellationToken).ConfigureAwait(false);
            checks.Add(Route(routes));
            checks.Add(Binding(routes));
            checks.Add(await ProviderCliAsync(env, providerCli, cancellationToken).ConfigureAwait(false));
            checks.Add(Harnesses(env));
            var profiles = client is null ? default : await ProfilesAsync(client, cancellationToken).ConfigureAwait(false);
            checks.Add(AgentHostsCheck(env.Harnesses?.Detect(), profiles.Profiles, profileNames, profiles.Unread));
            checks.Add(Autostart(env));
            checks.Add(Notifications(info));
            checks.Add(UiModule(info, SkillsSource.BuildVersion(env)));
            checks.Add(HarnessRoot(info));
            checks.Add(SendModes(info));
        }
        finally
        {
            client?.Dispose();
        }

        return new StatusReport(checks.All(check => !check.Required || check.State == CheckState.Ok), checks);
    }

    private static StatusCheck Runtime(Contracts.Discovery.RuntimeDescriptor? descriptor, SystemInfoResponse? info, Contracts.Update.UnfinishedUpdate? unfinished)
    {
        // First, and whether a runtime answers or not, once the update has touched the install path: its last steps are
        // the apply's, and a runtime started any other way runs outside them. A ledger that cannot say which step it is at
        // fails the same way, with no repair this verb can name. Before that step nothing has been replaced, and the
        // runtime check is the runtime's own; the optional update check names what is waiting.
        if (unfinished is { Replacing: true })
        {
            return unfinished.Ledger is { } ledger
                ? new StatusCheck(
                    "runtime",
                    true,
                    CheckState.Failed,
                    $"{unfinished.Said()}: `{Repair.FinishUpdate}` carries it on and starts the runtime itself, and `jason update rollback` goes back to {ledger.FromVersion}.",
                    Repair.FinishUpdate)
                : new StatusCheck("runtime", true, CheckState.Failed, $"{unfinished.Said()}.", null);
        }

        if (descriptor is null)
        {
            return new StatusCheck("runtime", true, CheckState.Failed, "No runtime is running: there is no endpoint descriptor to ask.", Repair.Start);
        }

        return info is null
            ? new StatusCheck("runtime", true, CheckState.Failed, $"A descriptor names {descriptor.BaseUrl} and nothing answered there.", Repair.Start)
            : new StatusCheck("runtime", true, CheckState.Ok, $"Running: version {info.RuntimeVersion}, pid {info.Pid.ToString(CultureInfo.InvariantCulture)}, data directory {info.DataDir}.", null);
    }

    /// <summary>
    /// Whether an update is waiting to be resumed. Optional: before the step that moves the installed build aside nothing
    /// has been replaced, and an apply that could not download stops there with the installation as it was; failing a
    /// required check would stop every agent that branches on <c>ready</c> until the network came back. From that step
    /// on the required runtime check fails as well, and a ledger that cannot be read is said there.
    /// </summary>
    private static StatusCheck UpdateInFlight(Contracts.Update.UnfinishedUpdate? unfinished) => unfinished switch
    {
        null => new StatusCheck("update", false, CheckState.Ok, "No update is in flight.", null),
        { Ledger: null } => new StatusCheck("update", false, CheckState.Unknown, "The update ledger cannot be read; the runtime check says why.", null),
        { Replacing: false } => new StatusCheck(
            "update",
            false,
            CheckState.Failed,
            $"{unfinished.Said()}; nothing has been replaced, and `{Repair.FinishUpdate}` resumes it.",
            Repair.FinishUpdate),
        _ => new StatusCheck(
            "update",
            false,
            CheckState.Failed,
            $"{unfinished.Said()}; the runtime check says the two ways on.",
            Repair.FinishUpdate),
    };

    /// <summary>
    /// Whether the runtime answering is the build this CLI is. Required, because a binary can change under a running
    /// runtime — a package manager's upgrade, a file copied over by hand — and the runtime goes on running the old one,
    /// starting every plugin host from its own image, which under Homebrew the upgrade has deleted: provider work then
    /// fails at launch until the runtime is restarted, and <c>ready</c> must not say yes to that.
    /// </summary>
    /// <remarks>
    /// The whole string, compared as it is: the pre-release part and the build metadata included. A development build and
    /// the release it is on its way to are two builds, and so are two builds of one version. The strings carry build
    /// metadata, so this is identity, not precedence: both sides are <see cref="JasonVersion.Current"/> of the build that
    /// wrote them, and a runtime test holds the runtime's side to it. A runtime that did not
    /// answer is not asked, and is unknown with the start the <c>runtime</c> check already asks for: one cause, failed
    /// once.
    /// </remarks>
    private static StatusCheck RuntimeVersion(SystemInfoResponse? info, string build)
    {
        if (info is null)
        {
            return new StatusCheck("runtime_version", true, CheckState.Unknown, "The runtime did not answer, so which build it is was not asked.", Repair.Start);
        }

        return string.Equals(info.RuntimeVersion, build, StringComparison.Ordinal)
            ? new StatusCheck("runtime_version", true, CheckState.Ok, $"The runtime running is {build}, the build this CLI is.", null)
            : new StatusCheck("runtime_version", true, CheckState.Failed, $"The runtime running is {info.RuntimeVersion}; this CLI is {build}.", Repair.Restart);
    }

    private static StatusCheck Migrations(SystemInfoResponse? info)
    {
        if (info is null)
        {
            return new StatusCheck("migrations", true, CheckState.Unknown, "The runtime did not answer, so its database was not asked about.", Repair.Start);
        }

        var applied = info.Database.AppliedMigrations.Count;
        return applied == 0
            ? new StatusCheck("migrations", true, CheckState.Failed, "The database has no migrations applied.", Repair.Restart)
            : new StatusCheck("migrations", true, CheckState.Ok, string.Create(CultureInfo.InvariantCulture, $"{applied} migrations applied."), null);
    }

    private static StatusCheck Plugins(SystemInfoResponse? info)
    {
        if (info is null)
        {
            return new StatusCheck("plugin_registry", true, CheckState.Unknown, "The runtime did not answer, so its plugin registry was not asked about.", Repair.Start);
        }

        // Zero plugins is alive. A registry that refused its last load is not, and that is the state worth
        // telling apart: nothing is loaded, on purpose, and nothing will run until somebody looks at why.
        return info.Plugins.LastReloadActivated == false
            ? new StatusCheck("plugin_registry", true, CheckState.Failed, "The last plugin load was rejected, so nothing is active.", Repair.ReloadPlugins)
            : new StatusCheck(
                "plugin_registry",
                true,
                CheckState.Ok,
                string.Create(CultureInfo.InvariantCulture, $"Alive: {info.Plugins.ActiveCount} plugins active, snapshot {info.Plugins.SnapshotId}."),
                null);
    }

    /// <summary>
    /// The harness root the runtime uses and what stands there, as the runtime reports it: where the person's files live is
    /// the runtime's to say, so nothing here works the root out. Optional, because the root is made at the first project or
    /// campaign; failed where the runtime can tell that first one would be refused, and no more, and the fact says what to
    /// change, since no command does it.
    /// </summary>
    private static StatusCheck HarnessRoot(SystemInfoResponse? info)
    {
        const string Name = "harness_root";
        if (info is null)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so the harness root it uses was not asked.", null);
        }

        if (info.Harness is not { } harness)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime is older than this CLI and does not say which harness root it uses.", null);
        }

        var root = harness.Root;
        return harness.RootState switch
        {
            HarnessRootState.Absent => new StatusCheck(Name, false, CheckState.Ok, $"The harness root is {root}; it is made at the first project or campaign.", null),
            HarnessRootState.Marked => new StatusCheck(Name, false, CheckState.Ok, $"The harness root is {root}, with its marker.", null),
            HarnessRootState.Unmarked => new StatusCheck(Name, false, CheckState.Ok, $"The harness root is {root}, a folder with no marker yet; the first project or campaign takes it as the root and changes nothing in it.", null),
            HarnessRootState.Taken => new StatusCheck(Name, false, CheckState.Failed, $"The harness root {root} is marked as something else, so the first project or campaign is refused until that is repaired: set Harness:Root to another folder, or move that folder away.", null),
            HarnessRootState.NotAFolder => new StatusCheck(Name, false, CheckState.Failed, $"A file stands at the harness root {root}, so the first project or campaign is refused until that is repaired: set Harness:Root to a folder, or move the file away.", null),
            HarnessRootState.Unreadable => new StatusCheck(Name, false, CheckState.Failed, $"The harness root {root} cannot be listed or read by this account, so the first project or campaign is refused until that is repaired: let this account read and write that folder, or set Harness:Root to one it can.", null),

            // Every state the runtime says has its arm above; the words are read by name, never as numbers, so no other
            // value is ever read from an answer.
            _ => throw new ArgumentOutOfRangeException(nameof(info), harness.RootState, "There is no such harness root state."),
        };
    }

    /// <summary>
    /// How the installation's sends go: for a contact who has not answered and for one who has, the mode in force and where
    /// it comes from, as the runtime reports it. Optional and never a failure: every message waiting for a person is a way
    /// to work, so this never decides readiness. Who chose a mode is the person's name, which this check never carries;
    /// <c>jason preference list</c> says it.
    /// </summary>
    private static StatusCheck SendModes(SystemInfoResponse? info)
    {
        const string Name = "send_modes";
        if (info is null)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so how its sends go was not asked.", Repair.Start);
        }

        if (info.SendModes is not { } modes)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime is older than this CLI and does not say how its sends go.", null);
        }

        var fact = $"To a contact who has not answered: {ModeInWords(modes.Cold)}. To a contact who has: {ModeInWords(modes.Warm)}.";
        return new StatusCheck(Name, false, CheckState.Ok, fact, null) with
        {
            Details = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cold"] = Word(modes.Cold.Mode),
                ["cold_source"] = Word(modes.Cold.Source),
                ["warm"] = Word(modes.Warm.Mode),
                ["warm_source"] = Word(modes.Warm.Source),
            },
        };

        static string ModeInWords(SendModeInForce mode) =>
            (mode.Mode == SendMode.Automatic ? "sent without asking" : "every message waits for a person")
            + mode.Source switch
            {
                SendModeSource.Preference => ", as the installation's preference says",
                SendModeSource.Campaign => ", as the campaign's own says",
                _ => ", as nobody has chosen yet",
            };

        static string Word<TEnum>(TEnum value)
            where TEnum : struct, Enum =>
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
    }

    /// <summary>
    /// Which UI is served, or that none is. Optional: a runtime works without one, so this never decides readiness. The
    /// default build is the release's, so one from another release is named with the command that places this one's —
    /// where neither is a development build: a candidate is published with its own UI, a development runtime has no
    /// published UI to compare with, and a custom build is the
    /// person's own. And only while the runtime is this CLI's build, which is the version <c>jason ui install</c> places:
    /// between a package manager's upgrade and the restart the two differ, the restart is the fix, and
    /// <c>runtime_version</c> names it.
    /// </summary>
    private static StatusCheck UiModule(SystemInfoResponse? info, string build)
    {
        if (info is null)
        {
            return new StatusCheck("ui_module", false, CheckState.Unknown, "The runtime did not answer, so which UI it serves was not asked.", Repair.Start);
        }

        if (info.Ui is null)
        {
            return new StatusCheck("ui_module", false, CheckState.Absent, "No UI is installed.", Repair.InstallUi);
        }

        var ui = info.Ui;
        if (ui.Problem is not null)
        {
            return new StatusCheck("ui_module", false, CheckState.Failed, $"Serving {ui.Source} from {ui.Directory}, with a problem: {ui.Problem}", null);
        }

        var released = ui.Source == "default";
        if (!ui.ApiVersionMatches)
        {
            var built = $"Serving {ui.Source} {ui.Version} from {ui.Directory}, built for API {ui.ApiVersion}; this runtime speaks {info.ApiVersion}.";
            return released
                ? new StatusCheck("ui_module", false, CheckState.Failed, built, Repair.InstallUi)
                : new StatusCheck("ui_module", false, CheckState.Failed, built, null);
        }

        if (released
            && string.Equals(info.RuntimeVersion, build, StringComparison.Ordinal)
            && SemanticVersion.TryParse(info.RuntimeVersion, out var runtime) && !runtime.IsDevelopment
            && SemanticVersion.TryParse(ui.Version, out var served) && !served.IsDevelopment
            && served != runtime)
        {
            return new StatusCheck("ui_module", false, CheckState.Failed, $"Serving default {ui.Version} from {ui.Directory}; this runtime is {info.RuntimeVersion}.", Repair.InstallUi);
        }

        return new StatusCheck("ui_module", false, CheckState.Ok, $"Serving {ui.Source} {ui.Version} from {ui.Directory}; built for API {ui.ApiVersion}, this runtime's; {ui.Digest}.", null);
    }

    /// <summary>
    /// What the role-skills fact says, after the problems, of a refused skill of a role Jason does not ship: its name and
    /// the directory to correct it in, and no word of the runtime's about the problem. A bundle carries it only where it reads
    /// as this composes it.
    /// </summary>
    internal static string OwnRoleRefused(string role, string directory) =>
        $"'{role}' is no role Jason ships, so no command of Jason's repairs its skill: correct it or remove it from {directory}.";

    /// <summary>
    /// A skill problem's sentence with its code after it, in brackets, where the runtime gave one: the code is what a
    /// reader of a bundle has where the sentence is withheld, and what a reader of this fact can search for. A runtime
    /// from before the codes gives none, and its sentence stands alone.
    /// </summary>
    private static string Coded(string problem, string? code) => code is null ? problem : $"{problem} [{code}]";

    /// <summary>
    /// The roles a person added that no deployed skill teaches, each said as what it is: one with an entry command of its own
    /// runs that, and one without launches untaught unless a skill named after it is put in the role skills directory.
    /// </summary>
    private static string Own(IEnumerable<RoleDto> roles, string directory) =>
        string.Concat(roles.OrderBy(role => role.Name, StringComparer.Ordinal).Select(role => role.EntryCommand.Count > 0
            ? $" '{role.Name}' is a role of your own: it runs its own entry command, and no skill Jason ships teaches it."
            : $" '{role.Name}' is a role of your own, and no skill Jason ships teaches it: it launches untaught unless a skill named after it is in {directory}."));

    /// <summary>
    /// The skills the roles are taught from, reported, with the deployment their record names as fields a program reads.
    /// </summary>
    /// <remarks>
    /// The fields are added here, once, to whatever the check came to: the deployment is read from the record whatever
    /// the state, so a program asking which ref the roles are taught from does not depend on why the check failed.
    /// </remarks>
    private static async Task<StatusCheck> RoleSkillsAsync(CliEnvironment env, RuntimeClient? client, SystemInfoResponse? info, CancellationToken cancellationToken) =>
        await RoleSkillsStateAsync(env, client, info, cancellationToken).ConfigureAwait(false) with { Details = RoleSkillsDetails(env, info) };

    /// <summary>
    /// The role root the check reads: the one the runtime reports, which is the directory it reads at every launch, and
    /// where the runtime did not answer, the one this CLI deploys role skills into.
    /// </summary>
    internal static string RoleSkillsRoot(CliEnvironment env, SkillsInfo? skills) =>
        string.IsNullOrWhiteSpace(skills?.RoleSkillsDirectory) ? env.Paths.RoleSkillsDirectory : skills.RoleSkillsDirectory;

    /// <summary>
    /// The deployment the record in the role root names, as <c>ref</c>, <c>source</c> and <c>commit</c> — empty where the
    /// record names none, a directory read where it stood — and <c>roles_taught</c>, the number of roles the runtime
    /// reports it teaches; null where there is no record or it cannot be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pack is the one the fact names: where the record holds packs at different refs, the one <see cref="Away"/>
    /// names, and otherwise the first. A role root holds the one pack the installer writes there.
    /// </para>
    /// <para>
    /// <c>roles_taught</c> is a number the runtime said, or nothing. A runtime that did not answer, or that could not read
    /// its own role root, has said none, and a guessed one would be read as a fact. A role whose skill the launcher would
    /// refuse is reported and not taught, so it is not counted.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string>? RoleSkillsDetails(CliEnvironment env, SystemInfoResponse? info)
    {
        var root = RoleSkillsRoot(env, info?.Skills);
        SkillsRecord? record;
        try
        {
            record = SkillsRecord.Read(root);
        }
        catch (SkillsRecordUnreadable)
        {
            return null;
        }

        var away = info?.Skills is null ? null : Away(root, info.RuntimeVersion, SkillsSource.BuildVersion(env));
        if ((away?.Deployment ?? record?.Packs.FirstOrDefault()) is not { } deployment)
        {
            return null;
        }

        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ref"] = deployment.Ref,
            ["source"] = deployment.Source,
            ["commit"] = deployment.Commit ?? string.Empty,
        };
        if (info?.Skills is { Problem: null } skills)
        {
            details["roles_taught"] = skills.Roles
                .Where(role => role.Problem is null)
                .Select(role => role.Role)
                .Distinct(StringComparer.Ordinal)
                .Count()
                .ToString(CultureInfo.InvariantCulture);
        }

        return details;
    }

    /// <summary>
    /// What the check came to. A runtime that cannot teach its roles is not ready, whatever else is true — and the check
    /// is not only presence: a deployment the launcher would refuse is worse than none, because it refuses every launch
    /// of that role rather than running it untaught.
    /// </summary>
    private static async Task<StatusCheck> RoleSkillsStateAsync(CliEnvironment env, RuntimeClient? client, SystemInfoResponse? info, CancellationToken cancellationToken)
    {
        if (info?.Skills is not { } skills)
        {
            return new StatusCheck("role_skills", true, CheckState.Unknown, "The runtime did not answer, so its role skills were not asked about.", Repair.Start);
        }

        if (skills.Problem is { } problem)
        {
            return new StatusCheck("role_skills", true, CheckState.Failed, Coded(problem, skills.ProblemCode), null);
        }

        // A roster that could not be read is not an empty roster. Turning one into the other made this check
        // answer "0 roles taught, all within the cap" and ready: true on a machine where every role launches
        // untaught -- which is the hole this whole increment exists to close, reported as closed, by the one
        // verb whose job is to be trusted about readiness. Everything else unreadable in this file says
        // Unknown, and so does this.
        var roster = client is null
            ? null
            : (await ReadAsync<Page<RoleDto>>(client, Operations.RoleList, cancellationToken).ConfigureAwait(false))?.Items;

        if (roster is null)
        {
            return new StatusCheck(
                "role_skills",
                true,
                CheckState.Unknown,
                $"{skills.Roles.Count} role directories are deployed, and the roster could not be read, so whether every seeded role has one is unknown.",
                null);
        }

        // Only the roles Jason ships are required to have a skill: the repair deploys those and nothing else. A role a
        // person added is reported, never failed -- failing it left an installation not ready for good, with a repair
        // that changed nothing for it.
        var seeded = roster.Where(role => role.Builtin).Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var rostered = roster.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var deployed = skills.Roles.Select(role => role.Role).ToHashSet(StringComparer.Ordinal);

        // A refused skill still fails the check whoever's role it is: every launch of that role is refused. Its repair
        // writes the shipped roles alone, so it is named only where a shipped role's skill is refused; a skill of any
        // other name is the person's to correct, and the fact says where.
        var refused = skills.Roles.Where(role => role.Problem is not null).ToList();
        if (refused.Count > 0)
        {
            var fact = string.Join(" ", refused.Select(role => Coded(role.Problem!, role.ProblemCode))) + string.Concat(refused
                .Where(role => !seeded.Contains(role.Role))
                .Select(role => role.Role)
                .Order(StringComparer.Ordinal)
                .Select(name => " " + OwnRoleRefused(name, skills.RoleSkillsDirectory)));
            return refused.Any(role => seeded.Contains(role.Role))
                ? new StatusCheck("role_skills", true, CheckState.Failed, fact, Repair.Reinstall)
                : new StatusCheck("role_skills", true, CheckState.Failed, fact, null);
        }

        var untaught = seeded.Except(deployed).Order(StringComparer.Ordinal).ToList();
        if (untaught.Count > 0)
        {
            return new StatusCheck(
                "role_skills",
                true,
                CheckState.Failed,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{untaught.Count} of {seeded.Count} seeded roles have no skill, so they would launch untaught: {string.Join(", ", untaught)}."),
                Repair.InstallRoles);
        }

        var stranger = deployed.Except(rostered).Order(StringComparer.Ordinal).ToList();
        var note = stranger.Count == 0
            ? string.Empty
            : $" {string.Join(", ", stranger.Select(name => $"'{name}'"))} is deployed and is no role on this runtime's roster, so nothing reads it.";
        note += Own(roster.Where(role => !role.Builtin && !deployed.Contains(role.Name)), skills.RoleSkillsDirectory);
        var taught = string.Create(CultureInfo.InvariantCulture, $"{deployed.Count} roles taught, all within the {skills.MaxSkillBytes}-byte cap.{note}");

        // Present and launchable is not current. After `jason update apply` the roles went on being taught from the
        // tag the build before installed, and this said ok, naming that tag as though it were the runtime's own.
        // Three cases, decided by equality and never by a version order. At the runtime's own tag: ok. At the tag
        // this build installed, under a runtime not yet restarted into this build: ok, and said, because that restart
        // is demanded where it belongs and one cause is not made two failures. At any other tag, older or newer --
        // a rollback leaves a record newer than both: failed, with the line that writes this build's tag, so the
        // next status lands in one of the first two. A record that pins its ref, made with --ref or marked so by an
        // earlier `skills update`, passes with that line.
        var version = SkillsSource.BuildVersion(env);
        if (Away(skills.RoleSkillsDirectory, info.RuntimeVersion, version) is (var deployment, var runtime, var build))
        {
            if (deployment.RefOverridden)
            {
                return new StatusCheck("role_skills", true, CheckState.Ok, $"{taught} Role skills are from {deployment.Ref}; this runtime is {runtime}. `{Repair.InstallRolesFrom(deployment.Source)}` moves it to {build}.", null);
            }

            if (string.Equals(deployment.Ref, build, StringComparison.Ordinal))
            {
                return new StatusCheck("role_skills", true, CheckState.Ok, $"{taught} Role skills are at {build}, the build installed; this runtime is {runtime} and catches up when it is restarted into the new build.", null);
            }

            return new StatusCheck("role_skills", true, CheckState.Failed, $"Role skills are from {deployment.Ref}; this runtime is {runtime}.", Repair.InstallRolesFrom(deployment.Source));
        }

        return new StatusCheck(
            "role_skills",
            true,
            CheckState.Ok,
            taught + Deployment(skills.RoleSkillsDirectory),
            null);
    }

    /// <summary>
    /// The deployment in the role root that is not at the tag the runtime's own release carries, with that tag and the
    /// tag this build installs; null where there is nothing to compare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two consumers, two versions. The role root is the runtime's: the runtime reports it and the roles it launches
    /// read it, so it is held to the runtime's tag rather than this CLI's, and until the runtime is restarted into a
    /// new build the old one is still the one teaching. The tag this build installs is carried beside it, because it
    /// is what the repair writes, and a deployment already there waits only for that restart.
    /// </para>
    /// <para>
    /// Compared only where both sides have a tag. A development runtime carries the pin of a release not published
    /// yet, which every such runtime would fail against; and so does a development build of this CLI, whose repair
    /// would write that pin and so never clear. A candidate is a release: its tag is published, with its packs. A
    /// development build compares nothing here, as it moves
    /// nothing in <c>skills update</c> and compares nothing in the harness check. A directory read where it stood has
    /// no ref at all, and its record says so by naming no commit. A record that cannot be read compares nothing either;
    /// <see cref="Deployment"/> reports it. Where packs differ, the one to repair is named first, then one its record
    /// pins, then one at this build's tag.
    /// </para>
    /// </remarks>
    private static (SkillsDeployment Deployment, string Runtime, string Build)? Away(string roleSkillsRoot, string runtimeVersion, string buildVersion)
    {
        if (!SkillsSource.IsRelease(runtimeVersion) || !SkillsSource.IsRelease(buildVersion))
        {
            return null;
        }

        SkillsRecord? record;
        try
        {
            record = SkillsRecord.Read(roleSkillsRoot);
        }
        catch (SkillsRecordUnreadable)
        {
            return null;
        }

        var runtime = SkillsSource.DefaultRef(runtimeVersion);
        var build = SkillsSource.DefaultRef(buildVersion);
        var away = (record?.Packs ?? [])
            .Where(pack => pack.Commit is not null && !string.Equals(pack.Ref, runtime, StringComparison.Ordinal))
            .ToList();
        var named = away.Find(pack => !pack.RefOverridden && !string.Equals(pack.Ref, build, StringComparison.Ordinal))
            ?? away.Find(pack => pack.RefOverridden)
            ?? away.FirstOrDefault();
        return named is null ? null : (named, runtime, build);
    }

    /// <summary>
    /// What the record in the role root says was put there: which pack, from which ref.
    /// </summary>
    /// <remarks>
    /// Here, because this is the check whose subject that root is. It used to be reported by the harness
    /// check, which is about the person's own harnesses and was answering about a directory that is not one.
    /// A root with no record is not a problem for this check to raise -- the roles are on disk, which is what
    /// it asks -- so it simply says nothing.
    /// </remarks>
    private static string Deployment(string roleSkillsRoot)
    {
        try
        {
            var record = SkillsRecord.Read(roleSkillsRoot);
            var packs = record?.Packs ?? [];
            return packs.Count == 0
                ? string.Empty
                : " Deployed: " + string.Join(", ", packs.Select(pack => $"{pack.Pack} at {pack.Ref}")) + ".";
        }
        catch (SkillsRecordUnreadable unreadable)
        {
            return $" The record there could not be read: {unreadable.Message}";
        }
    }

    /// <summary>
    /// Whether the product can be typed by name, and which file answers when it is. Both halves matter: a
    /// second installation earlier on PATH is why a person's <c>jason</c> is a version they did not install.
    /// </summary>
    /// <remarks>
    /// It stays <b>required</b>. Everything this product documents for an agent -- the README prompt,
    /// <c>docs/INSTALL.md</c> -- tells it to type <c>jason</c>, and an installation where that name resolves
    /// to nothing cannot do the work, whatever else is true. What was missing was the repair, not the
    /// requirement.
    /// </remarks>
    private static StatusCheck Path(CliEnvironment env)
    {
        var resolved = Executables.OnPath("jason", env.SearchPath);

        // A package manager puts its installation on the PATH itself, through its link or its own directory, and the file
        // answering is then the one behind that link: one installation, reached the manager's way.
        var owner = PackageOwner.Of(env);
        if (resolved is not null && owner is not null && Through(owner, resolved) is { } through)
        {
            return new StatusCheck("path", true, CheckState.Ok, $"'jason' resolves to {resolved}, through {owner.Manager}'s {through}.", null);
        }

        if (resolved is null && Persisted(env) is { } persisted)
        {
            // Two different facts had one answer. The PATH this process inherited is the shell's, fixed when
            // that shell started; the one this account keeps is what every shell started afterwards reads. In
            // the shell the installer ran in, the first does not carry Jason and the second does -- and this
            // check said `failed` there, beside a repair that appended the directory a second time. The
            // installation is right and the shell is old, so the check says so, and says how to reach it here.
            return new StatusCheck(
                "path",
                true,
                CheckState.Ok,
                $"'jason' does not resolve in this shell, but {persisted.Where} carries {persisted.Directory}, so {persisted.Reader} started from now on finds it: this one started before it was put there. Here, type it by its full path, {persisted.Executable}.",
                null);
        }

        if (resolved is null && owner is not null)
        {
            // Required all the same, and with no line: the entry that puts a manager's installation on the PATH is the
            // manager's to make, and one written into a profile or this account's Path would be a second entry the
            // manager knows nothing about and never takes off.
            return new StatusCheck(
                "path",
                true,
                CheckState.Failed,
                $"'jason' does not resolve on PATH, so nothing can be typed by name. {owner.Manager} installed this Jason and puts it on the PATH itself, so no line is offered here for an entry that is {owner.Manager}'s to make.",
                null);
        }

        if (resolved is null)
        {
            // Required, and now with the line that fixes it. This check is the one a from-source
            // installation fails, and it failed with `Fix: null` -- so the verdict was "not ready" and the
            // whole repair list was somebody else's optional check. A required failure with nothing to do
            // about it tells a person the tool is broken at the moment they most need it not to be.
            //
            // The line is not invented here: it is the one this product's own installer writes, composed by
            // the same rule `jason uninstall` reads backwards. One definition of "how Jason goes on a PATH"
            // serves the installer, this repair and the removal.
            return new StatusCheck(
                "path",
                true,
                CheckState.Failed,
                "'jason' does not resolve on PATH, so nothing can be typed by name.",
                Repair.OnPath(env));
        }

        var running = env.InstallPath;
        if (running is not null && !string.Equals(System.IO.Path.GetFullPath(running), System.IO.Path.GetFullPath(resolved), StringComparison.OrdinalIgnoreCase))
        {
            return new StatusCheck(
                "path",
                true,
                CheckState.Ok,
                $"'jason' resolves to {resolved}, which is not the file answering here ({running}): two installations are on this machine.",
                null);
        }

        return new StatusCheck("path", true, CheckState.Ok, $"'jason' resolves to {resolved}.", null);
    }

    /// <summary>
    /// How a name on the PATH reaches a manager's installation: its link, or its own directory. Null where it is neither.
    /// </summary>
    private static string? Through(PackageOwner owner, string resolved)
    {
        static bool Same(string? left, string? right) =>
            left is not null && right is not null
            && string.Equals(System.IO.Path.GetFullPath(left), System.IO.Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

        return Same(resolved, owner.Link) ? "link"
            : Same(System.IO.Path.GetDirectoryName(resolved), owner.Directory) ? "directory"
            : null;
    }

    /// <summary>
    /// Where this account keeps the directory of the file answering here on its PATH for shells not yet
    /// started, or null where it does not — or where that cannot be read, which leaves the check as it was.
    /// </summary>
    /// <remarks>
    /// Read through the seam that owns the machine, the same read <c>jason uninstall</c> plans by: this
    /// account's registry on Windows, its login profiles on Unix. A reader that worked it out here would be a
    /// second definition of "how Jason is on a PATH", and this file already had to be taught once that there
    /// is only one.
    /// </remarks>
    private static (string Where, string Directory, string Reader, string Executable)? Persisted(CliEnvironment env)
    {
        // Never `Environment.ProcessPath`, for the reason the repair gives: under `dotnet jason.dll` that is
        // the muxer, and under `dotnet run` a build's launcher.
        var executable = env.InstallPath ?? SelfExecutable.InstalledImage;
        if (env.Removes is not { } machine || executable is null || System.IO.Path.GetDirectoryName(executable) is not { Length: > 0 } directory)
        {
            return null;
        }

        PathEntryPlan entry;
        try
        {
            entry = machine.ReadPathEntry(directory);
        }
        catch (Exception exception) when (exception is RemovalRefused or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        // Whether a new shell finds it, which is not whether Jason put it there: a directory on the Path by
        // somebody else's hand is found all the same. And on Unix the one file this account's login shell reads,
        // matched as a whole line: any profile at all carrying the text anywhere used to be enough, so a line in
        // ~/.profile that a bash with a ~/.bash_profile never reads -- or one commented out -- said `ok`.
        if (entry.Persisted is not { } where)
        {
            return null;
        }

        return OperatingSystem.IsWindows()
            ? (where, directory, "a shell", executable)
            : (where, directory, "a login shell", executable);
    }

    private static StatusCheck ProviderPlugin(SystemInfoResponse? info) =>
        info is null
            ? new StatusCheck("provider_plugin", false, CheckState.Unknown, "The runtime did not answer.", null)
            : info.Plugins.ActiveCount == 0
                ? new StatusCheck("provider_plugin", false, CheckState.Absent, "No provider plugin is installed, so no campaign can reach a provider yet.", null)
                : new StatusCheck("provider_plugin", false, CheckState.Ok, string.Create(CultureInfo.InvariantCulture, $"{info.Plugins.ActiveCount} installed."), null);

    /// <summary>
    /// Whether each plugin package this runtime's release publishes is the one installed. Optional: a provider package is
    /// the person's to choose, so this never decides readiness. An earlier release's package fails it, and the fact names the
    /// command that installs this one's, <c>jason plugin install</c>, with what it replaces. One built or edited here
    /// passes with a line that says so, a development build compares against its own tree, named in the fact, and the
    /// packages not installed are one sentence.
    /// </summary>
    private static StatusCheck PluginPackage(SystemInfoResponse? info)
    {
        const string Name = "plugin_package";
        if (info is null)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so which plugin packages its release publishes was not asked.", null);
        }

        if (info.ReleasedPlugins is not { } packages)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime is older than this CLI and does not say which plugin packages its release publishes.", null);
        }

        if (packages.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Absent, $"Runtime {info.RuntimeVersion} carries no plugin package.", null);
        }

        var state = packages.Any(package => package.State == ReleasedPluginState.Behind) ? CheckState.Failed
            : packages.Any(package => package.State is ReleasedPluginState.Current or ReleasedPluginState.Differs) ? CheckState.Ok
            : CheckState.Absent;
        return new StatusCheck(Name, false, state, string.Join(" ", PackageLines(info, packages)), null);
    }

    /// <summary>
    /// A sentence per package, in the order the runtime says them, but for the packages not installed: two or more are one
    /// sentence naming their ids, where the first of them stands, since a release publishes more packages than a person
    /// installs and what there is to do is the same for each.
    /// </summary>
    private static IEnumerable<string> PackageLines(SystemInfoResponse info, IReadOnlyList<ReleasedPluginInfo> packages)
    {
        var missing = packages.Where(package => package.State == ReleasedPluginState.NotInstalled).Select(package => package.Id).ToList();
        var said = false;
        foreach (var package in packages)
        {
            if (package.State != ReleasedPluginState.NotInstalled || missing.Count == 1)
            {
                yield return PackageLine(info, package);
            }
            else if (!said)
            {
                said = true;
                var ids = $"{string.Join(", ", missing[..^1])} and {missing[^1]}";
                yield return $"{ids} are not installed; runtime {info.RuntimeVersion} carries each, the package its release publishes, and `jason plugin install <id>` installs one.";
            }
        }
    }

    private static string PackageLine(SystemInfoResponse info, ReleasedPluginInfo package)
    {
        var runtime = info.RuntimeVersion;
        var released = $"{package.Id} {package.Released.Version}";
        var installed = package.Installed is { } mine ? $"{package.Id} {mine.Version}" : package.Id;

        // The runtime's own data directory, written with the separator it writes, so the line names the folder a person
        // finds on that machine.
        var separator = info.DataDir.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        var directory = string.Join(separator, info.DataDir.TrimEnd('/', '\\'), "plugins", package.Id);

        return package.State switch
        {
            ReleasedPluginState.Current => $"{installed} is installed, the package runtime {runtime} carries.",
            ReleasedPluginState.Behind =>
                $"{installed} is installed; runtime {runtime} carries {released}, the package its release publishes. "
                + $"`jason plugin install {package.Id} --replace` places it in {directory} in place of the one there, and asks the runtime to load it.",
            ReleasedPluginState.Differs => $"{installed} is installed and is not the package runtime {runtime} carries ({released}); it names the same version or a later one, so it is a later release's package or one built or edited on this machine.",
            ReleasedPluginState.NotInstalled => $"{installed} is not installed; runtime {runtime} carries {released}, the package its release publishes, and `jason plugin install {package.Id}` installs it.",

            // Every state the runtime says has its arm above; the words are read by name, never as numbers.
            _ => throw new ArgumentOutOfRangeException(nameof(package), package.State, "There is no such package state."),
        };
    }

    /// <summary>The most unavailable plugins the availability fact names, and the most codes it names for one: past them it counts.</summary>
    private const int MostNamed = 10;

    /// <summary>
    /// Whether every plugin in the active snapshot can run, read from <c>plugin.list</c>: each one this machine holds back
    /// — <c>unavailable</c>, which only an environmental problem makes a plugin — named with its problem codes. Optional:
    /// work routed to an unavailable plugin fails at the claim, which refuses it with <c>plugin_unavailable</c> before a
    /// child exists, and an unavailable notification plugin delivers nothing; nothing else stops. And an installation
    /// with no plugin, or no route, is ready while no provider work can run at all, so a plugin's availability never
    /// decides readiness.
    /// </summary>
    /// <remarks>
    /// Only the runtime's own words are quoted: an id a package can have, and codes that are words. A problem's message is
    /// a sentence about this machine — a path, a program, a version a vendor's tool printed — and a manifest's name is
    /// its author's text, so neither is, and the fact names the listing that has them. A bundle carries this fact as it
    /// is, beside its own plugin list, which keeps words of the same two kinds and never a message.
    /// </remarks>
    private static StatusCheck PluginAvailability(bool asked, PluginRegistryDto? registry)
    {
        const string Name = "plugin_availability";
        if (!asked)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so whether its plugins are available was not asked.", null);
        }

        if (registry?.Plugins is not { } listed)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "plugin.list did not answer, so whether each plugin is available is not known.", null);
        }

        var plugins = listed.Where(plugin => plugin is not null).ToList();
        if (plugins.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Absent, "No plugin is installed, so none is unavailable.", null);
        }

        var unavailable = plugins.Where(plugin => plugin.Status == PluginStatus.Unavailable).ToList();
        if (unavailable.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Ok, string.Create(CultureInfo.InvariantCulture, $"Every plugin installed is available ({plugins.Count})."), null);
        }

        var named = string.Join(" ", unavailable.Take(MostNamed).Select(UnavailableLine));
        var more = unavailable.Count - MostNamed;
        if (more > 0)
        {
            named += string.Create(CultureInfo.InvariantCulture, $" {more} more {(more == 1 ? "plugin is" : "plugins are")} unavailable.");
        }

        return new StatusCheck(
            Name,
            false,
            CheckState.Failed,
            named
                + " This machine holds an unavailable plugin back, not its package. Work routed to such a plugin fails at the claim, "
                + "and a notification plugin delivers nothing; nothing else stops, so this check never decides readiness. "
                + $"`{Repair.ListPlugins}` says what each problem is, and `{Repair.ReloadPlugins}` reads the plugins again once it is repaired.",
            null);
    }

    /// <summary>
    /// Which plugins the active snapshot hands journal kinds to, read from the same <c>plugin.list</c> answer as
    /// <c>plugin_availability</c>: each plugin granted a kind, named with the kinds it is granted. Optional, and never
    /// deciding readiness: a kind nobody handles is the ordinary state of an installation, and a subscriber is a plugin
    /// like any other, whose availability the check above already says.
    /// </summary>
    /// <remarks>
    /// Only the runtime's own words are quoted, as in the check above: an id a package can have, and kinds that are words.
    /// </remarks>
    private static StatusCheck PluginSubscriptions(bool asked, PluginRegistryDto? registry)
    {
        const string Name = "plugin_subscriptions";
        if (!asked)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so which plugins handle journal kinds was not asked.", null);
        }

        if (registry?.Plugins is not { } listed)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "plugin.list did not answer, so which plugins handle journal kinds is not known.", null);
        }

        var subscribers = listed
            .Where(plugin => plugin?.Capabilities?.Subscriptions?.Granted is { Count: > 0 })
            .ToList();
        if (subscribers.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Absent, "No plugin is granted a journal kind, so none handles one.", null);
        }

        var named = string.Join(" ", subscribers.Take(MostNamed).Select(SubscriberLine));
        var more = subscribers.Count - MostNamed;
        if (more > 0)
        {
            named += string.Create(CultureInfo.InvariantCulture, $" {more} more {(more == 1 ? "plugin handles" : "plugins handle")} journal kinds.");
        }

        return new StatusCheck(
            Name,
            false,
            CheckState.Ok,
            named + $" `{Repair.ListPlugins}` says which kinds each plugin declares and which it is granted.",
            null);
    }

    /// <summary>
    /// Which plugins ask for a capability and are granted nothing of it, read from the same <c>plugin.list</c> answer as
    /// <c>plugin_availability</c>: each named with the keys under <c>Plugins:Grants:&lt;id&gt;</c> that would grant it.
    /// Optional, and never deciding readiness: such a plugin runs without what it is not granted, and an installation with
    /// no plugin is ready too. A plugin this machine holds back is named here as well, whatever its status: the runtime
    /// resolves its grants before it decides its status, so what it is granted is as true of it as of any other, and one
    /// report then says both its problems, in the check above, and its grant.
    /// </summary>
    /// <remarks>
    /// Only ids and capability keys are quoted: what a manifest asks for — a program, a host, a variable — is its author's
    /// text, and a bundle carries this fact as it is, so the full sentence stays in <c>jason plugin list --human</c>.
    /// </remarks>
    private static StatusCheck PluginGrants(bool asked, PluginRegistryDto? registry)
    {
        const string Name = "plugin_grants";
        if (!asked)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so what its plugins are granted was not asked.", null);
        }

        if (registry?.Plugins is not { } listed)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "plugin.list did not answer, so what each plugin is granted is not known.", null);
        }

        var plugins = listed.Where(plugin => plugin is not null).ToList();
        if (plugins.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Absent, "No plugin is installed, so none waits for a grant.", null);
        }

        var waiting = plugins
            .Where(plugin => plugin.Capabilities is not null)
            .Select(plugin => (Plugin: plugin, Keys: PluginRenderers.Asked(plugin.Capabilities!).Select(ask => ask.Key).ToList()))
            .Where(entry => entry.Keys.Count > 0)
            .ToList();
        if (waiting.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Ok, "Every plugin is granted something of each capability it asks for.", null);
        }

        var named = string.Join(" ", waiting.Take(MostNamed).Select(entry =>
            $"{(PluginIds.IsWellFormed(entry.Plugin.Id) ? entry.Plugin.Id : "A plugin whose id no package can have")} is granted nothing it asks for under {string.Join(", ", entry.Keys)}."));
        var more = waiting.Count - MostNamed;
        if (more > 0)
        {
            named += string.Create(CultureInfo.InvariantCulture, $" {more} more {(more == 1 ? "plugin is granted nothing it asks" : "plugins are granted nothing they ask")} for.");
        }

        return new StatusCheck(
            Name,
            false,
            CheckState.Failed,
            named
                + " Such a plugin runs without what it is not granted, and nothing else stops, so this check never decides readiness."
                + $" `{Repair.ListPlugins} --human` says what each asks for, the key that grants it and the settings file, and"
                + $" `{Repair.ReloadPlugins}` reads the grant.",
            null);
    }

    private static string SubscriberLine(PluginDto plugin)
    {
        var who = PluginIds.IsWellFormed(plugin.Id) ? plugin.Id : "A plugin whose id no package can have";
        var granted = plugin.Capabilities!.Subscriptions!.Granted!;
        var kinds = granted.OfType<string>().Where(IsWord).Distinct(StringComparer.Ordinal).ToList();
        var unnamed = granted.Distinct(StringComparer.Ordinal).Count() - kinds.Count;
        var more = Math.Max(kinds.Count - MostNamed, 0) + unnamed;
        if (kinds.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{who} handles {more} {(more == 1 ? "kind" : "kinds")}.");
        }

        var tail = more > 0 ? string.Create(CultureInfo.InvariantCulture, $", and {more} more") : string.Empty;
        return $"{who} handles {string.Join(", ", kinds.Take(MostNamed))}{tail}.";
    }

    private static string UnavailableLine(PluginDto plugin)
    {
        var who = PluginIds.IsWellFormed(plugin.Id) ? plugin.Id : "A plugin whose id no package can have";
        var codes = (plugin.Problems ?? [])
            .Select(problem => problem?.Code)
            .OfType<string>()
            .Where(IsWord)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (codes.Count == 0)
        {
            return $"{who} is unavailable.";
        }

        var more = codes.Count > MostNamed ? string.Create(CultureInfo.InvariantCulture, $", and {codes.Count - MostNamed} more") : string.Empty;
        // "its problems", never "unavailable:": the loader lists a plugin's warnings before its environmental problems, and
        // which code is a warning is the runtime's vocabulary, so a colon would read the first code as the cause.
        return $"{who} is unavailable; its problems: {string.Join(", ", codes.Take(MostNamed))}{more}.";
    }

    /// <summary>A code as the runtime spells one: a lowercase letter, then lowercase letters, digits and underscores, 64 at most.</summary>
    private static bool IsWord(string code) =>
        code.Length is > 0 and <= 64
        && char.IsAsciiLetterLower(code[0])
        && code.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private static StatusCheck Route(RoutesDto? routes)
    {
        if (routes is null)
        {
            return new StatusCheck("route", false, CheckState.Unknown, "The runtime did not answer.", null);
        }

        var configured = routes.Global.Default is not null
            || routes.Global.Operations.Count > 0
            || routes.Global.Channels is { Count: > 0 }
            || routes.Campaigns.Count > 0;
        var byChannel = ChannelRoutes(routes);
        return configured
            ? new StatusCheck(
                "route",
                false,
                CheckState.Ok,
                byChannel == 0
                    ? $"Routed: default {routes.Global.Default?.PluginId ?? "none"}."
                    : $"Routed: default {routes.Global.Default?.PluginId ?? "none"}, {byChannel} of them by channel.",
                null)
            : new StatusCheck(
                "route",
                false,
                CheckState.Absent,
                // No repair, because there is no command. A global default route is written in the settings
                // file and frozen by a reload; `route set` needs --campaign and refuses without it, so the
                // line printed here was itself a usage error -- verbatim the failure the guard beside this
                // one exists to prevent.
                "Nothing is routed anywhere yet: set Routes in the settings file, or route one campaign.",
                null);
    }

    private static StatusCheck Binding(RoutesDto? routes)
    {
        if (routes is null)
        {
            return new StatusCheck("binding", false, CheckState.Unknown, "The runtime did not answer.", null);
        }

        var bound = Every(routes).Any(route => route.Binding is not null);
        return bound
            ? new StatusCheck("binding", false, CheckState.Ok, "A route names an account.", null)
            : new StatusCheck("binding", false, CheckState.Absent, "No route names an account yet.", null);
    }

    /// <summary>How many routes name a channel, global and campaign together: each is a plugin picked for one channel of an operation.</summary>
    private static int ChannelRoutes(RoutesDto routes) =>
        Channelled(routes.Global.Channels).Count() + routes.Campaigns.Sum(campaign => Channelled(campaign.Channels).Count());

    private static IEnumerable<RouteDto> Channelled(IReadOnlyDictionary<string, IReadOnlyDictionary<string, RouteDto>>? channels) =>
        channels is null ? [] : channels.Values.Where(byChannel => byChannel is not null).SelectMany(byChannel => byChannel.Values);

    private static IEnumerable<RouteDto> Every(RoutesDto routes)
    {
        if (routes.Global.Default is { } global)
        {
            yield return global;
        }

        foreach (var route in routes.Global.Operations.Values)
        {
            yield return route;
        }

        foreach (var route in Channelled(routes.Global.Channels))
        {
            yield return route;
        }

        foreach (var campaign in routes.Campaigns)
        {
            if (campaign.Default is { } fallback)
            {
                yield return fallback;
            }

            foreach (var route in campaign.Operations.Values)
            {
                yield return route;
            }

            foreach (var route in Channelled(campaign.Channels))
            {
                yield return route;
            }
        }
    }

    /// <summary>
    /// What the provider check says when it looked a program up on a search path: the CLI process's own, which is not
    /// necessarily the one the runtime started with.
    /// </summary>
    private const string SearchPathRead = "It read the CLI's PATH; the runtime's PATH may differ, see the install page.";

    /// <summary>
    /// Whether the provider's own CLI is on this machine and answering. It names the command it runs and the file
    /// that command resolved to, it is bounded, and it prints no credential: not the key, not a prefix of it, not
    /// its length. A name that resolves to nothing a program may be started through is not run at all.
    /// </summary>
    /// <remarks>
    /// The program is named by the caller, never by this build. Which provider somebody uses is theirs, and a
    /// readiness check that knew one vendor's command by heart would be this product naming a vendor in its
    /// own sources -- which A7(a) forbids and a guard enforces.
    /// </remarks>
    private static async Task<StatusCheck> ProviderCliAsync(CliEnvironment env, string? providerCli, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerCli))
        {
            return new StatusCheck(
                "provider_cli",
                false,
                CheckState.Absent,
                "No provider CLI was named, so none was checked. Name one with --provider-cli <program> and it will be run with --version.",
                null);
        }

        var command = $"{providerCli} {string.Join(' ', ProviderArguments)}";
        if (env.Programs is not { } runner)
        {
            return new StatusCheck("provider_cli", false, CheckState.Unknown, $"This environment cannot run programs, so '{command}' was not run.", null);
        }

        // A confined call runs a bare name only: a path, which on Windows takes in a value naming a drive, names a file
        // anyone who can write there chose, so it is neither looked up nor started.
        var confined = env.Confined();
        if (confined && IsPath(providerCli))
        {
            return new StatusCheck(
                "provider_cli",
                false,
                CheckState.Unknown,
                $"This environment cannot run a program named by a path, so '{command}' was not run: {Confinement.Variable} is set. "
                    + "Name a program on the search path, or run it in a terminal.",
                null);
        }

        var searchPath = confined ? FullyQualified(env.SearchPath ?? Environment.GetEnvironmentVariable("PATH")) : env.SearchPath;
        var (found, directory) = FindProvider(providerCli, searchPath);

        // A name is looked up on this process's search path; a path names its directory and reads none, except that a shim
        // given by path takes its interpreter from the search path when none stands beside it. A runtime that started at
        // logon, or from another shell, may hold another search path.
        var readPath = !IsPath(providerCli) || found.InterpreterPathRead;
        string Said(string fact) => readPath ? $"{fact} {SearchPathRead}" : fact;

        if (found is not { Outcome: ProgramLookupOutcome.Program or ProgramLookupOutcome.NpmShim, Path: { } program })
        {
            var script = found.Outcome == ProgramLookupOutcome.NotFound ? ScriptNamed(providerCli, searchPath) : null;
            return new StatusCheck("provider_cli", false, CheckState.Absent, Said(NotRun(found, providerCli, directory, script, command)), null);
        }

        // What the name came to is said first: a person reading "did not answer" has to know which file did not, and
        // on Windows that is often not the file the name suggests. A program found as itself has File equal to Path; only
        // npm's shim sets them apart, by the interpreter it names or by the executable it starts.
        var resolved = found.Outcome switch
        {
            ProgramLookupOutcome.NpmShim =>
                $"'{providerCli}' resolves to {Listed(found.File!)}, npm's shim, which runs {found.Launch[0]} with {program}.",
            _ when !string.Equals(found.File, program, StringComparison.OrdinalIgnoreCase) =>
                $"'{providerCli}' resolves to {Listed(found.File!)}, npm's shim, which starts {program}.",
            _ => $"'{providerCli}' resolves to {Listed(found.File!)}.",
        };

        var result = await runner.RunAsync(program, [.. found.Launch, .. ProviderArguments], null, ProviderTimeout, cancellationToken).ConfigureAwait(false);
        if (result.NotStarted)
        {
            return new StatusCheck("provider_cli", false, CheckState.Absent, Said($"{resolved} '{command}' could not be started."), null);
        }

        if (result.TimedOut)
        {
            return new StatusCheck("provider_cli", false, CheckState.Absent, Said($"{resolved} '{command}' did not answer in {ProviderTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds."), null);
        }

        return result.ExitCode == 0
            ? new StatusCheck("provider_cli", false, CheckState.Ok, Said($"{resolved} '{command}' answered."), null)
            : new StatusCheck("provider_cli", false, CheckState.Absent, Said(string.Create(CultureInfo.InvariantCulture, $"{resolved} '{command}' did not answer: exit code {result.ExitCode}.")), null);
    }

    /// <summary>
    /// The provider CLI as Jason finds every program it starts by name (<see cref="ProgramLookup"/>): on Windows an
    /// <c>.exe</c> or a <c>.com</c>, or npm's shim read for what it starts (the interpreter and entry script it names, or
    /// the executable its package ships), and never a script
    /// a shell would have to interpret. A value that is a path (<see cref="ProgramLookup.IsPath"/>) is looked up in the
    /// directory it names alone by the same rule; the directory comes back with the answer, so a name not found there says where it was looked for.
    /// A shim found there takes its interpreter from the search path, as the shim itself would when it runs.
    /// </summary>
    private static (FoundProgram Found, string? Directory) FindProvider(string providerCli, string? searchPath)
    {
        // Read from this process, as the path check reads it: it only widens what is refused, and never what is found.
        var pathExt = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATHEXT") : null;
        var onPath = new ProgramSearchPath(searchPath ?? Environment.GetEnvironmentVariable("PATH"), pathExt);
        if (!IsPath(providerCli))
        {
            return (ProgramLookup.Find(providerCli, onPath, onPath), null);
        }

        var full = System.IO.Path.GetFullPath(providerCli);
        var name = System.IO.Path.GetFileName(full);
        var directory = System.IO.Path.GetDirectoryName(full);
        return name.Length == 0 || directory is null
            ? (new FoundProgram(providerCli, ProgramLookupOutcome.NotFound, null, [], null, null), directory ?? full)
            : (ProgramLookup.Find(name, new ProgramSearchPath(directory, pathExt), onPath), directory);
    }

    /// <summary>
    /// The entries of a search path a confined call looks a bare name up in: the fully qualified ones. A relative or empty
    /// entry, or on Windows one rooted on no drive, names a folder relative to wherever the call runs, which is a working
    /// folder anyone the call serves may write in.
    /// </summary>
    private static string FullyQualified(string? searchPath) =>
        string.Join(
            System.IO.Path.PathSeparator,
            (searchPath ?? string.Empty)
                .Split(System.IO.Path.PathSeparator)
                .Where(entry => entry.Trim().Trim('"') is { Length: > 0 } directory && System.IO.Path.IsPathFullyQualified(directory)));

    /// <summary>
    /// What a path is, for the confined refusal and the path branch alike, as the lookup itself reads it: a separator, a
    /// root, or on Windows a colon, so a value naming a drive is a path.
    /// </summary>
    private static bool IsPath(string providerCli) => ProgramLookup.IsPath(providerCli);

    /// <summary>The extensions a value may carry that name a script only a shell could run.</summary>
    private static readonly string[] ScriptExtensions = [".cmd", ".bat", ".ps1"];

    /// <summary>
    /// The script a value names by its own extension, when nothing answered for it. On Windows the lookup adds the
    /// extension itself, so <c>acme-sdr.cmd</c> is a name it never finds while the file is there, and "not found" would
    /// be false. Null where the value names no such script, or that script is neither on the search path nor in the
    /// directory given.
    /// </summary>
    private static string? ScriptNamed(string providerCli, string? searchPath)
    {
        if (!OperatingSystem.IsWindows()
            || !ScriptExtensions.Contains(System.IO.Path.GetExtension(providerCli), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsPath(providerCli))
        {
            return Executables.OnPath(providerCli, searchPath);
        }

        var full = System.IO.Path.GetFullPath(providerCli);
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// A file as its directory lists it. The lookup spells a file it reached through <c>PATHEXT</c> with that
    /// variable's case, <c>acme-sdr.CMD</c>, which is not the name a person finds in the listing.
    /// </summary>
    private static string Listed(string file)
    {
        var directory = System.IO.Path.GetDirectoryName(file);
        if (!OperatingSystem.IsWindows() || directory is null)
        {
            return file;
        }

        var name = System.IO.Path.GetFileName(file);
        try
        {
            foreach (var entry in Directory.EnumerateFiles(directory, name))
            {
                if (string.Equals(System.IO.Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
                {
                    return System.IO.Path.Combine(directory, System.IO.Path.GetFileName(entry));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Gone or unreadable since the lookup saw it: the lookup's spelling still names it.
        }

        return file;
    }

    /// <summary>
    /// Why nothing was run: nothing answered for the name, or only a file that no program may be started through. A
    /// refusal says what Jason can run instead, since the person reading it has a CLI to put right.
    /// </summary>
    private static string NotRun(FoundProgram found, string providerCli, string? directory, string? script, string command) => found.Outcome switch
    {
        ProgramLookupOutcome.ShellOnly =>
            $"'{providerCli}' resolves only to {Listed(found.File!)}, {ProgramLookup.ShellScript}, so '{command}' was not run: {ProgramLookup.ProgramsNotShells}; {Instead(found.Name)}",
        ProgramLookupOutcome.RefusedShim =>
            $"'{providerCli}' resolves only to {Listed(found.File!)}, which {found.Refusal}, so '{command}' was not run: starting it would mean cmd.exe interprets the arguments; {Instead(found.Name)}",
        ProgramLookupOutcome.NotFound when found.File is { } shim =>
            $"'{providerCli}' resolves to {Listed(shim)}, npm's shim, whose executable is not there, so '{command}' was not run.",
        _ when script is not null =>
            $"'{providerCli}' resolves only to {Listed(script)}, {ProgramLookup.ShellScript}, so '{command}' was not run: {ProgramLookup.ProgramsNotShells}; give '{providerCli[..^System.IO.Path.GetExtension(providerCli).Length]}', the name without its extension, and Jason looks for an .exe, a .com or npm's own shim for it.",
        _ when directory is not null => $"'{found.Name}' was not found in {directory}, so '{command}' was not run.",
        _ => $"'{providerCli}' was not found on the search path, so '{command}' was not run.",
    };

    /// <summary>What Jason can run in place of a refused file: the clause that ends a refusal.</summary>
    private static string Instead(string name) =>
        $"it can run an .exe, a .com or npm's own shim for '{name}', or the CLI's own executable given by path.";

    private static StatusCheck Harnesses(CliEnvironment env)
    {
        if (env.Harnesses is not { } locator)
        {
            return new StatusCheck("harness_skills", false, CheckState.Unknown, "This environment does not detect agent harnesses.", null);
        }

        // The harness roots, and only them. Appending the runtime's own role root here made this check answer
        // `ok` on the strength of a root that is not a harness -- naming it, while the harness it had
        // detected held nothing and the skills that really landed went unmentioned. The `absent` branch named
        // only the detected harnesses, so the two branches disagreed about what the check was even about.
        //
        // Nothing is lost by taking it out: `role_skills` is required, its subject is that root, and it now
        // reports the pack and ref the record there names.
        var roots = locator.Detect();
        if (roots.Count == 0)
        {
            return new StatusCheck("harness_skills", false, CheckState.Absent, "No agent harness was found on this machine.", null);
        }

        // Two consumers, two versions. The role root is the runtime's: the runtime reports it and the roles it
        // launches read it, so `role_skills` holds it to the runtime's tag. These roots are read by the person's own
        // agent, which drives this CLI and works with no runtime at all, so they are held to this build's tag -- and
        // their repair, `jason skills update`, moves a deployment to exactly that tag, so it converges by
        // construction. A pack its record pins, made with --ref or marked so by an earlier `skills update`, passes
        // with the line that moves it, since an update keeps a pinned ref. A development build, whose tag no release
        // publishes, and a directory, whose record names no commit, compare nothing.
        var build = SkillsSource.BuildVersion(env);
        var own = SkillsSource.IsRelease(build) ? SkillsSource.DefaultRef(build) : null;

        var deployed = new List<string>();
        var gone = new List<string>();
        var behind = new List<string>();
        var empty = new List<string>();
        var pinned = new List<string>();
        foreach (var root in roots.Select(root => root.Directory).Distinct(HarnessLocators.SamePath))
        {
            try
            {
                var record = SkillsRecord.Read(root);
                if (record is not null)
                {
                    // The files, not the record alone. An uninstall keeps the record beside a file the person edited, so a
                    // record can name two packs over a root holding one file of them: read alone, it answered `ok` there.
                    foreach (var pack in record.Packs)
                    {
                        var missing = pack.Files.Count(file => !File.Exists(System.IO.Path.Combine(root, file.Path)));
                        if (missing > 0)
                        {
                            gone.Add(string.Create(CultureInfo.InvariantCulture, $"{pack.Pack} in {root}: {missing} of the {pack.Files.Count} file(s) its record names are not there."));
                        }
                    }

                    deployed.AddRange(record.Packs.Select(pack => $"{pack.Pack} at {pack.Ref} in {root}{ReadBy(root)}"));
                    var away = record.Packs
                        .Where(pack => own is not null && pack.Commit is not null && !string.Equals(pack.Ref, own, StringComparison.Ordinal))
                        .ToList();
                    behind.AddRange(away
                        .Where(pack => !pack.RefOverridden)
                        .Select(pack => $"{pack.Pack} in {root} is from {pack.Ref}; this build is {own}."));
                    pinned.AddRange(away
                        .Where(pack => pack.RefOverridden)
                        .Select(pack => $"{pack.Pack} in {root} is from {pack.Ref}; this build is {own}. `{Repair.InstallPackFrom(pack.Pack, pack.Source)}` moves it to {own}."));
                }
                else
                {
                    empty.Add(root);
                }
            }
            catch (SkillsRecordUnreadable unreadable)
            {
                // Never absent: a root whose record cannot be read is one nothing may guess at.
                return new StatusCheck("harness_skills", false, CheckState.Unknown, unreadable.Message, null);
            }
        }

        if (deployed.Count == 0)
        {
            return new StatusCheck("harness_skills", false, CheckState.Absent, $"Nothing is deployed into {string.Join(", ", roots.Select(root => root.Directory))}.", Repair.Install);
        }

        // A detected root no record names is one `skills update` never carries, since it carries only the roots a record
        // names: a person who installed another host after the first deployment sees nothing reach it. The check stays
        // as it is and says so, with the line that fills the root.
        var unfilled = empty.Select(root =>
            HostWords(root) is { Count: > 0 } hosts
                ? $"Nothing is deployed into {root}, which {string.Join(", ", hosts)} read: `{Repair.InstallForHost(hosts[0])}` fills it."
                : $"Nothing is deployed into {root}.").ToList();

        // A pack with files gone is written again by an update, which writes what is missing and keeps an edited file, from
        // the source and at the ref its record names and into the roots it names; and it moves a pack that is behind. An
        // install would move a pack from another source, or pinned, to this build's default, and deploy into every harness.
        return gone.Count > 0 || behind.Count > 0
            ? new StatusCheck("harness_skills", false, CheckState.Failed, string.Join(" ", gone.Concat(behind).Concat(pinned).Concat(unfilled)), Repair.UpdateSkills)
            : new StatusCheck("harness_skills", false, CheckState.Ok, string.Join("; ", deployed) + "." + string.Concat(pinned.Concat(unfilled).Select(note => " " + note)), null);

        // The hosts that read a folder, by their words.
        List<string> HostWords(string root) =>
            [.. roots.Where(found => HarnessLocators.SamePath.Equals(found.Directory, root)).SelectMany(found => found.Hosts).Distinct().Select(AgentHosts.Word)];

        // The hosts that read a folder, by their words, after it; a folder named with --root and no host says none.
        string ReadBy(string root) =>
            roots.Where(found => HarnessLocators.SamePath.Equals(found.Directory, root)).SelectMany(found => found.Hosts).Distinct().ToList() is { Count: > 0 } hosts
                ? " (" + string.Join(", ", hosts.Select(AgentHosts.Word)) + ")"
                : string.Empty;
    }

    /// <summary>
    /// Which agent hosts this machine has, by the marker each keeps under the home directory, and whether every enabled
    /// profile names one of them. Never required: a profile naming a host that is not here fails the work it launches with
    /// <c>host_not_available</c>, not the installation. A host the runtime wrote that this build of the CLI does not know —
    /// a newer runtime's — is named as given. Null <paramref name="roots"/> is an environment that detects nothing; null
    /// <paramref name="profiles"/> profiles that could not be read, for the reason <paramref name="unread"/> says, or because
    /// the runtime did not answer where it says none. Without <paramref name="names"/> the profiles are
    /// counted by host, in the table's order and then the words this CLI does not know, and the repair names none.
    /// </summary>
    internal static StatusCheck AgentHostsCheck(IReadOnlyList<HarnessRoot>? roots, IReadOnlyList<ProfileHost>? profiles, bool names, string? unread = null)
    {
        const string Name = "agent_hosts";
        const string Unknown = "a host this build of the CLI does not know";
        const string NotHere = "not detected";

        if (roots is null)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "This environment does not detect agent hosts.", null);
        }

        var detected = AgentHosts.All
            .Select(host => (host.Kind, Root: roots.FirstOrDefault(root => root.Hosts.Contains(host.Kind))))
            .Where(found => found.Root is not null)
            .Select(found => (found.Kind, Word: AgentHosts.Word(found.Kind), found.Root!.Directory))
            .ToList();
        var hosts = detected.Count == 0
            ? "No agent host was detected on this machine."
            : "Detected: " + string.Join("; ", detected.Select(host => $"{host.Word} ({host.Directory})")) + ".";

        if (profiles is null)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, hosts + " The profiles could not be read: " + (unread ?? "the runtime did not answer") + ".", null);
        }

        if (profiles.Count == 0)
        {
            return new StatusCheck(Name, false, detected.Count == 0 ? CheckState.Absent : CheckState.Ok, hosts + " No enabled profile names a host.", null);
        }

        bool Here(string host) => detected.Any(found => string.Equals(found.Word, host, StringComparison.Ordinal));
        string? Why(string host) => Here(host) ? null : AgentHosts.Parse(host) is not null ? NotHere : Unknown;

        string said;
        if (names)
        {
            said = "Profiles: " + string.Join("; ", profiles.Select(profile => Why(profile.Host) switch
            {
                null => $"{profile.Name} names {profile.Host}",
                NotHere => $"{profile.Name} names {profile.Host}, which was not detected on this machine",
                var why => $"{profile.Name} names {profile.Host}, {why}",
            })) + ".";
        }
        else
        {
            var order = AgentHosts.All.Select(host => AgentHosts.Word(host.Kind)).ToList();
            said = "Profiles by host: " + string.Join("; ", profiles
                .GroupBy(profile => profile.Host, StringComparer.Ordinal)
                .OrderBy(group => order.IndexOf(group.Key) is var at and >= 0 ? at : order.Count)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key} {group.Count()}") + (Why(group.Key) is { } why ? ", " + why : string.Empty))) + ".";
        }

        // A profile whose current revision this build's rules for its host refuse: the runtime says so on the profile, and
        // work that runs under it ends before a child exists. The repair reads what to change; the act is the update.
        var refused = profiles.Where(profile => profile.Refused).ToList();
        if (refused.Count > 0)
        {
            said += names
                ? " The current revision of " + string.Join(", ", refused.Select(profile => profile.Name)) + " is refused by this build's rules for its host; 'jason profile update' changes it."
                : string.Create(CultureInfo.InvariantCulture, $" {refused.Count} of them have a current revision this build's rules for its host refuse; 'jason profile update' changes one.");
        }

        var astray = profiles.FirstOrDefault(profile => !Here(profile.Host));
        if (astray is null)
        {
            if (refused.Count == 0)
            {
                return new StatusCheck(Name, false, CheckState.Ok, hosts + " " + said, null);
            }

            var toRead = names ? refused[0].Name : "<name>";
            return new StatusCheck(Name, false, CheckState.Failed, hosts + " " + said, Repair.ReadProfile(toRead));
        }

        // The repair moves the profile to a host that is here; with none here there is no command to give, since installing
        // a host is the person's.
        if (detected.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Failed, hosts + " " + said, null);
        }

        var profile = names ? astray.Name : "<name>";
        var host = detected[0].Kind;
        return new StatusCheck(Name, false, CheckState.Failed, hosts + " " + said, Repair.MoveProfile(profile, host));
    }

    /// <summary>The most pages of profiles the check reads; past them the answer is not one it can read whole.</summary>
    private const int MaxProfilePages = 100;

    /// <summary>
    /// Every page of the enabled profiles, each by its name and its host as the runtime wrote it — a string, so a host this
    /// build does not know is read rather than thrown on. Where they cannot be read, none, and why in the check's own
    /// words: the runtime did not answer, refused, answered with something this cannot read — a host that is not a
    /// snake_case word among it, which is not repeated — or with more pages than the check reads.
    /// </summary>
    private static async Task<(IReadOnlyList<ProfileHost>? Profiles, string? Unread)> ProfilesAsync(RuntimeClient client, CancellationToken cancellationToken)
    {
        const string Unreadable = "the runtime answered with something this build of the CLI cannot read";
        var profiles = new List<ProfileHost>();
        string? cursor = null;
        for (var page = 0; page < MaxProfilePages; page++)
        {
            var body = cursor is null ? "{}" : new JsonObject { ["cursor"] = cursor }.ToJsonString();
            try
            {
                var response = await client.PostAsync(Operations.ProfileList, body, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    return (null, "the runtime refused to list them");
                }

                if (JsonNode.Parse(response.Body) is not JsonObject answer || answer["items"] is not JsonArray items)
                {
                    return (null, Unreadable);
                }

                foreach (var item in items)
                {
                    if (Text(item?["name"]) is not { } name || Text(item?["revision"]?["host"]) is not { } host)
                    {
                        return (null, Unreadable);
                    }

                    if (!IsHostWord(host))
                    {
                        return (null, "a profile names its host by something that is not a host word");
                    }

                    // A runtime that predates the field sends none, which is no problem; one that sends something this cannot
                    // read as a list is not guessed at.
                    var refused = item?["problems"] is JsonArray problems && problems.Count > 0;
                    profiles.Add(new ProfileHost(name, host, refused));
                }

                cursor = Text(answer["next_cursor"]);
                if (cursor is null)
                {
                    return (profiles, null);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return (null, null);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return (null, Unreadable);
            }
        }

        return (null, string.Create(CultureInfo.InvariantCulture, $"the runtime answered with more than {MaxProfilePages} pages of them, more than this check reads"));

        static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>A host word as Jason writes one: lowercase letters, digits and underscores, starting with a letter.</summary>
    private static bool IsHostWord(string host) =>
        host.Length is > 0 and <= 64 && char.IsAsciiLetterLower(host[0]) && host.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private static StatusCheck Autostart(CliEnvironment env)
    {
        if (env.Autostart is not { } registrar)
        {
            return new StatusCheck("autostart", false, CheckState.Unknown, "This environment does not read autostart registrations.", null);
        }

        AutostartState state;
        try
        {
            state = registrar.Read();
        }
        catch (AutostartException failure)
        {
            return new StatusCheck("autostart", false, CheckState.Unknown, failure.Message, null);
        }

        return state.Registered
            ? new StatusCheck("autostart", false, CheckState.Ok, "Registered at logon: the runtime starts by itself each time you sign in.", null)
            : new StatusCheck("autostart", false, CheckState.Absent, "No logon registration: the next time you sign in, the runtime stays off until it is started by hand.", Repair.EnableAutostart);
    }

    /// <summary>
    /// Whether anything tells a person when Jason starts waiting for them, read from what <c>system.info</c> says each
    /// channel did last — and, for the toast before it has been sent at all, from whether the runtime has a desktop,
    /// which it decides when it starts. Optional: notifications are a convenience, never a condition of working.
    /// </summary>
    /// <remarks>
    /// A person's own choice is never a failure: notifications switched off, or a machine with no desktop by design.
    /// A runtime in session 0 is: a service or an SSH session runs there, but on a desktop it is most likely a runtime a
    /// 0.1.0 logon task left running, and for that one the fact names the acts in the order docs/release-and-update.md
    /// §8 gives them — the registration, the stop, a start — never the stop alone, which would leave the old task to
    /// start another runtime at the next sign-in.
    /// </remarks>
    private static StatusCheck Notifications(SystemInfoResponse? info)
    {
        const string Name = "notifications";
        if (info is null)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "The runtime did not answer, so whether its notifications reach anybody is not known.", null);
        }

        if (info.Notifications is not { } notifications)
        {
            return new StatusCheck(Name, false, CheckState.Unknown, "This runtime does not say what its notifications did.", null);
        }

        var on = notifications.Channels.Where(channel => channel.On).ToList();
        if (!notifications.Enabled || on.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Absent, "Notifications are off: nothing tells anybody when Jason starts waiting for them.", null);
        }

        var failures = new List<string>();
        var asides = new List<string>();
        var reaching = new List<string>();
        var onAgain = new List<string>();
        var hidden = false;
        DateTimeOffset? lastDelivered = null;
        foreach (var channel in on)
        {
            // Whether there is a desktop is this runtime's own fact, decided when it started and found again by its own
            // sends: a toast outcome a line from an earlier runtime carried never outweighs it.
            var noDesktop = NotificationReading.NoDesktop(channel, info.StartedAt);
            var outcome = NotificationReading.Outcome(channel, info.StartedAt);

            if (noDesktop == ChannelPresence.SessionZero)
            {
                // Failed, though the check is optional: on a desktop the likely cause is a runtime 0.1.0's logon task
                // started, and the remedy is three acts in order. A service or an SSH session runs there by design.
                failures.Insert(0, "This runtime runs in session 0, where nothing it shows reaches a desktop. A service or an SSH session runs there, "
                    + "and so does a runtime that 0.1.0's logon task started. If 0.1.0's task started it: from an ordinary prompt, run "
                    + "`jason runtime autostart enable` (or `disable`) once — if that answers Access is denied, follow its message first — "
                    + "then `jason runtime stop` once, then `jason runtime start` (or sign in again, with autostart enabled). "
                    + "0.1.0's task ran the runtime for the whole machine, in session 0; this version's task runs it in your own session, "
                    + "where what it shows reaches you.");
            }
            else if (noDesktop is not null)
            {
                asides.Add($"No desktop for this runtime to show a notification on ({noDesktop}). `Notifications:Toast` set to `false` turns the toast off.");
            }
            else if (outcome == ChannelResults.Silenced)
            {
                asides.Add(channel.Reason switch
                {
                    SilencedReasons.DisabledForApplication => "Windows has Jason's notifications switched off (Settings > System > Notifications > Jason).",
                    SilencedReasons.DisabledByGroupPolicy => "A group policy on this machine switches Windows' notifications off; an administrator sets it.",
                    SilencedReasons.DisabledByManifest => "Windows says this program's own manifest switches its notifications off.",
                    _ => "Windows has notifications switched off for every app.",
                });
            }
            else if (outcome == ChannelResults.Failed)
            {
                failures.Add($"{channel.Channel}: {channel.Code} ({channel.Class}) at {When(channel.At)}{Detail(channel.Detail)}.");
            }
            else if (outcome == ChannelResults.NotAttempted)
            {
                failures.Add($"{channel.Channel} was not attempted at {When(channel.At)}: {channel.Reason}{Detail(channel.Detail)}.");
            }
            else
            {
                reaching.Add(channel.Channel);
                if (NotificationReading.OnAgain(channel))
                {
                    onAgain.Add(channel.Kind == ChannelKinds.Toast ? "the toast" : channel.Channel);
                }

                hidden |= NotificationReading.Hidden(channel, info.StartedAt, notifications.LastAt);

                if (outcome == ChannelResults.Delivered && channel.At is { } at && (lastDelivered is null || at > lastDelivered))
                {
                    lastDelivered = at;
                }
            }
        }

        if (failures.Count > 0)
        {
            return new StatusCheck(Name, false, CheckState.Failed, string.Join(" ", failures), null);
        }

        if (reaching.Count == 0)
        {
            return new StatusCheck(Name, false, CheckState.Absent, string.Join(" ", asides), null);
        }

        // When a channel that reaches the person last delivered — not when anything was last tried. "Yet" is said only
        // where the last notification, if any, is this runtime's own: a channel the person turned off and on again has
        // sent nothing since; and one whose last outcome an earlier runtime wrote, or one with no outcome at all beside a
        // last notification older than this runtime — which is what a restart hands the toast — has sent nothing since
        // this runtime started; either may have reached them before. The plural is defensive and unreachable today: only
        // the toast is ever written as off (ToastChannel.Off), and a plugin turned off is not a channel at all, so at most
        // one channel is on again.
        var last = lastDelivered is { } delivered
            ? $"the last one was delivered at {When(delivered)}."
            : notifications.LastNotificationId is null ? "nothing has needed one yet."
            : onAgain.Count > 0 ? $"none has reached you since {string.Join(" and ", onAgain)} {(onAgain.Count == 1 ? "was" : "were")} turned on again."
            : hidden ? "none has reached you since this runtime started."
            : "none has reached you yet.";
        return new StatusCheck(
            Name,
            false,
            CheckState.Ok,
            string.Join(" ", [$"Notifications reach you by {string.Join(" and ", reaching)}; {last}", .. asides]),
            null);

        static string When(DateTimeOffset? at) =>
            at?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "an unknown time";

        static string Detail(string? detail) => detail is null ? string.Empty : ": " + detail;
    }

    private static async Task<T?> ReadAsync<T>(RuntimeClient client, string operation, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await client.PostAsync(operation, cancellationToken).ConfigureAwait(false);
            return response.IsSuccess ? JsonSerializer.Deserialize<T>(response.Body, JasonJson.Options) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>One enabled profile as the <c>agent_hosts</c> check reads it: its name, and its host as the runtime wrote it.</summary>
internal sealed record ProfileHost(string Name, string Host, bool Refused = false);
