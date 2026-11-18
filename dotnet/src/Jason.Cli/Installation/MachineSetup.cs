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
using Jason.Contracts.Harness;
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
/// <param name="Placed">
/// Whether a new terminal finds this jason, as read back: the Path entry on Windows; on macOS the wrapper, with the profile
/// lines in place or the folder on a terminal's PATH already. The install scripts end non-zero where it is false.
/// </param>
/// <param name="OpenShell">
/// What a shell that was already open types instead, for the rest of its life: it never reads a profile or a Path again.
/// </param>
/// <param name="Changed">Whether this run wrote the wrapper or the Path entry.</param>
/// <param name="Profiles">The profiles this run put <c>install.sh</c>'s two lines into; macOS only.</param>
/// <param name="TakenBack">The application's Windows command files this run removed from <c>WindowsApps</c>.</param>
/// <param name="Finds">
/// The file a terminal opened from now on runs for <c>jason</c>, read as that terminal reads its PATH once this run has
/// placed the command and retired what it retires (<see cref="NewTerminal"/>): this jason, the wrapper running it, or
/// another file, which is then a problem naming what removes it. Null where none is found, or the command was not placed.
/// </param>
/// <param name="FindsThis">
/// Whether <paramref name="Finds"/> is this jason or the wrapper running it, decided here once (<see cref="NewTerminal.Runs"/>):
/// the install scripts read this and never compare paths themselves. False where nothing is found or the command was not
/// placed.
/// </param>
public sealed record InstallCommandOutcome(string Runs, bool Placed, string OpenShell, bool Changed, IReadOnlyList<string> Profiles, IReadOnlyList<string> TakenBack, string? Finds = null, bool FindsThis = false);

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

    /// <summary>How long taking a drain off again is waited for, on a wait of its own: within the time a cancelled verb is given to end.</summary>
    private static readonly TimeSpan ResumeWait = TimeSpan.FromSeconds(5);

    public static async Task<InstallReport> RunAsync(CliEnvironment env, CarriedExecutable carried, SetupPlaces places, InstallOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(options);

        // From a place that goes away, nothing at all: the update's door runs this routine with no InstallCommand before it.
        if (VanishingPlace.Refusal(carried, places) is { } vanishing)
        {
            var running = DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance) is null ? RuntimeStates.NotRunning : RuntimeStates.Running;
            return new InstallReport(carried.Version, carried.Executable, null, [], null, null, running, [], [], [vanishing]);
        }

        // The command-line executables an earlier run renamed aside while a process held them, once that process ended: before
        // this run renames any, whose process still runs.
        RetiredSweep.Sweep(places, options.Retire);

        var run = new Run(env, carried, places, options, cancellationToken);
        string? old = null;
        var runtime = RuntimeStates.NotRunning;
        var holding = false;
        InstallCommandOutcome? command = null;
        InstallRetired? retired = null;
        IReadOnlyList<InstallSkillsOutcome> skills = [];
        InstallSkillsOutcome? roles = null;

        // Each step in its own guard, so a failure of any kind no step expected ends in the one document like any other
        // problem, with every step before it reported.
        await run.Step("Looking for a command-line install", async () =>
        {
            old = run.FindCommandLineInstall();
            if (old is null)
            {
                runtime = run.RuntimeOutside();
            }
            else if (run.LayoutRefuses(old))
            {
                // Retiring stops a runtime and moves a registration: neither is done over a layout this jason does not run.
                runtime = run.RuntimeOutside();
                holding = true;
            }
            else
            {
                // Held from the moment it is found until the stop has given its answer: a stop that throws past this line
                // leaves nothing of the install to be written over, retired or registered again.
                holding = true;
                (runtime, holding) = await run.StopOldRuntimeAsync(old, cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        await run.Step("Placing the terminal command", () =>
        {
            command = places.Platform is SetupSystem.Windows ? run.WindowsCommand(old, holding) : run.MacCommand(old, holding);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        if (old is not null && !holding)
        {
            await run.Step($"Retiring the command-line install at {old}", () =>
            {
                // Only once a new terminal finds this jason, as read back: otherwise the old one is what still answers.
                retired = run.CommandPlaced ? run.Retire(old) : run.NotRetired(old);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        // Read back once more, as a new terminal reads its PATH, now that what this run retires is gone: the entry or the
        // wrapper being there is not yet that terminal finding this jason, where another file comes first.
        if (command is { Placed: true } placed)
        {
            await run.Step("Reading what a new terminal finds", () =>
            {
                var (finds, findsThis) = run.Finds();
                command = placed with { Finds = finds, FindsThis = findsThis };
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        await run.Step("Deploying the harness skills", async () => skills = await run.HarnessSkillsAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        if (options.Roles)
        {
            await run.Step("Deploying the role skills", async () => roles = await run.RoleSkillsAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        // The images earlier updates moved out of the application, taken away where nothing runs from them any more; quietly,
        // and never while an update is in flight, nor once the run is cancelled.
        if (!cancellationToken.IsCancellationRequested)
        {
            CarriedUpdate.SweepWhereNothingIsInFlight(CarriedUpdate.Store(places), new UpdatePaths(env.Paths));
        }

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

    private sealed class Run(CliEnvironment env, CarriedExecutable carried, SetupPlaces places, InstallOptions options, CancellationToken cancellation)
    {
        public List<string> Done { get; } = [];

        public List<string> Left { get; } = [];

        public List<string> Problems { get; } = [];

        private bool Windows => places.Platform is SetupSystem.Windows;

        private string Bundle => Path.GetDirectoryName(carried.Executable)!;

        private StringComparison PathCase => Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Whether the terminal command step left a command a new terminal finds, as read back: the Path entry on Windows; the
        /// wrapper on macOS, with the profile lines in place or the folder on a terminal's PATH already.
        /// </summary>
        public bool CommandPlaced { get; private set; }

        /// <summary>
        /// One step, any failure it did not expect said as a problem rather than thrown past the report: an argument, a JSON
        /// or a cancelled wait as much as an input/output failure, since the callers read the one document, always.
        /// </summary>
        public async Task Step(string what, Func<Task> step)
        {
            // A cancelled run starts no further step: whoever cancelled it is no longer waiting for one.
            if (cancellation.IsCancellationRequested)
            {
                Problems.Add($"{what} was not done: the run was cancelled.");
                return;
            }

            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception failed)
            {
                Problems.Add($"{what} stopped part-way: {failed.Message}");
            }
        }

        /// <summary>
        /// Whether this data directory's layout stamp refuses this jason, which then stops no runtime and moves no
        /// registration: the gate the layout-gated verbs keep, kept here whenever retiring would touch either.
        /// </summary>
        public bool LayoutRefuses(string old)
        {
            if (LayoutStamp.Refusal(env.Paths, SkillsSource.BuildVersion(env)) is not { } refused)
            {
                return false;
            }

            Problems.Add($"{refused.Code}: {refused.Message} So the command-line install at {old} was not retired: its runtime and its logon registration were left as they are.");
            return true;
        }

        /// <summary>
        /// The command-line install left, where the terminal command was not placed whole. Where it sat at
        /// <c>~/.local/bin/jason</c> the wrapper has already taken that file's place, and the report says so; elsewhere the old
        /// executable is what still answers.
        /// </summary>
        public InstallRetired? NotRetired(string old)
        {
            var target = Path.Combine(places.LocalBin, "jason");
            if (!Windows && string.Equals(old, Path.GetFullPath(target), StringComparison.Ordinal) && IsWrapper(target))
            {
                Problems.Add($"The wrapper that runs {carried.Executable} has taken the place of the command-line install's executable at {old}, but a new terminal may not find {places.LocalBin} on its PATH (said above), so the rest of that install was not retired: its notice files and its logon registration were left as they are, and a registration naming {old} now starts this jason through the wrapper.");
                return null;
            }

            Problems.Add($"{old} was not retired, since a new terminal would not find this jason in its place; it still answers as jason.");
            return null;
        }

        // --- the command-line install --------------------------------------------------------------------------

        /// <summary>The command-line install to retire, by the rule <see cref="CommandLineInstalls.Find"/> keeps for install and uninstall alike.</summary>
        public string? FindCommandLineInstall() => CommandLineInstalls.Find(env, places, options.Retire, Problems);

        /// <summary>What runs on this data directory, where nothing is retired: said, and left as it is.</summary>
        public string RuntimeOutside() =>
            DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance) is null ? RuntimeStates.NotRunning : RuntimeStates.Running;

        /// <summary>
        /// A runtime running from the executable being retired is drained and stopped before its file is touched: its
        /// plugin hosts start from its own image, and a file replaced under it would start them from another version.
        /// </summary>
        /// <returns>The <c>runtime</c> member's word, and whether the old install is held: nothing of it may be retired.</returns>
        public async Task<(string State, bool Holding)> StopOldRuntimeAsync(string old, CancellationToken cancellationToken)
        {
            var processes = env.Processes ?? RuntimeProcessControl.Instance;
            if (DescriptorProcess.Live(env.Paths, processes) is not { } descriptor)
            {
                return (RuntimeStates.NotRunning, false);
            }

            if (descriptor.InstalledImage is not { } image)
            {
                // Written before runtimes named their image, or run through the dotnet muxer: it may run from the old file,
                // whose plugin hosts would then start from whatever replaced it.
                Problems.Add($"The runtime that runs on {env.Paths.Root} does not name the executable it runs from, so {old}, which it may run from, was not retired and the runtime was left running. Stop it with `jason runtime stop` and run this again.");
                return (RuntimeStates.Running, true);
            }

            if (!string.Equals(Path.GetFullPath(image), old, PathCase))
            {
                Left.Add($"The runtime that runs on {env.Paths.Root} runs from {image}, not from {old}, so it was left running.");
                return (RuntimeStates.Running, false);
            }

            // The stop owes a start, and this routine starts none: the note says so from before the drain, so a routine killed
            // or timed out during the stop's wait, or a caller that quits before it starts one, leaves it standing for the
            // next to find. It is taken back only where the runtime goes on running having taken no stop (StopNotEndedAsync); a
            // runtime's own start removes it, and so does a stop through `jason runtime stop`.
            string? unwritten = null;
            try
            {
                StartOwed.Write(env.Paths, (env.Clock ?? TimeProvider.System).GetUtcNow());
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                unwritten = refused.Message;
            }

            using var said = new StringWriter();
            var quiet = env with { Out = said };
            var drained = false;
            var stopSent = false;
            bool? stopTaken = null;
            int exit;
            try
            {
                var (_, drain) = await OperationRunner.SendAsync(quiet, Operations.SystemDrain, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
                drained = drain is { IsSuccess: true };
                if (drained)
                {
                    await WaitForAttemptsAsync(quiet, cancellationToken).ConfigureAwait(false);
                }

                stopSent = true;
                exit = await RuntimeStopCommand.RunAsync(quiet, human: false, cancellationToken, options.StopTimeout, options.StopPoll, answered: taken => stopTaken = taken).ConfigureAwait(false);
            }
            catch (Exception unfinished)
            {
                // Cancelled, or failed in a way no step expected, before the stop gave its answer: the install stays held, and the
                // drain, which may have been taken even where its call failed, is taken off where the runtime goes on.
                var why = unfinished is OperationCanceledException ? "the run was cancelled" : unfinished.Message;
                return await StopNotEndedAsync(old, quiet, said, why, resume: true, stopSent, stopTaken).ConfigureAwait(false);
            }

            if (exit != ExitCodes.Success)
            {
                // The stop gave its answer and the runtime did not end within its wait: read by the same rule.
                return await StopNotEndedAsync(old, quiet, said, why: null, resume: drained, stopSent, stopTaken).ConfigureAwait(false);
            }

            Done.Add($"Stopped the runtime that ran from {old}.");
            Left.Add(unwritten is null
                ? $"No runtime was started: `jason runtime start` starts one from {carried.Executable}. Until one starts, {env.Paths.StartOwedFile} says a start is owed."
                : $"No runtime was started: `jason runtime start` starts one from {carried.Executable}.");
            if (unwritten is not null)
            {
                Problems.Add($"The note that a start is owed could not be written at {env.Paths.StartOwedFile} ({unwritten}), so nothing that reads it will start the runtime this stopped: `jason runtime start` starts one.");
            }

            return (RuntimeStates.Stopped, false);
        }

        /// <summary>
        /// A stop that did not end the runtime: cancelled or failed before it gave its answer, or answered with the runtime
        /// not ended within its wait. The install stays held, and one rule says what becomes of the runtime and of the note
        /// that a start is owed, whichever way the stop ended:
        /// <list type="bullet">
        /// <item>the runtime runs no longer: it went down, so the note stands for the next to find, and nothing is resumed;</item>
        /// <item>it took the shutdown: it is going down, so the note stands for when it has ended, and nothing is resumed;</item>
        /// <item>it refused the shutdown, or none was sent: it goes on running, so no start is owed beside it, the note is
        /// taken back, and its drain is taken off;</item>
        /// <item>a shutdown was sent and its answer never came: whether it was taken is not known, so the note stands, inert
        /// beside a runtime that answers and taken back by its own start or a deliberate <c>jason runtime stop</c>, and the
        /// drain is taken off all the same.</item>
        /// </list>
        /// The drain is taken off only where it may have been taken (<paramref name="resume"/>); where that cannot be done, the
        /// report says the command that does it.
        /// </summary>
        private async Task<(string State, bool Holding)> StopNotEndedAsync(string old, CliEnvironment quiet, StringWriter said, string? why, bool resume, bool stopSent, bool? stopTaken)
        {
            why ??= stopTaken switch
            {
                true => "it had not ended within the stop's wait",
                false => "it refused the stop",
                null => "the stop's answer never came",
            };
            env.Error.Write(said.ToString());
            var left = StartOwed.After(env.Paths, env.Processes ?? RuntimeProcessControl.Instance, stopSent, stopTaken);
            if (left is StopLeft.Ended)
            {
                Problems.Add($"Stopping the runtime that ran from {old} did not finish ({why}), though it runs no longer, so nothing of that install was retired in this run. `jason runtime start` starts one; run this again to retire it.");
                return (RuntimeStates.Stopped, true);
            }

            if (left is StopLeft.GoingDown)
            {
                Problems.Add($"The runtime that runs from {old} took the stop and had not ended when stopping it stopped part-way ({why}), so nothing of that install was retired. {env.Paths.StartOwedFile} says a start is owed once it has ended; run this again to retire it.");
                return (RuntimeStates.WouldNotStop, true);
            }

            var unknown = left is StopLeft.Unknown;
            if (!unknown)
            {
                StartOwed.Remove(env.Paths);
            }

            var resumed = resume && await ResumeAsync(quiet).ConfigureAwait(false);
            var drain = resumed ? "; it was resumed and goes on as it was" : resume ? "; it could not be resumed, and `jason runtime resume` resumes it" : string.Empty;
            Problems.Add(unknown
                ? $"Stopping the runtime that runs from {old} did not finish ({why}) after the stop was sent, and whether the runtime took it is not known, so nothing of that install was retired{drain}. {env.Paths.StartOwedFile} says a start is owed in case it ends; its own start or `jason runtime stop` takes that back. Run this again once it has ended, or stop it with `jason runtime stop` and run this again."
                : $"Stopping the runtime that runs from {old} did not finish ({why}), so nothing of that install was retired and the runtime goes on running{drain}. Stop it with `jason runtime stop` and run this again.");
            return (stopSent ? RuntimeStates.WouldNotStop : RuntimeStates.Running, true);
        }

        /// <summary>
        /// The drain taken off again, on a wait of its own: a run that was cancelled still leaves no runtime draining that it
        /// can resume, and a runtime that does not answer is not waited on past <see cref="ResumeWait"/>.
        /// </summary>
        private static async Task<bool> ResumeAsync(CliEnvironment quiet)
        {
            using var bounded = new CancellationTokenSource(ResumeWait);
            try
            {
                return (await OperationRunner.SendAsync(quiet, Operations.SystemResume, RequestBody.Empty(), bounded.Token).ConfigureAwait(false)).Item2 is { IsSuccess: true };
            }
            catch (Exception failed) when (failed is OperationCanceledException or IOException or InvalidOperationException or JsonException)
            {
                return false;
            }
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
                    ? $"{target} was left as it is, since the command-line install there is not retired in this run."
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

            // Read back: the wrapper is what a new terminal runs, so it is there in the application's shape, or nothing counts.
            var wrapperPlaced = File.Exists(target) && !IsLink(target) && string.Equals(ReadSmall(target), text, StringComparison.Ordinal);
            if (!wrapperPlaced)
            {
                Problems.Add($"{target} does not read back as the wrapper that was written, so a new terminal may not find this jason.");
            }

            var added = new List<string>();
            var linesPlaced = true;
            if (PathEntry.OnPath(TerminalPath(), places.LocalBin))
            {
                Done.Add($"{places.LocalBin} is on the PATH a terminal has already, so no profile was written.");
            }
            else if (PathEntry.SplitsPath(places.LocalBin))
            {
                // install.sh refuses a folder holding PATH's own separator: PATH would read the line as two other folders.
                linesPlaced = false;
                Problems.Add($"{places.LocalBin} holds ':', which separates PATH's entries, so no line can put it on PATH and no profile was written. A new terminal finds this jason only by its full path, {target}.");
            }
            else if (PathEntry.UnsafeFolder(places.LocalBin))
            {
                // install.sh's own refusal: the line reads the folder inside double quotes, where these are code.
                linesPlaced = false;
                Problems.Add($"{places.LocalBin} holds a line break, '\"', '$', a backtick or '\\', which the line install.sh writes would read as something else, so no profile was written. Add this line to your profile by hand: {PathEntry.HandLine(places.LocalBin)}");
            }
            else
            {
                foreach (var profile in PathEntry.ProfileTargets(places.Home, places.Shell))
                {
                    if (IsLink(profile) && !LinksToARegularFile(profile))
                    {
                        // A link into a dotfiles folder is followed, as the script's >> follows it; a link to anything else
                        // would be appended through to a file nobody meant, or made where it points.
                        linesPlaced = false;
                        Problems.Add($"{profile} is a link to something other than a file, so it was left as it is and a new terminal may not find jason. Add these two lines by hand to the file it stands for: {PathEntry.Marker} / {PathEntry.HandLine(places.LocalBin)}");
                        continue;
                    }

                    try
                    {
                        // Whether this run brings the file into being, recorded so the uninstall takes away only a file Jason
                        // made and that holds nothing else by then (CreatedProfiles).
                        var created = !File.Exists(profile) && !IsLink(profile);
                        if (PathEntry.AddToProfile(profile, places.LocalBin))
                        {
                            added.Add(profile);
                            if (created)
                            {
                                RecordCreated(profile);
                            }
                        }
                    }
                    catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                    {
                        linesPlaced = false;
                        Problems.Add($"{profile} could not be written ({refused.Message}), so a new terminal may not find jason. Add these two lines to it by hand: {PathEntry.Marker} / {PathEntry.HandLine(places.LocalBin)}");
                    }
                }
            }

            if (added.Count > 0)
            {
                Done.Add($"Added {places.LocalBin} to the PATH in {string.Join(" and ", added)}.");
            }

            CommandPlaced = wrapperPlaced && linesPlaced;

            // 0.48's application placed a jason-desktop wrapper beside a command-line jason; taken back as the Windows command
            // files are, once this jason answers, and only where it is the application's shape.
            var takenBack = new List<string>();
            var desktop = Path.Combine(places.LocalBin, "jason-desktop");
            if (CommandPlaced && IsWrapper(desktop))
            {
                try
                {
                    places.Delete(desktop);
                    takenBack.Add(desktop);
                    Done.Add($"Took back the command file Jason Desktop placed: {desktop}.");
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"{desktop}, which Jason Desktop placed, could not be removed: {refused.Message}");
                }
            }

            return new InstallCommandOutcome(target, CommandPlaced, target, changed, added, takenBack);
        }

        /// <summary>
        /// Windows: one entry in this account's Path naming the folder this jason is in, where it is not there, the value's
        /// kind kept and running programs told; and the application's own command files in <c>WindowsApps</c> taken back,
        /// each only where it is one of the application's shapes.
        /// </summary>
        public InstallCommandOutcome? WindowsCommand(string? old, bool holding)
        {
            if (!OperatingSystem.IsWindows())
            {
                Problems.Add("The Windows Path is edited on Windows only.");
                return null;
            }

            // A command-line install held in this run still answers as jason, and an entry appended after its own would leave
            // a new terminal finding it, while the report said this jason was placed: the Path is left, as the wrapper is on
            // macOS, and the scripts end non-zero.
            if (old is not null && holding)
            {
                Problems.Add($"The command-line install at {old} is not retired in this run, so this account's Path was left as it is: a new terminal still finds that jason.");
                CommandPlaced = false;
                return new InstallCommandOutcome(Bundle, false, carried.Executable, false, [], []);
            }

            var changed = false;
            try
            {
                changed = places.AccountPathValue.Append(Bundle);
                Done.Add(changed ? $"Added {Bundle} to this account's Path." : $"{Bundle} was on this account's Path already.");
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Problems.Add($"This account's Path could not be written ({refused.Message}), so a new terminal does not find jason: {carried.Executable} runs it by its full path.");
            }

            // Read back: the entry is what a new terminal finds this jason by, so nothing else answering jason goes without it.
            try
            {
                var stored = places.AccountPathValue.Read();
                CommandPlaced = stored is not null && PathEntry.Carries(stored.Raw, Bundle, PathEntry.ReadAs(stored.Kind != Microsoft.Win32.RegistryValueKind.String));
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Problems.Add($"This account's Path could not be read back ({unread.Message}).");
            }

            var takenBack = new List<string>();
            foreach (var name in CommandPlaced ? CommandFiles.Names : [])
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

            return new InstallCommandOutcome(Bundle, CommandPlaced, carried.Executable, changed, [], takenBack);
        }

        /// <summary>
        /// The file a terminal opened from now on runs for <c>jason</c>, read as it reads its PATH (<see cref="NewTerminal"/>);
        /// where it is not this jason or the wrapper running it, a problem names it and what removes it, and it is left as
        /// it is.
        /// </summary>
        public (string? Finds, bool FindsThis) Finds()
        {
            var finds = Windows
                ? NewTerminal.FindsOnWindows(places)
                : NewTerminal.FindsOnMac(TerminalPath(), places.LocalBin, inFront: !PathEntry.OnPath(TerminalPath(), places.LocalBin));
            if (NewTerminal.Runs(finds, carried.Executable))
            {
                return (finds, true);
            }

            if (finds is null)
            {
                Problems.Add($"A terminal opened from now on finds no jason by name, as its PATH reads now: {carried.Executable} runs this jason by its full path.");
                return (null, false);
            }

            var image = new FileInfo(finds).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? finds;
            var owner = env.Managers is { } records ? PackageOwner.Of(image, string.Equals(image, finds, PathCase) ? null : finds, records) : null;
            Problems.Add(NewTerminal.Problem(finds, carried.Executable, CommandLineInstalls.IsJasons(env, places, image), owner));
            return (finds, false);
        }

        // --- retiring the command-line install ---------------------------------------------------------------

        public InstallRetired Retire(string old)
        {
            var reregistered = Reregister(old);
            var replacedByWrapper = !Windows && string.Equals(old, Path.GetFullPath(Path.Combine(places.LocalBin, "jason")), StringComparison.Ordinal);
            var (aside, removed) = new CommandLineRemoval(places, Left, Problems).Remove(old, replacedByWrapper);
            Done.Add(removed.Count > 0 || replacedByWrapper
                ? $"Retired the command-line install at {old}{(removed.Count > 0 ? ": removed " + string.Join(", ", removed) : string.Empty)}."
                : $"Retired the command-line install at {old}.");
            return new InstallRetired(old, aside, removed, reregistered);
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

                var named = RuntimeAutostartCommands.DataDirectoryIn(state.Run);
                if (named is not null && !string.Equals(Path.GetFullPath(named), env.Paths.Root, PathCase))
                {
                    Left.Add($"The logon registration names {old} with the data directory {named}, not this one, so it was left as it is.");
                    return false;
                }

                // One that named no data directory ran on the default one, and keeps naming none.
                var registration = RuntimeAutostartCommands.Registration(env with { InstallPath = carried.Executable }, registrar.Platform, namesDataDirectory: named is not null);
                if (registrar.Platform is AutostartPlatform.MacOs)
                {
                    // The agent is RunAtLoad: loading it now would start a runtime, which this never does. The file is moved,
                    // and launchd loads it at the next logon; nothing is unloaded either, since the runtime is already stopped.
                    registrar.Apply(registration with { Apply = [], Remove = [] });
                    Done.Add($"Registered the runtime's logon start again, naming {carried.Executable}; it takes effect at the next logon, and nothing was started.");
                    return true;
                }

                registrar.Apply(registration);
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
                try
                {
                    _ = SkillsRecord.Read(root.Directory);
                }
                catch (SkillsRecordUnreadable unreadable)
                {
                    Problems.Add($"{root.Directory}: {unreadable.Message} Nothing was deployed there.");
                    continue;
                }

                // The whole plan: what is the person's in this root is judged by the deployer's one rule, which every door
                // deploys through, and comes back named in its report.
                var plan = SkillsPlanner.Compose(staged, null, [root], SkillsPlanner.DocumentedCap, false, null);
                outcomes.Add(Deploy(plan, root.Directory, hosts));
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
            return Deploy(plan, env.Paths.RoleSkillsDirectory, []);
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

        private InstallSkillsOutcome Deploy(SkillsPlan plan, string root, IReadOnlyList<string> hosts)
        {
            foreach (var refusal in plan.Refusals)
            {
                Problems.Add($"{root}: {refusal}");
            }

            if (plan.Refusals.Count > 0)
            {
                return new InstallSkillsOutcome(root, hosts, 0, 0, []);
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

            // Each said in the deployer's own words, as every door says them: adopted as done, left alone as left.
            Done.AddRange(report.Adopted.Select(SkillsDeployer.AdoptedSentence));
            Left.AddRange(SkillsDeployer.LeftAloneSentences(report.LeftAlone));
            var leftAlone = report.LeftAlone.Select(skill => skill.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

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
            if (string.IsNullOrWhiteSpace(named))
            {
                return null;
            }

            // Set is not named: a runtime starts every child it runs with the variable set, the move's routine among them,
            // so the default data directory is never written into the wrapper, which would hold a terminal's own choice to it.
            var theDefault = Path.Combine(places.Home, JasonPaths.DefaultFolder);
            return FolderPaths.Same(FolderPaths.Normal(env.Paths.Root), FolderPaths.Normal(theDefault))
                ? null
                : env.Paths.Root;
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

        /// <summary>
        /// Records a profile this run created. One that cannot be recorded is said, and stays at an uninstall like a file the
        /// person had: the safe direction.
        /// </summary>
        private void RecordCreated(string profile)
        {
            try
            {
                CreatedProfiles.Add(places.Home, profile);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                Left.Add($"{profile} was created here, and that could not be recorded at {CreatedProfiles.RecordFile(places.Home)} ({refused.Message}), so an uninstall takes Jason's lines out of it and leaves the file.");
            }
        }

        /// <summary>A link whose final target is a regular file: a profile kept in a dotfiles folder.</summary>
        private static bool LinksToARegularFile(string path)
        {
            try
            {
                return File.ResolveLinkTarget(path, returnFinalTarget: true) is FileInfo { Exists: true } final && final.LinkTarget is null;
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
