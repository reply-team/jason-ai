using System.Globalization;
using System.Text;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;

namespace Jason.Cli.Installation;

/// <summary>What was asked of <c>jason install</c>, as parsed.</summary>
/// <param name="Human">Lines for a person instead of the one JSON document.</param>
/// <param name="Roles">Also the role skills, into the data directory.</param>
/// <param name="Retire">The executable of a command-line install to retire, where it is not one of the places looked in.</param>
/// <param name="Drain">How long a runtime running from that executable is given to finish what it holds. Null is a minute.</param>
/// <param name="StopTimeout">How long it is given to go. Null is the stop verb's own wait.</param>
/// <param name="StopPoll">How often it is asked. Null is the stop verb's own interval.</param>
public sealed record InstallOptions(bool Human, bool Roles, string? Retire, TimeSpan? Drain = null, TimeSpan? StopTimeout = null, TimeSpan? StopPoll = null, string? TerminalPath = null);

/// <summary>The terminal command, as this run left it.</summary>
/// <param name="Runs">
/// What a new terminal finds <c>jason</c> through: the wrapper <c>~/.local/bin/jason</c> on macOS, the folder this
/// account's Path names on Windows.
/// </param>
/// <param name="OpenShell">
/// What a shell that was already open types instead, for the rest of its life: it never reads a profile or a Path again.
/// </param>
/// <param name="Changed">Whether this run wrote the wrapper or the Path entry.</param>
/// <param name="Profiles">The profiles this run put <c>install.sh</c>'s two lines into; macOS only.</param>
/// <param name="TakenBack">The application's Windows command files this run removed from <c>WindowsApps</c>.</param>
public sealed record InstallCommandOutcome(string Runs, string OpenShell, bool Changed, IReadOnlyList<string> Profiles, IReadOnlyList<string> TakenBack);

/// <summary>One skills root, as this run left it.</summary>
/// <param name="Root">The folder.</param>
/// <param name="Hosts">The hosts that read it, in the vocabulary of execution profiles; none for the role skills.</param>
/// <param name="Written">Files written.</param>
/// <param name="Unchanged">Files already as the bundle has them.</param>
/// <param name="LeftAlone">
/// Skill folders under a name Jason ships that no record of Jason's names there: the person's own, left as they are.
/// </param>
public sealed record InstallSkillsOutcome(string Root, IReadOnlyList<string> Hosts, int Written, int Unchanged, IReadOnlyList<string> LeftAlone);

/// <summary>The command-line install this run retired.</summary>
/// <param name="Executable">Its executable, where it was found.</param>
/// <param name="Aside">
/// Where that executable was renamed to, on Windows, because a running process held it: nothing waits to remove it, and
/// it goes once nothing runs from it. Null where it was removed, or replaced by the wrapper.
/// </param>
/// <param name="Removed">What of it went: its files, its Path entry or profile block, its folder.</param>
/// <param name="Reregistered">Whether the logon registration, which named it, was registered again naming this jason.</param>
public sealed record InstallRetired(string Executable, string? Aside, IReadOnlyList<string> Removed, bool Reregistered);

/// <summary>What one run of <c>jason install</c> did: the one document it prints.</summary>
/// <param name="Version">The version of the jason the application carries, which is the one this run set up.</param>
/// <param name="Executable">That jason.</param>
/// <param name="Command">The terminal command.</param>
/// <param name="Skills">The harness skills, one entry per root of a host found.</param>
/// <param name="Roles">The role skills, with <c>--roles</c>; null without it.</param>
/// <param name="Retired">The command-line install retired; null where none was found.</param>
/// <param name="Runtime">
/// <c>not_running</c>; <c>running</c>, a runtime of another executable, left as it is; <c>stopped</c>, a runtime that ran from
/// the retired executable, drained and stopped and started by nobody here; <c>would_not_stop</c>, the same one still running,
/// and then nothing of that install was retired.
/// </param>
/// <param name="Done">What this run did, one sentence each.</param>
/// <param name="Left">What it left, and why, one sentence each.</param>
/// <param name="Problems">What it set out to do and did not; the run exits 1 when there is any.</param>
public sealed record InstallReport(
    string Version,
    string Executable,
    InstallCommandOutcome? Command,
    IReadOnlyList<InstallSkillsOutcome> Skills,
    InstallSkillsOutcome? Roles,
    InstallRetired? Retired,
    string Runtime,
    IReadOnlyList<string> Done,
    IReadOnlyList<string> Left,
    IReadOnlyList<string> Problems)
{
    /// <summary>Whether everything it set out to do is done.</summary>
    public bool Completed => Problems.Count == 0;
}

