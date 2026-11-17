using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Files;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// What Jason Desktop wrote in a request's file, as the carried verb reads it: the members it acts on.
/// </summary>
/// <param name="State"><c>checking</c>, <c>downloading</c>, <c>downloaded</c>, <c>installing</c>, <c>current</c> or <c>refused</c>.</param>
/// <param name="Reason">At <c>refused</c>, the Desktop's code; null otherwise.</param>
/// <param name="Message">At <c>refused</c>, the Desktop's sentence; null otherwise.</param>
/// <param name="Offered">The version the Desktop's feed offered, where it was read.</param>
/// <param name="Window">Whether a running window took the request.</param>
/// <param name="RuntimeStopped">Whether the Desktop stopped the runtime the application carries for it.</param>
/// <param name="Kept">The copy of the application the Desktop kept aside, where one was.</param>
public sealed record DesktopAnswer(string State, string? Reason, string? Message, string? Offered, bool Window, bool RuntimeStopped, string? Kept)
{
    /// <summary>Whether the state is one a request ends at.</summary>
    public bool IsEnd => State is Installing or Current or Refused;

    /// <summary>The update is downloaded, and the request waits for the verb's go.</summary>
    public const string Downloaded = "downloaded";

    public const string Installing = "installing";

    public const string Current = "current";

    public const string Refused = "refused";

