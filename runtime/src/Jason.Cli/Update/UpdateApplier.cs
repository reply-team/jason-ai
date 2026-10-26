using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Discovery;
using Jason.Cli.Files;
using Jason.Cli.Plugins;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Ui;
using Jason.Cli.Uninstall;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Files;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>What an update was asked to do: which feed, which version if one was named, and how long to drain.</summary>
public sealed record UpdateRequest(Uri Feed, SemanticVersion? Version, TimeSpan Drain);

/// <summary>
/// The step machine. Every step writes the ledger <b>before</b> it acts, so the last step a ledger names is the
/// last one that was begun — and every step is written so that doing it twice is the same as doing it once,
/// because "begun" is all the ledger can promise.
/// </summary>
/// <remarks>
/// <para>
/// The order is staged, drained, stopped, kept, swapped, plugins, started, healthy, complete — and then the skills and the UI,
/// which are no steps of the ledger (<see cref="RedeploySkillsAsync"/>, <see cref="PlaceUiAsync"/>). Between <c>kept</c> and
/// <c>swapped</c> the install path is <b>empty</b> — the old executable has been moved aside, its notices after it,
/// and the new one is not in place yet — and that window is a few renames wide. It is also the one window a person
/// cannot get out of by typing <c>jason update</c>, because there is no <c>jason</c> on the PATH to type: the way
/// out is the copy of the applier under the data directory, or putting <c>previous/</c> back by hand. Both are on
/// the page.
/// </para>
/// <para>
/// Health is asked of the runtime rather than of the file. A file that prints the right version proves that a
/// file exists; what an update has to know is that the <em>runtime now serving</em> is the new build and that
/// its database is ready — and the same answer carries what a rollback would need to undo the migration it just
/// performed.
/// </para>
/// </remarks>
/// <param name="runningAs">
/// The file this applier is itself running as. It matters in one place: an update resumed after a kill between
/// the applier copy and the rename is run <em>from that copy</em>, and a copy cannot be written over while it is
/// the program doing the copying. Null asks the operating system, which is the answer everywhere but a test —
/// where nothing is really running and the overwrite would silently succeed.
/// </param>
public sealed class UpdateApplier(CliEnvironment env, UpdatePaths update, TimeProvider clock, string installPath, string? runningAs = null)
{
    /// <summary>How often the drain and the stop are re-read while waiting for them.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    /// <summary>How long to wait for a new runtime to publish its descriptor and answer.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The name the skills lines go under: a step of the apply, and not one of the ledger's.</summary>
    private const string Skills = "skills";

    /// <summary>The name the UI's line goes under, which is no step of the ledger either.</summary>
    private const string Ui = "ui";

    /// <summary>Everything the applier did, in order, for the caller to print.</summary>
    public List<string> Steps { get; } = [];

    /// <summary>
    /// How long a move of the executable waits for another program to let go of it, and how often it tries: the uninstall's
    /// own rule. A test holds it short.
    /// </summary>
    internal HeldFileRule HeldFiles { get; init; } = InstallationRemovers.HeldFiles;

    /// <summary>
    /// The same rule for a rename of a plugin package's directory, with access denied counted as a hold: Windows refuses
    /// a directory whose file is held open that way, as the plugin installer's own moves count it.
    /// </summary>
    private HeldFileRule PackageMoves => HeldFiles with { AccessDeniedCounts = true };

    /// <summary>How a pause between two tries is taken: <see cref="HeldFileRetry.Sleep"/> outside a test, which counts them.</summary>
    internal Action<TimeSpan, CancellationToken> Pause { get; init; } = HeldFileRetry.Sleep;

    /// <summary>
    /// How a plugin package kept aside is renamed back into its empty place after a refused replacement:
    /// <see cref="Directory.Move"/> outside a test.
    /// </summary>
    internal Action<string, string> MovePackageBack { get; init; } = Directory.Move;

    /// <summary>
    /// What loads every assembly this process may still need, before <c>kept</c> renames the file it may be running as:
    /// <see cref="AssemblyWarmUp.Run"/> outside a test, which sees when it ran.
    /// </summary>
    internal Action WarmUp { get; init; } = AssemblyWarmUp.Run;

    /// <summary>
    /// Whether a run that puts the new executable in place itself stops there, and leaves the rest of the update to that
    /// executable: what <c>jason update apply</c> asks for. Left off, every step is taken in this process, which is how a
    /// test drives the step machine alone.
    /// </summary>
    /// <remarks>
    /// The process that typed <c>jason update apply</c> runs from the install path, so after the swap the file at its own
    /// path is another build. A single-file executable reads its assemblies out of its own file as it first needs them,
    /// and one read out of the other build's file fails; the warm-up before <c>kept</c> loads what can be named, and the
    /// rest of the update — the start, the health check, the skills and the UI — is the new build's to run, from a file
    /// that is its own. It reads the ledger at <c>swapped</c> and carries on from there, as it would for a person who
    /// typed it.
    /// </remarks>
    public bool HandsOver { get; init; }

    /// <summary>Whether this run stopped at <c>swapped</c> for the new executable to finish the update: see <see cref="HandsOver"/>.</summary>
    public bool HandedOver { get; private set; }

    /// <summary>Whether this run moved the new executable onto the install path, rather than finding it there.</summary>
    private bool _placed;

    /// <summary>
    /// Runs an update from wherever it stands: a fresh one from the feed, or the one a ledger says is in flight.
    /// </summary>
    public async Task<UpdateLedger> ApplyAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ledger = UpdateLedger.ReadFile(update.Ledger) is { Step: not UpdateStep.Complete } inFlight
            ? Resuming(inFlight, request)
            : await PlanAsync(request, cancellationToken).ConfigureAwait(false);