/// <summary>
/// The machine setup: the routine behind <c>jason install</c>, run by the jason the application carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every step is idempotent and says what it did.</b> A second run finds the wrapper, the lines, the Path entry and the
/// skills as the first left them and writes nothing; it finds no command-line install to retire.
/// </para>
/// <para>
/// <b>It starts no process that outlives it.</b> A caller that waits on its whole process tree — PowerShell's
/// <c>Start-Process -Wait</c> around the installer that runs this — would wait for ever on a runtime or a waiter started
/// here. So a runtime this run stops is started by nobody here, and an executable a running process holds is renamed aside
/// with nothing left waiting to delete it.
/// </para>
/// <para>
/// <b>Without <c>--roles</c> it writes nothing under the data directory</b>, so a person's first start of the application
/// still finds a data directory that holds nothing and opens on the Demo. Only retiring an install that already has data — a
/// runtime to stop, a logon registration to make again — touches it, and that data was there before.
/// </para>
/// </remarks>
public static class MachineSetup
{
    private static readonly TimeSpan DefaultDrain = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan DrainPoll = TimeSpan.FromMilliseconds(500);

    public static async Task<InstallReport> RunAsync(CliEnvironment env, CarriedExecutable carried, SetupPlaces places, InstallOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(options);

        var run = new Run(env, carried, places, options);
        var old = run.FindCommandLineInstall();
        var runtime = old is null ? run.RuntimeOutside() : await run.StopOldRuntimeAsync(old, cancellationToken).ConfigureAwait(false);
        var holding = runtime == RuntimeStates.WouldNotStop;

        var command = places.Platform is SetupSystem.Windows ? run.WindowsCommand() : run.MacCommand(old, holding);
        var retired = old is null || holding ? null : run.Retire(old);

        var skills = await run.HarnessSkillsAsync(cancellationToken).ConfigureAwait(false);
        var roles = options.Roles ? await run.RoleSkillsAsync(cancellationToken).ConfigureAwait(false) : null;

        return new InstallReport(carried.Version, carried.Executable, command, skills, roles, retired, runtime, run.Done, run.Left, run.Problems);
    }

    /// <summary>The words the <c>runtime</c> member takes.</summary>
    public static class RuntimeStates
    {
        public const string NotRunning = "not_running";
        public const string Running = "running";
        public const string Stopped = "stopped";
        public const string WouldNotStop = "would_not_stop";
    }

    private sealed class Run(CliEnvironment env, CarriedExecutable carried, SetupPlaces places, InstallOptions options)
    {
        public List<string> Done { get; } = [];

        public List<string> Left { get; } = [];

        public List<string> Problems { get; } = [];

        private bool Windows => places.Platform is SetupSystem.Windows;

        private string Bundle => Path.GetDirectoryName(carried.Executable)!;

        private StringComparison PathCase => Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // --- the command-line install --------------------------------------------------------------------------

