using System.CommandLine;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Update;
using Jason.Contracts.Update;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>update</c> verb group. One verb in this version, <c>check</c>: ask the release feed for the newest
/// version and say whether it is newer than this one. It asks the feed itself rather than the runtime, so the
/// question can be asked on a machine whose runtime will not start — and it asks through the same type the
/// runtime's own unattended check reads through, so the two cannot disagree about what a manifest is or about
/// what is sent to get one. Nothing here downloads or installs anything.
/// </summary>
public static class UpdateCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var update = new Command("update", "Find out whether a newer version of Jason has been released, and install it.");
        update.Subcommands.Add(Check(env, actor));
        update.Subcommands.Add(Apply(env, actor));
        update.Subcommands.Add(RollBack(env, actor));
        update.Subcommands.Add(Status(env, actor));
        return update;
    }

    private static Command Check(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("check", "Ask the release feed for the newest version and say whether it is newer than this one. Nothing is downloaded or installed.");
        var feed = new Option<string?>("--feed")
        {
            Description = "Read a different feed: the https address of a manifest.json, or http on this machine. Default: the public release feed.",
        };
        var human = VerbOptions.Human();
        command.Options.Add(feed);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            // About this build rather than about business state, so no claim travels with it — but a malformed
            // claim is still refused, so --actor behaves the same on every verb.
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return CheckAsync(env, parseResult.GetValue(feed), parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }

    private static Command Apply(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "apply",
            "Install the newest release: download it, stop this machine's runtime, put the new executable and its licence files in place, start it again, redeploy the skills this machine recorded at the new release's tag, and place the release's UI. If the skills or the UI cannot be placed the update still stands; the step's line says why, and 'jason status' names the repair. "
            + "On macOS and Windows a command-line install updated to 0.49 or later is then moved into Jason Desktop: an application of that version or later already there is reused, none there is downloaded, checked against checksums.txt and the publisher's signature and installed, one older is left as it is; the application's own `jason install` retires the command-line install, and a runtime it stopped is started again from the application's jason. The move never fails the update: its lines follow the update's, each beginning 'move:', and the document's move member says where it stands (done, pending, failed, or not_applicable on Linux) and why; <data>/update/move.json keeps it, 'jason status' names a move that has not ended, and this command run again at the same version carries it on. "
            + "Run as the jason Jason Desktop carries, the application is updated by the Desktop's own updater with no window, and this command still ends with one JSON document and its exit code: it starts the application with --update-apply --await-go, and once the update is downloaded drains the runtime, stops it, steps out of the application's folder (on Windows it moves its own file out and keeps running, on macOS a copy of it under the data directory goes on) and gives the Desktop its go last, then waits for its answer and for its version.json to name the new version; the new jason then replaces the plugin packages, starts the runtime and checks it, redeploys the recorded skills, runs `jason install`, places the UI and has the application record the update as kept. "
            + "A refusal of the Desktop's ends with update_refused_by_desktop and its reason, a jason at the application's path and the runtime as it was found; a cancel (Ctrl+C, a closed console) before the installer has the update ends with update_cancelled, the record put back and the runtime launched without a wait. One update of a data directory acts at a time, its lock kept from one part to the next: another beside it, or `jason install` beside it, is refused with update_in_progress, and while `jason install` holds it the update is refused with install_in_progress. Run elevated (an administrator's elevated terminal under User Account Control, another account than the session's, root) it is refused with update_elevated before anything is begun: run it from an ordinary terminal. On Linux, or where the application is not where its jason says, that jason is refused with update_carried_by_desktop: Check for Updates in Jason Desktop updates it.");
        var feed = new Option<string?>("--feed")
        {
            Description = "Read a different feed: the https address of a manifest.json, or http on this machine. Default: the public release feed.",
        };
        var version = new Option<string?>("--version")
        {
            Description = "Install this version rather than the newest one. The release's own manifest is read.",
        };
        var drain = new Option<int?>("--drain-seconds")
        {
            Description = "How long to wait for work in flight to finish before stopping the runtime. 0 waits for none of it. Default: 120.",
        };
        var finish = new Option<bool>("--finish")
        {
            Description = "The finishing step of a jason Jason Desktop carries. With an update past 'swapped' in flight, it finishes that update, as the update's own hand-over runs it. With none in flight, it brings what belongs to the installed version up to it - the plugin packages its release publishes, by the update's own plan (an edited package is left as it is), every skills deployment this installation records, and the UI - starts and stops no runtime, asks a running one to reload where a package changed, and answers one document: version, data_dir, data_dir_found, installation_found, complete, and parts (plugins, skills, ui), each with state done, already_there or not_done, a reason where not_done, and its lines. Run again it changes nothing. With no data directory, or one holding nothing but Jason Desktop's own, it reads no release, makes nothing and says so, installation_found false. A data directory it cannot list is refused with data_dir_unreadable, which is retryable, before anything is made or read. Refused with update_in_progress while an update stands before 'swapped'; a usage error for a jason the application does not carry.",
        };
        var human = VerbOptions.Human();
        command.Options.Add(feed);
        command.Options.Add(version);
        command.Options.Add(drain);
        command.Options.Add(finish);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return ApplyAsync(
                env,
                parseResult.GetValue(feed),
                parseResult.GetValue(version),
                parseResult.GetValue(drain),
                parseResult.GetValue(human),
                parseResult.GetValue(finish),
                cancellationToken);
        });

        return command;
    }

    public static Task<int> ApplyAsync(
        CliEnvironment env,
        string? feed,
        string? version,
        int? drainSeconds,
        bool human,
        CancellationToken cancellationToken) =>
        ApplyAsync(env, feed, version, drainSeconds, human, finish: false, cancellationToken);

    public static async Task<int> ApplyAsync(
        CliEnvironment env,
        string? feed,
        string? version,
        int? drainSeconds,
        bool human,
        bool finish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        // A jason Jason Desktop carries hands the update to the Desktop, and the copy of it under the data directory carries
        // on what it began; neither replaces a file of the application's itself.
        if (CarriedExecutable.Of(env) is { } carried)
        {
            return await CarriedApplyAsync(env, carried, continuing: false, feed, version, drainSeconds, human, finish, cancellationToken).ConfigureAwait(false);
        }

        if (ContinuingFor(env) is { } recorded)
        {
            return await CarriedApplyAsync(env, recorded, continuing: true, feed, version, drainSeconds, human, finish, cancellationToken).ConfigureAwait(false);
        }

        if (finish)
        {
            throw new UsageException("--finish finishes an update a jason Jason Desktop carries handed over, and this jason is not one the application carries.");
        }

        // Before anything at all: before a byte is fetched, the runtime is asked to drain or a step is recorded.
        if (RefusedAsRecordedCarried(env, "apply", completeToo: false)
            || RefusedAsManaged(env, "apply"))
        {
            return ExitCodes.ApiError;
        }

        var request = Request(feed, version, drainSeconds);

        // Inside the try: an installation that cannot be updated is a refusal with a code, like every other.
        UpdateApplier applier;
        try
        {
            applier = new UpdateApplier(env, new UpdatePaths(env.Paths), TimeProvider.System, env.InstallPath ?? UpdateApplier.ResolveInstallPath())
            {
                HandsOver = true,
            };
            var ledger = await applier.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
            if (applier.HandedOver)
            {
                return await HandOverAsync(
                    env,
                    applier.Steps,
                    [ledger.InstallPath, "update", "apply", "--feed", request.Feed.AbsoluteUri],
                    human,
                    (why, cause) => NotFinished(env, ledger, why, cause),
                    recover: null,
                    updateLock: null,
                    cancellationToken).ConfigureAwait(false);
            }

            // The update is complete; the move of this command-line install into Jason Desktop comes after it, never fails it,
            // and is said beside it.
            var move = await MoveAsync(env, ledger, request.Feed, cancellationToken).ConfigureAwait(false);
            Print(env, human, [.. applier.Steps, .. move?.Lines ?? []], ledger, move?.Response);
            return ExitCodes.Success;
        }
        catch (UpdateException error) when (error.Code == UpdateCodes.UpToDate && MoveToCarryOn(env) is { } complete)
        {
            // Nothing newer to apply, and a move of this install left to carry on: `jason update apply` carries it on at the
            // version it is.
            var move = await MoveAsync(env, complete, request.Feed, cancellationToken).ConfigureAwait(false);
            Print(env, human, move?.Lines ?? [], complete with { FromVersion = complete.ToVersion }, move?.Response);
            return ExitCodes.Success;
        }
        catch (UpdateException error)
        {
            // Whatever was done up to here is in the ledger, and the message says what to type next.
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, error.Retryable));
            return ExitCodes.ApiError;
        }
        catch (UpdateFeedException error)
        {
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: error.Code == UpdateFeedException.Unreachable));
            return ExitCodes.ApiError;
        }
        catch (UpdateLedgerException error)
        {
            // What is on disk is not a ledger. `status` already answers this way; a verb that acts on the same
            // file should not answer differently, least of all with a bare line and no code.
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: false));
            return ExitCodes.ApiError;
        }
    }

    /// <summary>
    /// The move of the command-line install a completed update installed this version at, into Jason Desktop: where this
    /// version is the one the update installed and the install is a command-line one. Null where there is no such install;
    /// on a system the move does not apply to, the answer says so and nothing is changed.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Lines, UpdateMoveResponse Response)?> MoveAsync(CliEnvironment env, UpdateLedger ledger, Uri feed, CancellationToken cancellationToken)
    {
        if (ledger.Step != UpdateStep.Complete
            || ledger.ToVersion != SemanticVersion.Current
            || string.IsNullOrWhiteSpace(ledger.InstallPath)
            || CarriedExecutable.Of(ledger.InstallPath) is not null)
        {
            return null;
        }

        var move = new CommandLineMove(env, new UpdatePaths(env.Paths), env.Clock ?? TimeProvider.System);
        try
        {
            var response = await move.RunAsync(ledger, feed, cancellationToken).ConfigureAwait(false);
            return (move.Lines, response);
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            // Nothing of the move can turn a complete update into a refusal: what escaped it is said as the move's failure.
            env.Error.WriteLine(unexpected.Message);
            var reason = $"The move into Jason Desktop stopped: {unexpected.Message} `jason update apply` carries it on.";
            return ([.. move.Lines, $"move: {MoveStates.Failed} — {reason}"], new UpdateMoveResponse(MoveStates.Failed, null, null, reason));
        }
    }

    /// <summary>
    /// The completed update whose move an apply that finds nothing newer carries on: this version's, of a command-line
    /// install, on a system the move applies to, with a move that is not done. Null otherwise.
    /// </summary>
    private static UpdateLedger? MoveToCarryOn(CliEnvironment env)
    {
        if (env.Setup is null)
        {
            return null;
        }

        var paths = new UpdatePaths(env.Paths);
        UpdateLedger? complete;
        try
        {
            complete = UpdateLedger.ReadFile(paths.Ledger);
        }
        catch (Exception unread) when (unread is UpdateLedgerException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (complete is not { Step: UpdateStep.Complete }
            || complete.ToVersion != SemanticVersion.Current
            || string.IsNullOrWhiteSpace(complete.InstallPath)
            || CarriedExecutable.Of(complete.InstallPath) is not null)
        {
            return null;
        }

        try
        {
            return MoveRecord.Read(paths.Move) is { State: MoveStates.Done } ? null : complete;
        }
        catch (MoveRecordUnreadable)
        {
            return complete;
        }
    }

    /// <summary>
    /// The rest of an update, from <c>swapped</c> on, run by the executable now at the install path: <c>jason update
    /// apply</c> typed into it, which reads the ledger and carries on (<see cref="UpdateApplier.HandsOver"/> says why).
    /// </summary>
    /// <remarks>
    /// <para>
    /// When it completes, its document is this verb's with the lines of the steps taken here put in front of its own, so
    /// the answer reads as one apply did all of it. It is asked for that document whatever this verb was asked for, and
    /// the lines are rendered here, for a person or as the document. When it ends as this CLI ends — an exit code this
    /// CLI gives and one JSON document — anything else it answers, a refusal above all, is passed on as it came, with its
    /// exit code. What it says on standard error is passed on as it says it.
    /// </para>
    /// <para>
    /// An executable that cannot be run at all, and one that ends any other way — a crash, a kill, a document cut short —
    /// is refused as <c>update_not_healthy</c>, with the step the ledger stands at: the new build is in place, typing
    /// <c>jason update apply</c> runs it and finishes, and <c>jason update rollback</c> puts the old one back.
    /// </para>
    /// <para>
    /// A carried update's child — the copy on macOS, the new carried jason's finish — goes on under the lock this process holds
    /// (<paramref name="updateLock"/>), and is given <see cref="ChildHold"/> after a cancel to run its own way out, since a
    /// terminal's Ctrl+C or hang-up reaches it too, or, where it has no console, to end the step it is in; only then is it ended
    /// and this process's own way out run. A child that answered within the hold is passed on as it answered.
    /// </para>
    /// </remarks>
    private static async Task<int> HandOverAsync(
        CliEnvironment env,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> command,
        bool human,
        Func<string, Exception?, UpdateException> unfinished,
        Func<bool, Task<string>>? recover,
        string? updateLock,
        CancellationToken cancellationToken)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        FinishedCommand finished;
        using var hold = new CancellationTokenSource();

        // A console's close leaves this process only the close's hold: the child is given the shorter hold then, so that this
        // process's own way out runs within it, and the lock is let go by a process that put back what it began.
        var closing = Closing.Current;
        using var held = recover is null ? default : cancellationToken.Register(() => hold.CancelAfter(closing is { ByClose: true } ? CloseChildHold : ChildHold));
        try
        {
            finished = await processes.RunAsync(env.Paths, command, env.Error, updateLock, recover is null ? cancellationToken : hold.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancelled) when (recover is not null && cancellationToken.IsCancellationRequested)
        {
            // The child was ended past its hold: what it began is put back as far as it can be, and the runtime launched
            // without a wait, so the answer comes before the process is ended too.
            var said = await recover(false).ConfigureAwait(false);
            throw new UpdateException(UpdateCodes.Cancelled, $"The update was cancelled while {command[0]} carried it. {said}", cancelled, retryable: true);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            if (recover is not null)
            {
                await recover(true).ConfigureAwait(false);
            }

            throw unfinished($"it could not be run to finish the update: {error.Message}", error);
        }

        if (finished.ExitCode is not (ExitCodes.Success or ExitCodes.ApiError or ExitCodes.Usage or ExitCodes.RuntimeUnavailable)
            || Document(finished.StandardOutput) is not { } document)
        {
            // Whoever asked for a runtime's stop starts one again: a hand-over that ended with no answer may have left none.
            if (recover is not null)
            {
                await recover(!cancellationToken.IsCancellationRequested).ConfigureAwait(false);
            }

            throw unfinished(
                string.Create(CultureInfo.InvariantCulture, $"the update was handed to it and it ended without an answer this command gives, with exit code {finished.ExitCode}."),
                null);
        }

        if (finished.ExitCode == ExitCodes.Success && WithStepsBefore(steps, document) is { } answer)
        {
            env.Out.WriteLine(human
                ? string.Join(Environment.NewLine, answer["steps"]!.AsArray().Select(step => "- " + step!.GetValue<string>()))
                : answer.ToJsonString(JasonJson.Options));
            return ExitCodes.Success;
        }

        env.Out.Write(finished.StandardOutput);
        return finished.ExitCode;
    }

    /// <summary>
    /// How long a carried update's child is given, after a cancel, to end by itself before it is ended: within the ten seconds
    /// a cancelled verb is held (<c>CliApp</c>), with room left for this process's own way out.
    /// </summary>
    internal static TimeSpan ChildHold { get; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// The child's hold after a console's close, which leaves this process <see cref="Closing.Hold"/> before Windows ends it:
    /// short enough that this process's own way out, which launches a runtime without waiting for it, runs within it.
    /// </summary>
    internal static TimeSpan CloseChildHold { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The refusal for a hand-over that did not finish the update: the new build is in place, and the step the ledger stands
    /// at — whatever the executable handed to reached — is named, with both ways on.
    /// </summary>
    private static UpdateException NotFinished(CliEnvironment env, UpdateLedger ledger, string why, Exception? cause)
    {
        UpdateStep step;
        try
        {
            step = UpdateLedger.ReadFile(new UpdatePaths(env.Paths).Ledger)?.Step ?? ledger.Step;
        }
        catch (UpdateLedgerException)
        {
            step = ledger.Step;
        }

        return new UpdateException(
            UpdateCodes.NotHealthy,
            $"{ledger.ToVersion} is in place at {ledger.InstallPath}, and {why} The update stands at '{SnakeCase(step)}'. "
            + $"Run `jason update apply` to finish it, or `jason update rollback` to put {ledger.FromVersion} back.",
            cause);
    }

    /// <summary>What an apply was asked for: the feed, the version, and the bound of the drain.</summary>
    private static UpdateRequest Request(string? feed, string? version, int? drainSeconds) =>
        new(
            Address(feed),
            Wanted(version),
            TimeSpan.FromSeconds(drainSeconds is { } seconds and >= 0
                ? seconds
                : drainSeconds is null
                    ? DefaultDrainSeconds
                    : throw new UsageException("--drain-seconds cannot be negative.")));

    /// <summary>
    /// The carried jason whose update this process continues: where it runs as the copy under
    /// <c>&lt;data&gt;/update/applier/</c> and the update in flight names a jason Jason Desktop carries. Null otherwise.
    /// </summary>
    private static CarriedExecutable? ContinuingFor(CliEnvironment env)
    {
        var paths = new UpdatePaths(env.Paths);
        if ((env.InstallPath ?? SelfExecutable.InstalledImage) is not { } self
            || !SamePath(self, Path.Combine(paths.Applier, ReleaseAssets.ExecutableName)))
        {
            return null;
        }

        try
        {
            return UpdateLedger.ReadFile(paths.Ledger) is { Step: not UpdateStep.Complete } ledger && !string.IsNullOrWhiteSpace(ledger.InstallPath)
                ? CarriedExecutable.Of(ledger.InstallPath)
                : null;
        }
        catch (Exception error) when (error is UpdateLedgerException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool SamePath(string path, string other) =>
        string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(other),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// The update of a jason Jason Desktop carries (<see cref="CarriedUpdate"/>), from wherever it stands: planned, carried
    /// through the Desktop's install to <c>swapped</c> — on Windows by this process, its image moved out of the application's
    /// folder, on macOS by a copy of this jason it waits for — and handed to the new carried jason; or finished by the new
    /// carried jason, which is this process.
    /// </summary>
    /// <remarks>
    /// The Windows installer refuses while any process runs from the carried file or holds a file in the folder, and on
    /// macOS the file is replaced under a running process. A refusal of the Desktop's, or no answer, leaves a jason at the
    /// carried path and the runtime as it was found, and puts back the record that stood; a hand-over that ends with no
    /// answer starts the runtime again where one was running.
    /// </remarks>
    private static async Task<int> CarriedApplyAsync(
        CliEnvironment env,
        CarriedExecutable carried,
        bool continuing,
        string? feed,
        string? version,
        int? drainSeconds,
        bool human,
        bool finish,
        CancellationToken cancellationToken)
    {
        var paths = new UpdatePaths(env.Paths);

        // The finishing step with nothing in flight makes nothing under a data directory that was never made: a machine
        // that only ever opened the Demo keeps none.
        if (finish && !continuing && !Directory.Exists(env.Paths.Root))
        {
            PrintFinish(env, human, UpdateApplier.NoDataDirectory(SemanticVersion.Current, env.Paths.Root));
            return ExitCodes.Success;
        }

        // Nor where the data directory holds nothing of an installation but Jason Desktop's own, as on its first start: it
        // reads no release and makes nothing, so a fresh machine stays new. One that cannot be listed is refused before the
        // lock rather than taken for an installation: failing closed, it makes nothing and reads no release either.
        if (finish && !continuing)
        {
            switch (InstallationAt(env.Paths.Root))
            {
                case DataDirHolds.DesktopsOwnOnly:
                    PrintFinish(env, human, UpdateApplier.NothingInstalled(SemanticVersion.Current, env.Paths.Root));
                    return ExitCodes.Success;
                case DataDirHolds.Unreadable:
                    env.Out.WriteLine(CliErrors.Serialize(
                        UpdateCodes.DataDirUnreadable,
                        $"The data directory at {env.Paths.Root} could not be listed, so whether an installation is there cannot be told: nothing was made and no release was read. Run jason update apply --finish again once it can be read.",
                        retryable: true));
                    return ExitCodes.ApiError;
            }
        }

        // A console's close ends this process soon after it is told, and nothing else tells a .NET program of it: it cancels
        // the update as Ctrl+C does, and holds the close until the way out has run (Closing).
        using var closing = new Closing(cancellationToken);
        cancellationToken = closing.Token;
        try
        {
            return await CarriedApplyClosableAsync(env, carried, continuing, feed, version, drainSeconds, human, finish, paths, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            closing.Settled();
        }
    }

    /// <summary>
    /// What a console's close does to a carried update: on Windows the close of its window, on macOS and Linux the hang-up of
    /// its terminal. .NET reports it as SIGHUP, which no part of the command line handles, and Windows ends the process
    /// shortly after the handler returns; so the handler cancels the update and waits, a few seconds at most, for its way out
    /// to have run.
    /// </summary>
    internal sealed class Closing : IDisposable
    {
        private readonly CancellationTokenSource _cancel;
        private readonly ManualResetEventSlim _settled = new(false);
        private readonly System.Runtime.InteropServices.PosixSignalRegistration? _registration;
        private static readonly AsyncLocal<Closing?> CurrentOne = new();
        private volatile bool _byClose;

        public Closing(CancellationToken outer)
        {
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(outer);
            CurrentOne.Value = this;
            try
            {
                _registration = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGHUP, Closed);
            }
            catch (Exception unsupported) when (unsupported is PlatformNotSupportedException or IOException)
            {
                _registration = null;
            }
        }

        /// <summary>The update's token: the caller's, and the console's close.</summary>
        public CancellationToken Token => _cancel.Token;

        /// <summary>The close of the update this flow runs, where one is: a hand-over reads by it how long its child is held.</summary>
        public static Closing? Current => CurrentOne.Value;

        /// <summary>Whether the update was cancelled by a console's close, rather than by Ctrl+C or a caller.</summary>
        public bool ByClose => _byClose;

        /// <summary>How long the close is held for the way out: under the five seconds Windows gives a close handler.</summary>
        public static TimeSpan Hold { get; } = TimeSpan.FromSeconds(4);

        /// <summary>The handler: the update cancelled, and the close held until it settled or the hold ran out.</summary>
        internal void Closed(System.Runtime.InteropServices.PosixSignalContext? context)
        {
            if (context is not null)
            {
                context.Cancel = true;
            }

            _byClose = true;
            _cancel.Cancel();
            _settled.Wait(Hold);
        }

        /// <summary>The way out has run: a close that came may end the process now.</summary>
        public void Settled() => _settled.Set();

        public void Dispose()
        {
            if (CurrentOne.Value == this)
            {
                CurrentOne.Value = null;
            }

            _registration?.Dispose();
            _settled.Set();
            _cancel.Dispose();
            _settled.Dispose();
        }
    }

    private static async Task<int> CarriedApplyClosableAsync(
        CliEnvironment env,
        CarriedExecutable carried,
        bool continuing,
        string? feed,
        string? version,
        int? drainSeconds,
        bool human,
        bool finish,
        UpdatePaths paths,
        CancellationToken cancellationToken)
    {
        // The lock the process that started this one holds and hands it, read once and taken out of this process's
        // environment, so nothing this process starts carries it on.
        var handed = UpdateLock.Handed(env.Variables);

        // An elevated terminal would start Jason Desktop, its installer and the runtime elevated, or under another account's
        // profile: refused before anything is read, staged or asked.
        if (env.Setup is { Elevated: true } elevated)
        {
            env.Out.WriteLine(CliErrors.Serialize(UpdateCodes.Elevated, Elevated(elevated.Platform), retryable: false));
            return ExitCodes.ApiError;
        }

        var handing = new CarriedUpdate(env, paths, TimeProvider.System, carried, env.Setup?.Platform)
        {
            // On Windows the carried process itself carries the update, its own image moved out of the application's folder.
            MovesItself = !continuing && env.Setup?.Platform is SetupSystem.Windows && carried.Is(env.InstallPath ?? SelfExecutable.InstalledImage),
        };

        // The record first, and only to know whether this jason was handed an update past the swap, which it finishes with
        // or without a Desktop to ask; a record that cannot be read hands nothing over.
        UpdateLedger? inFlight = null;
        Exception? unread = null;
        try
        {
            inFlight = UpdateLedger.ReadFile(paths.Ledger) is { Step: not UpdateStep.Complete } open ? open : null;
        }
        catch (Exception error) when (error is UpdateLedgerException or IOException or UnauthorizedAccessException)
        {
            unread = error;
        }

        // With nothing in flight, --finish brings what belongs to the installed version up to it, with or without a Desktop,
        // under the update's lock, and sweeps the images earlier updates moved out where nothing runs from them any more. A
        // fresh machine was turned away above, so here an installation is there, and the lock is always taken, its folder made
        // for it where it is not there yet: the finish writes into the installation, and nothing else may beside it.
        if (finish && !continuing && unread is null && inFlight is null)
        {
            UpdateLock held;
            try
            {
                held = UpdateLock.Take(paths, "the finishing step", handed);
            }
            catch (UpdateException refused)
            {
                env.Out.WriteLine(CliErrors.Serialize(refused.Code, refused.Message, refused.Retryable));
                return ExitCodes.ApiError;
            }

            using (held)
            {
                CarriedUpdate.Sweep(CarriedUpdate.Store(env.Setup));
                if (env.Setup is { } machine)
                {
                    RetiredSweep.Sweep(machine);
                }
                var finishing = new UpdateApplier(env, paths, TimeProvider.System, carried.Executable);
                PrintFinish(env, human, await finishing.FinishInstalledAsync(SemanticVersion.Current, Request(feed, null, null), cancellationToken).ConfigureAwait(false));
                return ExitCodes.Success;
            }
        }

        if (!handing.HasDesktop && inFlight is not { Step: >= UpdateStep.Swapped } && !(continuing && inFlight is not null))
        {
            env.Out.WriteLine(CliErrors.Serialize(UpdateCodes.CarriedByDesktop, NoDesktop(env, carried, "apply"), retryable: false));
            return ExitCodes.ApiError;
        }

        if (unread is not null)
        {
            env.Out.WriteLine(unread is UpdateLedgerException invalid
                ? CliErrors.Serialize(invalid.Code, invalid.Message, retryable: false)
                : CliErrors.Serialize(UpdateCodes.FileRefused, $"The update ledger at {paths.Ledger} could not be read: {unread.Message} Run `jason update apply` again once it can.", retryable: true));
            return ExitCodes.ApiError;
        }

        var request = Request(feed, version, drainSeconds);
        var wasLive = DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance) is not null;
        try
        {
            if (inFlight is not null && !carried.Is(inFlight.InstallPath))
            {
                throw new UpdateException(
                    UpdateCodes.InProgress,
                    $"An update of the jason at {inFlight.InstallPath} to {inFlight.ToVersion} is in flight at '{SnakeCase(inFlight.Step)}'. "
                    + $"Finish it with that jason, `{inFlight.InstallPath} update apply`, or see where it stands with `jason update status`.");
            }

            if (inFlight is not null && request.Version is { } wanted && wanted != inFlight.ToVersion)
            {
                throw new UpdateException(
                    UpdateCodes.InProgress,
                    $"An update to {inFlight.ToVersion} is in flight at '{SnakeCase(inFlight.Step)}'. Finish it with `jason update apply`, "
                    + $"or see where it stands with `jason update status`; {wanted} cannot be started until it is done.");
            }

            // Past the swap, or with the application already the new version, what is left is the new jason's: this process
            // where it is that jason, and the carried file run with --finish where this is the copy.
            if (inFlight is not null && (inFlight.Step >= UpdateStep.Swapped || handing.Names(inFlight.ToVersion)))
            {
                if (continuing)
                {
                    using var continued = UpdateLock.Take(paths, "the update", handed);
                    var nonce = continued.HandOver();
                    var swapped = inFlight.Step >= UpdateStep.Swapped ? inFlight : handing.Swapped(inFlight);
                    return await HandToFinishAsync(env, handing, swapped, request, human, wasLive, nonce, cancellationToken).ConfigureAwait(false);
                }

                using (UpdateLock.Take(paths, "the finishing step", handed))
                {
                    return await FinishAsync(env, handing, inFlight, request, human, wasLive, cancellationToken).ConfigureAwait(false);
                }
            }

            if (finish)
            {
                throw new UpdateException(
                    UpdateCodes.InProgress,
                    $"An update to {inFlight!.ToVersion} is in flight at '{SnakeCase(inFlight.Step)}', and the application is not {inFlight.ToVersion} yet, so "
                    + "--finish has nothing of it to finish: `jason update apply` carries it on, and `jason update status` says where it stands.");
            }

            if (continuing)
            {
                using var continued = UpdateLock.Take(paths, "the update", handed);
                var nonce = continued.HandOver();
                var installed = await handing.InstallAsync(inFlight!, request, cancellationToken).ConfigureAwait(false);
                return await HandToFinishAsync(env, handing, installed, request, human, wasLive, nonce, cancellationToken).ConfigureAwait(false);
            }

            // Windows: this process carries the update itself, the Desktop asked first and its image moved out of the folder
            // at the download, and hands the new jason the rest as a child it waits for, under the lock it keeps.
            if (handing.MovesItself)
            {
                using var held = UpdateLock.Take(paths, "the update", handed);
                var nonce = held.HandOver();
                var plan = inFlight ?? await handing.PlanAsync(request, cancellationToken).ConfigureAwait(false);
                var swapped = await handing.InstallAsync(plan, request, cancellationToken).ConfigureAwait(false);
                return await HandToFinishAsync(env, handing, swapped, request, human, wasLive, nonce, cancellationToken).ConfigureAwait(false);
            }

            // macOS: this jason plans and puts its copy aside under the lock, and waits for the copy, which goes on under the
            // same lock: it asks the Desktop, drains and waits.
            using var planning = UpdateLock.Take(paths, "the update", handed);
            var handedOn = planning.HandOver();
            var planned = inFlight ?? await handing.PlanAsync(request, cancellationToken).ConfigureAwait(false);
            handing.CopyAside(env.InstallPath ?? Environment.ProcessPath);

            return await HandOverAsync(
                env,
                handing.Steps,
                [handing.Copy, "update", "apply", "--feed", request.Feed.AbsoluteUri, "--drain-seconds", ((int)request.Drain.TotalSeconds).ToString(CultureInfo.InvariantCulture)],
                human,
                (why, cause) => CopyNotFinished(env, handing, planned, why, cause),
                async waits =>
                {
                    await handing.RecoverAsync(wasLive, waits).ConfigureAwait(false);
                    return handing.RuntimeSaid();
                },
                handedOn,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateException error)
        {
            foreach (var step in handing.Steps)
            {
                env.Error.WriteLine(step);
            }

            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, error.Retryable));
            return ExitCodes.ApiError;
        }
        catch (UpdateFeedException error)
        {
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: error.Code == UpdateFeedException.Unreachable));
            return ExitCodes.ApiError;
        }
        catch (UpdateLedgerException error)
        {
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: false));
            return ExitCodes.ApiError;
        }
    }

    /// <summary>
    /// The copy's part ends at <c>swapped</c>: the new carried jason, the application's own file now, takes the rest with
    /// <c>--finish</c>, and its document is this one with the copy's lines in front.
    /// </summary>
    private static Task<int> HandToFinishAsync(CliEnvironment env, CarriedUpdate handing, UpdateLedger swapped, UpdateRequest request, bool human, bool wasLive, string updateLock, CancellationToken cancellationToken) =>
        HandOverAsync(
            env,
            handing.Steps,
            [handing.Carried.Executable, "update", "apply", "--finish", "--feed", request.Feed.AbsoluteUri],
            human,
            (why, cause) => NotFinished(env, swapped, why, cause),
            async waits =>
            {
                await handing.RecoverAsync(wasLive, waits).ConfigureAwait(false);
                return $"The application is {swapped.ToVersion}, and {handing.RuntimeSaid()} `jason update apply` finishes the update.";
            },
            updateLock,
            cancellationToken);

    /// <summary>
    /// The new carried jason's part: <c>plugins</c>, <c>started</c>, <c>healthy</c>, <c>complete</c>, the skills this
    /// installation recorded, the machine setup and the UI, then the application's own record of the update settled.
    /// </summary>
    private static async Task<int> FinishAsync(CliEnvironment env, CarriedUpdate handing, UpdateLedger inFlight, UpdateRequest request, bool human, bool wasLive, CancellationToken cancellationToken)
    {
        if (inFlight.Step < UpdateStep.Swapped)
        {
            handing.Swapped(inFlight);
            handing.Steps.Add($"swapped: the application is {inFlight.ToVersion}");
        }

        var applier = new UpdateApplier(env, new UpdatePaths(env.Paths), TimeProvider.System, handing.Carried.Executable) { SetsUpTheMachine = true };
        try
        {
            var ledger = await applier.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
            var steps = handing.Steps.Concat(applier.Steps).ToList();
            if (ledger.Step == UpdateStep.Complete)
            {
                handing.Steps.Clear();
                await handing.SettleAsync(cancellationToken).ConfigureAwait(false);
                steps.AddRange(handing.Steps);
            }

            Print(env, human, steps, ledger);
            return ExitCodes.Success;
        }
        catch (UpdateException)
        {
            handing.Steps.AddRange(applier.Steps);
            throw;
        }
        catch (OperationCanceledException cancelled) when (cancellationToken.IsCancellationRequested)
        {
            // The update stopped the runtime; the new jason is in place, so it is started from it, not waited for.
            handing.Steps.AddRange(applier.Steps);
            await handing.RecoverAsync(wasLive, waits: false).ConfigureAwait(false);
            throw new UpdateException(
                UpdateCodes.Cancelled,
                $"The update to {inFlight.ToVersion} was cancelled while it was finished. The application is {inFlight.ToVersion}, and {handing.RuntimeSaid()} `jason update apply` finishes the update.",
                cancelled,
                retryable: true);
        }
    }

    /// <summary>What a data directory that exists holds, as the finishing step decides whether an installation is there.</summary>
    internal enum DataDirHolds
    {
        /// <summary>Something of an installation's is there.</summary>
        Here,

        /// <summary>Nothing but Jason Desktop's own, an update's lock files and the system's litter.</summary>
        DesktopsOwnOnly,

        /// <summary>The directory, or a folder in it the answer depends on, could not be listed.</summary>
        Unreadable,
    }

    /// <summary>
    /// The update's lock, <c>update/update.lock</c>, and the note its holder writes beside it, <c>update/update.lock.holder</c>
    /// (<see cref="UpdatePaths.Lock"/>, <see cref="UpdatePaths.LockHolder"/>): an <c>update/</c> holding these alone is no
    /// installation, a holder killed before it let go (a <c>jason install</c> Jason Desktop's quit ends, on Windows) included.
    /// </summary>
    private static readonly string[] LockFiles = ["update.lock", "update.lock.holder"];

    /// <summary>The files a system leaves in any folder it shows, passed over at the data directory's top and in its logs.</summary>
    private static readonly string[] SystemLitter = [".DS_Store", "desktop.ini", "Thumbs.db"];

    /// <summary>
    /// Whether the data directory holds an installation: the one definition of "an installation is here". It does not where it
    /// holds nothing but Jason Desktop's own — its folder, <c>desktop/</c>, and its log, <c>logs/desktop-*.jsonl</c>, which it
    /// writes from its first start — an <c>update/</c> folder holding nothing but the update's lock file and the note of its
    /// holder (<see cref="LockFiles"/>), which the tool makes for itself, and leaves behind where a holder is killed, and which
    /// never make a machine installed, and the system's litter (<see cref="SystemLitter"/>) at the top
    /// and in <c>logs/</c>. Anything else is an installation's, a record under <c>update/</c> among it. A directory, or one of
    /// those folders, that cannot be listed is <see cref="DataDirHolds.Unreadable"/>: never taken for either answer.
    /// </summary>
    internal static DataDirHolds InstallationAt(string root)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                var name = Path.GetFileName(entry);
                if (name == "desktop" && Directory.Exists(entry))
                {
                    continue;
                }

                if (File.Exists(entry) && SystemLitter.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                if (name == "logs"
                    && Directory.Exists(entry)
                    && Directory.EnumerateFileSystemEntries(entry).All(log => File.Exists(log)
                        && (SystemLitter.Contains(Path.GetFileName(log), StringComparer.Ordinal)
                            || (Path.GetFileName(log).StartsWith("desktop-", StringComparison.Ordinal) && log.EndsWith(".jsonl", StringComparison.Ordinal)))))
                {
                    continue;
                }

                if (name == "update"
                    && Directory.Exists(entry)
                    && Directory.EnumerateFileSystemEntries(entry).All(held => File.Exists(held) && LockFiles.Contains(Path.GetFileName(held), StringComparer.Ordinal)))
                {
                    continue;
                }

                return DataDirHolds.Here;
            }

            return DataDirHolds.DesktopsOwnOnly;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return DataDirHolds.Unreadable;
        }
    }

    /// <summary>The finishing step's report: its document, or for a person one line a part and the part's own lines under it.</summary>
    private static void PrintFinish(CliEnvironment env, bool human, UpdateFinishResponse report)
    {
        if (!human)
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
            return;
        }

        foreach (var part in report.Parts)
        {
            env.Out.WriteLine(part.Reason is { } reason ? $"{part.Part}: {part.State} - {reason}" : $"{part.Part}: {part.State}");
            foreach (var line in part.Lines)
            {
                env.Out.WriteLine($"  - {line}");
            }
        }
    }

    /// <summary>
    /// Why a jason Jason Desktop carries is not updated here: there is no Jason Desktop to hand the update to, on Linux, or
    /// where the application is not where its jason says.
    /// </summary>
    private static string NoDesktop(CliEnvironment env, CarriedExecutable carried, string verb) =>
        env.Setup is { } places
            ? $"This jason is the one the Jason desktop application carries, at {carried.Executable}, and the application is not at "
              + $"{CarriedUpdate.ApplicationOf(carried.Executable, places.Platform)}, so `jason update {verb}` has no Jason Desktop to hand the update to "
              + "and would change a file inside the application. Choose Check for Updates in the desktop's Jason menu, or in the menu of its icon in the tray."
            : $"This jason is the one the Jason desktop application carries, at {carried.Executable}, so `jason update {verb}` "
              + "would change a file inside the application. The application and the jason it carries are updated together: "
              + "choose Check for Updates in the desktop's Jason menu, or in the menu of its icon in the tray.";

    /// <summary>
    /// Why a carried update is not begun from an elevated process: the Desktop, its installer and the runtime it starts again
    /// would run elevated, or install under another account's profile, and the installer's look could not read the verb it
    /// would have to pass over.
    /// </summary>
    private static string Elevated(SetupSystem platform) =>
        (platform is SetupSystem.Windows
            ? "This jason runs as an administrator, elevated above this account's ordinary rights, or as another account than the one signed in to this session"
            : "This jason runs as root")
        + ", and an update begun from here would start Jason Desktop, its installer and the runtime the same way, or install into another account's profile, so nothing was read, staged or asked. "
        + (platform is SetupSystem.Windows
            ? "Run `jason update apply` from an ordinary terminal of the account signed in here, not run as an administrator, or choose Check for Updates in Jason Desktop's menu."
            : "Run `jason update apply` from an ordinary terminal, without sudo, or choose Check for Updates in Jason Desktop's menu.");

    /// <summary>The refusal for a copy that ended with no answer: the update stands at the step it reached, and is carried on.</summary>
    private static UpdateException CopyNotFinished(CliEnvironment env, CarriedUpdate handing, UpdateLedger ledger, string why, Exception? cause)
    {
        UpdateStep step;
        try
        {
            step = UpdateLedger.ReadFile(new UpdatePaths(env.Paths).Ledger)?.Step ?? ledger.Step;
        }
        catch (UpdateLedgerException)
        {
            step = ledger.Step;
        }

        return new UpdateException(
            UpdateCodes.NotHealthy,
            $"The update to {ledger.ToVersion} was handed to the copy of this jason at {handing.Copy}, and {why} The update stands at "
            + $"'{SnakeCase(step)}'. Run `jason update apply` to carry it on.",
            cause);
    }

    /// <summary>What a command printed, when it is one JSON object; null when it is anything else.</summary>
    private static JsonObject? Document(string printed)
    {
        try
        {
            return JsonNode.Parse(printed) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The document an apply printed, with <paramref name="before"/> in front of its steps; null where what it printed is
    /// not one, so that it is passed on untouched. Read as a tree rather than as this build's type, so that a field a
    /// later build adds is carried rather than dropped.
    /// </summary>
    internal static JsonObject? WithStepsBefore(IReadOnlyList<string> before, string printed) =>
        Document(printed) is { } document ? WithStepsBefore(before, document) : null;

    private static JsonObject? WithStepsBefore(IReadOnlyList<string> before, JsonObject answer)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (answer["steps"] is not JsonArray after || after.Any(step => step?.GetValueKind() != JsonValueKind.String))
        {
            return null;
        }

        answer["steps"] = new JsonArray([.. before.Select(step => (JsonNode?)JsonValue.Create(step)), .. after.Select(step => step!.DeepClone())]);
        return answer;
    }

    private static Command RollBack(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "rollback",
            "Put the version this machine updated from back: the executable it replaced with its licence files, and - if that update migrated the database - the backup it wrote. Run as the jason Jason Desktop carries, it puts back the plugin packages and the database, starts no runtime and names the one step by hand that puts the application back, and refuses an update that stands before 'kept', which put nothing aside; on Linux it is refused with update_carried_by_desktop. Refused too when the update recorded is another jason's, and, with update_carried_by_desktop, when that update moved a command-line install into Jason Desktop: it does not go back from 0.49, and its sentence says where that is described.");
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return RollBackAsync(env, parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }

    private static Command Status(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "status",
            "Say where this installation's update stands: what is in flight and which step it reached, or the record the last one left.");
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction(parseResult =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return StatusAsync(env, parseResult.GetValue(human));
        });

        return command;
    }

    public static async Task<int> RollBackAsync(CliEnvironment env, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        string? application = null;
        if (CarriedExecutable.Of(env) is { } carried)
        {
            if (RefusedAsAnotherRecord(env, carried))
            {
                return ExitCodes.ApiError;
            }

            if (env.Setup is not { } places)
            {
                env.Out.WriteLine(CliErrors.Serialize(UpdateCodes.CarriedByDesktop, NoDesktop(env, carried, "rollback"), retryable: false));
                return ExitCodes.ApiError;
            }

            application = CarriedUpdate.ApplicationFolderOf(carried.Executable, places.Platform);
            if (!File.Exists(CarriedUpdate.ApplicationOf(carried.Executable, places.Platform)))
            {
                env.Out.WriteLine(CliErrors.Serialize(UpdateCodes.CarriedByDesktop, NoDesktop(env, carried, "rollback"), retryable: false));
                return ExitCodes.ApiError;
            }
        }
        else if (RefusedAsRecordedCarried(env, "rollback", completeToo: true)
            || RefusedAsManaged(env, "rollback"))
        {
            return ExitCodes.ApiError;
        }

        var rollback = new UpdateRollback(env, new UpdatePaths(env.Paths), TimeProvider.System) { Application = application };
        try
        {
            // A carried rollback holds the update's lock, so it never reads the ledger an update or a finish beside it writes.
            using var held = application is null ? null : UpdateLock.Take(new UpdatePaths(env.Paths), "the rollback");
            var ledger = await rollback.RollBackAsync(cancellationToken).ConfigureAwait(false);
            Print(env, human, rollback.Steps, ledger);
            return ExitCodes.Success;
        }
        catch (UpdateException error)
        {
            // A rollback that could not restore the database still put the executable back, and the steps say
            // so: the refusal and the account of what was done are printed together.
            foreach (var step in rollback.Steps)
            {
                env.Error.WriteLine(step);
            }

            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, error.Retryable));
            return ExitCodes.ApiError;
        }
        catch (UpdateLedgerException error)
        {
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: false));
            return ExitCodes.ApiError;
        }
    }

    /// <summary>
    /// Refuses, and says so, where a jason Jason Desktop carries is asked to roll back an update whose record names another
    /// file: a rollback acts on the file the record names, and that file is not this application's.
    /// </summary>
    private static bool RefusedAsAnotherRecord(CliEnvironment env, CarriedExecutable carried)
    {
        var path = new UpdatePaths(env.Paths).Ledger;
        UpdateLedger? ledger;
        try
        {
            ledger = UpdateLedger.ReadFile(path);
        }
        catch (Exception error) when (error is UpdateLedgerException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (ledger is null || string.IsNullOrWhiteSpace(ledger.InstallPath) || carried.Is(ledger.InstallPath))
        {
            return false;
        }

        // The update that moved a command-line install into the application: the file it names is retired, and no command puts
        // the install back, so none is named.
        if (MovedFrom(env, ledger.InstallPath))
        {
            env.Out.WriteLine(CliErrors.Serialize(
                UpdateCodes.CarriedByDesktop,
                "This Jason was moved from a command-line install into Jason Desktop, and a rollback here does not take it back from 0.49; nothing was changed. "
                + $"Going back from 0.49 is described at {PublicRepository.GoingBackFrom049}",
                retryable: false));
            return true;
        }

        env.Out.WriteLine(CliErrors.Serialize(
            UpdateCodes.CarriedByDesktop,
            $"The update recorded at {path} is of the jason at {ledger.InstallPath}, not of the one the Jason desktop application carries, "
            + $"at {carried.Executable}, so this jason does not roll it back: that jason does, with `{ledger.InstallPath} update rollback`.",
            retryable: false));
        return true;
    }

    /// <summary>
    /// Whether the command-line install at <paramref name="installPath"/> was moved into Jason Desktop: its move is done, or the
    /// install is retired by the rule <c>jason status</c> reads (<see cref="MoveRecord.Retired"/>), which a move stopped after the
    /// routine leaves with its record not done.
    /// </summary>
    private static bool MovedFrom(CliEnvironment env, string installPath)
    {
        try
        {
            return MoveRecord.Read(new UpdatePaths(env.Paths).Move) is { } record
                && string.Equals(Path.GetFullPath(record.From), Path.GetFullPath(installPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && (record.State == MoveStates.Done || MoveRecord.Retired(record.From));
        }
        catch (MoveRecordUnreadable)
        {
            return false;
        }
    }

    /// <summary>
    /// Refuses, and says so, where the update record names a carried executable other than the one this process runs
    /// as. A resumed apply and a rollback act on the file the record names, not on their own, so another jason sharing
    /// the data directory, one the install line put on the PATH say, would otherwise replace the application's file.
    /// </summary>
    /// <remarks>
    /// An apply acts only on a record in flight, and plans a new update of its own file past a complete one; a rollback
    /// acts on any record. A record that cannot be read refuses nothing here: the verb reads it again and answers as it
    /// always has. The hand-over is not taken here: it is only for the carried file running as itself.
    /// </remarks>
    private static bool RefusedAsRecordedCarried(CliEnvironment env, string verb, bool completeToo)
    {
        var path = new UpdatePaths(env.Paths).Ledger;
        UpdateLedger? ledger;
        try
        {
            ledger = UpdateLedger.ReadFile(path);
        }
        catch (Exception error) when (error is UpdateLedgerException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (ledger is null
            || (!completeToo && ledger.Step == UpdateStep.Complete)
            || string.IsNullOrWhiteSpace(ledger.InstallPath)
            || CarriedExecutable.Of(ledger.InstallPath) is not { } carried
            || carried.Is(env.InstallPath ?? SelfExecutable.InstalledImage))
        {
            return false;
        }

        env.Out.WriteLine(CliErrors.Serialize(
            UpdateCodes.CarriedByDesktop,
            $"The update recorded at {path} belongs to the jason the Jason desktop application carries, at "
            + $"{carried.Executable}, so `jason update {verb}` would change a file inside the application. "
            + "The application and the jason it carries are updated together: choose Check for Updates in the "
            + "desktop's Jason menu, or in the menu of its icon in the tray.",
            retryable: false));
        return true;
    }

    /// <summary>
    /// Refuses, and says so, where a package manager installed this Jason. The manager keeps its own record of the
    /// version and owns the file these verbs would replace, so they leave it alone and print what to type instead.
    /// </summary>
    /// <remarks>
    /// Three lines and never one, because the manager knows nothing of the runtime Jason keeps running: it replaces the
    /// image every plugin host that runtime starts is launched from — under Homebrew it deletes the old keg — so the
    /// runtime is stopped before the manager's upgrade and started from the new image after it.
    /// </remarks>
    private static bool RefusedAsManaged(CliEnvironment env, string verb)
    {
        if (PackageOwner.Of(env) is not { } owner)
        {
            return false;
        }

        env.Out.WriteLine(CliErrors.Serialize(
            UpdateCodes.ManagedByPackageManager,
            $"This Jason was installed by {owner.Manager}, which keeps its own record of the version, so `jason update {verb}` "
            + $"would replace a file the package manager owns. Its version is changed through {owner.Manager}, with the "
            + "runtime stopped first and started after:\n"
            + string.Join("\n", owner.TypedUpgrade().Select(line => "  " + line)),
            retryable: false));
        return true;
    }

    private static void Print(CliEnvironment env, bool human, IReadOnlyList<string> steps, UpdateLedger ledger, UpdateMoveResponse? move = null)
    {
        var answer = new UpdateApplyResponse(
            ledger.FromVersion.ToString(),
            ledger.ToVersion.ToString(),
            SnakeCase(ledger.Step),
            [.. steps],
            move);

        env.Out.WriteLine(human
            ? string.Join(Environment.NewLine, steps.Select(step => "- " + step))
            : JsonSerializer.Serialize(answer, JasonJson.Options));
    }

    /// <summary>
    /// Reads the ledger, and asks no runtime anything. The moment a person most wants this answer is the moment
    /// an update has gone wrong, which is also the likeliest moment for there to be no runtime to ask — so
    /// nothing here can fail for the want of one, and the exit code is 0 whether one is listening or not.
    /// </summary>
    public static int StatusAsync(CliEnvironment env, bool human)
    {
        ArgumentNullException.ThrowIfNull(env);

        UpdateLedger? ledger;
        try
        {
            ledger = UpdateLedger.ReadFile(new UpdatePaths(env.Paths).Ledger);
        }
        catch (UpdateLedgerException error)
        {
            // There is a file and it is not a ledger. Saying so is the whole answer: acting on half a document
            // is how a person is told an update reached a step it never began.
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: false));
            return ExitCodes.ApiError;
        }

        var answer = new UpdateStatusResponse(
            InFlight: ledger is { Step: not UpdateStep.Complete },
            Step: ledger is null ? null : SnakeCase(ledger.Step),
            From: ledger?.FromVersion.ToString(),
            To: ledger?.ToVersion.ToString(),
            StartedAt: ledger?.StartedAt,
            BackupFile: ledger?.BackupFile,
            NewlyApplied: ledger?.NewlyApplied ?? []);

        env.Out.WriteLine(human ? Sentence(answer) : JsonSerializer.Serialize(answer, JasonJson.Options));
        return ExitCodes.Success;
    }

    private static string Sentence(UpdateStatusResponse answer)
    {
        if (answer.Step is null)
        {
            return "No update has been applied on this installation, and none is under way.";
        }

        if (answer.InFlight)
        {
            return $"An update from {answer.From} to {answer.To} is under way and has reached '{answer.Step}'. "
                + "Carry it on with `jason update apply`, or go back with `jason update rollback`.";
        }

        var migrations = answer.NewlyApplied.Count == 0
            ? " It applied no migrations."
            : $" Its first start applied {string.Join(", ", answer.NewlyApplied)}, backed up to {answer.BackupFile}.";

        return $"The last update went from {answer.From} to {answer.To} and is complete.{migrations}";
    }

    /// <summary>Q7's answer: two minutes, and a person may say 0 to wait for none of it.</summary>
    private const int DefaultDrainSeconds = 120;

    private static string SnakeCase(UpdateStep step) => step.ToString().ToLowerInvariant();

    private static SemanticVersion? Wanted(string? version)
    {
        if (version is null)
        {
            return null;
        }

        return SemanticVersion.TryParse(version, out var parsed)
            ? parsed
            : throw new UsageException($"--version must be a version like 0.2.0, not '{version}'.");
    }

    internal static Uri Address(string? feed) => feed is null
        ? UpdateFeed.Default
        : Uri.TryCreate(feed, UriKind.Absolute, out var named) ? named : throw new UsageException("--feed must be an absolute address.");

    public static async Task<int> CheckAsync(CliEnvironment env, string? feed, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var address = Address(feed);

        // The handler the rest of the CLI sends through, so a test that substitutes it sees this request too —
        // and sees that no other is made.
        using var client = UpdateHttp.Client(env);
        client.Timeout = UpdateFeed.DefaultTimeout;

        UpdateManifest manifest;
        try
        {
            manifest = await new UpdateFeed(client).ReadAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateFeedException error)
        {
            // The feed's own code, in the envelope every other failure uses. Offline is the one worth trying
            // again; a document that is not a manifest, or an address this build will not fetch, is not.
            env.Out.WriteLine(CliErrors.Serialize(error.Code, error.Message, retryable: error.Code == UpdateFeedException.Unreachable));
            return ExitCodes.ApiError;
        }

        var running = SemanticVersion.Current;
        var owner = PackageOwner.Of(env);
        var available = manifest.IsNewerThan(running);

        // Below the release's floor, Jason's own installation reaches it through the floor's apply first: the two commands
        // the apply's refusal names, said before anybody types it. A manager replaces the file whole, with no applier of
        // this build's in between, so the floor says nothing to it.
        var first = owner is null && available && manifest.MinUpgradeFrom is { } floor && running < floor
            ? floor
            : (SemanticVersion?)null;
        var answer = new UpdateCheckResponse(
            running.ToString(),
            manifest.Version.ToString(),
            available,
            DateTimeOffset.UtcNow,
            manifest.ReleaseNotesUrl,
            owner?.UpgradeCommand ?? (first is { } through ? UpdateApplier.ApplyOf(through) : OwnUpgrade),
            first?.ToString(),
            PluginUpdates.Behind(manifest, available, env.Paths));

        var passThrough = first is { } version ? UpdateApplier.PassThrough(manifest.Version, running, version, pinned: false) : null;
        env.Out.WriteLine(human ? Sentence(answer, owner, passThrough) : JsonSerializer.Serialize(answer, JasonJson.Options));
        return ExitCodes.Success;
    }

    /// <summary>
    /// What a person is told about Jason itself, then a line per plugin package behind the release, after every line
    /// about Jason: the update comes first, and its <c>plugins</c> step replaces the package. A package manager's upgrade
    /// has no such step, so under one the line names the install that replaces it once the upgrade is done.
    /// </summary>
    private static string Sentence(UpdateCheckResponse answer, PackageOwner? owner, string? passThrough) =>
        string.Join(
            Environment.NewLine,
            [
                AboutJason(answer, owner, passThrough),
                .. (answer.PluginUpdates ?? []).Select(plugin =>
                    $"Plugin package {plugin.Id} {plugin.Installed} is installed, and Jason {answer.Latest} publishes {plugin.Id} {plugin.Latest}: "
                    + (owner is null
                        ? $"`{OwnUpgrade}` replaces it."
                        : $"after the upgrade, `jason plugin install {plugin.Id} --replace` replaces it.")),
            ]);

    /// <summary>What updates an installation that is Jason's own.</summary>
    private const string OwnUpgrade = "jason update apply";

    private static string AboutJason(UpdateCheckResponse answer, PackageOwner? owner, string? passThrough)
    {
        if (!answer.Available)
        {
            return $"Up to date: this is {answer.Current}, and the newest release is {answer.Latest}.";
        }

        var notes = answer.ReleaseNotesUrl is null ? string.Empty : $" Release notes: {answer.ReleaseNotesUrl}";
        if (owner is null)
        {
            // Below the floor, the words the apply would refuse with, on a line of their own: the two commands to type.
            return passThrough is null
                ? $"A newer version is available: {answer.Latest} (this is {answer.Current}). Install it with `{OwnUpgrade}`.{notes}"
                : string.Join(Environment.NewLine, $"A newer version is available: {answer.Latest} (this is {answer.Current}).{notes}", passThrough);
        }

        // Three lines a person types, never the one: the manager replaces the image the running runtime starts its
        // plugin hosts from, and knows nothing of that runtime.
        return string.Join(
            Environment.NewLine,
            [
                $"A newer version is available: {answer.Latest} (this is {answer.Current}).{notes}",
                $"{owner.Manager} installed this Jason, so it is updated through {owner.Manager}, with the runtime stopped first and started after:",
                .. owner.TypedUpgrade().Select(line => "  " + line),
            ]);
    }
}