    /// <summary>
    /// The request's file read as the Desktop writes it, or null where there is none, it cannot be read, it is not a whole
    /// JSON object, or it is another request's.
    /// </summary>
    public static DesktopAnswer? Read(string file, string id)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "request") != id
                || Text(root, "state") is not { } state)
            {
                return null;
            }

            return new DesktopAnswer(
                state,
                Text(root, "reason"),
                Text(root, "message"),
                Text(root, "offered"),
                Flag(root, "window"),
                Flag(root, "runtime_stopped"),
                Text(root, "kept"));
        }
        catch (JsonException)
        {
            // Half a file: a reader that meets the Desktop's rename on the way, and reads again.
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>
/// The update of a jason Jason Desktop carries, handed to the Desktop's updater: the half of the contract in
/// <c>docs/release-and-update.md</c> §7, "Handing the update to Jason Desktop", that the command line builds.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape of the command line's own hand-over.</b> The jason being replaced does what only it can do — it plans,
/// asks the Desktop for the update, drains the runtime and steps out of the application's folder — and the new carried
/// jason does the rest from a file that is its own: <c>plugins</c>, <c>started</c>, <c>healthy</c>, <c>complete</c>, the
/// skills, the machine setup and the UI, then the application's own record of the update settled with no window. The caller
/// waits on the process it started and gets one document on both systems.
/// </para>
/// <para>
/// <b>The go.</b> The Desktop is asked first, with <c>--await-go</c>: it downloads the version and its request names
/// <c>downloaded</c>, then it waits for the go. Only then does this verb drain the runtime, stop it, step out of the folder and
/// record itself for the installer's look, within <see cref="GoBound"/>; the go, <c>&lt;data&gt;/update/desktop-&lt;id&gt;.go</c>
/// made by an exclusive create, is the last act, so the record stands before the installer can look. A verb that ends
/// before the go has stopped nothing it does not start again, and the Desktop, given no go in its wait
/// (<see cref="DesktopGoWait"/>), installs nothing.
/// </para>
/// <para>
/// <b>Stepping out of the folder.</b> On Windows the installer refuses while a process runs from the carried path or holds a
/// file in the application's folder, so the carried process moves its own image out, to a folder of the updater's store on
/// the same volume, and goes on running from there (<see cref="MovesItself"/>); Windows still reports the old path for it,
/// so it records its id and start beside the carried file, which the installer's look passes over, and nothing else. Every
/// way it ends where the installer put no new jason at the carried path puts one back first, a copy of its own image. On
/// macOS the application is replaced under a running process without a refusal, and the update goes on from a copy of this
/// jason under the data directory, which the carried process waits for.
/// </para>
/// <para>
/// <b>The ledger is the command line's ledger</b>, written before each step as every update's is, with the steps' own
/// words read for this hand-over: <c>staged</c> the plan, <c>drained</c> the drain at <c>downloaded</c>, <c>stopped</c> the stop
/// and the go; <c>kept</c> the application kept aside and handed to the installer, and <c>swapped</c> the application's
/// <c>version.json</c> naming the new version. No step is added, so every Jason that reads the ledger reads this one. A
/// request that installs nothing puts back the record that was there before.
/// </para>
/// <para>
/// <b>Whoever asked for a stop starts one again.</b> An update that does not complete leaves the runtime as it was found:
/// a drained runtime still running is resumed, and one that was running and is gone is started again from whichever
/// executable is whole — after a cancel without a wait, so the answer comes at once. The one exception is the installer's
/// own time: while it has the update the application's folder is its, and the command that finishes the update starts the
/// runtime once it lands.
/// </para>
/// </remarks>
/// <param name="platform">
/// The system the application is installed on, which says where its executable is; null where Jason Desktop takes no
/// request, on Linux, and then only an update past <c>swapped</c> is finished here.
/// </param>
public sealed class CarriedUpdate(CliEnvironment env, UpdatePaths update, TimeProvider clock, CarriedExecutable carried, SetupSystem? platform)
{
    /// <summary>Everything this half did, in order, for the caller to print.</summary>
    public List<string> Steps { get; } = [];

    /// <summary>How long the application's <c>version.json</c> is waited for once the installer has the update.</summary>
    internal TimeSpan InstallWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a request a running window took is waited for to name an end. A window that fails before it serves the
    /// request holds it for ever, so this is a bound, said when it passes.
    /// </summary>
    internal TimeSpan WindowWait { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a request a running window took is waited for to have a file at all.</summary>
    internal TimeSpan WindowAnswers { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a start of the Desktop of its own is waited for: its own bound for a request that waits for the go, 30 minutes
    /// with the install and 15 for the go, and a margin.
    /// </summary>
    internal TimeSpan DesktopWait { get; init; } = TimeSpan.FromMinutes(50);

    /// <summary>
    /// The longest this verb takes from reading <c>downloaded</c> to making the go: the drain, the stop and the move. Past it,
    /// it gives no go and puts everything back.
    /// </summary>
    internal TimeSpan GoBound { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long Jason Desktop waits for the go once its request names <c>downloaded</c> (<c>goWaitMs</c> in
    /// <c>desktop/src/update-request.ts</c>): <see cref="GoBound"/> and a margin of five minutes.
    /// </summary>
    public static readonly TimeSpan DesktopGoWait = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The drain's own wait inside <see cref="GoBound"/>, whatever <c>--drain-seconds</c> says: what is left of the bound is the
    /// stop's and the move's.
    /// </summary>
    internal TimeSpan DrainAtMost => GoBound > TimeSpan.FromMinutes(4) ? GoBound - TimeSpan.FromMinutes(2) : GoBound / 2;

    /// <summary>How long the application's settling start is waited for before it is ended.</summary>
    internal TimeSpan SettleWait { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How a step of the ledger is written; a test makes one fail.</summary>
    internal Action<UpdateLedger>? Writes { get; init; }

    /// <summary>How this process's image is moved out of the folder; a test makes one that copies and leaves the source.</summary>
    internal Action<string, string> Mover { get; init; } = (from, to) => File.Move(from, to);

    /// <summary>How long a runtime started again is waited for to answer.</summary>
    internal TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How often a file or a runtime is looked at while one of the waits above runs.</summary>
    internal TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A fresh request id for each request: letters and digits, never a path.</summary>
    internal Func<string> NewRequest { get; init; } = () => "cli-" + Guid.NewGuid().ToString("N");

    /// <summary>What loads every assembly this process may still need, before the application it runs from is replaced.</summary>
    internal Action WarmUp { get; init; } = AssemblyWarmUp.Run;

    /// <summary>
    /// Whether this process moves its own image out of the application's folder before the application is started: on
    /// Windows, where it runs from the carried file. Set by the caller, which knows what it runs from.
    /// </summary>
    internal bool MovesItself { get; init; }

    /// <summary>This process as the installer's look reads it: its id, and its start in Windows file time.</summary>
    internal Func<(int Id, long Started)> Self { get; init; } = () =>
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        return (self.Id, self.StartTime.ToFileTimeUtc());
    };

    /// <summary>Where the image is moved for a request, where a test says; else <see cref="MovedImage"/>'s own place.</summary>
    internal Func<string, string>? MovedImageFor { get; init; }

    /// <summary>
    /// The file beside the carried jason that names the one process the installer's look passes over: this one, by its id
    /// and its start, written right before the application is started and taken away once the install has ended.
    /// </summary>
    public const string PidFileName = "jason-update.pid";

    /// <summary>The record for the installer's look: <see cref="PidFileName"/> beside the carried file.</summary>
    public string PidFile => Path.Combine(Path.GetDirectoryName(carried.Executable)!, PidFileName);

    /// <summary>Where this process's image went, once it moved; null before, and on macOS.</summary>
    public string? Moved { get; private set; }

    /// <summary>
    /// Where this process's image is moved for a request: a folder of its own in the updater's store under the local
    /// application data, which is on the application's volume, outside the folder the installer replaces.
    /// </summary>
    public string MovedImage(string id) =>
        MovedImageFor?.Invoke(id)
        ?? Path.Combine(
            env.Setup?.LocalApplicationData is { Length: > 0 } local ? Path.Combine(local, StoreFolder) : Path.Combine(update.Root, "carried"),
            id,
            ReleaseAssets.ExecutableName);

    /// <summary>The store under the local application data, where a request's folder holds the image it moved.</summary>
    private static readonly string StoreFolder = Path.Combine("jason-desktop-updater", "jason-carried");

    /// <summary>The carried jason this update replaces, and the new one finishes it.</summary>
    public CarriedExecutable Carried => carried;

    /// <summary>The application's own executable, which is started with the request; null where there is none to start.</summary>
    public string? Application => platform is { } system ? ApplicationOf(carried.Executable, system) : null;

    /// <summary>Whether there is a Jason Desktop to hand the update to: an application where its carried jason says.</summary>
    public bool HasDesktop => Application is { } application && File.Exists(application);

    /// <summary>The copy of the carried jason under the data directory, which continues once this one has ended.</summary>
    public string Copy => Path.Combine(update.Applier, ReleaseAssets.ExecutableName);

    /// <summary>
    /// The application's executable from the carried jason's path: on Windows <c>Jason.exe</c>, two folders above
    /// <c>resources\bundle\jason.exe</c>; on macOS <c>Jason.app/Contents/MacOS/Jason</c>, beside
    /// <c>Contents/Resources/bundle/jason</c>.
    /// </summary>
    public static string ApplicationOf(string carriedExecutable, SetupSystem platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carriedExecutable);
        var bundle = Path.GetDirectoryName(Path.GetFullPath(carriedExecutable))!;
        var resources = Path.GetDirectoryName(bundle) ?? bundle;
        var above = Path.GetDirectoryName(resources) ?? resources;
        return platform is SetupSystem.Windows
            ? Path.Combine(above, "Jason.exe")
            : Path.Combine(above, "MacOS", "Jason");
    }

    /// <summary>
    /// The folder the application was installed as, which its kept copy is put back in the place of: the install folder on
    /// Windows, <c>Jason.app</c> on macOS.
    /// </summary>
    public static string ApplicationFolderOf(string carriedExecutable, SetupSystem platform)
    {
        // Windows: the folder Jason.exe is in. macOS: Jason.app, two folders above Contents/MacOS/Jason.
        var folder = Path.GetDirectoryName(ApplicationOf(carriedExecutable, platform))!;
        return platform is SetupSystem.Windows ? folder : Path.GetDirectoryName(Path.GetDirectoryName(folder) ?? folder) ?? folder;
    }

    /// <summary>
    /// The address the Desktop reads its own update file from, for a feed this verb was given: the feed's directory where
    /// it is http on the loopback address, which is all the Desktop takes; null for the releases.
    /// </summary>
    public static string? DesktopFeed(Uri feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return feed.Scheme == Uri.UriSchemeHttp && feed.IsLoopback && feed.UserInfo.Length == 0 && feed.Query.Length == 0 && feed.Fragment.Length == 0
            ? new Uri(feed, "./").AbsoluteUri
            : null;
    }

    /// <summary>Where the record that stood before this update is kept, so a refused request can put it back.</summary>
    private string Before => Path.Combine(update.Root, "ledger.before-desktop.json");

    /// <summary>A request's file: <c>&lt;data&gt;/update/desktop-&lt;id&gt;.json</c>.</summary>
    public string RequestFile(string id) => Path.Combine(update.Root, $"desktop-{id}.json");

    /// <summary>A request's go: <c>&lt;data&gt;/update/desktop-&lt;id&gt;.go</c>, beside its file.</summary>
    public string GoFile(string id) => Path.Combine(update.Root, $"desktop-{id}.go");

    /// <summary>Whether the application's <c>version.json</c> names <paramref name="version"/>.</summary>
    public bool Names(SemanticVersion version) =>
        CarriedExecutable.Of(carried.Executable) is { } now && SemanticVersion.TryParse(now.Version, out var named) && named == version;

    /// <summary>
    /// A new update of the carried jason: what the feed offers, held to what this installation is, and the ledger written at
    /// <c>staged</c>, with the record it replaces kept for a refusal to put back.
    /// </summary>
    public async Task<UpdateLedger> PlanAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var address = request.Version is { } pinned ? UpdateFeed.PinnedFor(request.Feed, pinned) : request.Feed;
        using var client = UpdateHttp.Client(env);
        client.Timeout = UpdateFeed.DefaultTimeout;
        var manifest = await new UpdateFeed(client).ReadAsync(address, cancellationToken).ConfigureAwait(false);

        if (request.Version is { } wanted && manifest.Version != wanted)
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The manifest at {address} names {manifest.Version}, and the version asked for is {wanted}: nothing was applied.");
        }

        if (!manifest.IsNewerThan(SemanticVersion.Current) && request.Version is null)
        {
            throw new UpdateException(
                UpdateCodes.UpToDate,
                $"This installation is {SemanticVersion.Current} and the feed offers {manifest.Version}: there is nothing to apply.");
        }

        if (manifest.MinUpgradeFrom is { } floor && SemanticVersion.Current < floor)
        {
            throw new UpdateException(
                UpdateCodes.NotDirectlyApplicable,
                UpdateApplier.PassThrough(manifest.Version, SemanticVersion.Current, floor, pinned: request.Version is not null));
        }

        // No archive of this platform is asked for: the application is the Desktop's to download, and an Intel Mac, whose
        // jason no archive carries, updates the same way.
        var ledger = new UpdateLedger(
            SemanticVersion.Current,
            manifest.Version,
            UpdateStep.Staged,
            clock.GetUtcNow(),
            carried.Executable,
            update.StagedExecutable(manifest.Version),
            update.PreviousExecutable);

        OnDisk("keeping the record of the last update", Before, () =>
        {
            Directory.CreateDirectory(update.Root);
            if (File.Exists(update.Ledger))
            {
                File.Copy(update.Ledger, Before, overwrite: true);
            }
            else
            {
                File.Delete(Before);
            }
        });

        Record(ledger);
        Say(UpdateStep.Staged, $"{manifest.Version}, for Jason Desktop to install");
        return ledger;
    }

    /// <summary>
    /// This jason copied under <c>&lt;data&gt;/update/applier/</c>, which continues the update once this one has ended: the
    /// Windows installer refuses while any process runs from the carried file, and on macOS the file is replaced under it.
    /// </summary>
    public void CopyAside(string? runningAs)
    {
        WarmUp();
        OnDisk("putting a copy of this jason where the update continues from", update.Applier, () =>
        {
            Directory.CreateDirectory(update.Applier);
            if (!Same(Copy, runningAs))
            {
                File.Copy(carried.Executable, Copy, overwrite: true);
            }
        });
    }

    /// <summary>
    /// The update from wherever the ledger stands before <c>swapped</c>, run by the carried process on Windows and by the copy
    /// on macOS, in the order the go hand-shake gives it: the Desktop asked first, to download and wait; then, once its
    /// request names <c>downloaded</c>, the drain, the stop, the move and the record, and the go last; then the wait for the
    /// installer, and <c>swapped</c> recorded once the application's <c>version.json</c> names the new version. Returns the
    /// ledger at <c>swapped</c>.
    /// </summary>
    /// <remarks>
    /// Every way out after the request is in one place (<see cref="SettleOnTheWayOutAsync"/>): a refusal, no answer, an
    /// install that did not land, a ledger that could not be written, a bound that passed, and a cancel, a console's close
    /// among them. Before the go nothing was stopped that is not started again, and before <c>kept</c> the record that stood is
    /// put back; while the installer has the update the folder is the installer's, and once it landed the new jason runs the
    /// runtime.
    /// </remarks>
    public async Task<UpdateLedger> InstallAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(request);

        if (Names(ledger.ToVersion))
        {
            Say(UpdateStep.Swapped, $"the application is {ledger.ToVersion} already");
            return Swapped(ledger);
        }

        _wasLive = Live() is not null;
        _written = ledger;
        var id = NewRequest();
        _goFile = GoFile(id);
        using var bound = new CancellationTokenSource(DesktopWait, clock);
        using var desktop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        try
        {
            return await HandShakeAsync(ledger, request, id, desktop.Token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // The caller's cancel is the reason whatever it surfaced as: a wait it ended, or a start of the Desktop's it ended.
            var cancelled = cancellationToken.IsCancellationRequested;
            await EndTheDesktopStartAsync(desktop).ConfigureAwait(false);
            var tail = await SettleOnTheWayOutAsync(ledger, waits: !cancelled).ConfigureAwait(false);
            if (cancelled)
            {
                throw new UpdateException(
                    UpdateCodes.Cancelled,
                    $"The update to {ledger.ToVersion} was cancelled. {tail}",
                    error,
                    retryable: true);
            }

            if (error is UpdateException refused)
            {
                throw new UpdateException(refused.Code, $"{refused.Message} {tail}", refused.InnerException, refused.Retryable);
            }

            if (error is OperationCanceledException or IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                throw new UpdateException(UpdateCodes.FileRefused, $"The update to {ledger.ToVersion} stopped: {error.Message} {tail}", error, retryable: true);
            }

            throw;
        }
        finally
        {
            TryDelete(_goFile);

            // The folder is the installer's while it has the update: nothing is copied into it then.
            if (!InstallerOwnsTheFolder)
            {
                PutImageBack();
            }
            else
            {
                TryDelete(PidFile);
            }
        }
    }

    /// <summary>
    /// The Desktop's start this verb made, ended where it still runs and nothing was handed to the installer: one that waits
    /// for a go no one will give would hold the application's lock until its own bound, and one past the go would install
    /// what this verb is putting back.
    /// </summary>
    private async Task EndTheDesktopStartAsync(CancellationTokenSource desktop)
    {
        if (_run is not { IsCompleted: false } run || InstallerOwnsTheFolder || _stage == Stage.Landed)
        {
            return;
        }

        await desktop.CancelAsync().ConfigureAwait(false);
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Its end is the process control's; this verb answers all the same.
        }
    }

    private Task<(FinishedCommand? Finished, string? Why)>? _run;

    private async Task<UpdateLedger> HandShakeAsync(UpdateLedger ledger, UpdateRequest request, string id, CancellationToken desktop, CancellationToken cancellationToken)
    {
        var file = RequestFile(id);

        // Before the Desktop is asked anything, so before anything is drained or stopped: what would refuse the move later
        // refuses now. With a runtime of this installation running, its plugin hosts run from the carried file too, so the
        // look for other processes there waits until it has stopped.
        if (MovesItself)
        {
            SameVolume(id);
            if (!_wasLive)
            {
                NoOtherAtTheCarriedPath();
            }
        }

        if (File.Exists(file) || File.Exists(_goFile!))
        {
            throw new UpdateException(
                UpdateCodes.DesktopUnanswered,
                $"A file of the request's id, {(File.Exists(file) ? file : _goFile)}, was there before the request was made, so it is no answer to it.",
                null,
                retryable: true);
        }

        List<string> command = [Application!, "--update-apply", "--to", ledger.ToVersion.ToString(), "--request", id, "--await-go"];
        if (DesktopFeed(request.Feed) is { } feed)
        {
            command.AddRange(["--feed", feed]);
        }

        _stage = Stage.Asked;
        Say("asked", $"Jason Desktop asked to download {ledger.ToVersion} and wait for the go, request {id}");
        var run = _run = RunDesktopAsync(command, desktop);

        var (first, why) = await AnswerAsync(run, file, id, atDownloaded: true, cancellationToken).ConfigureAwait(false);
        if (first is null)
        {
            throw NoAnswer(ledger, why);
        }

        if (first.State == DesktopAnswer.Refused)
        {
            throw RefusedBy(ledger, first);
        }

        if (first.State == DesktopAnswer.Downloaded)
        {
            ledger = await PrepareAsync(ledger, request, id, cancellationToken).ConfigureAwait(false);
            MakeGo(id);
            var (answer, after) = await AnswerAsync(run, file, id, atDownloaded: false, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                throw NoAnswer(ledger, after);
            }

            if (answer.State == DesktopAnswer.Refused)
            {
                throw RefusedBy(ledger, answer);
            }

            first = answer;
        }

        if (first.State == DesktopAnswer.Installing)
        {
            _installingSince = clock.GetUtcNow();
            ledger = ledger.At(UpdateStep.Kept) with { KeptApplication = first.Kept };
            try
            {
                Record(ledger);
            }
            catch (UpdateException unwritten)
            {
                // The installer has the update whatever this file says; the update goes on, and the record stands where it was.
                Say(UpdateStep.Kept, $"the record of the update could not be written at kept ({unwritten.Message}); it stays at '{_written.Step.ToString().ToLowerInvariant()}'");
            }

            Say(UpdateStep.Kept, first.Kept is { } kept
                ? $"Jason Desktop kept the application at {kept} and handed {ledger.ToVersion} to its installer"
                : $"Jason Desktop handed {ledger.ToVersion} to its installer");
            if (first.Kept is { } copy)
            {
                MakeKeptWhole(copy, ledger.FromVersion);
            }
        }
        else
        {
            Say(UpdateStep.Kept, $"Jason Desktop says the application is {ledger.ToVersion} already");
        }

        if (!await InstalledAsync(ledger.ToVersion, cancellationToken).ConfigureAwait(false))
        {
            var named = CarriedExecutable.Of(carried.Executable)?.Version;
            var shipIt = platform is SetupSystem.MacOs ? ShipItSaid() : null;
            throw new UpdateException(
                UpdateCodes.DesktopNotInstalled,
                $"Jason Desktop handed {ledger.ToVersion} to its installer, and after {InstallWait.TotalMinutes:F0} minutes the application's "
                + $"{CarriedExecutable.RecordName} at {carried.Record} names {named ?? "no version"}."
                + (shipIt is null ? string.Empty : $" ShipIt said: {shipIt}")
                + (named is null ? $" Run `jason update apply` again, from {Moved ?? Copy} if jason does not answer." : " Run `jason update apply` again."),
                null,
                retryable: true);
        }

        _stage = Stage.Landed;
        Say(UpdateStep.Swapped, $"the application is {ledger.ToVersion}");
        return Swapped(ledger);
    }

    /// <summary>
    /// At <c>downloaded</c>: the drain, the stop, the move and the record, within <see cref="GoBound"/>, which the Desktop's
    /// wait for the go outlasts by a margin. On Windows this process stops the runtime, an earlier demo's too, since the
    /// Desktop stops one through the carried jason, whose file is moved by then.
    /// </summary>
    private async Task<UpdateLedger> PrepareAsync(UpdateLedger ledger, UpdateRequest request, string id, CancellationToken cancellationToken)
    {
        using var bound = new CancellationTokenSource(GoBound, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        try
        {
            _stage = Stage.Draining;
            ledger = ledger.At(UpdateStep.Drained);
            Record(ledger);
            var drain = new UpdateApplier(env, update, clock, carried.Executable);
            try
            {
                await drain.DrainAsync(ledger, request with { Drain = request.Drain < DrainAtMost ? request.Drain : DrainAtMost }, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                Steps.AddRange(drain.Steps);
            }

            ledger = ledger.At(UpdateStep.Stopped) with { StoppedAt = clock.GetUtcNow() };
            Record(ledger);
            if (MovesItself)
            {
                var said = drain.Steps.Count;
                try
                {
                    ledger = await drain.StopAsync(ledger, linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    Steps.AddRange(drain.Steps.Skip(said));
                }

                await StopDemoRuntimeAsync(linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                Record(ledger);
                NoOtherAtTheCarriedPath();
                StepOut(id);
            }

            // Never a go after a cancel or past the bound.
            linked.Token.ThrowIfCancellationRequested();
            return ledger;
        }
        catch (OperationCanceledException late) when (bound.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException(
                UpdateCodes.RuntimeUnreachable,
                $"The drain, the stop and the move did not end within {GoBound.TotalMinutes:F0} minutes of the download, so Jason Desktop was given no go.",
                late,
                retryable: true);
        }
    }

    /// <summary>The go: made by an exclusive create, last of all, so the record stands before the installer can look.</summary>
    private void MakeGo(string id)
    {
        try
        {
            using (new FileStream(_goFile!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
        }
        catch (IOException there) when (File.Exists(_goFile))
        {
            _goFile = null;
            throw new UpdateException(
                UpdateCodes.DesktopUnanswered,
                $"A go for request {id} was there before this jason made one, so it is no go, and Jason Desktop was given none.",
                there,
                retryable: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateCodes.FileRefused, $"The go for request {id} could not be made at {_goFile}: {error.Message}", error, retryable: true);
        }

        _stage = Stage.Gone;
        Say(UpdateStep.Stopped, $"Jason Desktop told to go, request {id}");
    }

    /// <summary>The ledger at <c>swapped</c>, and the record kept for a refusal let go: the update is happening.</summary>
    public UpdateLedger Swapped(UpdateLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var swapped = ledger.At(UpdateStep.Swapped);
        Record(swapped);
        try
        {
            File.Delete(Before);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A file nothing reads past this point.
        }

        return swapped;
    }

    /// <summary>
    /// <c>--update-settle</c>: the application's own record of the update kept, as a window's first start would, once the new
    /// version's runtime answered, waited for up to <see cref="SettleWait"/>. Never fails the update: its line says what came
    /// of it.
    /// </summary>
    public async Task SettleAsync(CancellationToken cancellationToken)
    {
        if (!HasDesktop)
        {
            return;
        }

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        const string later = "its next Live window's start records it";
        using var bound = new CancellationTokenSource(SettleWait, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        try
        {
            var settled = await processes.RunAsync(env.Paths, [Application!, "--update-settle"], TextWriter.Null, linked.Token).ConfigureAwait(false);
            Say("settle", settled.ExitCode == 0
                ? "Jason Desktop recorded the update as kept"
                : $"Jason Desktop could not record the update as kept (exit code {settled.ExitCode}); {later}");
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Say("settle", $"Jason Desktop did not record the update as kept within {SettleWait.TotalMinutes:F0} minutes, and its start was ended; {later}");
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Say("settle", $"Jason Desktop could not be started to record the update as kept ({error.Message}); {later}");
        }
    }

    /// <summary>
    /// After a hand-over to the copy that did not complete: before <c>kept</c> the record that stood is put back where the
    /// application is still the version the update began from; a runtime that answers drained is resumed, and where one was
    /// running before and none answers now, one is started from whichever executable is whole: the carried one where its
    /// record names a version, else the copy of this jason. A cancel waits for nothing: the start is launched and said.
    /// </summary>
    public async Task RecoverAsync(bool wasLive, bool waits = true)
    {
        UpdateLedger? ledger;
        try
        {
            ledger = UpdateLedger.ReadFile(update.Ledger);
        }
        catch (UpdateLedgerException)
        {
            ledger = null;
        }

        if (ledger is { Step: < UpdateStep.Kept } && !Names(ledger.ToVersion))
        {
            PutLedgerBack();
        }

        var whole = CarriedExecutable.Of(carried.Executable) is not null && File.Exists(carried.Executable) ? carried.Executable : Moved ?? Copy;
        _wasLive = wasLive;
        await StartAgainAsync(whole, ledger, waits).ConfigureAwait(false);
    }

    /// <summary>What the runtime and the update are now, after a way out: one sentence for the answer to end with.</summary>
    public string RuntimeSaid() => _runtime;

    /// <summary>
    /// Every way out after the request, in one place, under no cancel: the go taken back where the Desktop has not read it,
    /// then by where the update stands. While the installer has the update and its wait has not run out, the folder is the
    /// installer's: nothing is copied into it, no record is put back and no runtime is started there, and the answer names the
    /// command that finishes the update once it lands. Once it landed, the new jason at the carried path runs the runtime and
    /// the record stays for `jason update apply` to finish. Otherwise nothing was installed: a jason is put back at the carried
    /// path, the record that stood before the update is put back, and the runtime is as it was found — resumed where it answers
    /// drained, started again where it was running and is gone, launched without a wait where the answer must come at once.
    /// Returns the sentences the answer ends with.
    /// </summary>
    private async Task<string> SettleOnTheWayOutAsync(UpdateLedger ledger, bool waits)
    {
        if (_stage < Stage.Gone)
        {
            TryDelete(_goFile);
        }

        if (InstallerOwnsTheFolder)
        {
            TryDelete(PidFile);
            _runtime = $"the installer has the update, so nothing was put back in the application's folder and no runtime was started there.";
            Say("left", $"the application's folder to the installer; once the application is {ledger.ToVersion}, `jason update apply` finishes the update and starts the runtime");
            return $"Jason Desktop's installer has {ledger.ToVersion}: once the application is {ledger.ToVersion}, `jason update apply` finishes the update and starts the runtime, and `jason status` names it until then.";
        }

        if (_stage == Stage.Landed || Names(ledger.ToVersion))
        {
            await StartAgainAsync(Whole, _written, waits).ConfigureAwait(false);
            return $"The application is {ledger.ToVersion}, and {_runtime} `jason update apply` finishes the update.";
        }

        PutImageBack();
        var nothingInstalled = _installingSince is null || CarriedExecutable.Of(carried.Executable) is { } still && SemanticVersion.TryParse(still.Version, out var named) && named == ledger.FromVersion;
        if (nothingInstalled)
        {
            PutLedgerBack();
        }

        // Before the drain nothing of the runtime was touched, so nothing of it is resumed or started.
        if (_stage >= Stage.Draining)
        {
            await StartAgainAsync(Whole, nothingInstalled ? null : _written, waits).ConfigureAwait(false);
        }

        return _stage switch
        {
            Stage.None => $"Nothing was asked of Jason Desktop, and {_runtime}",
            Stage.Asked => $"Nothing was drained, stopped or installed, and {_runtime}",
            < Stage.Gone => $"Jason Desktop was given no go, so nothing was installed, and {_runtime}",
            _ when nothingInstalled => $"Nothing was installed, and {_runtime}",
            _ => $"The update stays in flight for `jason update apply`, and {_runtime}",
        };
    }

    /// <summary>Where the update stands, for the way out: each stage is reached before its acts begin.</summary>
    private enum Stage
    {
        None,
        Asked,
        Draining,
        Gone,
        Landed,
    }

    private Stage _stage = Stage.None;

    private bool _wasLive;

    private string? _goFile;

    private DateTimeOffset? _installingSince;

    /// <summary>The last record of the ledger this update wrote.</summary>
    private UpdateLedger _written = null!;

    /// <summary>Whether the installer has the update and its wait has not run out: the folder is the installer's then.</summary>
    private bool InstallerOwnsTheFolder =>
        _installingSince is { } since && _stage < Stage.Landed && !Names(_written.ToVersion) && clock.GetUtcNow() - since < InstallWait;

    /// <summary>The record that stood before this update, put back: nothing was installed.</summary>
    private void PutLedgerBack()
    {
        try
        {
            if (File.Exists(Before))
            {
                File.Move(Before, update.Ledger, overwrite: true);
            }
            else
            {
                File.Delete(update.Ledger);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            env.Error.WriteLine(error.Message);
            Say("put back", $"the record of this update at {update.Ledger} could not be taken back ({error.Message}); `jason update apply` carries it on");
        }
    }

    /// <summary>The jason a runtime is started again from: the one at the carried path, else this one where it moved.</summary>
    private string Whole => File.Exists(carried.Executable) || Moved is null ? carried.Executable : Moved;

    /// <summary>A refusal of the Desktop's, in its own words.</summary>
    private static UpdateException RefusedBy(UpdateLedger ledger, DesktopAnswer answer)
    {
        var reason = answer.Reason ?? "failed";
        return new UpdateException(
            UpdateCodes.RefusedByDesktop,
            $"Jason Desktop refused to install {ledger.ToVersion} ({reason}): {answer.Message ?? "it gave no sentence."}"
            + (answer.Offered is { } offered ? $" Its feed offered {offered}." : string.Empty),
            null,
            retryable: reason is "busy" or "window_open" or "check_failed" or "download_failed" or "runtime_not_stopped" or "timed_out" or "go_timed_out" or "go_there");
    }

    private static UpdateException NoAnswer(UpdateLedger ledger, string why) =>
        new(
            UpdateCodes.DesktopUnanswered,
            $"Jason Desktop was asked to install {ledger.ToVersion} and gave no answer: {why}. Run `jason update apply` again, or choose Check for Updates in Jason Desktop.",
            null,
            retryable: true);

    /// <summary>
    /// Windows, before the Desktop is asked anything: the folder the image moves to is on the application's volume. A move
    /// to another volume would be a copy that leaves the image in the folder, which the installer cannot replace.
    /// </summary>
    private void SameVolume(string id)
    {
        var target = Path.GetFullPath(MovedImage(id));
        var from = Path.GetFullPath(carried.Executable);
        if (!string.Equals(Path.GetPathRoot(target), Path.GetPathRoot(from), StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"This jason moves its own file out of the application's folder for the installer, and {target} is not on the volume of {from}, so it would not be moved.");
        }
    }

    /// <summary>
    /// Windows: every process running the carried jason but this one, which the installer would find at that path and wait
    /// for: refused, naming them, before anything is moved.
    /// </summary>
    private void NoOtherAtTheCarriedPath()
    {
        if (env.Removes is not { } removes)
        {
            return;
        }

        var (self, _) = Self();
        var others = removes.RunningFrom(carried.Executable).Where(pid => pid != self).Order().ToList();
        if (others.Count > 0)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"Other processes run the jason at {carried.Executable}: {string.Join(", ", others.Select(pid => pid.ToString(CultureInfo.InvariantCulture)))}. "
                + "The installer cannot replace that file while they run, so the update was not begun there. Let them end — a runtime of another data directory stops with `jason runtime stop` and its JASON_DATA_DIR — then run this again.",
                null,
                retryable: true);
        }
    }

    /// <summary>
    /// Windows: the runtime an earlier version's demo may have left running on the demo's data directory, which runs the
    /// carried jason too, stopped as `jason uninstall` stops it; one that will not stop refuses the update.
    /// </summary>
    private async Task StopDemoRuntimeAsync(CancellationToken cancellationToken)
    {
        if (env.Setup is not { } places)
        {
            return;
        }

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        foreach (var folder in Uninstall.DesktopRemoval.DemoFolders(env, places))
        {
            var paths = new JasonPaths(folder);
            if (DescriptorProcess.Live(paths, processes) is null)
            {
                continue;
            }

            using var said = new StringWriter(CultureInfo.InvariantCulture);
            await Commands.RuntimeStopCommand.RunAsync(env with { Paths = paths, Out = said }, human: false, cancellationToken).ConfigureAwait(false);
            if (DescriptorProcess.Live(paths, processes) is { } still)
            {
                throw new UpdateException(
                    UpdateCodes.RuntimeUnreachable,
                    string.Create(CultureInfo.InvariantCulture, $"The runtime an earlier version's demo left running, process {still.Pid}, on {folder}, did not stop, and it runs the carried jason. ")
                    + $"Stop it with `jason runtime stop` with JASON_DATA_DIR set to {folder}, then run this again.",
                    null,
                    retryable: true);
            }

            Say(UpdateStep.Stopped, $"the runtime an earlier version's demo left running on {folder}");
        }
    }

    /// <summary>
    /// Windows, at <c>downloaded</c> and last before the go: this process's own image moved out of the application's folder
    /// to <see cref="MovedImage"/>, on the same volume, where it goes on running; the version it is written beside it; and its
    /// id and start recorded in <see cref="PidFile"/> for the installer's look to pass over. A move that left the source where
    /// it was — a copy across volumes behind one drive letter — is no move: the copy is taken away and the update refused.
    /// </summary>
    private void StepOut(string id)
    {
        WarmUp();
        var target = Path.GetFullPath(MovedImage(id));
        var from = Path.GetFullPath(carried.Executable);
        SameVolume(id);

        var store = Path.GetDirectoryName(Path.GetDirectoryName(target)!)!;
        Sweep(store);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Mover(from, target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(from))
            {
                TryDelete(target);
            }
            else if (File.Exists(target))
            {
                Moved = target;
            }

            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"This jason could not move its own file from {from} to {target}: {error.Message}",
                error);
        }

        if (File.Exists(from))
        {
            // The move copied and left the image where it runs: the two places are two volumes behind one letter.
            TryDelete(target);
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"This jason's own file at {from} was still there after it was moved to {target}, which is on another volume behind the same drive letter, so it was not moved and the copy was taken away.");
        }

        Moved = target;
        Say("moved", $"this jason's own file to {target}, where it goes on running, so the installer finds none in the application's folder");
        WriteVersionBeside(target);
        var (pid, started) = Self();
        try
        {
            File.WriteAllText(PidFile, PidRecord(pid, started));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"This jason could not record itself at {PidFile}, which the installer's look reads to pass over it: {error.Message}",
                error);
        }
    }

    /// <summary>The record the installer's look reads: the process id and its start in Windows file time, one space between.</summary>
    public static string PidRecord(int pid, long started) => string.Create(CultureInfo.InvariantCulture, $"{pid} {started}");

    /// <summary>
    /// The name of the file beside a moved image that says which version of the application it came from: the version alone,
    /// as the application's <c>version.json</c> names it, with nothing after it.
    /// </summary>
    public const string MovedVersionName = "jason.version";

    /// <summary>
    /// The version the moved image came from, beside it, so that a start of the application finds a missing carried jason
    /// it can put back from the store when the application is still that version. Failing quietly: it is a repair's, not the
    /// update's.
    /// </summary>
    private void WriteVersionBeside(string target)
    {
        try
        {
            if (CarriedExecutable.Of(carried.Executable)?.Version is { } version)
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(target)!, MovedVersionName), version);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The image is whole without it; only the repair at a start cannot tell its version.
        }
    }

    /// <summary>
    /// Windows: the record for the installer's look taken away, and a jason put back at the carried path where the installer
    /// put none there, copied from this process's own image, so every way this verb ends leaves a working installation. Not
    /// into an application that is no longer there: an uninstall that removed it took the folder too.
    /// </summary>
    private void PutImageBack()
    {
        if (Moved is null)
        {
            return;
        }

        TryDelete(PidFile);
        if (File.Exists(carried.Executable))
        {
            return;
        }

        if (Application is { } application && !File.Exists(application))
        {
            Say("put back", $"no jason at {carried.Executable}: the application at {application} is gone, and this one is at {Moved}");
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(carried.Executable)!);
            File.Copy(Moved, carried.Executable);
            Say("put back", $"a jason at {carried.Executable}, copied from this one at {Moved}, since no new one was installed there");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            env.Error.WriteLine(error.Message);
            Say("put back", $"no jason could be put at {carried.Executable} ({error.Message}); this one is at {Moved}. Choose Check for Updates in Jason Desktop, or run its installer again, to put one there");
        }
    }

    /// <summary>
    /// Windows: the application the Desktop kept was copied while this process's file was out of the folder, so the copy is
    /// given this jason, which is the version it was kept as, and none of the record for the installer's look.
    /// </summary>
    private void MakeKeptWhole(string kept, SemanticVersion from)
    {
        if (Moved is null || platform is not SetupSystem.Windows)
        {
            return;
        }

        var inKept = Path.Combine(kept, Path.GetRelativePath(ApplicationFolderOf(carried.Executable, SetupSystem.Windows), carried.Executable));
        try
        {
            if (!Directory.Exists(kept))
            {
                return;
            }

            TryDelete(Path.Combine(Path.GetDirectoryName(inKept)!, PidFileName));
            if (!File.Exists(inKept))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(inKept)!);
                File.Copy(Moved, inKept);
                Say(UpdateStep.Kept, $"this jason's {from} put at {inKept}, since the application was kept while its file was out of the folder");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Say(UpdateStep.Kept, $"the application kept at {kept} has no jason at {inKept} ({error.Message}); a copy of {Moved} belongs there before that copy is put back by hand");
        }
    }

    /// <summary>
    /// The store a carried update moves its image to on Windows, <c>%LOCALAPPDATA%\jason-desktop-updater\jason-carried</c>,
    /// for the places of this machine; null where there is none.
    /// </summary>
    public static string? Store(SetupPlaces? places) =>
        places is { Platform: SetupSystem.Windows, LocalApplicationData: { Length: > 0 } local } ? Path.Combine(local, StoreFolder) : null;

    /// <summary>
    /// The folders requests moved an image to, taken away where nothing runs from them any more: Windows refuses to delete an
    /// image a process still runs, so a delete that fails leaves that folder, quietly, for the next sweep. Only the folders
    /// directly under the store, never the updater's own downloads beside it.
    /// </summary>
    public static void Sweep(string? store)
    {
        if (store is null)
        {
            return;
        }

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(store))
            {
                try
                {
                    DirectoryTrees.Delete(folder);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // An image that still runs, or one this account may not take away: left for the next sweep.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // No store yet.
        }
    }

    private static void TryDelete(string? file)
    {
        if (file is null)
        {
            return;
        }

        try
        {
            File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A file nothing depends on: the look reads it by the process's start, so one left behind names no process.
        }
    }

    private string _runtime = "the runtime was not touched.";

    /// <summary>
    /// The runtime as it was found: one that answers drained is told to claim work again, and one that was running and is
    /// gone is started from <paramref name="executable"/>, under the update's mark, which a start is refused without while an
    /// update stands past <c>kept</c>. Under no cancel; where <paramref name="waits"/> is false, as after a cancel whose answer
    /// must come at once, the start is launched and not waited for.
    /// </summary>
    private async Task StartAgainAsync(string executable, UpdateLedger? ledger, bool waits)
    {
        if (Live() is not null)
        {
            using var said = new StringWriter(CultureInfo.InvariantCulture);
            var (_, answer) = await OperationRunner.SendAsync(env with { Out = said }, Operations.SystemResume, RequestBody.Empty(), CancellationToken.None).ConfigureAwait(false);
            _runtime = answer is { IsSuccess: true }
                ? "the runtime that was drained claims work again."
                : "the runtime that was drained did not answer the resume; `jason runtime restart` brings it back.";
            Say("resumed", answer is { IsSuccess: true } ? "the runtime claims work again" : "the runtime did not answer; run: jason runtime restart");
            return;
        }

        if (!_wasLive)
        {
            _runtime = "no runtime was running before, so none was started.";
            return;
        }

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        IProcessHandle child;
        try
        {
            child = processes.Launch(env.Paths, [executable], UpdateLaunch.At(ledger?.Step ?? UpdateStep.Started));
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            _runtime = $"the runtime the update stopped could not be started again from {executable}: {error.Message}; `jason runtime start` starts it.";
            Say("started", $"not started again from {executable} — {error.Message}; run: jason runtime start");
            return;
        }

        using (child)
        {
            if (!waits)
            {
                _runtime = $"the runtime the update stopped was started again from {executable}, and not waited for; `jason runtime status` says whether it answers.";
                Say("started", $"the runtime again, from {executable}, not waited for");
                return;
            }

            var deadline = clock.GetUtcNow() + StartWait;
            while (clock.GetUtcNow() < deadline)
            {
                if (Live() is not null)
                {
                    _runtime = $"the runtime the update stopped was started again from {executable}.";
                    Say("started", $"the runtime again, from {executable}");
                    return;
                }

                if (child.HasExited)
                {
                    break;
                }

                await Task.Delay(Poll, clock, CancellationToken.None).ConfigureAwait(false);
            }

            var reason = child.HasExited
                ? string.Create(CultureInfo.InvariantCulture, $"it exited with code {child.ExitCode}") + (child.Said.Length > 0 ? $" and said: {child.Said}" : string.Empty)
                : $"nothing answered within {StartWait.TotalSeconds:F0}s";
            _runtime = $"the runtime the update stopped was started again from {executable}, and {reason}; `jason runtime start` starts it.";
            Say("started", $"not answering, from {executable} — {reason}; run: jason runtime start");
        }
    }

    /// <summary>The Desktop's start, run to its end under its bound: its exit, or why it has none.</summary>
    private Task<(FinishedCommand? Finished, string? Why)> RunDesktopAsync(IReadOnlyList<string> command, CancellationToken bounded)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        return Run();

        async Task<(FinishedCommand?, string?)> Run()
        {
            try
            {
                return (await processes.RunAsync(env.Paths, command, TextWriter.Null, bounded).ConfigureAwait(false), null);
            }
            catch (OperationCanceledException) when (bounded.IsCancellationRequested)
            {
                return (null, $"its start at {Application} did not end within {DesktopWait.TotalMinutes:F0} minutes");
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
            {
                return (null, $"it could not be started at {Application}: {error.Message}");
            }
        }
    }

    /// <summary>
    /// The request's file read while the Desktop's start runs and after it: at <paramref name="atDownloaded"/>, the answer
    /// that names <c>downloaded</c> or an end, else the one that names an end. A start of its own that ended is read a few
    /// more times, and one that handed the request to a running window (exit 3) is read up to the window's bounds. Null,
    /// with why, where there is none.
    /// </summary>
    private async Task<(DesktopAnswer? Answer, string Why)> AnswerAsync(Task<(FinishedCommand? Finished, string? Why)> run, string file, string id, bool atDownloaded, CancellationToken cancellationToken)
    {
        bool Wanted(DesktopAnswer answer) => answer.IsEnd || (atDownloaded && answer.State == DesktopAnswer.Downloaded);

        cancellationToken.ThrowIfCancellationRequested();

        while (!run.IsCompleted)
        {
            if (DesktopAnswer.Read(file, id) is { } answer && Wanted(answer))
            {
                return (answer, string.Empty);
            }

            await Task.WhenAny(run, Task.Delay(Poll, clock, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var (finished, why) = await run.ConfigureAwait(false);
        if (finished is null)
        {
            // A start that did not end in time was ended: what it wrote names an end, or there is none.
            return Ended(file, id) is { } ended ? (ended, string.Empty) : (null, why!);
        }

        if (finished.ExitCode == 3)
        {
            // A running Jason Desktop took it, at its own pace; a window that failed before it served the request never
            // answers, so the wait is bounded and says so.
            var began = clock.GetUtcNow();
            while (true)
            {
                if (DesktopAnswer.Read(file, id) is { } answer && Wanted(answer))
                {
                    return (answer, string.Empty);
                }

                var waited = clock.GetUtcNow() - began;
                if (waited >= WindowWait || (waited >= WindowAnswers && !File.Exists(file)))
                {
                    return (null, $"a running Jason Desktop took the request, and {file} named no end within {WindowWait.TotalMinutes:F0} minutes; a window that failed before it served the request never names one");
                }

                await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
            }
        }

        // A start of its own that ended: a `downloaded` it left is no one's to go on, so only an end answers.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (Ended(file, id) is { } answer)
            {
                return (answer, string.Empty);
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }

        return (null, string.Create(CultureInfo.InvariantCulture, $"its start ended with exit code {finished.ExitCode}, and {file} names no end"));
    }

    private static DesktopAnswer? Ended(string file, string id) => DesktopAnswer.Read(file, id) is { IsEnd: true } answer ? answer : null;

    private async Task<bool> InstalledAsync(SemanticVersion version, CancellationToken cancellationToken)
    {
        var deadline = clock.GetUtcNow() + InstallWait;
        while (true)
        {
            if (Names(version))
            {
                return true;
            }

            if (clock.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The end of ShipIt's own log, where a macOS install that failed says why; null where there is none.</summary>
    private string? ShipItSaid()
    {
        if (env.Setup is not { } places)
        {
            return null;
        }

        var log = Path.Combine(places.Home, "Library", "Caches", "ai.jasonharness.desktop.ShipIt", "ShipIt_stderr.log");
        try
        {
            var text = File.ReadAllText(log);
            var tail = text.Length > 800 ? text[^800..] : text;
            var line = string.Join(' ', tail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return line.Length == 0 ? null : $"{line} ({log})";
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Record(UpdateLedger ledger)
    {
        OnDisk($"recording the {ledger.Step.ToString().ToLowerInvariant()} step", update.Ledger, () => (Writes ?? (written => written.Write(update.Ledger)))(ledger));
        _written = ledger;
    }

    private void OnDisk(string what, string path, Action act)
    {
        try
        {
            act();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"{what} failed at '{path}': {error.Message} Make sure {update.Root} is a directory this account can write to, then run `jason update apply` again.",
                error);
        }
    }

    private RuntimeDescriptor? Live() => DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance);

    private static bool Same(string path, string? other) =>
        other is not null
        && string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(other),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void Say(UpdateStep step, string what) => Say(step.ToString().ToLowerInvariant(), what);

    private void Say(string step, string what) => Steps.Add(string.Create(CultureInfo.InvariantCulture, $"{step}: {what}"));
}