        /// <summary>
        /// The executable an older command-line install placed, or null where none is here: the one <c>--retire</c> names,
        /// else the one the last <c>jason update apply</c> recorded, else the installer's default place. A file is that only
        /// where it is not the application's (no <c>version.json</c> beside it), not the application's wrapper, and Jason's
        /// by a sign of its own: named by the flag or the record, a notice file of Jason's beside it, or on Windows the
        /// installer's own folder.
        /// </summary>
        public string? FindCommandLineInstall()
        {
            if (options.Retire is { } named)
            {
                var full = Path.GetFullPath(named);
                if (!File.Exists(full))
                {
                    Problems.Add($"--retire names {full}, and there is no file there, so nothing was retired.");
                    return null;
                }

                if (CarriedExecutable.Of(full) is not null)
                {
                    Problems.Add($"--retire names {full}, which an application carries (a version.json beside it), so it was not retired.");
                    return null;
                }

                return full;
            }

            var recorded = Recorded();
            foreach (var candidate in new[] { recorded, places.DefaultCommandLineExecutable }.OfType<string>())
            {
                var full = Path.GetFullPath(candidate);
                if (!IsRegularFile(full) || CarriedExecutable.Of(full) is not null || IsWrapper(full))
                {
                    continue;
                }

                var signed = string.Equals(full, recorded, PathCase)
                    || ReleaseAssets.Notices.Any(notice => File.Exists(ReleaseAssets.NoticeBeside(full, notice)))
                    || (Windows && string.Equals(full, Path.GetFullPath(places.DefaultCommandLineExecutable), PathCase));
                if (signed)
                {
                    return full;
                }
            }

            return null;
        }

        /// <summary>The install path the update ledger names, where there is a readable one; reading it writes nothing.</summary>
        private string? Recorded()
        {
            try
            {
                return UpdateLedger.ReadFile(new UpdatePaths(env.Paths).Ledger)?.InstallPath is { Length: > 0 } path ? Path.GetFullPath(path) : null;
            }
            catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or UpdateLedgerException or JsonException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>What runs on this data directory, where nothing is retired: said, and left as it is.</summary>
        public string RuntimeOutside() =>
            DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance) is null ? RuntimeStates.NotRunning : RuntimeStates.Running;

