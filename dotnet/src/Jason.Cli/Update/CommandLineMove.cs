using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// The move of a command-line install into Jason Desktop: run by the jason an update of that install put at its path, once
/// that update is complete, and by <c>jason update apply</c> typed into it again, which carries a move on at the version it
/// is. It installs the application, or reuses one of 0.49.0 or later that is already there, runs the application's own
/// <c>jason install</c>, which retires the command-line install and stops its runtime, and starts the runtime again from
/// the application's jason.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never fails the update</b>, which is complete before it begins: whatever it meets, the update's answer stays a
/// success, and where the move stands is said beside it, in the document's <c>move</c> and in <c>&lt;data&gt;/update/move.json</c>.
/// </para>
/// <para>
/// <b>Every prefix of its steps leaves a working installation.</b> Until the application's routine retires the command-line
/// install, that install is untouched and answers as <c>jason</c>; the routine places the application's jason in the terminal
/// before it retires anything. The one stop of a runtime is the routine's, after the download and both checks; whoever stops
/// a runtime leaves the note that a start is owed, and this move, which asked for it, starts one again from the application's
/// jason, which is whole by then.
/// </para>
/// <para>
/// <b>It never renames, replaces or deletes an application that is there, and never closes one.</b> One of 0.49.0 or later is
/// reused and its own routine run; one older than that leaves the move pending, as an elevated process, an Intel Mac and a
/// setup that has not ended within its bound do. Pending is never failed: <c>jason update apply</c> carries it on.
/// </para>
/// <para>
/// <b>One at a time, under the update's lock</b>, held for the whole move. The installer's own <c>jason install</c> finds it
/// held and is refused, which the installer ignores; the move runs the application's routine itself once the installer has
/// returned, handing it the lock.
/// </para>
/// </remarks>
public sealed class CommandLineMove(CliEnvironment env, UpdatePaths update, TimeProvider clock)
{
    /// <summary>The first version whose application carries <c>jason install</c>: an older one is never set up from here. Compared by its numbers, a pre-release part ignored, as the install scripts compare it.</summary>
    public static SemanticVersion FirstSettingUp { get; } = new(0, 49, 0, null);

    /// <summary>The installer a Windows release publishes.</summary>
    public const string WindowsAsset = "jason-desktop-windows-x64-setup.exe";

    /// <summary>The application a macOS release publishes for Apple silicon, as the updater downloads it.</summary>
    public const string MacAsset = "jason-desktop-macos-arm64.zip";

    /// <summary>A download is at most this large: an installer is a few hundred megabytes at most.</summary>
    internal const long MaxDownloadBytes = 1024L * 1024 * 1024;

    /// <summary>What the move asks of the operating system: the environment's (<see cref="CliEnvironment.Moves"/>).</summary>
    private IMoveSystem System { get; set; } = null!;

