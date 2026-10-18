using System.Globalization;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Skills;

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

        public const string ReloadPlugins = "jason plugin reload";

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
        /// Deliberately the same as <see cref="InstallRoles"/>. Overwriting is not what repairs a role whose
        /// directory is missing a SKILL.md or is over the cap -- a plain install replaces it -- and --force
        /// overwrites every edited file in every root of the plan, so printing it as the repair for any role
        /// problem would cost an operator unrelated work for a problem that did not need it.
        /// </summary>
        public const string Reinstall = InstallRoles;

        /// <summary>
        /// <see cref="InstallRoles"/>, from the source a deployment came from wherever that is not this build's
        /// default: the repair for role skills at neither the runtime's tag nor the one this build installs.
        /// </summary>
        /// <remarks>
        /// Bare, it would move a deployment taught from a mirror, or from a repository on this machine, to the default
        /// repository — or fail there, where the tag it asks for may not exist yet — and its record would then say it
        /// had always come from there. The record names its source, and that is the one value a repair here is
        /// composed from: a guard reads this as the one member handed anything but the environment, and types what it
        /// composes.
        /// </remarks>
        public static string InstallRolesFrom(string source) =>
            string.Equals(source, SkillsSource.DefaultSource, StringComparison.Ordinal) ? InstallRoles : $"{InstallRoles} --source {source}";

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
        /// <see cref="Install"/> for one pack, from the source a deployment came from wherever that is not this build's
        /// default: the line that moves a harness deployment its record pins to this build's tag.
        /// </summary>
        /// <remarks>
        /// Printed in a fact, not as a repair, because the check passes: the record pins its ref, made with --ref or
        /// marked so by an earlier <c>skills update</c>, and <see cref="UpdateSkills"/> keeps a pinned ref. An install
        /// that names no ref writes the pack again at this build's tag, and its record no longer pins it. Its reach is
        /// the pack's: for the runtime pack that includes the role half in the runtime's own root, which no flag
        /// separates.
        /// </remarks>
        public static string InstallPackFrom(string pack, string source) =>
            string.Equals(source, SkillsSource.DefaultSource, StringComparison.Ordinal) ? $"{Install} --pack {pack}" : $"{Install} --pack {pack} --source {source}";

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
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home.Length == 0
                ? null
                : PathEntry.AppendCommand(directory, PathEntry.LoginProfile(home, Environment.GetEnvironmentVariable("SHELL")));
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

    public static async Task<StatusReport> ComposeAsync(CliEnvironment env, string? providerCli, CancellationToken cancellationToken)
    {
        var checks = new List<StatusCheck>();

        var descriptor = new DescriptorReader(env.Paths).Read();
        SystemInfoResponse? info = null;
        RuntimeClient? client = null;
        try
        {
            if (descriptor is not null)
            {
                client = new RuntimeClient(descriptor, env.HttpHandler);
                info = await ReadAsync<SystemInfoResponse>(client, Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
            }

            checks.Add(Runtime(descriptor, info));
            checks.Add(RuntimeVersion(info, SkillsSource.BuildVersion(env)));
            checks.Add(Migrations(info));
            checks.Add(Plugins(info));
            checks.Add(await RoleSkillsAsync(env, client, info, cancellationToken).ConfigureAwait(false));
            checks.Add(Path(env));

            checks.Add(ProviderPlugin(info));
            var routes = client is null ? null : await ReadAsync<RoutesDto>(client, Operations.RouteList, cancellationToken).ConfigureAwait(false);
            checks.Add(Route(routes));
            checks.Add(Binding(routes));
            checks.Add(await ProviderCliAsync(env, providerCli, cancellationToken).ConfigureAwait(false));
            checks.Add(Harnesses(env));
            checks.Add(Autostart(env));
            checks.Add(Notifications(info));
            checks.Add(UiModule(info));
        }
        finally
        {
            client?.Dispose();
        }

        return new StatusReport(checks.All(check => !check.Required || check.State == CheckState.Ok), checks);
    }

    private static StatusCheck Runtime(Contracts.Discovery.RuntimeDescriptor? descriptor, SystemInfoResponse? info)
    {
        if (descriptor is null)
        {
            return new StatusCheck("runtime", true, CheckState.Failed, "No runtime is running: there is no endpoint descriptor to ask.", Repair.Start);
        }

        return info is null
            ? new StatusCheck("runtime", true, CheckState.Failed, $"A descriptor names {descriptor.BaseUrl} and nothing answered there.", Repair.Start)
            : new StatusCheck("runtime", true, CheckState.Ok, $"Running: version {info.RuntimeVersion}, pid {info.Pid.ToString(CultureInfo.InvariantCulture)}, data directory {info.DataDir}.", null);
    }

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
    /// A skill problem's sentence with its code after it, in brackets, where the runtime gave one: the code is what a
    /// reader of a bundle has where the sentence is withheld, and what a reader of this fact can search for. A runtime
    /// from before the codes gives none, and its sentence stands alone.
    /// </summary>
    /// <summary>Which UI is served, or that none is. Optional: a runtime works without one, so this never decides readiness.</summary>
    private static StatusCheck UiModule(SystemInfoResponse? info)
    {
        if (info is null)
        {
            return new StatusCheck("ui_module", false, CheckState.Unknown, "The runtime did not answer, so which UI it serves was not asked.", Repair.Start);
        }

        if (info.Ui is null)
        {
            return new StatusCheck("ui_module", false, CheckState.Absent, "No UI is installed; `jason ui open` says how to put one in place.", null);
        }

        var ui = info.Ui;
        if (ui.Problem is not null)
        {
            return new StatusCheck("ui_module", false, CheckState.Failed, $"Serving {ui.Source} from {ui.Directory}, with a problem: {ui.Problem}", null);
        }

        if (!ui.ApiVersionMatches)
        {
            return new StatusCheck("ui_module", false, CheckState.Failed, $"Serving {ui.Source} {ui.Version} from {ui.Directory}, built for API {ui.ApiVersion}; this runtime speaks {info.ApiVersion}; reinstall from a release that matches.", null);
        }

        return new StatusCheck("ui_module", false, CheckState.Ok, $"Serving {ui.Source} {ui.Version} from {ui.Directory}; built for API {ui.ApiVersion}, this runtime's; {ui.Digest}.", null);
    }

    private static string Coded(string problem, string? code) => code is null ? problem : $"{problem} [{code}]";

    /// <summary>
    /// The skills the roles are taught from, reported. A runtime that cannot teach its roles is not ready,
    /// whatever else is true — and the check is not only presence: a deployment the launcher would refuse is
    /// worse than none, because it refuses every launch of that role rather than running it untaught.
    /// </summary>
    private static async Task<StatusCheck> RoleSkillsAsync(CliEnvironment env, RuntimeClient? client, SystemInfoResponse? info, CancellationToken cancellationToken)
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

        var seeded = roster.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var deployed = skills.Roles.Select(role => role.Role).ToHashSet(StringComparer.Ordinal);

        var refused = skills.Roles.Where(role => role.Problem is not null).ToList();
        if (refused.Count > 0)
        {
            return new StatusCheck(
                "role_skills",
                true,
                CheckState.Failed,
                string.Join(" ", refused.Select(role => Coded(role.Problem!, role.ProblemCode))),
                Repair.Reinstall);
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

        var stranger = deployed.Except(seeded).Order(StringComparer.Ordinal).ToList();
        var note = stranger.Count == 0
            ? string.Empty
            : $" {string.Join(", ", stranger.Select(name => $"'{name}'"))} is deployed and is not a role this runtime seeds, so nothing reads it.";
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
    /// Compared only where both sides have a tag. A runtime that is not a release carries the pin of a release not
    /// published yet, which every development runtime would fail against; and so does a build that is not a release,
    /// whose repair would write that pin and so never clear. A development build compares nothing here, as it moves
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

    private static StatusCheck Route(RoutesDto? routes)
    {
        if (routes is null)
        {
            return new StatusCheck("route", false, CheckState.Unknown, "The runtime did not answer.", null);
        }

        var configured = routes.Global.Default is not null || routes.Global.Operations.Count > 0 || routes.Campaigns.Count > 0;
        return configured
            ? new StatusCheck("route", false, CheckState.Ok, $"Routed: default {routes.Global.Default?.PluginId ?? "none"}.", null)
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
        }
    }

    /// <summary>
    /// Whether the provider's own CLI is on this machine and answering. It names the command it runs, it is
    /// bounded, and it prints no credential: not the key, not a prefix of it, not its length.
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

        var result = await runner.RunAsync(providerCli, ProviderArguments, null, ProviderTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            return new StatusCheck("provider_cli", false, CheckState.Absent, $"'{command}' did not answer in {ProviderTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.", null);
        }

        return result.ExitCode == 0
            ? new StatusCheck("provider_cli", false, CheckState.Ok, $"'{command}' answered.", null)
            : new StatusCheck("provider_cli", false, CheckState.Absent, $"'{command}' is not on this machine, or did not answer.", null);
    }

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
        // with the line that moves it, since an update keeps a pinned ref. A build that is not a release, and a
        // directory, whose record names no commit, compare nothing.
        var build = SkillsSource.BuildVersion(env);
        var own = SkillsSource.IsRelease(build) ? SkillsSource.DefaultRef(build) : null;

        var deployed = new List<string>();
        var behind = new List<string>();
        var pinned = new List<string>();
        foreach (var root in roots.Select(root => root.Directory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var record = SkillsRecord.Read(root);
                if (record is not null)
                {
                    deployed.AddRange(record.Packs.Select(pack => $"{pack.Pack} at {pack.Ref} in {root}"));
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

        return behind.Count > 0
            ? new StatusCheck("harness_skills", false, CheckState.Failed, string.Join(" ", behind.Concat(pinned)), Repair.UpdateSkills)
            : new StatusCheck("harness_skills", false, CheckState.Ok, string.Join("; ", deployed) + "." + string.Concat(pinned.Select(note => " " + note)), null);
    }

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
            ? new StatusCheck("autostart", false, CheckState.Ok, "The runtime is registered to start at logon.", null)
            : new StatusCheck("autostart", false, CheckState.Absent, "The runtime is not registered to start at logon.", Repair.EnableAutostart);
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
                    SilencedReasons.DisabledForApplication => "Windows has Jason's notifications switched off (Settings → System → Notifications → Jason).",
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