        /// <summary>
        /// A runtime running from the executable being retired is drained and stopped before its file is touched: its
        /// plugin hosts start from its own image, and a file replaced under it would start them from another version.
        /// </summary>
        public async Task<string> StopOldRuntimeAsync(string old, CancellationToken cancellationToken)
        {
            var processes = env.Processes ?? RuntimeProcessControl.Instance;
            if (DescriptorProcess.Live(env.Paths, processes) is not { } descriptor)
            {
                return RuntimeStates.NotRunning;
            }

            if (descriptor.InstalledImage is not { } image || !string.Equals(Path.GetFullPath(image), old, PathCase))
            {
                Left.Add($"The runtime that runs on {env.Paths.Root} runs from {descriptor.InstalledImage ?? "an executable it does not name"}, not from {old}, so it was left running.");
                return RuntimeStates.Running;
            }

            using var said = new StringWriter();
            var quiet = env with { Out = said };
            var (_, drain) = await OperationRunner.SendAsync(quiet, Operations.SystemDrain, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
            if (drain is { IsSuccess: true })
            {
                await WaitForAttemptsAsync(quiet, cancellationToken).ConfigureAwait(false);
            }

            var exit = await RuntimeStopCommand.RunAsync(quiet, human: false, cancellationToken, options.StopTimeout, options.StopPoll).ConfigureAwait(false);
            if (exit != ExitCodes.Success)
            {
                env.Error.Write(said.ToString());
                Problems.Add($"The runtime running from {old} would not stop, so nothing of that install was retired. Stop it with `jason runtime stop` and run this again.");
                return RuntimeStates.WouldNotStop;
            }

            Done.Add($"Stopped the runtime that ran from {old}.");
            Left.Add($"No runtime was started: `jason runtime start` starts one from {carried.Executable}.");
            return RuntimeStates.Stopped;
        }

        private async Task WaitForAttemptsAsync(CliEnvironment quiet, CancellationToken cancellationToken)
        {
            var clock = env.Clock ?? TimeProvider.System;
            var deadline = clock.GetUtcNow() + (options.Drain ?? DefaultDrain);
            while (true)
            {
                var (_, info) = await OperationRunner.SendAsync(quiet, Operations.SystemInfo, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
                var running = info is { IsSuccess: true } ? JsonSerializer.Deserialize<SystemInfoResponse>(info.Body, JasonJson.Options)?.Dispatcher.RunningAttempts : null;
                if (running is null or 0)
                {
                    return;
                }

                if (clock.GetUtcNow() >= deadline)
                {
                    Left.Add(string.Create(CultureInfo.InvariantCulture, $"{running} attempt(s) were still running when the runtime was stopped; they keep their leases."));
                    return;
                }

                await Task.Delay(DrainPoll, clock, cancellationToken).ConfigureAwait(false);
            }
        }

        // --- the terminal command ----------------------------------------------------------------------------

        /// <summary>
        /// macOS: the wrapper at <c>~/.local/bin/jason</c>, in the application's own shape, then, where the PATH a terminal has
        /// does not name <c>~/.local/bin</c> (<see cref="TerminalPath"/>, judged by <c>install.sh</c>'s own test), its two lines
        /// in each profile it writes them into, where that profile does not hold the line already. The wrapper goes over an
        /// older wrapper of the application's, an older command-line install's executable, or a link to an application's jason;
        /// any other file there is another program's, and stays.
        /// </summary>
        public InstallCommandOutcome? MacCommand(string? old, bool holding)
        {
            var target = Path.Combine(places.LocalBin, "jason");
            var text = CommandFiles.Wrapper(carried.Executable, DataDirectoryNamed());
            var changed = false;

            var replaceable = !File.Exists(target) && !IsLink(target)
                || IsWrapper(target)
                || (old is not null && string.Equals(old, Path.GetFullPath(target), StringComparison.Ordinal) && !holding)
                || LinksToAnApplication(target);

            if (!replaceable)
            {
                Problems.Add(old is not null && holding && string.Equals(old, Path.GetFullPath(target), StringComparison.Ordinal)
                    ? $"{target} was left as it is, since the runtime running from it would not stop."
                    : $"{target} is another program's, so it was left as it is and no jason was placed there.");
                return null;
            }

            if (!(File.Exists(target) && !IsLink(target) && string.Equals(ReadSmall(target), text, StringComparison.Ordinal)))
            {
                try
                {
                    Directory.CreateDirectory(places.LocalBin);
                    var temporary = target + ".jason-install.tmp";
                    File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }

                    File.Move(temporary, target, overwrite: true);
                    changed = true;
                    Done.Add($"Wrote {target}, which runs {carried.Executable}.");
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"{target} could not be written: {refused.Message}");
                    return null;
                }
            }

            var added = new List<string>();
            if (PathEntry.OnPath(TerminalPath(), places.LocalBin))
            {
                Done.Add($"{places.LocalBin} is on the PATH a terminal has already, so no profile was written.");
                return new InstallCommandOutcome(target, target, changed, added, []);
            }

            foreach (var profile in PathEntry.ProfileTargets(places.Home, places.Shell))
            {
                try
                {
                    if (PathEntry.AddToProfile(profile, places.LocalBin))
                    {
                        added.Add(profile);
                    }
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"{profile} could not be written ({refused.Message}), so a new terminal may not find jason. Add these two lines to it by hand: {PathEntry.Marker} / {PathEntry.ExportLine(places.LocalBin)}");
                }
            }

            if (added.Count > 0)
            {
                Done.Add($"Added {places.LocalBin} to the PATH in {string.Join(" and ", added)}.");
            }

            return new InstallCommandOutcome(target, target, changed, added, []);
        }