        foreach (var step in Remaining(ledger.Step))
        {
            // Written first, always: the ledger promises that a step was begun, never that it finished, and
            // every step below is written so that beginning it twice is the same as beginning it once.
            ledger = ledger.At(step);
            Record(ledger, step);
            ledger = await TakeAsync(step, ledger, request, cancellationToken).ConfigureAwait(false);
            Record(ledger, step);

            // Whoever put the new executable in place hands it the rest. A run that found it there already is that
            // executable, or a person resuming by hand, and carries on itself.
            if (step == UpdateStep.Swapped && HandsOver && _placed)
            {
                HandedOver = true;
                return ledger;
            }
        }

        await RedeploySkillsAsync(ledger, cancellationToken).ConfigureAwait(false);
        await PlaceUiAsync(ledger, request, cancellationToken).ConfigureAwait(false);
        return ledger;
    }

    /// <summary>
    /// The new release's UI, placed as <c>jason ui install</c> places it, after the skills: from the release this update
    /// read, at the version it installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After <c>complete</c> and never a step of the ledger, for the skills' reason: the update has happened, so nothing
    /// here fails it. A UI that could not be placed says why on its line and names the command that places it, which is
    /// also what <c>jason status</c> names.
    /// </para>
    /// <para>
    /// The build is held to the API the new runtime says it speaks, not to this executable's: this is the build being
    /// replaced, and the release may speak an API it never knew. The runtime was started and found healthy just before,
    /// so one that does not answer now went away since; nothing is placed, and the line names the command, which needs
    /// no runtime.
    /// </para>
    /// </remarks>
    private async Task PlaceUiAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        string cause;
        try
        {
            var api = (await InfoAsync(cancellationToken).ConfigureAwait(false))?.ApiVersion
                ?? throw new UiInstallException("the new runtime did not say which API it speaks, so no build could be held to it.");
            var placed = await UiInstaller.PlaceAsync(env, request.Feed, ledger.ToVersion, api, cancellationToken).ConfigureAwait(false);
            Say(Ui, $"placed {placed.Manifest.Version}");
            return;
        }
        catch (UiNotPublishedException)
        {
            // A release from before the UI was published, which --version can name. Nothing failed, and the executable
            // just installed may have no `jason ui install` to name.
            Say(Ui, $"{ledger.ToVersion} publishes no UI, so none was placed");
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cause = "cancelled";
        }
        catch (Exception error)
        {
            // Everything, as the skills step catches everything: a completed update reported as refused is the one
            // outcome this step must never have.
            cause = error.Message;
        }

        env.Error.WriteLine(cause);
        Say(Ui, $"not updated — {ReasonSaid(cause)}; run: jason ui install");
    }

    /// <summary>
    /// The last thing an update does: the skills this installation recorded, redeployed at the tag of the build just
    /// installed. Without it the role skills stayed at the tag the build before had installed, and <c>jason status</c>
    /// said not ready until somebody typed the skills verbs by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>jason skills update</c>'s own body, run as the new build would run it: its version is the one that names the
    /// tag, and nothing else differs — the same source each record names, the same pin rule, planner, deployer and
    /// record. The skills come from that source and never from this executable, so the build being replaced can
    /// deploy what the new one carries.
    /// </para>
    /// <para>
    /// After <c>complete</c>, and never a step of the ledger. By now the executable is replaced and the new runtime is
    /// answering: whatever happens here, the update has happened. So nothing here fails it — a pack group that could not
    /// be deployed says why in its line and names what finishes the job — and nothing here can be resumed or rolled back,
    /// which is why it has no place in the ledger. A record written by a deployment that did not happen whole stays
    /// where it was, so <c>jason status</c> names the same repair it would have named with no step at all.
    /// </para>
    /// <para>
    /// What the verb prints is kept off this update's stdout, which carries one document. Its plan and its counts are
    /// the step's lines; when it failed, everything it said goes to stderr beside the line, as the stop's does.
    /// </para>
    /// </remarks>
    private async Task RedeploySkillsAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        using var said = new StringWriter(CultureInfo.InvariantCulture);
        using var complained = new StringWriter(CultureInfo.InvariantCulture);
        SkillsUpdateOutcome outcome;
        string? cause = null;

        // Filled by the verb as it goes, so that a group which throws does not take the lines of the groups deployed before
        // it — their records are written and their files are in place, and the document has to say so — and so that the
        // line for the one it threw in can name it.
        var progress = new SkillsUpdateProgress();
        try
        {
            outcome = await SkillsCommands
                .UpdateRecordedAsync(env with { BuildVersion = ledger.ToVersion.ToString(), Out = said, Error = complained }, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The person stopped the apply while the skills were being fetched. That stops the skills and not the update,
            // which was complete before this began: the answer still says so, rather than a cancellation saying it failed.
            cause = "cancelled";
            outcome = new SkillsUpdateOutcome(ExitCodes.ApiError, [.. progress.Carried], NothingRecorded: false);
        }
        catch (Exception error)
        {
            // Everything else, by construction rather than by name, and a cancellation nobody here asked for with it: a timeout
            // arrives as one. Naming the ones that can happen here missed one twice, and each miss turned a completed update
            // into one reported as refused.
            complained.WriteLine(error.Message);
            cause = error.Message;
            outcome = new SkillsUpdateOutcome(ExitCodes.ApiError, [.. progress.Carried], NothingRecorded: false);
        }

        if (outcome.NothingRecorded)
        {
            Say(Skills, "nothing on this machine was deployed by jason skills, so nothing was redeployed");
            return;
        }

        if (outcome.Exit != ExitCodes.Success)
        {
            env.Error.Write(said.ToString());
            env.Error.Write(complained.ToString());
        }

        // One line for each pack group the verb carried, as it fared: one group refused is not every group refused, and
        // the role half landing beside a refused business pack is what an agent reading this answer has to see.
        foreach (var group in outcome.Carried)
        {
            Say(Skills, group.Exit == ExitCodes.Success
                ? group.Line
                : $"{group.Pack} in {string.Join(", ", group.Roots)} not updated — {ReasonSaid(group.Refused)}; run: jason skills update");
        }

        // And where the verb stopped outside any group's own refusal — a record it could not read before it reached one, a
        // cancel, anything thrown, in the first group or a later one — one line for that, after the lines of the groups
        // that were deployed, naming the group it stopped in where it was in one.
        if (outcome.Exit != ExitCodes.Success && (outcome.Carried.Count == 0 || cause is not null))
        {
            var stoppedIn = cause is not null && progress.Entered is { } entered ? $"{entered.Pack} in {string.Join(", ", entered.Roots)} " : string.Empty;
            Say(Skills, $"{stoppedIn}not updated — {ReasonSaid(cause ?? complained.ToString())}; run: jason skills update");
        }
    }

    /// <summary>
    /// Why something was not updated, as one line before <c>; run:</c>, without the full stop that ends its own sentence.
    /// A deployment that gave no words — an exception with an empty message, a refusal that wrote nothing — is said to
    /// have given none, so the line never reads "not updated — ;".
    /// </summary>
    private static string ReasonSaid(string said) =>
        OneLine(said).TrimEnd('.') is { Length: > 0 } reason ? reason : "no reason was given";

    /// <summary>What a deployment said across lines, as one line: a step's line is one line.</summary>
    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// The ledger write, which is a filesystem call like the renames around it and fails for the same reasons.
    /// </summary>
    /// <remarks>
    /// Raw, it arrives as one line with no code — and the write before <c>swapped</c> happens with the install
    /// path empty, which is the one window a person cannot get out of by typing <c>jason update</c>. A code and
    /// a way out is the least that window owes somebody.
    /// </remarks>
    private void Record(UpdateLedger ledger, UpdateStep step)
    {
        OnDisk(
            $"recording the {step.ToString().ToLowerInvariant()} step",
            update.Ledger,
            RemedyForRecording(step, update.Root, Path.Combine(update.Applier, ReleaseAssets.ExecutableName)),
            () => ledger.Write(update.Ledger));
    }

    /// <summary>
    /// What to tell somebody whose ledger write refused. Everywhere but one step the answer is "fix the
    /// directory and run it again": the install path still holds an executable, so <c>jason</c> is still there
    /// to type. The write before <c>swapped</c> is the exception — it happens inside the one-step window where
    /// the install path is empty, and telling a person to type the program that is missing is telling them
    /// nothing. The program that exists then is the applier's own copy, which is what the swap's own remedy
    /// says and what this one did not.
    /// </summary>
    public static string RemedyForRecording(UpdateStep step, string root, string applierCopy) =>
        step == UpdateStep.Swapped
            ? $"There is nothing at the install path until this succeeds. Make sure {root} is a directory this account can write to, "
              + $"then run `jason update apply` again from {applierCopy}, because `jason` itself is not there to run."
            : $"Make sure {root} is a directory this account can write to, then run `jason update apply` again.";

    /// <summary>
    /// What reaches a release whose <c>min_upgrade_from</c> is above the version installed: the apply of that floor
    /// first, then the apply that was asked for — with the version it named, when it named one. The refusal says it, and
    /// <c>jason update check</c> says it before anybody types the apply, so the two cannot name different commands.
    /// </summary>
    internal static string PassThrough(SemanticVersion to, SemanticVersion installed, SemanticVersion floor, bool pinned) =>
        $"{to} cannot be applied to {installed} directly: install {floor} first with `{ApplyOf(floor)}`, "
        + $"then run `{(pinned ? ApplyOf(to) : "jason update apply")}`.";

    /// <summary>The apply that installs one named release.</summary>
    internal static string ApplyOf(SemanticVersion version) => $"jason update apply --version {version}";

    /// <summary>The steps still to take, beginning with the one the ledger names: it was begun, not finished.</summary>
    private static IEnumerable<UpdateStep> Remaining(UpdateStep from) =>
        Enum.GetValues<UpdateStep>().Where(step => step >= from);

    private static UpdateLedger Resuming(UpdateLedger ledger, UpdateRequest request)
    {
        if (request.Version is { } wanted && wanted != ledger.ToVersion)
        {
            throw new UpdateException(
                UpdateCodes.InProgress,
                $"An update to {ledger.ToVersion} is in flight at '{ledger.Step.ToString().ToLowerInvariant()}'. "
                + "Finish it with `jason update apply`, "
                + $"or see where it stands with `jason update status`; {wanted} cannot be started until it is done.");
        }

        return ledger;
    }

    /// <summary>A new update: what this installation is, what the feed offers, and where everything will go.</summary>
    private async Task<UpdateLedger> PlanAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        var manifest = await ReadFeedAsync(request, cancellationToken).ConfigureAwait(false);

        // First, before anything the manifest says is believed: a manifest read for a named version that names another is
        // not that release, so its floor and its artifacts are another release's, and saying so is the one true refusal.
        if (request.Version is { } wanted && manifest.Version != wanted)
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The manifest at {Address(request)} names {manifest.Version}, and the version asked for is {wanted}: nothing was applied.");
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
                PassThrough(manifest.Version, SemanticVersion.Current, floor, pinned: request.Version is not null));
        }

        var rid = ReleaseAssets.CurrentRid
            ?? throw new UpdateException(UpdateCodes.NotUpdatable, "No release is published for this platform.");

        if (!manifest.Artifacts.ContainsKey(rid))
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The release of {manifest.Version} publishes nothing for {rid}.");
        }

        // Before anything is downloaded, drained or stopped: an update that renames between two volumes cannot
        // work, and finding that out at the swap would mean a machine drained and stopped for nothing. The same
        // check runs again where the renames happen, because by then the paths are the ledger's rather than
        // this plan's.
        SameVolume(update.StagedExecutable(manifest.Version), installPath);

        return new UpdateLedger(
            SemanticVersion.Current,
            manifest.Version,
            UpdateStep.Staged,
            clock.GetUtcNow(),
            installPath,
            update.StagedExecutable(manifest.Version),
            update.PreviousExecutable);
    }

    /// <summary>
    /// The file an update would replace: this program, when this program is one file. An installation started
    /// through the muxer has no single file to replace and says so rather than guessing.
    /// </summary>
    /// <remarks>
    /// Resolved by the caller and handed in, rather than read inside the machine, for the reason the launch seam
    /// was changed for: an update resumed after a kill may be running from the copy of the applier under the data
    /// directory, so "where am I?" is exactly the question it must not ask about the file it is replacing.
    /// </remarks>
    public static string ResolveInstallPath() => ResolveInstallPath(SelfExecutable.Command, SelfExecutable.IsSingleFile);

    /// <summary>
    /// The same decision over what it depends on, so that what a muxed installation or a build's launcher
    /// refuses can be asked without being one: the answer is about the shape of the program, and nothing else.
    /// </summary>
    public static string ResolveInstallPath(IReadOnlyList<string> self, bool singleFile)
    {
        ArgumentNullException.ThrowIfNull(self);

        // The rule itself is SelfExecutable's, so that the verb which removes this installation and the verb
        // which replaces it cannot disagree about what "this installation" is. The code and the sentence stay
        // here, because what an update does about it is an update's business.
        return SelfExecutable.Image(self, singleFile)
            ?? throw new UpdateException(
                UpdateCodes.NotUpdatable,
                "This Jason is not a published single file - it is running through `dotnet`, or from a build's own "
                + "output - so there is no one executable to replace. Update the build you run it from, or install a "
                + "published release with the one-liner on the release page.");
    }

    private async Task<UpdateManifest> ReadFeedAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        using var client = UpdateHttp.Client(env);
        client.Timeout = UpdateFeed.DefaultTimeout;
        return await new UpdateFeed(client).ReadAsync(Address(request), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Where a request's manifest is read from: the release's own directory when a version was named, and the
    /// moving <c>latest</c> address when the question is "what is newest?".
    /// </summary>
    private static Uri Address(UpdateRequest request) =>
        request.Version is { } pinned ? UpdateFeed.PinnedFor(request.Feed, pinned) : request.Feed;

    private async Task<UpdateLedger> TakeAsync(
        UpdateStep step,
        UpdateLedger ledger,
        UpdateRequest request,
        CancellationToken cancellationToken) => step switch
    {
        UpdateStep.Staged => await StageAsync(ledger, request, cancellationToken).ConfigureAwait(false),
        UpdateStep.Drained => await DrainAsync(ledger, request, cancellationToken).ConfigureAwait(false),
        UpdateStep.Stopped => await StopAsync(ledger, cancellationToken).ConfigureAwait(false),
        UpdateStep.Kept => Keep(ledger),
        UpdateStep.Swapped => Swap(ledger),
        UpdateStep.Plugins => await ReplacePluginsAsync(ledger, request, cancellationToken).ConfigureAwait(false),
        UpdateStep.Started => await StartAsync(ledger, cancellationToken).ConfigureAwait(false),
        UpdateStep.Healthy => await HealthyAsync(ledger, cancellationToken).ConfigureAwait(false),
        UpdateStep.Complete => Done(ledger),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "There is no such update step."),
    };

    private async Task<UpdateLedger> StageAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        // This update's own release, for the manifest and for the archive that manifest names. `latest` is an
        // address that answers with whatever is newest at the moment it is asked, so taking the document from
        // one release's directory and the bytes from a moving address would be comparing one release's digest
        // against another release's file — and after a kill, hours later, they may really be two releases.
        var release = request with { Version = ledger.ToVersion };
        var manifest = await ReadFeedAsync(release, cancellationToken).ConfigureAwait(false);
        if (manifest.Version != ledger.ToVersion)
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The feed now offers {manifest.Version}, and this update is for {ledger.ToVersion}.");
        }

        using var client = UpdateHttp.Client(env);
        var staged = await new UpdateStager(client)
            .StageAsync(manifest, ReleaseAssets.CurrentRid!, Address(release), update, cancellationToken)
            .ConfigureAwait(false);

        Say(UpdateStep.Staged, $"{ledger.ToVersion}");
        return ledger with { StagedPath = staged };
    }

    /// <summary>
    /// Tells the runtime to claim nothing new and waits for what it is holding, up to the bound. A runtime that
    /// is not running is already drained: <c>jason update</c> is the same command whether one is up or not — and one
    /// whose descriptor was left behind by a process that has gone is not running.
    /// </summary>
    private async Task<UpdateLedger> DrainAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        if (Live() is null)
        {
            Say(UpdateStep.Drained, "no runtime is running, so there is nothing to drain");
            return ledger;
        }

        // What the call says when it cannot reach the runtime is kept off the stdout this update's one document goes to,
        // and said in the refusal instead, as is an error the runtime answered with: neither is a drain.
        using var said = new StringWriter();
        var (_, answer) = await OperationRunner.SendAsync(env with { Out = said }, Operations.SystemDrain, RequestBody.Empty(), cancellationToken)
            .ConfigureAwait(false);
        if (answer is not { IsSuccess: true })
        {
            var envelope = answer is null ? said.ToString() : OperationRunner.ErrorText(answer);
            throw new UpdateException(UpdateCodes.RuntimeUnreachable, $"The runtime would not drain: {Reason(envelope)}");
        }

        var deadline = clock.GetUtcNow() + request.Drain;
        while (true)
        {
            var running = await RunningAttemptsAsync(cancellationToken).ConfigureAwait(false);
            if (running is null or 0)
            {
                Say(UpdateStep.Drained, "nothing is running any more");
                return ledger;
            }

            if (request.Drain == TimeSpan.Zero || clock.GetUtcNow() >= deadline)
            {
                // Said rather than hidden: what is still running keeps its lease, and the next runtime will
                // finish it or the enforcer will take it back. Either way a person should know it happened.
                Say(UpdateStep.Drained, $"gave up with {running} attempt(s) still running; they keep their leases");
                return ledger;
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<UpdateLedger> StopAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        if (Live() is null)
        {
            Say(UpdateStep.Stopped, "no runtime is running, so there is nothing to stop");
            return ledger with { StoppedAt = ledger.StoppedAt ?? clock.GetUtcNow() };
        }

        // The stop is a step of this verb and not a verb of its own. `jason runtime stop` answers whoever typed
        // it, on stdout — and here nobody typed it: the stdout an update writes to carries one JSON document,
        // and a second one in front of it is not a longer answer but an unreadable one. What the stop had to
        // say is kept, and printed on stderr if it went wrong, beside this update's own refusal.
        using var stopping = new StringWriter();
        var exit = await RuntimeStopCommand.RunAsync(env with { Out = stopping }, human: false, cancellationToken).ConfigureAwait(false);
        if (exit != ExitCodes.Success)
        {
            env.Error.Write(stopping.ToString());
            throw new UpdateException(UpdateCodes.RuntimeUnreachable, "The runtime would not stop, so nothing was replaced.");
        }

        Say(UpdateStep.Stopped, "the runtime is gone");
        return ledger with { StoppedAt = clock.GetUtcNow() };
    }

    /// <summary>
    /// One step's worth of file moving, with whatever the filesystem refuses turned into this product's own
    /// refusal.
    /// </summary>
    /// <remarks>
    /// A read-only install directory, a permission, a path that is a directory where a file belongs: raw, these
    /// arrive at a person as one line with no code, and they arrive during the steps that empty the install
    /// path. A code and a remedy is the least an update owes somebody in that window.
    /// </remarks>
    private static void OnDisk(string what, string path, string remedy, Action move)
    {
        try
        {
            move();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"{what} failed at '{path}': {error.Message} {remedy}",
                error);
        }
    }

    /// <summary>Moves the installed executable aside. Doing it twice is doing it once: the second finds it gone.</summary>
    /// <remarks>
    /// A copy of the same executable is left under <c>&lt;data&gt;/update/applier/</c> first, and it is the way
    /// out of the only window this design cannot avoid. Between this step and the swap the install path is
    /// <b>empty</b>: if the process performing the update dies there, the person is left with no <c>jason</c> on
    /// the PATH to type, and "run it again" is not advice they can follow. The copy is an ordinary Jason — run
    /// <c>jason update apply</c> from it and it reads the ledger and finishes the update it finds, because a
    /// resumed update takes its paths from the ledger rather than from wherever it happens to be running. The
    /// other way out needs no Jason at all: put <c>previous/</c> back by hand.
    /// </remarks>
    private UpdateLedger Keep(UpdateLedger ledger)
    {
        // Before anything is renamed: from the first move on, the file at the path this process started from may not
        // be the file it is running.
        WarmUp();

        OnDisk(
            "keeping the installed executable",
            update.Previous,
            $"Make sure {update.Root} is a directory this account can write to, then run `jason update apply` again.",
            () => Directory.CreateDirectory(update.Previous));

        if (File.Exists(ledger.InstallPath))
        {
            // Unless the copy is the program doing the copying. After a kill between this step and the rename,
            // the way out is to run that copy — and an executable cannot be written over while it is running,
            // on Windows at all and on any platform to no purpose: it is already the file it would be copied to.
            var copy = Path.Combine(update.Applier, ReleaseAssets.ExecutableName);
            var self = runningAs ?? Environment.ProcessPath;
            OnDisk(
                "putting a copy of the applier where a resumed update can run it",
                update.Applier,
                $"Nothing has been replaced yet. Make sure {update.Root} is a directory this account can write to, then run `jason update apply` again.",
                () =>
                {
                    Directory.CreateDirectory(update.Applier);
                    if (!Same(copy, self))
                    {
                        File.Copy(ledger.InstallPath, copy, overwrite: true);
                    }
                });

            SameVolume(ledger.InstallPath, ledger.PreviousPath);

            // What an earlier update kept beside its executable goes before this one's executable takes its place, so the
            // kept directory never holds one version's executable beside another's notices. Here, and not with the moves
            // below: until the executable has moved, every notice in the kept directory is the earlier update's.
            OnDisk(
                "clearing what an earlier update kept",
                update.Previous,
                $"Nothing has been replaced yet. Make sure {update.Root} is a directory this account can write to, then run `jason update apply` again.",
                () =>
                {
                    foreach (var notice in ReleaseAssets.Notices)
                    {
                        File.Delete(ReleaseAssets.NoticeBeside(ledger.PreviousPath, notice));
                    }
                });

            const string Aside = "moving the installed executable aside";
            OnDisk(
                Aside,
                ledger.InstallPath,
                $"Nothing has been replaced yet. Make sure this account may write to {Path.GetDirectoryName(ledger.InstallPath)}, then run `jason update apply` again.",
                () => ExecutableMove.Run(
                    env,
                    HeldFiles,
                    clock,
                    Pause,
                    Aside,
                    ledger.InstallPath,
                    "Nothing has been replaced yet. Run `jason update apply` again once the file is free.",
                    () => File.Move(ledger.InstallPath, ledger.PreviousPath, overwrite: true)));
            Say(UpdateStep.Kept, "the installed executable is put aside");
        }

        // Its notices follow it, each a move of its own that a resumed update finds done or does: once the executable has
        // moved, every notice still beside the install path is the one that belongs to it.
        foreach (var notice in ReleaseAssets.Notices)
        {
            var installed = ReleaseAssets.NoticeBeside(ledger.InstallPath, notice);
            if (File.Exists(installed))
            {
                const string NoticeAside = "moving the installed notices aside";
                OnDisk(
                    NoticeAside,
                    installed,
                    EmptyInstallPath(ledger),
                    () => ExecutableMove.Run(env, HeldFiles, clock, Pause, NoticeAside, installed, EmptyInstallPath(ledger), () => File.Move(installed, ReleaseAssets.NoticeBeside(ledger.PreviousPath, notice), overwrite: true)));
            }
        }

        return ledger;
    }

    /// <summary>
    /// The way out of the one window this design cannot avoid, where the install path holds no executable: the update
    /// again, from the applier's own copy when <c>jason</c> is not there to type, or the kept executable put back by hand.
    /// </summary>
    private string EmptyInstallPath(UpdateLedger ledger) =>
        $"There is nothing at the install path until this succeeds: run `jason update apply` again, from {Path.Combine(update.Applier, ReleaseAssets.ExecutableName)} if `jason` is no longer on your PATH, or put {ledger.PreviousPath} back by hand.";

    /// <summary>The swap: one rename, and the only moment the install path is not a whole executable ends here.</summary>
    private UpdateLedger Swap(UpdateLedger ledger)
    {
        if (!File.Exists(ledger.InstallPath))
        {
            if (!File.Exists(ledger.StagedPath))
            {
                throw new UpdateException(
                    UpdateCodes.ArtifactUnexpected,
                    $"There is nothing staged at {ledger.StagedPath} and nothing installed at {ledger.InstallPath}. "
                    + $"Put {ledger.PreviousPath} back to return to {ledger.FromVersion}.");
            }

            SameVolume(ledger.StagedPath, ledger.InstallPath);
            const string InPlace = "putting the new executable in place";
            var remedy = EmptyInstallPath(ledger);
            OnDisk(
                InPlace,
                ledger.InstallPath,
                remedy,
                () => ExecutableMove.Run(env, HeldFiles, clock, Pause, InPlace, ledger.StagedPath, remedy, () => File.Move(ledger.StagedPath, ledger.InstallPath)));
            _placed = true;
            Say(UpdateStep.Swapped, $"{ledger.ToVersion} is at the install path");
        }

        // The new notices follow it in, each a move of its own. A notice the release did not carry was never staged, and
        // the one installed under that name went aside with the old executable, so none of that name is left.
        foreach (var notice in ReleaseAssets.Notices)
        {
            var staged = ReleaseAssets.NoticeBeside(ledger.StagedPath, notice);
            if (File.Exists(staged))
            {
                const string NoticeInPlace = "putting the new notices in place";
                const string Remedy = "The new executable is in place without them: run `jason update apply` again.";
                OnDisk(
                    NoticeInPlace,
                    staged,
                    Remedy,
                    () => ExecutableMove.Run(env, HeldFiles, clock, Pause, NoticeInPlace, staged, Remedy, () => File.Move(staged, ReleaseAssets.NoticeBeside(ledger.InstallPath, notice), overwrite: true)));
            }
        }

        return ledger;
    }

    /// <summary>
    /// Each plugin package installed as an earlier version the release records is replaced by the release's own, and the
    /// one it replaced is kept under <c>update/previous/plugins/</c> for a rollback. A package the record does not know —
    /// built or edited here — is left as it is, and so is one that is current, later, unreadable or not installed; each
    /// says so on a line of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After <c>swapped</c>, so the step is always the new build's: the build that put the executable in place hands the
    /// rest over there. Before <c>started</c>, because no runtime runs between the two: the new runtime's first load reads
    /// the new package.
    /// </para>
    /// <para>
    /// It never fails the update. The executable is swapped and the runtime stopped, so a download must not hold that
    /// hostage: a package that could not be fetched, checked or moved is left as it was — the kept copy put back where it
    /// had gone aside — and its line says why and names the command that places it. A person's interrupt is not such a
    /// refusal: it stops the apply here, and the next one carries on from the ledger.
    /// </para>
    /// <para>
    /// The packages it replaces are written to the ledger before the first of them moves. Taken again after a kill, it
    /// reads each one's place: the release's digest is done, the earlier one's is still to do, nothing there beside a
    /// kept copy is a move half made, and anything else is somebody's since and is left.
    /// </para>
    /// </remarks>
    private async Task<UpdateLedger> ReplacePluginsAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        var pinned = request with { Version = ledger.ToVersion };
        UpdateManifest? manifest = null;
        string? unread = null;
        try
        {
            manifest = await ReadFeedAsync(pinned, cancellationToken).ConfigureAwait(false);
            if (manifest.Version != ledger.ToVersion)
            {
                unread = $"The release at {Address(pinned)} is {manifest.Version}, and this update is to {ledger.ToVersion}.";
                manifest = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Everything, as the skills catch everything: an update refused over a plugin package is the one outcome this
            // step must never have.
            unread = error.Message;
        }

        if (ledger.Plugins.Count > 0)
        {
            foreach (var plugin in ledger.Plugins)
            {
                await ReplaceAsync(plugin, manifest, unread, Address(pinned), cancellationToken).ConfigureAwait(false);
            }

            return ledger;
        }

        if (manifest is null)
        {
            env.Error.WriteLine(unread);
            Say(UpdateStep.Plugins, $"not checked — {ReasonSaid(unread ?? string.Empty)}; run: jason status");
            return ledger;
        }

        if (manifest.Plugins.Count == 0)
        {
            Say(UpdateStep.Plugins, $"{ledger.ToVersion} publishes no plugin package");
            return ledger;
        }

        var plan = PluginUpdates.Plan(manifest, env.Paths);
        var behind = plan
            .Where(plugin => plugin.State == PluginPlanState.Behind)
            .Select(plugin => new UpdatePluginReplacement(plugin.Id, plugin.Installed!.Value, plugin.InstalledDigest!, plugin.Package.Version, plugin.Package.Digest))
            .ToList();

        string? unrecorded = null;
        if (behind.Count > 0)
        {
            try
            {
                // What an earlier update kept goes before this one keeps anything: a rollback of this update puts back this
                // update's packages, and nothing of this update's is kept yet, since nothing below has moved.
                DirectoryTrees.Delete(update.PreviousPlugins);
                ledger = ledger with { Plugins = behind };
                Record(ledger, UpdateStep.Plugins);
            }
            catch (Exception error) when (error is UpdateException or IOException or UnauthorizedAccessException)
            {
                // Nothing moves that the ledger does not name, so a package that cannot be named there is not moved.
                unrecorded = error.Message;
                ledger = ledger with { Plugins = [] };
            }
        }

        foreach (var plugin in plan)
        {
            if (plugin.State != PluginPlanState.Behind)
            {
                Say(UpdateStep.Plugins, Left(plugin));
            }
            else if (unrecorded is not null)
            {
                NotReplaced(plugin.Id, plugin.Installed!.Value, unrecorded);
            }
            else
            {
                await ReplaceAsync(behind.Single(entry => entry.Id == plugin.Id), manifest, unread, Address(pinned), cancellationToken).ConfigureAwait(false);
            }
        }

        return ledger;
    }

    /// <summary>What a package the step leaves as it is says about itself.</summary>
    private static string Left(PluginPlan plugin) => plugin.State switch
    {
        PluginPlanState.NotInstalled => $"{plugin.Id} is not installed, so none was placed; run: jason plugin install {plugin.Id}",
        PluginPlanState.Unreadable => $"{plugin.Id} could not be read as a package, so it was left as it is",
        PluginPlanState.Differs => $"{plugin.Id} differs from every package a release published, so it was left as it is",
        PluginPlanState.Current => $"{plugin.Id} {plugin.Installed} is current",
        PluginPlanState.Later => $"{plugin.Id} {plugin.Installed} is later than the {plugin.Package.Version} this release publishes, so it was left as it is",
        _ => throw new ArgumentOutOfRangeException(nameof(plugin), plugin.State, "There is no such package state."),
    };

    /// <summary>
    /// One package the ledger names: replaced, or found replaced already, or left as it was with the reason and the command
    /// that places it.
    /// </summary>
    private async Task ReplaceAsync(UpdatePluginReplacement plugin, UpdateManifest? manifest, string? unread, Uri release, CancellationToken cancellationToken)
    {
        var target = env.Paths.PluginPackageDirectory(plugin.Id);
        var kept = update.PreviousPlugin(plugin.Id);
        var installed = PluginUpdates.InstalledDigest(target);
        if (string.Equals(installed, plugin.ToDigest, StringComparison.Ordinal))
        {
            Say(UpdateStep.Plugins, Replaced(plugin));
            return;
        }

        PluginInPlace expected;
        string? keep;
        if (string.Equals(installed, plugin.FromDigest, StringComparison.Ordinal))
        {
            expected = PluginInPlace.Digest(plugin.FromDigest);
            keep = kept;
        }
        else if (!Path.Exists(target) && Directory.Exists(kept))
        {
            // Moved aside, and the release's not yet in: a kill between the two moves. The one replaced is already kept.
            expected = PluginInPlace.Nothing;
            keep = null;
        }
        else
        {
            Say(UpdateStep.Plugins, $"{plugin.Id} changed since this update read it, so it was left as it is");
            return;
        }

        string cause;
        try
        {
            if (manifest is null)
            {
                throw new PluginInstallException(unread ?? string.Empty);
            }

            if (!manifest.Plugins.TryGetValue(plugin.Id, out var package) || !string.Equals(package.Digest, plugin.ToDigest, StringComparison.Ordinal))
            {
                throw new PluginInstallException($"The release no longer publishes {plugin.Id} {plugin.To} as this update read it. Nothing was changed.");
            }

            await PluginInstaller.PlaceAsync(env, release, plugin.Id, package, expected, keep, PackageMoves, clock, Pause, cancellationToken).ConfigureAwait(false);
            Say(UpdateStep.Plugins, Replaced(plugin));
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            cause = error.Message;
        }

        // Left as it was: a kept copy whose place is empty goes back into it. A rename that fails is said with the refusal,
        // as the installer's own move back is: it must not fail the update either. Nor is it cancelled, for the same reason
        // the installer's is not: a package left aside is worse than the wait.
        if (!Path.Exists(target) && Directory.Exists(kept))
        {
            string? stuck;
            try
            {
                stuck = HeldFileRetry.Run(() => MovePackageBack(kept, target), PackageMoves, clock, Pause, CancellationToken.None)?.Message;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                stuck = failure.Message;
            }

            if (stuck is not null)
            {
                cause = $"{cause} {kept} could not be put back as {target} either: {stuck}";
            }
        }

        NotReplaced(plugin.Id, plugin.From, cause);
    }

    private void NotReplaced(string id, SemanticVersion installed, string cause)
    {
        env.Error.WriteLine(cause);
        Say(UpdateStep.Plugins, $"{id} {installed} not replaced — {ReasonSaid(cause)}; run: jason plugin install {id} --replace");
    }

    private static string Replaced(UpdatePluginReplacement plugin) => $"{plugin.Id} {plugin.From} -> {plugin.To}";

    private async Task<UpdateLedger> StartAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        if (Descriptor() is not null && await RunningAttemptsAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            Say(UpdateStep.Started, "a runtime is already answering");
            return ledger;
        }

        var processes = env.Processes ?? RuntimeProcessControl.Instance;

        // The install path, and never this process: an update resumed by hand may be running from the copy of the
        // applier under the data directory, so "start me again" would start the old build from the wrong place.
        using var child = processes.Launch(env.Paths, [ledger.InstallPath]);

        var waited = TimeSpan.Zero;
        while (waited < StartTimeout)
        {
            if (await RunningAttemptsAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                Say(UpdateStep.Started, $"{ledger.ToVersion}");
                return ledger;
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
            waited += Poll;
        }

        throw new UpdateException(
            UpdateCodes.NotHealthy,
            $"{ledger.ToVersion} was installed but no runtime answered within {StartTimeout.TotalSeconds:F0}s. "
            + $"`jason update rollback` puts {ledger.FromVersion} back.");
    }

    /// <summary>
    /// The runtime now serving says it is the new version and that its database is ready — and, in the same
    /// answer, what its first start did to that database, which is what a rollback would have to undo.
    /// </summary>
    private async Task<UpdateLedger> HealthyAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        var info = await InfoAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UpdateException(
                UpdateCodes.NotHealthy,
                $"{ledger.ToVersion} is installed but no runtime is answering. `jason update rollback` puts {ledger.FromVersion} back.");

        if (!SemanticVersion.TryParse(info.RuntimeVersion, out var running) || running != ledger.ToVersion)
        {
            throw new UpdateException(
                UpdateCodes.NotHealthy,
                $"The runtime that came up says it is {info.RuntimeVersion}, and this update installed {ledger.ToVersion}. "
                + $"`jason update rollback` puts {ledger.FromVersion} back.");
        }

        if (info.Database.AppliedMigrations.Count == 0)
        {
            throw new UpdateException(
                UpdateCodes.NotHealthy,
                $"The runtime that came up as {ledger.ToVersion} has no migrations applied, so its database is not ready.");
        }

        Say(UpdateStep.Healthy, $"{info.RuntimeVersion}, {info.Database.AppliedMigrations.Count} migrations applied");
        return ledger with
        {
            BackupFile = info.Database.BackupFile,
            NewlyApplied = info.Database.NewlyApplied,

            // Where the chronicle stood the moment this version was declared healthy. A rollback compares it:
            // the journal is append-only and every state change writes a line, so an id that has moved means
            // work has been done that restoring a database would erase.
            ChronicleId = await NewestChronicleIdAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private UpdateLedger Done(UpdateLedger ledger)
    {
        Say(UpdateStep.Complete, $"{ledger.FromVersion} -> {ledger.ToVersion}");
        return ledger;
    }

    /// <summary>Whether two paths name the same file, as this platform compares names.</summary>
    private static bool Same(string path, string? other) =>
        other is not null
        && string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(other),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Which volume a path is on: the drive on Windows, and on every other platform the mount point it falls
    /// under — the longest one that really is a parent of it.
    /// </summary>
    /// <remarks>
    /// <c>Path.GetPathRoot</c> answers <c>/</c> for every absolute path on Linux and macOS, so a check built on
    /// it could not see two volumes there at all — and that is where it is needed most, because .NET's own
    /// <c>File.Move</c> across a mount boundary falls back to copying and deleting, which is the very
    /// not-quite-atomic swap this refusal exists to prevent. The mount points come from the operating system;
    /// the choosing is a pure function so that it can be tested on a machine with one volume.
    /// </remarks>
    public static string VolumeOf(string path, IReadOnlyList<string> mountPoints)
    {
        ArgumentNullException.ThrowIfNull(mountPoints);
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return Path.GetPathRoot(full) ?? string.Empty;
        }

        var under = mountPoints
            .Where(mount => Under(full, mount))
            .OrderByDescending(mount => mount.Length)
            .FirstOrDefault();

        return under ?? Path.GetPathRoot(full) ?? "/";
    }

    /// <summary>Whether a path really falls under a directory, rather than merely starting with its letters.</summary>
    private static bool Under(string path, string directory)
    {
        var mount = directory.TrimEnd(Path.DirectorySeparatorChar);
        if (mount.Length == 0)
        {
            return path.StartsWith('/');
        }

        return path.Equals(mount, StringComparison.Ordinal)
            || path.StartsWith(mount + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string VolumeOf(string path) =>
        VolumeOf(path, [.. DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName)]);

    /// <summary>A rename is atomic; a copy across volumes is not, and a half-copied executable is the one state this avoids.</summary>
    private static void SameVolume(string from, string to)
    {
        if (!string.Equals(VolumeOf(from), VolumeOf(to), StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException(
                UpdateCodes.CrossVolume,
                $"'{from}' and '{to}' are on different volumes, and an update moves files rather than copying them. "
                + "Set JASON_DATA_DIR to a directory on the same volume as the executable, or install Jason on the data directory's volume.");
        }
    }

    private RuntimeDescriptor? Descriptor() => new DescriptorReader(env.Paths).Read();

    /// <summary>The descriptor of a runtime that may still be running; <see cref="DescriptorProcess.Live"/> decides.</summary>
    private RuntimeDescriptor? Live() => DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance);

    /// <summary>An error envelope as a refusal says it: its code and its message.</summary>
    private static string Reason(string envelope) =>
        JsonSerializer.Deserialize<ErrorResponse>(envelope, JasonJson.Options)?.Error is { } error
            ? $"{error.Code}: {error.Message}"
            : envelope.Trim();

    private async Task<int?> RunningAttemptsAsync(CancellationToken cancellationToken) =>
        (await InfoAsync(cancellationToken).ConfigureAwait(false))?.Dispatcher.RunningAttempts;

    /// <summary>The newest line in the runtime's chronicle, or null where it cannot be asked.</summary>
    private async Task<string?> NewestChronicleIdAsync(CancellationToken cancellationToken)
    {
        var (_, answer) = await OperationRunner
            .SendAsync(env, Operations.JournalList, new JsonObject { ["limit"] = 1 }, cancellationToken)
            .ConfigureAwait(false);

        if (answer is not { IsSuccess: true })
        {
            return null;
        }

        var page = JsonSerializer.Deserialize<Page<JournalEntryDto>>(answer.Body, JasonJson.Options);
        return page?.Items.Count > 0 ? page.Items[0].Id : null;
    }

    private async Task<SystemInfoResponse?> InfoAsync(CancellationToken cancellationToken)
    {
        if (Live() is null)
        {
            return null;
        }

        var (_, answer) = await OperationRunner.SendAsync(env, Operations.SystemInfo, RequestBody.Empty(), cancellationToken)
            .ConfigureAwait(false);

        return answer is { IsSuccess: true }
            ? JsonSerializer.Deserialize<SystemInfoResponse>(answer.Body, JasonJson.Options)
            : null;
    }

    /// <summary>
    /// One line of the account, under the name of the step it belongs to. Every line begins with that name,
    /// because the same list is read by a person and by a script — and because what a step did depends on what
    /// it found: "drained" on an installation whose runtime was not running reads "no runtime is running, so
    /// there is nothing to drain", and a reader looking for the drain should still find it.
    /// </summary>
    private void Say(UpdateStep step, string what) => Say(step.ToString().ToLowerInvariant(), what);

    private void Say(string step, string what) =>
        Steps.Add(string.Create(CultureInfo.InvariantCulture, $"{step}: {what}"));
}
