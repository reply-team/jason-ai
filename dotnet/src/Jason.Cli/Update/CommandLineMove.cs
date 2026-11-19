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
/// setup that has not ended within its bound do. Pending is never failed: <c>jason update apply</c> carries it on. The one
/// exception is Linux's deb, a package of the system's: one older than this version is upgraded by apt, as the system
/// upgrades every package.
/// </para>
/// <para>
/// <b>One at a time, under the update's lock</b>, held for the whole move. The setup is given the move's marker
/// (<see cref="MoveMarker"/>) and no nonce: its own <c>jason install</c> does nothing while the record stands not done, even
/// in a setup the move left running, and the move runs the application's routine itself once the installer has returned,
/// handing it the lock.
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

    /// <summary>The package a Linux release publishes, which apt installs where there is apt.</summary>
    public const string DebAsset = "jason-desktop-linux-x64.deb";

    /// <summary>The AppImage a Linux release publishes, which goes where there is no apt.</summary>
    public const string AppImageAsset = AppImageCopy.FileName;

    /// <summary>A download is at most this large: an installer is a few hundred megabytes at most.</summary>
    internal const long MaxDownloadBytes = 1024L * 1024 * 1024;

    /// <summary>What the move asks of the operating system: the environment's (<see cref="CliEnvironment.Moves"/>).</summary>
    private IMoveSystem System { get; set; } = null!;

    /// <summary>How long a download is given whole.</summary>
    internal TimeSpan DownloadWait { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a download may go without a byte before it counts as stalled.</summary>
    internal TimeSpan StallWait { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long the Windows setup, or on Linux apt's install of the package with the password it asks, is waited for. A
    /// Windows setup that has not ended by then is left running, never killed.
    /// </summary>
    internal TimeSpan SetupWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the application's <c>jason install</c> is waited for: its own waits are a minute's drain and the stop's.</summary>
    internal TimeSpan RoutineWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a runtime started from the application's jason is waited for.</summary>
    internal TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(60);

    internal TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How often a download says how far it has come, on standard error.</summary>
    internal TimeSpan ProgressEvery { get; init; } = TimeSpan.FromSeconds(15);

    private static long Megabytes(long bytes) => bytes / (1024 * 1024);

    /// <summary>Called with each record once it is written, as the update's own <c>Writes</c> is: where a test stands a kill.</summary>
    internal Action<MoveRecord>? Writes { get; init; }

    /// <summary>
    /// What loads every assembly this process may still need, before the application's routine replaces or renames the file
    /// it started from: <see cref="AssemblyWarmUp.Run"/> outside a test, which sees when it ran.
    /// </summary>
    internal Action WarmUp { get; init; } = AssemblyWarmUp.Run;

    /// <summary>What the move did, one line each under <c>move</c>, for the update's document.</summary>
    public List<string> Lines { get; } = [];

    private IRuntimeProcessControl Processes => env.Processes ?? RuntimeProcessControl.Instance;

    private MoveRecord? _record;

    /// <summary>
    /// Windows: the publisher Jason Desktop is held to, read at the look: a test build's named signer, else the verified signer
    /// of this jason; null before then and on macOS.
    /// </summary>
    private string? _publisher;

    /// <summary>The record a run before this one left, of this same install; null where there was none.</summary>
    private MoveRecord? _before;

    /// <summary>Linux: the form of Jason Desktop the install moves into, decided at the look; null before then and elsewhere.</summary>
    private LinuxForm? _linux;

    /// <summary>Linux, an AppImage: the version the file is, once read or placed; the copy of its jason is the one that sets up.</summary>
    private SemanticVersion? _appImageVersion;

    private enum LinuxForm
    {
        Deb,
        AppImage,
    }

    /// <summary>Linux: the program Jason Desktop runs as, from the deb's folder or from an AppImage's mount.</summary>
    private const string LinuxProgram = "jason-desktop-app";

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
            // A system that is none of the three: the command-line install is the installation there, and stays it.
            return new UpdateMoveResponse(
                MoveStates.NotApplicable,
                null,
                null,
                "Jason Desktop takes a command-line install in on macOS and Windows only; on this system the command line is the installation, and nothing was moved.");
        }

        var old = Path.GetFullPath(ledger.InstallPath);
        try
        {
            _before = MoveRecord.Read(update.Move);
        }
        catch (MoveRecordUnreadable)
        {
            // Written over below, under the lock.
        }

        if (_before is { State: MoveStates.Done } done && SamePath(done.From, old, places))
        {
            if (done.DoneAndStanding(places.Platform is SetupSystem.Windows))
            {
                Say($"{old} was moved into Jason Desktop already, at {done.Application}");
                return Response(done);
            }

            // Done once, and no longer so: the application is gone, or the install is a command-line install again. A move to
            // make again, whose record is written over below, under the lock.
            Say($"{old} was recorded as moved into Jason Desktop at {done.Application}, which no longer stands, so it is moved again");
            _before = null;
        }

        if (_before is not null && !SamePath(_before.From, old, places))
        {
            _before = null;
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
            // The marker an earlier run gave a setup it may have left running is kept, so that setup's own `jason install` still
            // finds it naming a record that stands not done.
            var marker = _before is { Marker.Length: > 0 } earlier ? earlier.Marker : Guid.NewGuid().ToString("N");
            _record = new MoveRecord(ledger.ToVersion.ToString(), old, MoveStates.Moving, null, null, null, null, feed.AbsoluteUri, clock.GetUtcNow(), marker);
            try
            {
                RetiredSweep.Sweep(places, old);
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
                "This is an Intel Mac, for which Jason Desktop is not offered yet, so the command-line install was not moved and nothing was changed; it answers as jason as it did.",
                waits: "Jason Desktop is offered for Intel Macs");
        }

        // Windows: the publisher Jason Desktop is held to is whoever signed this jason, its own signature verified by the routine
        // that verifies the installer; a test build's named signer comes first. Unverified, nothing is fetched or looked at.
        if (places.Platform is SetupSystem.Windows)
        {
            _publisher = MoveSignatures.TestSigner(env.Variables);
            if (_publisher is null)
            {
                var (own, why) = System.WindowsSigner(System.RunningExecutable);
                _publisher = why is null ? own : null;
                if (_publisher is null)
                {
                    var page = $"{PublicRepository.Url}/releases/tag/v{version}";
                    return End(
                        MoveStates.Failed,
                        $"This jason, {System.RunningExecutable}, does not verify as signed: {why ?? "its signer names no one"}. The move holds Jason Desktop's installer to whoever signed the jason that asks for it, so it moved nothing: nothing was downloaded or changed, and the command-line install answers as jason as it did. Install Jason Desktop yourself from {page}: its first start takes the command-line install in.",
                        waits: $"Jason Desktop is installed from {page}");
                }
            }

            Say($"checked this jason's own signature: Jason Desktop is held to {_publisher}");
        }

        var found = places.Platform is SetupSystem.Linux ? await LookOnLinuxAsync(places, version, cancellationToken).ConfigureAwait(false) : Look(places);
        if (found.Refusal is { } refusal)
        {
            return End(MoveStates.Pending, refusal, application: found.Application, waits: found.Waits);
        }

        Done(MoveSteps.Looked, found.Application);
        if (found.Version is { } there)
        {
            if (_linux is LinuxForm.AppImage && await PrepareAppImageAsync(places, found.Application, there, cancellationToken).ConfigureAwait(false) is { } unprepared)
            {
                return End(MoveStates.Failed, unprepared);
            }

            // Held to the publisher before anything of it runs, as a new one is.
            if (places.Platform is SetupSystem.MacOs
                && await System.MacSignatureRefusalAsync(found.Application, MoveSignatures.RequirementInForce(env.Variables), cancellationToken).ConfigureAwait(false) is { } unsigned)
            {
                env.Error.WriteLine(unsigned);
                return End(MoveStates.Failed, $"The Jason.app at {found.Application} does not verify as signed by the team that signs Jason, so its jason was not run and nothing was changed ({OneLine(unsigned)}). `jason update apply` carries the move on.");
            }

            if (places.Platform is SetupSystem.Windows
                && MoveSignatures.Refusal(System, Carried(places, found.Application)!, _publisher!) is { } unsignedHere)
            {
                return End(MoveStates.Failed, $"The jason that Jason Desktop at {found.Application} carries was not run: {unsignedHere}. Nothing was changed. `jason update apply` carries the move on.");
            }

            Say($"Jason Desktop {there} is at {found.Application} already, so it is kept as it is and nothing is downloaded");
            Done(MoveSteps.Fetched);
            Done(MoveSteps.Verified);
            Done(MoveSteps.Placed);
        }
        else
        {
            if (_linux is LinuxForm.AppImage && AppImageRefusal() is { } cannot)
            {
                return End(MoveStates.Pending, cannot.Reason, waits: cannot.Waits);
            }

            var installed = await InstallAsync(places, version, found.Application, feed, cancellationToken).ConfigureAwait(false);
            if (installed is not null)
            {
                return installed;
            }
        }

        // --- the application's own routine, under this move's lock ---
        var carried = Carried(places, _record!.Application)!;
        Begin(MoveSteps.SetUp);

        // Before the routine: it replaces or renames the file this process started from, and the start, the record's end
        // and the answer printed after it may each load an assembly from that path.
        WarmUp();
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
    /// The application installed where none is: downloaded, held to its line in checksums.txt and, on Windows and macOS, to
    /// the publisher's signature; on Windows its setup run silently and waited for, on macOS put in place by one rename, on
    /// Linux the deb installed by apt or the AppImage put in place by one rename. Null where it is in place; the move's answer
    /// where it stopped.
    /// </summary>
    private async Task<UpdateMoveResponse?> InstallAsync(SetupPlaces places, SemanticVersion version, string application, Uri feed, CancellationToken cancellationToken)
    {
        var windows = places.Platform is SetupSystem.Windows;
        var asset = _linux switch
        {
            LinuxForm.Deb => DebAsset,
            LinuxForm.AppImage => AppImageAsset,
            _ => windows ? WindowsAsset : MacAsset,
        };
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
            if (MoveSignatures.Refusal(System, download, _publisher!) is { } unsigned)
            {
                Discard(download);
                return End(MoveStates.Failed, $"{asset} was not run: {unsigned}. Nothing was installed or changed. `jason update apply` carries the move on.");
            }

            Done(MoveSteps.Verified);
            return await RunSetupAsync(places, version, application, download, cancellationToken).ConfigureAwait(false);
        }

        return _linux switch
        {
            LinuxForm.Deb => await PlaceDebAsync(places, version, application, download, cancellationToken).ConfigureAwait(false),
            LinuxForm.AppImage => await PlaceAppImageAsync(places, version, application, download, cancellationToken).ConfigureAwait(false),
            _ => await PlaceOnMacAsync(version, application, download, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Linux, the deb: the verified package installed by apt with the one password a system package needs, asked as the
    /// install line asks it, within the setup's bound. A refusal changes nothing and leaves the move pending, saying why and
    /// what carries it on; the package's own scripts write Jason's apt source, so later updates come with the system's.
    /// </summary>
    private async Task<UpdateMoveResponse?> PlaceDebAsync(SetupPlaces places, SemanticVersion version, string application, string package, CancellationToken cancellationToken)
    {
        Done(MoveSteps.Verified);
        Begin(MoveSteps.Placed);
        PackageRun run;
        try
        {
            run = await System.InstallPackageAsync(package, SetupWait, Progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception unstarted) when (unstarted is Win32Exception or IOException or InvalidOperationException)
        {
            return End(MoveStates.Failed, $"apt could not be started to install {DebAsset} ({OneLine(unstarted.Message)}), so nothing was installed or changed. `jason update apply` carries the move on.");
        }
        finally
        {
            Discard(package);
        }

        if (run.ExitCode != 0)
        {
            if (run.Said.Trim() is { Length: > 0 } said)
            {
                env.Error.WriteLine(said);
            }

            var (state, reason) = PackageRefusal(run);
            return End(state, reason);
        }

        if (Carried(places, application) is not { } carried || CarriedExecutable.Of(carried)?.Version != version.ToString())
        {
            return End(MoveStates.Failed, $"apt installed {DebAsset}, and {Carried(places, application)} is not Jason {version}; the command-line install was not touched. `jason update apply` carries the move on.");
        }

        Done(MoveSteps.Placed, application);
        Say($"installed Jason Desktop {version} to {application} with apt");
        return null;
    }

    /// <summary>
    /// Linux, the deb: what an install by apt that did not succeed comes to, by <see cref="PackageRun.Refusal"/>. Pending where
    /// nothing was installed and a later run carries the move on; failed where apt itself refused.
    /// </summary>
    internal static (string State, string Reason) PackageRefusal(PackageRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Refusal switch
        {
            AptRefusal.NoPassword => (MoveStates.Pending, "Moving into Jason Desktop needs an administrator's password once, and none was given; the command line is unchanged, and nothing was installed. `jason update apply` asks again; where this account is not an administrator, an administrator of this computer can type theirs."),
            AptRefusal.NoSession => (MoveStates.Pending, "Jason Desktop is installed from a system package here, which needs your password once, and this session cannot ask for it, so nothing was installed and the command line is unchanged. Run `jason update apply` in a terminal of your desktop to carry the move on."),
            AptRefusal.DialogFailed => (MoveStates.Pending, "The system's password dialog failed, so Jason Desktop was not installed and the command line is unchanged. Sign out and back in, or restart, then run `jason update apply` to carry the move on."),
            AptRefusal.LockHeld => (MoveStates.Pending, "Another installation, the system's own updates likely, held the package system for ten minutes, so Jason Desktop was not installed and the command line is unchanged. Run `jason update apply` again in a few minutes to carry the move on."),
            _ => (MoveStates.Failed, string.Create(CultureInfo.InvariantCulture, $"apt did not install Jason Desktop's package: it exited with {run.ExitCode}{(run.LastLine is { } last ? $" and said: {last}" : string.Empty)}. `sudo dpkg --configure -a` finishes a package left half installed, and `sudo apt purge jason-desktop` takes it off; the command-line install was not touched. `jason update apply` carries the move on.")),
        };
    }

    /// <summary>
    /// Linux, an AppImage: the verified file copied beside its place, made executable and read for its version by its own
    /// runtime, then put in place by one rename into an empty place only; then what the install line writes beside it
    /// (<see cref="PrepareAppImageAsync"/>). No root at any step.
    /// </summary>
    private async Task<UpdateMoveResponse?> PlaceAppImageAsync(SetupPlaces places, SemanticVersion version, string application, string download, CancellationToken cancellationToken)
    {
        var place = Path.GetDirectoryName(application)!;
        var stage = Path.Combine(place, $".jason-move.{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(place);

            // What a move killed while it staged left beside the place: only a file of this move's own name, under the lock.
            foreach (var left in Directory.EnumerateFiles(place, ".jason-move.*"))
            {
                if (Path.GetFileName(left) is { Length: 44 } name && name[12..].All(char.IsAsciiHexDigitLower) && !File.GetAttributes(left).HasFlag(FileAttributes.ReparsePoint))
                {
                    Discard(left);
                }
            }

            File.Copy(download, stage);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            Discard(download);
            Discard(stage);
            return End(MoveStates.Failed, $"{AppImageAsset} could not be put in {place} ({OneLine(refused.Message)}); nothing was installed or changed. `jason update apply` carries the move on.");
        }

        try
        {
            if (await AppImageVersionAsync(stage, cancellationToken).ConfigureAwait(false) != version)
            {
                return End(MoveStates.Failed, $"The {AppImageAsset} of the release is not Jason {version}; nothing was installed or changed. `jason update apply` carries the move on.");
            }

            Done(MoveSteps.Verified);
            Begin(MoveSteps.Placed);
            if (File.Exists(application) || Directory.Exists(application))
            {
                return End(MoveStates.Pending, $"{application} appeared while the move downloaded Jason Desktop, so it is left as it was and nothing was installed; `jason update apply` carries the move on into it.", application: application);
            }

            try
            {
                File.Move(stage, application);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                return End(MoveStates.Failed, $"{application} could not be put in place ({OneLine(refused.Message)}); nothing was installed or changed. `jason update apply` carries the move on.");
            }
        }
        finally
        {
            Discard(download);
            Discard(stage);
        }

        _appImageVersion = version;
        if (await PrepareAppImageAsync(places, application, version, cancellationToken).ConfigureAwait(false) is { } unprepared)
        {
            return End(MoveStates.Failed, unprepared, application: application);
        }

        Done(MoveSteps.Placed, application);
        Say($"installed Jason Desktop {version} to {application}");
        return null;
    }

    /// <summary>
    /// Linux, an AppImage: what the install line writes beside the file, made here as there. The copy of the jason it carries
    /// under the data directory, for its version, which sets the machine up; then the menu entry, which starts the file where it
    /// is and is hidden once that file is gone (<c>TryExec</c>), and the icons, in the account's data folder. Null where all of it
    /// is there; else why not.
    /// </summary>
    private async Task<string?> PrepareAppImageAsync(SetupPlaces places, string file, SemanticVersion version, CancellationToken cancellationToken)
    {
        if (!await AppImageCopy.MakeAsync(file, Bundles, version, System.ExtractAppImageAsync, cancellationToken).ConfigureAwait(false))
        {
            return $"The jason Jason Desktop carries could not be copied out of {file} to {Path.GetDirectoryName(AppImageCopy.CopyOf(Bundles, version))}, so nothing could set this machine up; the command-line install was not touched. `jason update apply` carries the move on.";
        }

        var share = places.DataHome ?? Path.Combine(places.Home, ".local", "share");
        var reading = Scratch();
        try
        {
            Directory.CreateDirectory(reading);
            var root = Path.Combine(reading, "squashfs-root");
            var carriedEntry = Path.Combine(root, "jason-desktop-app.desktop");
            if (!await System.ExtractAppImageAsync(file, "jason-desktop-app.desktop", reading, cancellationToken).ConfigureAwait(false) || !File.Exists(carriedEntry))
            {
                return $"Jason Desktop's menu entry could not be read out of {file}; the command-line install was not touched. `jason update apply` carries the move on.";
            }

            var entry = new List<string>();
            foreach (var line in File.ReadAllLines(carriedEntry))
            {
                if (line.StartsWith("TryExec=", StringComparison.Ordinal))
                {
                    continue;
                }

                entry.Add(line.StartsWith("Exec=", StringComparison.Ordinal) ? $"Exec=\"{file}\" %U" : line);
                if (line == "[Desktop Entry]")
                {
                    entry.Add($"TryExec={file}");
                }
            }

            var applications = Directory.CreateDirectory(Path.Combine(share, "applications")).FullName;
            File.WriteAllText(Path.Combine(applications, "jason-desktop.desktop"), string.Join('\n', entry) + "\n");

            // The icons are the entry's look: a file the runtime reads none out of is still started from the menu.
            var hicolor = Path.Combine(root, "usr", "share", "icons", "hicolor");
            if (await System.ExtractAppImageAsync(file, "usr/share/icons/hicolor/*/apps/jason-desktop-app.png", reading, cancellationToken).ConfigureAwait(false) && Directory.Exists(hicolor))
            {
                foreach (var size in Directory.EnumerateDirectories(hicolor))
                {
                    var icon = Path.Combine(size, "apps", "jason-desktop-app.png");
                    if (File.Exists(icon))
                    {
                        var apps = Directory.CreateDirectory(Path.Combine(share, "icons", "hicolor", Path.GetFileName(size), "apps")).FullName;
                        File.Copy(icon, Path.Combine(apps, "jason-desktop-app.png"), overwrite: true);
                    }
                }
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return $"Jason Desktop's menu entry could not be written under {share} ({OneLine(refused.Message)}); the command-line install was not touched. `jason update apply` carries the move on.";
        }
        finally
        {
            TryDeleteTree(reading);
        }

        return null;
    }

    /// <summary>Linux, an AppImage: the version the file carries, read out of it by its own runtime; null where none can be read.</summary>
    private async Task<SemanticVersion?> AppImageVersionAsync(string file, CancellationToken cancellationToken)
    {
        var reading = Scratch();
        try
        {
            Directory.CreateDirectory(reading);
            return await System.ExtractAppImageAsync(file, "resources/bundle/version.json", reading, cancellationToken).ConfigureAwait(false)
                && CarriedExecutable.Of(Path.Combine(reading, "squashfs-root", "resources", "bundle", "jason")) is { } named
                && SemanticVersion.TryParse(named.Version, out var carried)
                    ? carried
                    : null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            TryDeleteTree(reading);
        }
    }

    /// <summary>Linux, an AppImage: why one cannot run here, asked before anything is downloaded, as the install line asks; null where it can.</summary>
    private (string Reason, string? Waits)? AppImageRefusal()
    {
        if (!System.UserNamespacesAllowed())
        {
            return (
                "This system does not let Jason Desktop run in its sandbox (user namespaces are refused), so the command line stays your installation: nothing was downloaded or changed, and it answers as jason as it did. On Arch this is the linux-hardened kernel, or a setting that turns user namespaces off.",
                "this system lets applications make user namespaces");
        }

        return System.FuseMounts()
            ? null
            : ("Jason Desktop needs FUSE 3 here; install the fuse3 package, then run `jason update apply`. Nothing was downloaded or changed, and the command-line install answers as jason as it did.", null);
    }

    /// <summary>A folder of its own under the move's downloads, for what an AppImage's runtime reads out of it.</summary>
    private string Scratch() => Path.Combine(update.MoveDownloads, $".read-{Guid.NewGuid():N}");

    /// <summary>Linux: the folder of the copies an AppImage's jason runs as.</summary>
    private string Bundles => AppImageCopy.BundlesUnder(env.Paths.Root);

    /// <summary>
    /// Windows: the setup run silently into the application's place and waited for, within its bound; the place looked at
    /// again first, as the install script does at its last moment. Its own install step, under the move's marker, does
    /// nothing: the routine is the move's to run.
    /// </summary>
    private async Task<UpdateMoveResponse?> RunSetupAsync(SetupPlaces places, SemanticVersion version, string application, string setup, CancellationToken cancellationToken)
    {
        Begin(MoveSteps.Placed);
        if (Look(places) is { } again && (again.Refusal is not null || again.Version is not null))
        {
            // An application appeared while the setup was downloaded and checked: its own, never installed over.
            return again.Refusal is { } refusal
                ? End(MoveStates.Pending, refusal, application: again.Application, waits: again.Waits)
                : End(MoveStates.Pending, $"Jason Desktop {again.Version} appeared at {again.Application} while the move downloaded its installer, so nothing was installed over it; `jason update apply` carries the move on into it.", application: again.Application);
        }

        Say($"installing Jason Desktop {version} to {application}");
        SetupRun run;
        try
        {
            run = await System.RunSetupAsync(setup, application, _record!.Marker!, SetupWait, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception unstarted) when (unstarted is Win32Exception or IOException or InvalidOperationException)
        {
            return End(MoveStates.Failed, $"{setup} could not be started ({OneLine(unstarted.Message)}), so nothing was installed or changed. `jason update apply` carries the move on.");
        }

        if (!run.Ended)
        {
            return End(MoveStates.Pending, string.Create(CultureInfo.InvariantCulture, $"The installer of Jason Desktop {version} has not ended within {SetupWait.TotalMinutes:F0} minutes, so it was left running, never ended. Under the move's marker its own setup of this machine does nothing: the command-line install answers as jason as it did, and its runtime runs as it did. Once the installer has ended, `jason update apply` carries the move on into the application it installed."));
        }

        switch (run.ExitCode)
        {
            case 0:
                break;
            case SetupExits.ProcessRunsFromPlace:
                return End(MoveStates.Failed, $"The installer stopped before it replaced a file: a Jason process still runs from {application}, a runtime that did not stop say, or its look for one failed and could not tell. Stop a process that runs from there, if one does, then run `jason update apply` to carry the move on; the command-line install was not touched.");
            case SetupExits.StoppedBeforeInstalling:
                return End(MoveStates.Failed, $"The installer stopped before Jason Desktop {version} was installed: another installer of Jason Desktop was running at the same time, or the uninstaller of the Jason Desktop already installed refused, a file of it held, and left that version as it was. The command-line install was not touched. Once no other installer of Jason Desktop runs and Jason Desktop is quit, run `jason update apply` to carry the move on.");
            case SetupExits.ApplicationRunning:
                return End(MoveStates.Pending, $"The installer of Jason Desktop {version} found Jason Desktop running at {application}, in its window or in the tray, or its look for it failed and could not tell whether it runs, and a silent installer then changes nothing and ends nothing, so Jason Desktop {version} was not installed; the command-line install answers as jason as it did. Quit Jason Desktop if it runs, then run `jason update apply` again to carry the move on.");
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

            if (await System.MacSignatureRefusalAsync(staged, MoveSignatures.RequirementInForce(env.Variables), cancellationToken).ConfigureAwait(false) is { } unsigned)
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
    /// application is never replaced, renamed or closed from a terminal: one whose window is open, whatever its version, one
    /// older than <see cref="FirstSettingUp"/> and one whose version cannot be read are refusals, and the last two wait on what
    /// no command typed here changes (<c>Waits</c>). The one exception is a Windows setup this move began at that very place,
    /// killed or left before it ended: where nothing runs from it, the setup is run there again.
    /// </summary>
    private (string Application, SemanticVersion? Version, string? Refusal, string? Waits) Look(SetupPlaces places)
    {
        // The setup this move downloaded, left running by an earlier run past its bound or after a kill, may be writing the
        // application now: nothing at its place is used, reused or installed over until it has ended.
        if (places.Platform is SetupSystem.Windows && System.Runs(Path.Combine(update.MoveDownloads, WindowsAsset)))
        {
            var place = WindowsApplication(places);
            return (
                place,
                null,
                $"The installer of Jason Desktop this move began earlier is still running, installing to {place}, so nothing it may be writing there is used and nothing was changed; the command-line install answers as jason as it did. Once that installer has ended, `jason update apply` carries the move on.",
                null);
        }

        foreach (var application in Places(places))
        {
            var windows = places.Platform is SetupSystem.Windows;
            if (!ApplicationStands(application, windows))
            {
                continue;
            }

            // The program a window runs: Jason.exe, or the bundle's own executable.
            var program = windows ? Path.Combine(application, "Jason.exe") : Path.Combine(application, "Contents", "MacOS", "Jason");
            var carried = Carried(places, application)!;
            var placed = CarriedExecutable.Of(carried);
            SemanticVersion version = default;
            var readable = placed is not null && SemanticVersion.TryParse(placed.Version, out version);
            if (System.Runs(program))
            {
                return (
                    application,
                    null,
                    $"Jason Desktop{(readable ? " " + version : string.Empty)} at {application} has its window open, and the move never sets the machine up under an open window, so nothing was changed; the command-line install answers as jason as it did. Close Jason Desktop, then `jason update apply` carries the move on.",
                    null);
            }

            if (readable && (version with { PreRelease = null }) >= FirstSettingUp)
            {
                return (application, version, null, null);
            }

            if (readable)
            {
                return (
                    application,
                    null,
                    $"Jason Desktop {version} at {application} is older than {FirstSettingUp}, so it cannot be set up from a terminal and the move of the command-line install waits; nothing was changed, and the command-line install answers as jason as it did. Update Jason Desktop from its own menu (Check for Updates): its first start of {FirstSettingUp} or later takes the command-line install in, and so does `jason update apply` run again after it.",
                    $"Jason Desktop at {application} is updated to {FirstSettingUp} or later from its own menu");
            }

            if (windows && SetupBegunHere(application, carried))
            {
                Say($"the setup this move began at {application} did not end, and nothing runs from it, so it is run there again");
                return (application, null, null, null);
            }

            var page = $"{PublicRepository.Url}/releases/tag/v{_record!.Version}";
            return (
                application,
                null,
                $"Jason Desktop at {application} has no version that can be read, perhaps an install that was interrupted, so the move of the command-line install waits; nothing was changed, and the command-line install answers as jason as it did. Run Jason Desktop's installer from {page} again: then `jason update apply` carries the move on.",
                $"Jason Desktop is installed again at {application} from {page}");
        }

        return (NewPlace(places), null, null, null);
    }

    /// <summary>
    /// Whether the application at <paramref name="application"/>, of no readable version, is what a setup this move began there
    /// left: the move's own record put the application there and never said it was placed, and no process runs from it, the
    /// window, its jason or the setup this move downloaded.
    /// </summary>
    private bool SetupBegunHere(string application, string carried) =>
        _before is { State: not MoveStates.Done, Step: MoveSteps.Verified } before
        && before.Application is { } recorded
        && string.Equals(Path.GetFullPath(recorded), Path.GetFullPath(application), StringComparison.OrdinalIgnoreCase)
        && !System.Runs(Path.Combine(application, "Jason.exe"))
        && !System.Runs(carried)
        && !System.Runs(Path.Combine(update.MoveDownloads, WindowsAsset));

    /// <summary>
    /// Whether an application is at <paramref name="application"/>, by the one look the move and a record of it both take: on
    /// Windows its <c>Jason.exe</c>, so a folder left without it is no application; on macOS the bundle.
    /// </summary>
    internal static bool ApplicationStands(string application, bool windows)
    {
        var there = windows ? Path.Combine(application, "Jason.exe") : application;
        return File.Exists(there) || Directory.Exists(there);
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

    /// <summary>
    /// Linux: the form the install moves into, by the install line's rule — an AppImage already at its place stays the
    /// installation, apt or not; else the deb where there is apt; else an AppImage — and the application already there, or
    /// the place a new one goes. Jason Desktop running, from either form, is a refusal, as an open window is. A package of this
    /// version or later is reused, and an older one is installed over by apt, as the system upgrades its packages; an
    /// AppImage, the person's own file, is reused at the version it is where that is this version or later, and is never
    /// replaced, renamed or deleted from here.
    /// </summary>
    private async Task<(string Application, SemanticVersion? Version, string? Refusal, string? Waits)> LookOnLinuxAsync(SetupPlaces places, SemanticVersion version, CancellationToken cancellationToken)
    {
        var appImage = AppImageCopy.FileOf(AppImageCopy.CopyOf(Bundles, version), places.Home);
        _linux = File.Exists(appImage) || !System.HasApt() ? LinuxForm.AppImage : LinuxForm.Deb;
        var application = _linux is LinuxForm.Deb ? places.DebApplication : appImage;
        if (System.RunsNamed(LinuxProgram))
        {
            return (
                application,
                null,
                "Jason Desktop is running, in its window or in the tray, and the move never sets the machine up while it runs, so nothing was changed; the command-line install answers as jason as it did. Quit Jason Desktop (Quit in its tray menu), then `jason update apply` carries the move on.",
                null);
        }

        if (_linux is LinuxForm.Deb)
        {
            return CarriedExecutable.Of(Carried(places, application)!) is { } placed && SemanticVersion.TryParse(placed.Version, out var packaged) && AtLeast(packaged, version)
                ? (application, packaged, null, null)
                : (application, null, null, null);
        }

        if (!File.Exists(appImage))
        {
            return (application, null, null, null);
        }

        if (await AppImageVersionAsync(appImage, cancellationToken).ConfigureAwait(false) is not { } named)
        {
            return (
                application,
                null,
                $"The file at {appImage} is not a Jason Desktop whose version can be read, so it is left as it is and the move of the command-line install waits; nothing was changed, and the command-line install answers as jason as it did. Move that file away: then `jason update apply` installs Jason Desktop there and carries the move on.",
                $"the file at {appImage} is moved away");
        }

        if (!AtLeast(named, version))
        {
            return (
                application,
                null,
                $"Jason Desktop {named} at {appImage} is older than {version}, so its jason cannot set this machine up for this version and the move of the command-line install waits; nothing was changed, and the command-line install answers as jason as it did. Update Jason Desktop from its own menu (Check for Updates): then `jason update apply` carries the move on.",
                $"Jason Desktop at {appImage} is updated to {version} or later from its own menu");
        }

        _appImageVersion = named;
        return (application, named, null, null);
    }

    /// <summary>Whether <paramref name="found"/> is <paramref name="version"/> or later, by their numbers, a pre-release part ignored.</summary>
    private static bool AtLeast(SemanticVersion found, SemanticVersion version) =>
        (found with { PreRelease = null }) >= (version with { PreRelease = null });

    /// <summary>
    /// The jason an application at <paramref name="application"/> carries; null for no application. On Linux the deb's, or for
    /// an AppImage the copy of its version under the data directory, null until that version is known.
    /// </summary>
    private string? Carried(SetupPlaces places, string? application) =>
        application is null
            ? null
            : places.Platform switch
            {
                SetupSystem.Windows => Path.Combine(application, "resources", "bundle", "jason.exe"),
                SetupSystem.Linux when _linux is LinuxForm.AppImage => _appImageVersion is { } copied ? AppImageCopy.CopyOf(Bundles, copied) : null,
                SetupSystem.Linux => Path.Combine(application, "resources", "bundle", "jason"),
                _ => Path.Combine(application, "Contents", "Resources", "bundle", "jason"),
            };

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

            var size = response.Content.Headers.ContentLength;
            Progress(size is { } known
                ? string.Create(CultureInfo.InvariantCulture, $"downloading {address} ({Megabytes(known)} MB)")
                : $"downloading {address}");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var body = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var chunk = new byte[81920];
                long total = 0;
                int read;
                var said = clock.GetUtcNow();
                while ((read = await body.ReadAsync(chunk, stall.Token).ConfigureAwait(false)) > 0)
                {
                    stall.CancelAfter(StallWait);
                    total += read;
                    if (clock.GetUtcNow() - said >= ProgressEvery)
                    {
                        said = clock.GetUtcNow();
                        Progress(string.Create(CultureInfo.InvariantCulture, $"downloaded {Megabytes(total)}{(size is { } all ? $" of {Megabytes(all)}" : string.Empty)} MB"));
                    }
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

    private void Begin(string step)
    {
        Write(_record! with { State = MoveStates.Moving, Doing = step, Reason = null, Waits = null });

        // One line on standard error at each step, which the 0.48 parent passes through as it comes: minutes of a download,
        // a setup and a routine are never minutes of silence.
        Progress(step switch
        {
            MoveSteps.Looked => "looking for Jason Desktop",
            MoveSteps.Fetched => "downloading Jason Desktop",
            MoveSteps.Verified => _linux is null
                ? "checking Jason Desktop against the release's checksums and its publisher's signature"
                : "checking Jason Desktop against the release's checksums",
            MoveSteps.Placed => "installing Jason Desktop",
            MoveSteps.SetUp => "setting this machine up through Jason Desktop's jason",
            MoveSteps.Started => "starting the runtime from Jason Desktop's jason",
            _ => step,
        });
    }

    /// <summary>A line of progress on standard error; never part of the document.</summary>
    private void Progress(string line) => env.Error.WriteLine($"move: {line}");

    private void Done(string step, string? application = null) =>
        Write(_record! with { Step = step, Doing = null, Application = application ?? _record!.Application });

    private UpdateMoveResponse End(string state, string? reason, string? application = null, string? waits = null)
    {
        Write(_record! with { State = state, Doing = null, Reason = reason, Application = application ?? _record!.Application, Waits = waits });
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
}

/// <summary>A download the move could not make, in a sentence that names the address.</summary>
public sealed class MoveDownloadException(string message) : Exception(message);