        /// <summary>
        /// Windows: one entry in this account's Path naming the folder this jason is in, where it is not there, the value's
        /// kind kept and running programs told; and the application's own command files in <c>WindowsApps</c> taken back,
        /// each only where it is one of the application's shapes.
        /// </summary>
        public InstallCommandOutcome? WindowsCommand()
        {
            if (!OperatingSystem.IsWindows())
            {
                Problems.Add("The Windows Path is edited on Windows only.");
                return null;
            }

            var changed = false;
            try
            {
                changed = new UserPathValue(places.PathKey).Append(Bundle);
                Done.Add(changed ? $"Added {Bundle} to this account's Path." : $"{Bundle} was on this account's Path already.");
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Problems.Add($"This account's Path could not be written ({refused.Message}), so a new terminal does not find jason: {carried.Executable} runs it by its full path.");
            }

            var takenBack = new List<string>();
            foreach (var name in CommandFiles.Names)
            {
                foreach (var file in new[] { Path.Combine(places.WindowsApps, name + ".cmd"), Path.Combine(places.WindowsApps, name) })
                {
                    if (!IsRegularFile(file) || ReadSmall(file) is not { } text || !CommandFiles.IsOurs(text))
                    {
                        continue;
                    }

                    try
                    {
                        places.Delete(file);
                        takenBack.Add(file);
                    }
                    catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                    {
                        Problems.Add($"{file}, which Jason Desktop placed, could not be removed: {refused.Message}");
                    }
                }
            }

            if (takenBack.Count > 0)
            {
                Done.Add($"Took back the command files Jason Desktop placed: {string.Join(", ", takenBack)}.");
            }

            return new InstallCommandOutcome(Bundle, carried.Executable, changed, [], takenBack);
        }

        // --- retiring the command-line install ---------------------------------------------------------------

        public InstallRetired Retire(string old)
        {
            var removed = new List<string>();
            var reregistered = Reregister(old);
            string? aside = null;

            var replacedByWrapper = !Windows && string.Equals(old, Path.GetFullPath(Path.Combine(places.LocalBin, "jason")), StringComparison.Ordinal);
            if (!replacedByWrapper)
            {
                aside = RemoveExecutable(old, removed);
            }

            var beside = ReleaseAssets.Notices.Select(notice => ReleaseAssets.NoticeBeside(old, notice)).ToList();
            if (Windows)
            {
                beside.Add(Path.Combine(Path.GetDirectoryName(old)!, "jason.previous.exe"));
            }

            foreach (var file in beside.Where(File.Exists))
            {
                try
                {
                    places.Delete(file);
                    removed.Add(file);
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"{file} could not be removed: {refused.Message}");
                }
            }

            var directory = Path.GetDirectoryName(old)!;
            if (Windows)
            {
                TakePathEntry(directory, removed);
            }
            else if (!replacedByWrapper)
            {
                TakeProfileBlock(directory, removed);
            }

            if (!replacedByWrapper && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                try
                {
                    Directory.Delete(directory);
                    removed.Add(directory);
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Left.Add($"{directory} is empty and was left: {refused.Message}");
                }
            }

