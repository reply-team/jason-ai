using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
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
/// <param name="State"><c>checking</c>, <c>downloading</c>, <c>installing</c>, <c>current</c> or <c>refused</c>.</param>
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
/// copies itself aside, drains the runtime and asks the Desktop to install — and the new carried jason does the rest from a
/// file that is its own: <c>plugins</c>, <c>started</c>, <c>healthy</c>, <c>complete</c>, the skills, the machine setup and the
/// UI, then the application's own record of the update settled with no window.
/// </para>
/// <para>
/// <b>The ledger is the command line's ledger</b>, written before each step as every update's is, with the steps' own
/// words read for this hand-over: <c>staged</c> the plan, <c>drained</c> the drain, <c>stopped</c> the request the Desktop takes,
/// whose start stops the runtime the application carries; <c>kept</c> the application kept aside and handed to the installer,
/// and <c>swapped</c> the application's <c>version.json</c> naming the new version. No step is added, so every Jason that
/// reads the ledger reads this one. A request the Desktop refuses, or never answers, puts back the record that was there
/// before, since nothing was installed.
/// </para>
/// <para>
/// <b>Whoever asked for a stop starts one again.</b> An update that does not complete leaves the runtime as it was found:
/// a drained runtime still running is resumed, and one that was running and is gone — stopped by the Desktop for a request
/// that then went no further — is started again from whichever executable is whole.
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
    /// How long a start of the Desktop of its own is waited for: its own bound, 30 minutes with the install, and a margin.
    /// </summary>
    internal TimeSpan DesktopWait { get; init; } = TimeSpan.FromMinutes(35);

    /// <summary>How long a runtime started again is waited for to answer.</summary>
    internal TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How often a file or a runtime is looked at while one of the waits above runs.</summary>
    internal TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A fresh request id for each request: letters and digits, never a path.</summary>
    internal Func<string> NewRequest { get; init; } = () => "cli-" + Guid.NewGuid().ToString("N");

    /// <summary>What loads every assembly this process may still need, before the application it runs from is replaced.</summary>
    internal Action WarmUp { get; init; } = AssemblyWarmUp.Run;

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
    /// The update from wherever the ledger stands before <c>swapped</c>, run by the copy: the drain, the request, the wait for
    /// the installer, and <c>swapped</c> recorded once the application's <c>version.json</c> names the new version. Returns the
    /// ledger at <c>swapped</c>; a refusal, no answer, or an install that did not land is thrown, with the runtime left as it
    /// was found.
    /// </summary>
    public async Task<UpdateLedger> InstallAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(request);

        if (Names(ledger.ToVersion))
        {
            Say(UpdateStep.Swapped, $"the application is {ledger.ToVersion} already");
            return Swapped(ledger);
        }

        var wasLive = Live() is not null;
        ledger = ledger.At(UpdateStep.Drained);
        Record(ledger);
        var drain = new UpdateApplier(env, update, clock, carried.Executable);
        await drain.DrainAsync(ledger, request, cancellationToken).ConfigureAwait(false);
        Steps.AddRange(drain.Steps);

        ledger = ledger.At(UpdateStep.Stopped) with { StoppedAt = clock.GetUtcNow() };
        Record(ledger);
        var (answer, why) = await AskAsync(ledger, request, cancellationToken).ConfigureAwait(false);

        if (answer is null)
        {
            await PutBackAsync(ledger, wasLive, cancellationToken).ConfigureAwait(false);
            throw new UpdateException(
                UpdateCodes.DesktopUnanswered,
                $"Jason Desktop was asked to install {ledger.ToVersion} and gave no answer: {why}. Nothing was installed that this "
                + $"command can tell, and {RuntimeSaid()} Run `jason update apply` again, or choose Check for Updates in Jason Desktop.",
                null,
                retryable: true);
        }

        if (answer.State == DesktopAnswer.Refused)
        {
            await PutBackAsync(ledger, wasLive, cancellationToken).ConfigureAwait(false);
            var reason = answer.Reason ?? "failed";
            throw new UpdateException(
                UpdateCodes.RefusedByDesktop,
                $"Jason Desktop refused to install {ledger.ToVersion} ({reason}): {answer.Message ?? "it gave no sentence."} "
                + (answer.Offered is { } offered ? $"Its feed offered {offered}. " : string.Empty)
                + $"Nothing was installed, and {RuntimeSaid()}",
                null,
                retryable: reason is "busy" or "window_open" or "check_failed" or "download_failed" or "runtime_not_stopped" or "timed_out");
        }

        if (answer.State == DesktopAnswer.Installing)
        {
            ledger = ledger.At(UpdateStep.Kept) with { KeptApplication = answer.Kept };
            Record(ledger);
            Say(UpdateStep.Kept, answer.Kept is { } kept
                ? $"Jason Desktop kept the application at {kept} and handed {ledger.ToVersion} to its installer"
                : $"Jason Desktop handed {ledger.ToVersion} to its installer");
        }
        else
        {
            Say(UpdateStep.Kept, $"Jason Desktop says the application is {ledger.ToVersion} already");
        }

        if (!await InstalledAsync(ledger.ToVersion, cancellationToken).ConfigureAwait(false))
        {
            var named = CarriedExecutable.Of(carried.Executable)?.Version;
            var shipIt = platform is SetupSystem.MacOs ? ShipItSaid() : null;
            if (named is not null && SemanticVersion.TryParse(named, out var still) && still == ledger.FromVersion)
            {
                // The application is still the one this update began from, whole: nothing was installed.
                await PutBackAsync(ledger, wasLive, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Neither version can be read from the application, so the copy of this jason, which is whole, runs the
                // runtime, and the update stays in flight for the next apply.
                await StartAgainAsync(Copy, ledger, wasLive, cancellationToken).ConfigureAwait(false);
            }

            throw new UpdateException(
                UpdateCodes.DesktopNotInstalled,
                $"Jason Desktop handed {ledger.ToVersion} to its installer, and after {InstallWait.TotalMinutes:F0} minutes the application's "
                + $"{CarriedExecutable.RecordName} at {carried.Record} names {named ?? "no version"}. "
                + (shipIt is null ? string.Empty : $"ShipIt said: {shipIt} ")
                + $"{RuntimeSaid()} Run `jason update apply` again"
                + (named is null ? $", from {Copy} if jason does not answer" : string.Empty)
                + ".",
                null,
                retryable: true);
        }

        Say(UpdateStep.Swapped, $"the application is {ledger.ToVersion}");
        return Swapped(ledger);
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
    /// version's runtime answered. Never fails the update: its line says what came of it.
    /// </summary>
    public async Task SettleAsync(CancellationToken cancellationToken)
    {
        if (!HasDesktop)
        {
            return;
        }

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        try
        {
            var settled = await processes.RunAsync(env.Paths, [Application!, "--update-settle"], TextWriter.Null, cancellationToken).ConfigureAwait(false);
            Say("settle", settled.ExitCode == 0
                ? "Jason Desktop recorded the update as kept"
                : $"Jason Desktop could not record the update as kept (exit code {settled.ExitCode}); its next Live window's start records it");
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Say("settle", $"Jason Desktop could not be started to record the update as kept ({error.Message}); its next Live window's start records it");
        }
    }

    /// <summary>
    /// After a hand-over that did not complete: a runtime that answers drained is resumed, and where one was running before
    /// and none answers now, one is started from whichever executable is whole: the carried one where its record names a
    /// version, else the copy of this jason.
    /// </summary>
    public async Task RecoverAsync(bool wasLive, CancellationToken cancellationToken)
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

        var whole = CarriedExecutable.Of(carried.Executable) is not null && File.Exists(carried.Executable) ? carried.Executable : Copy;
        await StartAgainAsync(whole, ledger, wasLive, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A refused or unanswered request: nothing was installed, so the record that stood is put back, and the runtime.</summary>
    private async Task PutBackAsync(UpdateLedger ledger, bool wasLive, CancellationToken cancellationToken)
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

        await StartAgainAsync(carried.Executable, ledger, wasLive, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What the runtime is now, after the steps above: one sentence for a refusal to end with.</summary>
    private string RuntimeSaid() => _runtime;

    private string _runtime = "the runtime is as it was.";

    /// <summary>
    /// The runtime as it was found: one that answers drained is told to claim work again, and one that was running and is
    /// gone is started from <paramref name="executable"/>, under the update's mark, which a start is refused without while an
    /// update stands past <c>kept</c>.
    /// </summary>
    private async Task StartAgainAsync(string executable, UpdateLedger? ledger, bool wasLive, CancellationToken cancellationToken)
    {
        if (Live() is not null)
        {
            using var said = new StringWriter(CultureInfo.InvariantCulture);
            var (_, answer) = await OperationRunner.SendAsync(env with { Out = said }, Operations.SystemResume, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
            _runtime = answer is { IsSuccess: true }
                ? "the runtime that was drained claims work again."
                : "the runtime that was drained did not answer the resume; `jason runtime restart` brings it back.";
            Say("resumed", answer is { IsSuccess: true } ? "the runtime claims work again" : "the runtime did not answer; run: jason runtime restart");
            return;
        }

        if (!wasLive)
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

                await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
            }

            var reason = child.HasExited
                ? string.Create(CultureInfo.InvariantCulture, $"it exited with code {child.ExitCode}") + (child.Said.Length > 0 ? $" and said: {child.Said}" : string.Empty)
                : $"nothing answered within {StartWait.TotalSeconds:F0}s";
            _runtime = $"the runtime the update stopped was started again from {executable}, and {reason}; `jason runtime start` starts it.";
            Say("started", $"not answering, from {executable} — {reason}; run: jason runtime start");
        }
    }

    /// <summary>
    /// The request: the application started with it, waited for, and its file read. Returns the answer that names an end, or
    /// null with why there is none.
    /// </summary>
    private async Task<(DesktopAnswer? Answer, string Why)> AskAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        var id = NewRequest();
        var file = RequestFile(id);
        if (File.Exists(file))
        {
            return (null, $"a file of the request's id, {file}, was there before the request was made, so it is no answer to it");
        }

        List<string> command = [Application!, "--update-apply", "--to", ledger.ToVersion.ToString(), "--request", id];
        if (DesktopFeed(request.Feed) is { } feed)
        {
            command.AddRange(["--feed", feed]);
        }

        Say(UpdateStep.Stopped, $"Jason Desktop asked to install {ledger.ToVersion}, request {id}");
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        FinishedCommand finished;
        using (var bound = new CancellationTokenSource(DesktopWait, clock))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token))
        {
            try
            {
                finished = await processes.RunAsync(env.Paths, command, TextWriter.Null, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (Ended(file, id), $"its start at {Application} did not end within {DesktopWait.TotalMinutes:F0} minutes");
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
            {
                return (null, $"it could not be started at {Application}: {error.Message}");
            }
        }

        if (finished.ExitCode == 3)
        {
            // A running Jason Desktop took it, at its own pace; a window that failed before it served the request never
            // answers, so the wait is bounded and says so.
            var answer = await WindowAnswerAsync(file, id, cancellationToken).ConfigureAwait(false);
            return answer is not null
                ? (answer, string.Empty)
                : (null, $"a running Jason Desktop took the request, and {file} named no end within {WindowWait.TotalMinutes:F0} minutes; a window that failed before it served the request never names one");
        }

        // Every other exit is read the same way: the file names the end, or there is none.
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

    private async Task<DesktopAnswer?> WindowAnswerAsync(string file, string id, CancellationToken cancellationToken)
    {
        var began = clock.GetUtcNow();
        while (true)
        {
            if (Ended(file, id) is { } answer)
            {
                return answer;
            }

            var waited = clock.GetUtcNow() - began;
            if (waited >= WindowWait || (waited >= WindowAnswers && !File.Exists(file)))
            {
                return null;
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

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

    private void Record(UpdateLedger ledger) =>
        OnDisk($"recording the {ledger.Step.ToString().ToLowerInvariant()} step", update.Ledger, () => ledger.Write(update.Ledger));

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