    /// <summary>How long a download is given whole.</summary>
    internal TimeSpan DownloadWait { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a download may go without a byte before it counts as stalled.</summary>
    internal TimeSpan StallWait { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How long the Windows setup is waited for. One that has not ended by then is left running, never killed.</summary>
    internal TimeSpan SetupWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the application's <c>jason install</c> is waited for: its own waits are a minute's drain and the stop's.</summary>
    internal TimeSpan RoutineWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a runtime started from the application's jason is waited for.</summary>
    internal TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(60);

    internal TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Called with each record once it is written, as the update's own <c>Writes</c> is: where a test stands a kill.</summary>
    internal Action<MoveRecord>? Writes { get; init; }

    /// <summary>What the move did, one line each under <c>move</c>, for the update's document.</summary>
    public List<string> Lines { get; } = [];

    private IRuntimeProcessControl Processes => env.Processes ?? RuntimeProcessControl.Instance;

    private MoveRecord? _record;

    /// <summary>
    /// The move of the install <paramref name="ledger"/> updated, at the version it installed, from <paramref name="feed"/>:
    /// carried as far as it goes, and said.
    /// </summary>
    public async Task<UpdateMoveResponse> RunAsync(UpdateLedger ledger, Uri feed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(feed);

        if (env.Setup is not { } places)
        {
            // Linux, and any system that is neither: the command-line install is the installation there, and stays it.
            return new UpdateMoveResponse(
                MoveStates.NotApplicable,
                null,
                null,
                "Jason Desktop takes a command-line install in on macOS and Windows only; on this system the command line is the installation, and nothing was moved.");
        }

        var old = Path.GetFullPath(ledger.InstallPath);
        try
        {
            if (MoveRecord.Read(update.Move) is { State: MoveStates.Done } done && SamePath(done.From, old, places))
            {
                Say($"{old} was moved into Jason Desktop already, at {done.Application}");
                return Response(done);
            }
        }
        catch (MoveRecordUnreadable)
        {
            // Written over below, under the lock.
        }

        // Refused before anything is read, asked or written: neither is any move's to change.
        if (PackageOwner.Of(env with { InstallPath = old }) is { } owner)
        {
            return Unrecorded(
                MoveStates.Pending,
                $"This Jason was installed by {owner.Manager}, which keeps its own record of the file, so it is not moved into Jason Desktop from here; nothing was changed.");
        }

        // Nothing is written either: a record an elevated process wrote would be another account's file in this one's folder.
        if (places.Elevated)
        {
            return Unrecorded(MoveStates.Pending, Elevated(places.Platform));
        }

        if (env.Moves is not { } system)
        {
            return Unrecorded(MoveStates.Pending, "This environment installs no application, so the command-line install was not moved and nothing was changed.");
        }

        System = system;
        UpdateLock held;
        try
        {
            held = UpdateLock.Take(update, "the move into Jason Desktop");
        }
        catch (UpdateException refused)
        {
            return Unrecorded(
                MoveStates.Pending,
                refused.Code is UpdateCodes.InProgress or CliErrors.InstallInProgress
                    ? $"Another jason holds {update.Lock}, so the move into Jason Desktop was not begun: let it end, then run `jason update apply` to carry the move on."
                    : refused.Message);
        }

        using (held)
        {
            _record = new MoveRecord(ledger.ToVersion.ToString(), old, MoveStates.Moving, null, null, null, null, feed.AbsoluteUri, clock.GetUtcNow());
            RetiredSweep.Sweep(places, old);
            try
            {
                return await MoveAsync(places, ledger.ToVersion, old, feed, held, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StartOwedAsync(Carried(places, _record.Application), waits: false).ConfigureAwait(false);
                return End(MoveStates.Pending, $"The move into Jason Desktop was cancelled while it was {Doing()}. `jason update apply` carries it on.");
            }
            catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
            {
                // Everything: a completed update reported as refused is the one outcome the move must never have.
                env.Error.WriteLine(unexpected.Message);
                await StartOwedAsync(Carried(places, _record.Application), waits: true).ConfigureAwait(false);
                return End(MoveStates.Failed, $"The move into Jason Desktop stopped while it was {Doing()}: {unexpected.Message} `jason update apply` carries it on.");
            }
        }
    }

    private async Task<UpdateMoveResponse> MoveAsync(SetupPlaces places, SemanticVersion version, string old, Uri feed, UpdateLock held, CancellationToken cancellationToken)
    {
        // --- the look: an application already in either place is the one the move takes the install into ---
        Begin(MoveSteps.Looked);
        if (places.Platform is SetupSystem.MacOs && !System.IsAppleSilicon())
        {
            // Never attempted: whether Intel Macs are offered Jason Desktop is not decided here.
            return End(
                MoveStates.Pending,
                "This is an Intel Mac, for which Jason Desktop is not offered yet, so the command-line install was not moved and nothing was changed; it answers as jason as it did.");
        }

        var found = Look(places);
        if (found.Refusal is { } refusal)
        {
            return End(MoveStates.Pending, refusal, application: found.Application);
        }

        Done(MoveSteps.Looked, found.Application);
        if (found.Version is { } there)
        {
            if (places.Platform is SetupSystem.MacOs
                && await System.MacSignatureRefusalAsync(found.Application, MoveSignatures.RequirementInForce, cancellationToken).ConfigureAwait(false) is { } unsigned)
            {
                env.Error.WriteLine(unsigned);
                return End(MoveStates.Failed, $"The Jason.app at {found.Application} does not verify as signed by the team that signs Jason, so its jason was not run and nothing was changed ({OneLine(unsigned)}). `jason update apply` carries the move on.");
            }

            Say($"Jason Desktop {there} is at {found.Application} already, so it is kept as it is and nothing is downloaded");
            Done(MoveSteps.Fetched);
            Done(MoveSteps.Verified);
            Done(MoveSteps.Placed);
        }
        else
        {
            var installed = await InstallAsync(places, version, found.Application, feed, cancellationToken).ConfigureAwait(false);
            if (installed is not null)
            {
                return installed;
            }
        }

        // --- the application's own routine, under this move's lock ---
        var carried = Carried(places, _record!.Application)!;
        Begin(MoveSteps.SetUp);
        var routine = await RoutineAsync(carried, old, held, cancellationToken).ConfigureAwait(false);
        if (routine.Refusal is { } stopped)
        {
            await StartOwedAsync(carried, waits: true).ConfigureAwait(false);
            return End(routine.Pending ? MoveStates.Pending : MoveStates.Failed, stopped);
        }

        Done(MoveSteps.SetUp);

        // --- the runtime, started again by the caller that asked for its stop ---
        Begin(MoveSteps.Started);
        if (routine.Stopped || StartOwed.Read(env.Paths).Stands)
        {
            await StartOwedAsync(carried, waits: true).ConfigureAwait(false);
        }
        else
        {
            Say("no runtime was stopped, so none was started");
        }

        Done(MoveSteps.Started);
        return End(MoveStates.Done, null);
    }

    /// <summary>
    /// The application installed where none is: downloaded, held to its line in checksums.txt and to the publisher's
    /// signature, and on Windows its setup run silently and waited for, on macOS put in place by one rename. Null where it
    /// is in place; the move's answer where it stopped.
    /// </summary>
    private async Task<UpdateMoveResponse?> InstallAsync(SetupPlaces places, SemanticVersion version, string application, Uri feed, CancellationToken cancellationToken)
    {
        var windows = places.Platform is SetupSystem.Windows;
        var asset = windows ? WindowsAsset : MacAsset;
        var release = UpdateFeed.PinnedFor(feed, version);

        Begin(MoveSteps.Fetched);
        using var client = UpdateHttp.Client(env);
        client.Timeout = Timeout.InfiniteTimeSpan;

        // The release read again: a feed that offers another version now would hand over another application.
        UpdateManifest manifest;
        try
        {
            using var asking = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            asking.CancelAfter(UpdateFeed.DefaultTimeout);
            manifest = await new UpdateFeed(client).ReadAsync(release, asking.Token).ConfigureAwait(false);
        }
        catch (Exception unread) when (unread is UpdateFeedException or HttpRequestException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return End(MoveStates.Pending, $"The release at {release} could not be read ({OneLine(unread.Message)}), so Jason Desktop was not downloaded and nothing was changed. `jason update apply` carries the move on.");
        }

        if (manifest.Version != version)
        {
            return End(MoveStates.Pending, $"The release at {release} is {manifest.Version}, not {version}, which this jason is, so Jason Desktop was not downloaded and nothing was changed. `jason update apply` updates to it and carries the move on.");
        }

        Directory.CreateDirectory(update.MoveDownloads);
        var sums = UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Checksums);
        string listed;
        try
        {
            listed = Encoding.UTF8.GetString(await FetchSmallAsync(client, sums, cancellationToken).ConfigureAwait(false));
        }
        catch (MoveDownloadException failed)
        {
            return End(MoveStates.Pending, $"{failed.Message} Jason Desktop was not downloaded and nothing was changed. `jason update apply` carries the move on.");
        }

        if (ReleaseChecksums.DigestOf(listed, asset, out var problem) is not { } expected)
        {
            return End(MoveStates.Failed, $"{problem ?? $"{sums} lists no {asset}."} Jason Desktop was not downloaded and nothing was changed. `jason update apply` carries the move on.");
        }

        var download = Path.Combine(update.MoveDownloads, asset);
        string actual;
        try
        {
            actual = await FetchToFileAsync(client, UpdateFeed.DownloadUrlFor(release, asset), download, cancellationToken).ConfigureAwait(false);
        }
        catch (MoveDownloadException failed)
        {
            return End(MoveStates.Pending, $"{failed.Message} Nothing was installed or changed. `jason update apply` carries the move on.");
        }

        Done(MoveSteps.Fetched);
        Say($"downloaded {asset} {version}");

        // --- the checks, before anything of it runs ---
        Begin(MoveSteps.Verified);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Discard(download);
            return End(MoveStates.Failed, $"{asset} does not match {ReleaseAssets.Checksums} (sha256 {actual}, expected {expected}), so it was not run and nothing was installed or changed. `jason update apply` carries the move on.");
        }

        if (windows)
        {
            if (System.WindowsSignatureRefusal(download, MoveSignatures.PublisherInForce) is { } unsigned)
            {
                Discard(download);
                return End(MoveStates.Failed, $"{asset} was not run: {unsigned}. Nothing was installed or changed. `jason update apply` carries the move on.");
            }

            Done(MoveSteps.Verified);
            return await RunSetupAsync(places, version, application, download, cancellationToken).ConfigureAwait(false);
        }

        return await PlaceOnMacAsync(version, application, download, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Windows: the setup run silently into the application's place and waited for, within its bound; the place looked at
    /// again first, as the install script does at its last moment. Its own install step finds this move's lock held and is
    /// refused: the routine is the move's to run.
    /// </summary>
    private async Task<UpdateMoveResponse?> RunSetupAsync(SetupPlaces places, SemanticVersion version, string application, string setup, CancellationToken cancellationToken)
    {
        Begin(MoveSteps.Placed);
        if (Look(places) is { } again && (again.Refusal is not null || again.Version is not null))
        {
            // An application appeared while the setup was downloaded and checked: its own, never installed over.
            return again.Refusal is { } refusal
                ? End(MoveStates.Pending, refusal, application: again.Application)
                : End(MoveStates.Pending, $"Jason Desktop {again.Version} appeared at {again.Application} while the move downloaded its installer, so nothing was installed over it; `jason update apply` carries the move on into it.", application: again.Application);
        }

        Say($"installing Jason Desktop {version} to {application}");
        SetupRun run;
        try
        {
            run = await System.RunSetupAsync(setup, ["/S", "/D=" + application], SetupWait, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception unstarted) when (unstarted is Win32Exception or IOException or InvalidOperationException)
        {
            return End(MoveStates.Failed, $"{setup} could not be started ({OneLine(unstarted.Message)}), so nothing was installed or changed. `jason update apply` carries the move on.");
        }

        if (!run.Ended)
        {
            return End(MoveStates.Pending, string.Create(CultureInfo.InvariantCulture, $"The installer of Jason Desktop {version} has not ended within {SetupWait.TotalMinutes:F0} minutes, so it was left running and the command-line install was not touched: it answers as jason as it did. Once Jason Desktop is installed, `jason update apply` carries the move on."));
        }

        switch (run.ExitCode)
        {
            case 0:
                break;
            case 1:
                return End(MoveStates.Failed, $"The installer stopped before it replaced a file: a Jason process runs from {application}. Stop it, then run `jason update apply` to carry the move on; the command-line install was not touched.");
            case 2:
                return End(MoveStates.Failed, "The installer stopped before it installed: another install of Jason ran at the same time. Run `jason update apply` to carry the move on; the command-line install was not touched.");
            default:
                return End(MoveStates.Failed, string.Create(CultureInfo.InvariantCulture, $"The installer exited with {run.ExitCode}, and Jason Desktop was not installed; the command-line install was not touched. `jason update apply` carries the move on."));
        }

        if (Carried(places, application) is not { } carried || CarriedExecutable.Of(carried)?.Version != version.ToString())
        {
            return End(MoveStates.Failed, $"The installer exited 0, and {Carried(places, application)} is not Jason {version}; the command-line install was not touched. `jason update apply` carries the move on.");
        }

        Discard(setup);
        Done(MoveSteps.Placed, application);
        Say($"installed Jason Desktop {version} to {application}");
        return null;
    }

    /// <summary>
    /// macOS: the zip unpacked with ditto into a staging folder beside the place, so that putting it there is one rename on one
    /// volume; held to the publisher's requirement and to the version before anything of it runs; renamed into an empty place
    /// only.
    /// </summary>
    private async Task<UpdateMoveResponse?> PlaceOnMacAsync(SemanticVersion version, string application, string zip, CancellationToken cancellationToken)
    {
        var place = Path.GetDirectoryName(application)!;
        string stage;
        try
        {
            Directory.CreateDirectory(place);

            // What a move killed while it unpacked left beside the place: only a staging folder of this move's own name, and
            // only under the lock, so none of a move in flight.
            foreach (var left in Directory.EnumerateDirectories(place, ".jason-move.*"))
            {
                if (Path.GetFileName(left) is { Length: 44 } name && name[12..].All(char.IsAsciiHexDigitLower) && !File.GetAttributes(left).HasFlag(FileAttributes.ReparsePoint))
                {
                    TryDeleteTree(left);
                }
            }

            stage = Path.Combine(place, $".jason-move.{Guid.NewGuid():N}");
            Directory.CreateDirectory(stage);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return End(MoveStates.Failed, $"No folder could be made in {place} to unpack Jason Desktop into ({OneLine(refused.Message)}); nothing was installed or changed. `jason update apply` carries the move on.");
        }

        try
        {
            if (await System.UnzipAsync(zip, stage, cancellationToken).ConfigureAwait(false) is { } unpacked)
            {
                return End(MoveStates.Failed, $"{MacAsset} could not be unpacked into {stage} ({OneLine(unpacked)}); nothing was installed or changed. `jason update apply` carries the move on.");
            }

            var staged = Path.Combine(stage, "Jason.app");
            if (!Directory.Exists(staged))
            {
                return End(MoveStates.Failed, $"{MacAsset} holds no Jason.app; nothing was installed or changed. `jason update apply` carries the move on.");
            }

            if (await System.MacSignatureRefusalAsync(staged, MoveSignatures.RequirementInForce, cancellationToken).ConfigureAwait(false) is { } unsigned)
            {
                env.Error.WriteLine(unsigned);
                return End(MoveStates.Failed, $"The Jason.app in {MacAsset} does not verify as signed by the team that signs Jason ({OneLine(unsigned)}), so nothing of it ran and nothing was installed or changed. `jason update apply` carries the move on.");
            }

            if (CarriedExecutable.Of(Path.Combine(staged, "Contents", "Resources", "bundle", "jason"))?.Version != version.ToString())
            {
                return End(MoveStates.Failed, $"The Jason.app in {MacAsset} is not Jason {version}; nothing was installed or changed. `jason update apply` carries the move on.");
            }

            Done(MoveSteps.Verified);

            // --- placed by one rename, into an empty place only ---
            Begin(MoveSteps.Placed);
            if (Directory.Exists(application) || File.Exists(application))
            {
                return End(MoveStates.Pending, $"{application} appeared while the move unpacked Jason Desktop, so it is left as it was and nothing was installed; `jason update apply` carries the move on into it.", application: application);
            }

            try
            {
                Directory.Move(staged, application);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                return End(MoveStates.Failed, $"{application} could not be put in place ({OneLine(refused.Message)}); nothing was installed or changed. `jason update apply` carries the move on.");
            }

            // The look and the rename are two steps: a Jason.app that appeared between them took this one inside it. It is
            // taken back out, and that one is left as it was.
            if (!Directory.Exists(staged) && Directory.Exists(Path.Combine(application, "Jason.app")))
            {
                TryMove(Path.Combine(application, "Jason.app"), staged);
                return End(MoveStates.Pending, $"{application} appeared while the move ran and is left as it was; nothing was installed. `jason update apply` carries the move on into it.", application: application);
            }

            Done(MoveSteps.Placed, application);
            Say($"installed Jason Desktop {version} to {application}");
            return null;
        }
        finally
        {
            Discard(zip);
            TryDeleteTree(stage);
        }
    }

    /// <summary>The application's own <c>jason install --retire</c>, under this move's lock, read by its one report.</summary>
    private async Task<(string? Refusal, bool Pending, bool Stopped)> RoutineAsync(string carried, string old, UpdateLock held, CancellationToken cancellationToken)
    {
        FinishedCommand finished;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(RoutineWait);
        try
        {
            finished = await Processes.RunAsync(env.Paths, [carried, "install", "--retire", old], env.Error, held.HandOver(), bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ($"The application's `jason install` did not end within {RoutineWait.TotalMinutes:F0} minutes and was ended. `jason update apply` carries the move on.", true, false);
        }
        catch (Exception unstarted) when (unstarted is Win32Exception or IOException or InvalidOperationException)
        {
            return ($"{carried} could not be run to set this machine up ({OneLine(unstarted.Message)}); the command-line install was not touched. `jason update apply` carries the move on.", false, false);
        }

        JsonObject? report;
        try
        {
            report = JsonNode.Parse(finished.StandardOutput) as JsonObject;
        }
        catch (JsonException)
        {
            report = null;
        }

        if (report is null)
        {
            return (string.Create(CultureInfo.InvariantCulture, $"The application's `jason install` ended with exit code {finished.ExitCode} and printed no report, so whether it retired the command-line install is not known. `jason update apply` carries the move on."), false, false);
        }

        if (report["error"] is JsonObject error)
        {
            var code = error["code"]?.GetValue<string>();
            var message = error["message"]?.GetValue<string>() ?? string.Empty;
            return ($"The application's `jason install` refused ({code}): {message}", code is UpdateCodes.InProgress or CliErrors.InstallInProgress, false);
        }

        var runtime = report["runtime"]?.GetValueKind() == JsonValueKind.String ? report["runtime"]!.GetValue<string>() : null;
        var stopped = runtime == MachineSetup.RuntimeStates.Stopped;
        foreach (var line in Strings(report["done"]).Concat(Strings(report["left"])))
        {
            Say(OneLine(line).TrimEnd('.'));
        }

        if (report["retired"] is not JsonObject)
        {
            var problems = Strings(report["problems"]).ToList();
            foreach (var problem in problems)
            {
                env.Error.WriteLine(problem);
            }

            var why = problems.Count > 0 ? string.Join(" ", problems.Select(OneLine)) : "it found no command-line install to retire.";
            return (
                $"The application's `jason install` did not retire the command-line install at {old}: {why} `jason update apply` carries the move on.",
                runtime is MachineSetup.RuntimeStates.WouldNotStop or MachineSetup.RuntimeStates.Running,
                stopped);
        }

        foreach (var problem in Strings(report["problems"]))
        {
            env.Error.WriteLine(problem);
            Say($"{OneLine(problem).TrimEnd('.')}; run: jason install");
        }

        return (null, false, stopped);
    }

    /// <summary>
    /// A runtime started from the application's jason where a start is owed and none answers: the setup this move asked for
    /// stopped one, or left the note that says so. Under no cancel; launched and not waited for where <paramref name="waits"/>
    /// is false.
    /// </summary>
    private async Task StartOwedAsync(string? carried, bool waits)
    {
        if (DescriptorProcess.Live(env.Paths, Processes) is not null || !StartOwed.Read(env.Paths).Stands)
        {
            return;
        }

        if (carried is null || !File.Exists(carried))
        {
            Say("a start is owed and the application's jason is not in place, so none was started; run: jason runtime start");
            return;
        }

        IProcessHandle child;
        try
        {
            child = Processes.Launch(env.Paths, [carried], null);
        }
        catch (Exception unstarted) when (unstarted is Win32Exception or IOException or InvalidOperationException)
        {
            Say($"the runtime could not be started from {carried} — {OneLine(unstarted.Message)}; run: jason runtime start");
            return;
        }

        using (child)
        {
            if (!waits)
            {
                Say($"started the runtime from {carried}, not waited for");
                return;
            }

            var deadline = clock.GetUtcNow() + StartWait;
            while (clock.GetUtcNow() < deadline)
            {
                if (DescriptorProcess.Live(env.Paths, Processes) is not null)
                {
                    Say($"started the runtime from {carried}");
                    return;
                }

                if (child.HasExited)
                {
                    break;
                }

                await Task.Delay(Poll, clock, CancellationToken.None).ConfigureAwait(false);
            }

            var reason = child.HasExited
                ? string.Create(CultureInfo.InvariantCulture, $"it exited with code {child.ExitCode}") + (child.Said.Length > 0 ? $" and said: {OneLine(child.Said)}" : string.Empty)
                : string.Create(CultureInfo.InvariantCulture, $"nothing answered within {StartWait.TotalSeconds:F0}s");
            Say($"the runtime started from {carried} does not answer — {reason}; run: jason runtime start");
        }
    }

    // --- the look ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The application already here, in either place, and its version; or, where none is, the place a new one goes. An
    /// application older than <see cref="FirstSettingUp"/>, or one whose version cannot be read, is a refusal: never
    /// replaced from a terminal.
    /// </summary>
    private (string Application, SemanticVersion? Version, string? Refusal) Look(SetupPlaces places)
    {
        foreach (var application in Places(places))
        {
            var program = places.Platform is SetupSystem.Windows ? Path.Combine(application, "Jason.exe") : application;
            if (!File.Exists(program) && !Directory.Exists(program))
            {
                continue;
            }

            var carried = Carried(places, application)!;
            if (CarriedExecutable.Of(carried) is { } placed && SemanticVersion.TryParse(placed.Version, out var version) && (version with { PreRelease = null }) >= FirstSettingUp)
            {
                return (application, version, null);
            }

            var named = CarriedExecutable.Of(carried)?.Version ?? "of an unknown version";
            var open = places.Platform is SetupSystem.Windows && System.Runs(program) ? ", and its window is open" : string.Empty;
            return (
                application,
                null,
                $"Jason Desktop {named} at {application} is older than {FirstSettingUp}{open}, so it cannot be set up from a terminal and the move of the command-line install waits; nothing was changed, and the command-line install answers as jason as it did. Update Jason Desktop from its own menu (Check for Updates): its first start of {FirstSettingUp} or later takes the command-line install in, and so does `jason update apply` run again after it.");
        }

        return (NewPlace(places), null, null);
    }

    /// <summary>Where an application is looked for: on macOS this account's own folder first, then the shared one.</summary>
    private IEnumerable<string> Places(SetupPlaces places) =>
        places.Platform is SetupSystem.Windows
            ? [WindowsApplication(places)]
            : [Path.Combine(places.Home, "Applications", "Jason.app"), Path.Combine(System.SharedApplications, "Jason.app")];

    /// <summary>
    /// Where a new application goes: on Windows this account's programs folder; on macOS the shared folder where this account
    /// may write there, as the application's updater needs, else the account's own.
    /// </summary>
    private string NewPlace(SetupPlaces places) =>
        places.Platform is SetupSystem.Windows
            ? WindowsApplication(places)
            : Directory.Exists(System.SharedApplications) && System.MayWriteInto(System.SharedApplications)
                ? Path.Combine(System.SharedApplications, "Jason.app")
                : Path.Combine(places.Home, "Applications", "Jason.app");

    private static string WindowsApplication(SetupPlaces places) =>
        Path.Combine(places.LocalApplicationData ?? places.Home, "Programs", "jason-desktop");

    /// <summary>The jason an application at <paramref name="application"/> carries; null for no application.</summary>
    private static string? Carried(SetupPlaces places, string? application) =>
        application is null
            ? null
            : places.Platform is SetupSystem.Windows
                ? Path.Combine(application, "resources", "bundle", "jason.exe")
                : Path.Combine(application, "Contents", "Resources", "bundle", "jason");

    // --- downloads ---------------------------------------------------------------------------------------------

    private async Task<byte[]> FetchSmallAsync(HttpClient client, Uri address, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(StallWait);
        return await ReleaseFiles.FetchAsync(
            client,
            address,
            ReleaseFiles.MaxChecksumsBytes,
            $"a release's {ReleaseAssets.Checksums}",
            StallWait,
            refused => new MoveDownloadException(refused),
            answered => new MoveDownloadException(answered),
            cancellationToken,
            deadline.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// One file of the release streamed into <paramref name="target"/> and hashed as it arrives: within <see cref="DownloadWait"/>
    /// whole, and never <see cref="StallWait"/> without a byte. The file is written beside its name and renamed onto it once
    /// whole. Its SHA-256, lower-case hex.
    /// </summary>
    private async Task<string> FetchToFileAsync(HttpClient client, Uri address, string target, CancellationToken cancellationToken)
    {
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        whole.CancelAfter(DownloadWait);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(whole.Token);
        stall.CancelAfter(StallWait);
        var partial = target + ".download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            UpdateFeed.SayWhoAsks(request);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new MoveDownloadException(response.StatusCode == HttpStatusCode.NotFound
                    ? $"{address} answered 404: the release publishes no Jason Desktop for this system."
                    : string.Create(CultureInfo.InvariantCulture, $"{address} answered {(int)response.StatusCode}."));
            }

            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            {
                throw new MoveDownloadException($"{address} is larger than any Jason Desktop is.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var body = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var chunk = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(chunk, stall.Token).ConfigureAwait(false)) > 0)
                {
                    stall.CancelAfter(StallWait);
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        throw new MoveDownloadException($"{address} is larger than any Jason Desktop is.");
                    }

                    hash.AppendData(chunk, 0, read);
                    await file.WriteAsync(chunk.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                }
            }

            File.Move(partial, target, overwrite: true);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Discard(partial);
            throw new MoveDownloadException(whole.IsCancellationRequested
                ? string.Create(CultureInfo.InvariantCulture, $"{address} did not arrive within {DownloadWait.TotalMinutes:F0} minutes.")
                : string.Create(CultureInfo.InvariantCulture, $"{address} stalled: nothing arrived for {StallWait.TotalSeconds:F0} seconds."));
        }
        catch (Exception failed) when (failed is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Discard(partial);
            throw new MoveDownloadException($"{address} could not be downloaded: {OneLine(failed.Message)}");
        }
        catch (MoveDownloadException)
        {
            Discard(partial);
            throw;
        }
    }

    // --- the record --------------------------------------------------------------------------------------------

    private void Begin(string step) => Write(_record! with { State = MoveStates.Moving, Doing = step, Reason = null });

    private void Done(string step, string? application = null) =>
        Write(_record! with { Step = step, Doing = null, Application = application ?? _record!.Application });

    private UpdateMoveResponse End(string state, string? reason, string? application = null)
    {
        Write(_record! with { State = state, Doing = null, Reason = reason, Application = application ?? _record!.Application });
        if (reason is not null)
        {
            Say(state == MoveStates.Done ? reason : $"{state} — {OneLine(reason)}");
        }
        else if (state == MoveStates.Done)
        {
            Say($"done: {_record!.From} is in Jason Desktop at {_record.Application}");
        }

        return Response(_record!);
    }

    /// <summary>A move that never took the lock writes no record: what is recorded is another's, or nothing was begun.</summary>
    private UpdateMoveResponse Unrecorded(string state, string reason)
    {
        Say($"{state} — {OneLine(reason)}");
        return new UpdateMoveResponse(state, null, null, reason);
    }

    private void Write(MoveRecord record)
    {
        _record = record with { UpdatedAt = clock.GetUtcNow() };
        try
        {
            _record.Write(update.Move);
            Writes?.Invoke(_record);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            // The move goes on: the record is what status reads, and its own document still says where the move stands.
            env.Error.WriteLine($"The record of the move could not be written at {update.Move}: {refused.Message}");
        }
    }

    private static UpdateMoveResponse Response(MoveRecord record) => new(record.State, record.Step, record.Application, record.Reason);

    private string Doing() => _record?.Doing is { } doing ? $"at '{doing}'" : "beginning";

    private void Say(string line) => Lines.Add($"move: {line}");

    // --- small things --------------------------------------------------------------------------------------------

    private static string Elevated(SetupSystem platform) =>
        (platform is SetupSystem.Windows
            ? "This jason runs as an administrator, elevated above this account's ordinary rights, or as another account than the one signed in to this session"
            : "This jason runs as root")
        + ", and a move begun from here would install Jason Desktop and start the runtime the same way, or into another account's profile, so the command-line install was not moved and nothing was changed. "
        + (platform is SetupSystem.Windows
            ? "Run `jason update apply` from an ordinary terminal of the account signed in here, not run as an administrator, to carry the move on."
            : "Run `jason update apply` from an ordinary terminal, without sudo, to carry the move on.");

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Where(item => item?.GetValueKind() == JsonValueKind.String).Select(item => item!.GetValue<string>()) : [];

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool SamePath(string one, string other, SetupPlaces places) =>
        string.Equals(Path.GetFullPath(one), Path.GetFullPath(other), places.Platform is SetupSystem.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void Discard(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // A download nothing runs: the next move writes over it.
        }
    }

    private static void TryDeleteTree(string folder)
    {
        try
        {
            Files.DirectoryTrees.Delete(folder);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // A staging folder of a dot name beside the application's place: nothing reads it.
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // Left inside the one that appeared: said by the pending sentence's place.
        }
    }
}

/// <summary>A download the move could not make, in a sentence that names the address.</summary>
public sealed class MoveDownloadException(string message) : Exception(message);