            Done.Add(removed.Count > 0 || replacedByWrapper
                ? $"Retired the command-line install at {old}{(removed.Count > 0 ? ": removed " + string.Join(", ", removed) : string.Empty)}."
                : $"Retired the command-line install at {old}.");
            return new InstallRetired(old, aside, removed, reregistered);
        }

        /// <summary>
        /// Deleted where nothing runs from it; on Windows, where a running process holds it, renamed into a folder of its
        /// own on its own volume and left there, with nothing started to wait for that process.
        /// </summary>
        private string? RemoveExecutable(string old, List<string> removed)
        {
            try
            {
                places.Delete(old);
                removed.Add(old);
                return null;
            }
            catch (Exception held) when (Windows && held is IOException or UnauthorizedAccessException)
            {
                var folder = AsideFolder(old);
                try
                {
                    Directory.CreateDirectory(folder);
                    var aside = Path.Combine(folder, Path.GetFileName(old));
                    File.Move(old, aside);
                    Left.Add($"{old} is held by a running process, so it was renamed to {aside}; nothing waits to remove it, and it can be deleted once nothing runs from it.");
                    return aside;
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"{old} could neither be removed nor renamed aside: {refused.Message}");
                    return null;
                }
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                Problems.Add($"{old} could not be removed: {refused.Message}");
                return null;
            }
        }

        /// <summary>The temporary directory where it is on the executable's volume, else beside its folder: a rename never crosses one.</summary>
        private string AsideFolder(string old)
        {
            var name = $"jason-retired-{Guid.NewGuid():N}";
            if (string.Equals(Path.GetPathRoot(old), Path.GetPathRoot(Path.GetFullPath(places.Temporary)), StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(places.Temporary, name);
            }

            var folder = Path.GetDirectoryName(old)!;
            return Path.Combine(Path.GetDirectoryName(folder) ?? folder, "." + name);
        }

        /// <summary>The old folder's Path entry, by the uninstall's rule: only where the folder holds nothing but what <c>install.ps1</c> writes.</summary>
        private void TakePathEntry(string directory, List<string> removed)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var value = new UserPathValue(places.PathKey);
                var stored = value.Read();
                var expand = PathEntry.ReadAs(stored?.Kind != Microsoft.Win32.RegistryValueKind.String);
                if (stored is null || !PathEntry.Carries(stored.Raw, directory, expand))
                {
                    return;
                }

                if (!PathEntry.JasonsOwn(directory, windows: true))
                {
                    Left.Add(PathEntry.WhyLeft(new PathEntryPlan(directory, [], stored.Raw, false, "this account's Path value"), windows: true));
                    return;
                }

                value.Write(PathEntry.WithoutDirectory(stored.Raw, directory, expand), stored.Kind);
                removed.Add($"the Path entry {directory}");
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Problems.Add($"The Path entry {directory} could not be taken off: {refused.Message}");
            }
        }

        /// <summary>A retired folder's profile block on macOS, by the uninstall's rule, where it was not <c>~/.local/bin</c>.</summary>
        private void TakeProfileBlock(string directory, List<string> removed)
        {
            var plan = PathEntry.ReadProfiles(directory, places.Home, places.Shell);
            if (plan.Profiles.Count == 0)
            {
                return;
            }

            if (!plan.Ours)
            {
                Left.Add(PathEntry.WhyLeft(plan, windows: false));
                return;
            }

            foreach (var profile in plan.Profiles)
            {
                try
                {
                    var bytes = File.ReadAllBytes(profile);
                    var without = PathEntry.WithoutEntry(bytes, directory);
                    if (!ReferenceEquals(without, bytes))
                    {
                        File.WriteAllBytes(profile, without);
                        removed.Add($"the block for {directory} in {profile}");
                    }
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"{profile} could not be rewritten ({refused.Message}): take the line {PathEntry.ExportLine(directory)} and the line {PathEntry.Marker} above it out by hand.");
                }
            }
        }

        /// <summary>
        /// The logon registration, where it names the retired executable and this data directory, registered again naming
        /// this jason, the way <c>jason runtime autostart enable</c> run from it registers.
        /// </summary>
        private bool Reregister(string old)
        {
            var registrar = RuntimeAutostartCommands.Registrar(env);
            if (registrar.Platform is AutostartPlatform.Unsupported)
            {
                return false;
            }

            try
            {
                var state = registrar.Read();
                if (!state.Registered || state.Run.Count == 0 || !string.Equals(Path.GetFullPath(state.Run[0]), old, PathCase))
                {
                    return false;
                }

                if (RuntimeAutostartCommands.DataDirectoryIn(state.Run) is { } named && !string.Equals(Path.GetFullPath(named), env.Paths.Root, PathCase))
                {
                    Left.Add($"The logon registration names {old} with the data directory {named}, not this one, so it was left as it is.");
                    return false;
                }

                registrar.Apply(RuntimeAutostartCommands.Registration(env with { InstallPath = carried.Executable }, registrar.Platform));
                Done.Add($"Registered the runtime's logon start again, naming {carried.Executable}.");
                return true;
            }
            catch (AutostartException refused)
            {
                Problems.Add($"The logon registration names {old} and could not be registered again: {refused.Message}");
                return false;
            }
        }

        // --- the skills --------------------------------------------------------------------------------------

        /// <summary>
        /// Both packs from the skills this jason carries, into the folder of each host found by its home marker, Claude Code
        /// and Codex; recorded. A skill folder of a name Jason ships that no record of Jason's names is the person's own, and
        /// is left as it is and named.
        /// </summary>
        public async Task<IReadOnlyList<InstallSkillsOutcome>> HarnessSkillsAsync(CancellationToken cancellationToken)
        {
            if (env.Harnesses is not { } locator)
            {
                Problems.Add("This environment does not detect agent hosts, so no skills were deployed.");
                return [];
            }

            var roots = locator.Detect().Where(root => root.Hosts.Contains(AgentHostKind.ClaudeCode) || root.Hosts.Contains(AgentHostKind.Codex)).ToList();
            if (roots.Count == 0)
            {
                Left.Add("No Claude Code (~/.claude) or Codex (~/.codex) was found, so no harness skills were deployed; a run after one is installed deploys them.");
                return [];
            }

            if (await StageAsync(cancellationToken).ConfigureAwait(false) is not { } staged)
            {
                return [];
            }

            var outcomes = new List<InstallSkillsOutcome>();
            foreach (var root in roots)
            {
                var hosts = root.Hosts.Select(Jason.Contracts.Hosts.AgentHosts.Word).ToList();
                HashSet<string> recorded;
                try
                {
                    recorded = SkillsRecord.Read(root.Directory)?.Packs.SelectMany(pack => pack.Files).Select(file => file.Path.Split('/')[0]).ToHashSet(StringComparer.Ordinal) ?? [];
                }
                catch (SkillsRecordUnreadable unreadable)
                {
                    Problems.Add($"{root.Directory}: {unreadable.Message} Nothing was deployed there.");
                    continue;
                }

                var plan = SkillsPlanner.Compose(staged, null, [root], SkillsPlanner.DocumentedCap, false, null);
                var leftAlone = plan.Packs.SelectMany(pack => pack.Units)
                    .Where(unit => !recorded.Contains(unit.Name) && (Directory.Exists(unit.Unit) || File.Exists(unit.Unit)))
                    .Select(unit => unit.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                plan = plan with { Packs = [.. plan.Packs.Select(pack => pack with { Units = [.. pack.Units.Where(unit => !leftAlone.Contains(unit.Name))] })] };

                outcomes.Add(Deploy(plan, root.Directory, hosts, leftAlone));
            }

            return outcomes;
        }

        /// <summary>The role skills, from the skills this jason carries, into the data directory: <c>--roles</c> alone.</summary>
        public async Task<InstallSkillsOutcome?> RoleSkillsAsync(CancellationToken cancellationToken)
        {
            if (await StageAsync(cancellationToken).ConfigureAwait(false) is not { } staged)
            {
                return null;
            }

            var plan = SkillsPlanner.Compose(staged, env.Paths.RoleSkillsDirectory, [], SkillsPlanner.DocumentedCap, false, SkillPacks.Runtime);
            return Deploy(plan, env.Paths.RoleSkillsDirectory, [], []);
        }

        private StagedSource? _staged;

        private async Task<StagedSource?> StageAsync(CancellationToken cancellationToken)
        {
            if (_staged is not null)
            {
                return _staged;
            }

            try
            {
                // The folder beside this jason, read where it stands: no download, nothing written under the data directory.
                _staged = await SkillsSource.StageAsync(env with { Out = TextWriter.Null }, Bundle, null, null, cancellationToken).ConfigureAwait(false);
                return _staged;
            }
            catch (SkillsSourceUnavailable unavailable)
            {
                Problems.Add($"The skills beside {carried.Executable} could not be read, so none were deployed: {unavailable.Message}");
                return null;
            }
        }

        private InstallSkillsOutcome Deploy(SkillsPlan plan, string root, IReadOnlyList<string> hosts, IReadOnlyList<string> leftAlone)
        {
            foreach (var refusal in plan.Refusals)
            {
                Problems.Add($"{root}: {refusal}");
            }

            if (plan.Refusals.Count > 0)
            {
                return new InstallSkillsOutcome(root, hosts, 0, 0, leftAlone);
            }

            var report = SkillsDeployer.Apply(plan, env.Paths, force: false, env.Clock ?? TimeProvider.System);
            foreach (var problem in report.Problems)
            {
                Problems.Add($"{root}: {problem}");
            }

            if (report.Edited.Count > 0)
            {
                Left.Add(string.Create(CultureInfo.InvariantCulture, $"Kept {report.Edited.Count} file(s) in {root} that you have edited: {string.Join(", ", report.Edited)}."));
            }

            foreach (var name in leftAlone)
            {
                Left.Add($"{Path.Combine(root, name)} is a skill under a name Jason ships that no record of Jason's names there, so it is yours: left as it is.");
            }

            Done.Add(report.Written == 0
                ? $"The skills in {root} were current."
                : string.Create(CultureInfo.InvariantCulture, $"Deployed the skills into {root}: {report.Written} file(s) written, {report.Unchanged} already in place."));
            return new InstallSkillsOutcome(root, hosts, report.Written, report.Unchanged, leftAlone);
        }

        // --- reading the machine -----------------------------------------------------------------------------

        /// <summary>
        /// The PATH a terminal of this account has, which decides whether the profile lines are written, as it decides for
        /// <c>install.sh</c>: the one the caller names with <c>--terminal-path</c>, null where it names the empty value — its
        /// login shell gave no answer, and a folder counts as not there — and, where it names none, this process's own,
        /// which is a terminal's when a script runs this.
        /// </summary>
        private string? TerminalPath() =>
            options.TerminalPath switch
            {
                null => env.SearchPath ?? Environment.GetEnvironmentVariable("PATH"),
                "" => null,
                var named => named,
            };

        /// <summary><c>JASON_DATA_DIR</c> where it is set, which the wrapper then names as the application's does; else null.</summary>
        private string? DataDirectoryNamed()
        {
            var named = env.Variables is { } variables
                ? variables.GetValueOrDefault(JasonPaths.DataDirectoryVariable)
                : Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
            return string.IsNullOrWhiteSpace(named) ? null : env.Paths.Root;
        }

        private static bool IsRegularFile(string path)
        {
            try
            {
                var file = new FileInfo(path);
                return file.Exists && file.LinkTarget is null;
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsLink(string path)
        {
            try
            {
                return new FileInfo(path).LinkTarget is not null;
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>A link whose final file is a jason an application carries.</summary>
        private static bool LinksToAnApplication(string path)
        {
            if (!IsLink(path))
            {
                return false;
            }

            try
            {
                return File.ResolveLinkTarget(path, returnFinalTarget: true) is FileInfo { Exists: true } final && CarriedExecutable.Of(final.FullName) is not null;
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsWrapper(string path) => IsRegularFile(path) && ReadSmall(path) is { } text && CommandFiles.WrapperTarget(text) is not null;

        /// <summary>A small file's text, or null for a large one (an executable) or one that cannot be read.</summary>
        private static string? ReadSmall(string path)
        {
            try
            {
                return new FileInfo(path).Length > 64 * 1024 ? null : File.ReadAllText(path);
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
