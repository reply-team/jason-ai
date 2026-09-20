using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Discovery;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
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
/// The order is staged, drained, stopped, kept, swapped, started, healthy, complete. Between <c>kept</c> and
/// <c>swapped</c> the install path is <b>empty</b> — the old executable has been moved aside and the new one is
/// not in place yet — and that window is one rename wide. It is also the one window a person cannot get out of
/// by typing <c>jason update</c>, because there is no <c>jason</c> on the PATH to type: the way out is the copy
/// of the applier under the data directory, or putting <c>previous/</c> back by hand. Both are on the page.
/// </para>
/// <para>
/// Health is asked of the runtime rather than of the file. A file that prints the right version proves that a
/// file exists; what an update has to know is that the <em>runtime now serving</em> is the new build and that
/// its database is ready — and the same answer carries what a rollback would need to undo the migration it just
/// performed.
/// </para>
/// </remarks>
public sealed class UpdateApplier(CliEnvironment env, UpdatePaths update, TimeProvider clock, string installPath)
{
    /// <summary>How often the drain and the stop are re-read while waiting for them.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    /// <summary>How long to wait for a new runtime to publish its descriptor and answer.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Everything the applier did, in order, for the caller to print.</summary>
    public List<string> Steps { get; } = [];

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
            ledger.Write(update.Ledger);
            ledger = await TakeAsync(step, ledger, request, cancellationToken).ConfigureAwait(false);
            ledger.Write(update.Ledger);
        }

        return ledger;
    }

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
                $"{manifest.Version} cannot be applied to {SemanticVersion.Current} directly: install {floor} first.");
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
    /// was changed for: the applier runs from a copy of itself under the data directory, so "where am I?" is
    /// exactly the question it must not ask about the file it is replacing.
    /// </remarks>
    public static string ResolveInstallPath() => ResolveInstallPath(SelfExecutable.Command);

    /// <summary>
    /// The same decision over the command it depends on, so that what a muxed installation refuses can be
    /// asked without being one: the answer is about the shape of the command, and nothing else.
    /// </summary>
    public static string ResolveInstallPath(IReadOnlyList<string> self)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self.Count != 1)
        {
            throw new UpdateException(
                UpdateCodes.NotUpdatable,
                "This Jason is running through `dotnet`, so there is no single executable to replace. "
                + "Update the build you run it from, or install a published release with the one-liner on the release page.");
        }

        return self[0];
    }

    private async Task<UpdateManifest> ReadFeedAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        using var client = env.HttpHandler is null ? new HttpClient() : new HttpClient(env.HttpHandler, disposeHandler: false);
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

        using var client = env.HttpHandler is null ? new HttpClient() : new HttpClient(env.HttpHandler, disposeHandler: false);
        var staged = await new UpdateStager(client)
            .StageAsync(manifest, ReleaseAssets.CurrentRid!, Address(release), update, cancellationToken)
            .ConfigureAwait(false);

        Say(UpdateStep.Staged, $"{ledger.ToVersion}");
        return ledger with { StagedPath = staged };
    }

    /// <summary>
    /// Tells the runtime to claim nothing new and waits for what it is holding, up to the bound. A runtime that
    /// is not running is already drained: <c>jason update</c> is the same command whether one is up or not.
    /// </summary>
    private async Task<UpdateLedger> DrainAsync(UpdateLedger ledger, UpdateRequest request, CancellationToken cancellationToken)
    {
        if (Descriptor() is null)
        {
            Say(UpdateStep.Drained, "no runtime is running, so there is nothing to drain");
            return ledger;
        }

        var (code, body) = await OperationRunner.SendAsync(env, Operations.SystemDrain, RequestBody.Empty(), cancellationToken)
            .ConfigureAwait(false);
        if (code != ExitCodes.Success)
        {
            throw new UpdateException(UpdateCodes.RuntimeUnreachable, $"The runtime would not drain: {body}");
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
        var descriptor = Descriptor();
        if (descriptor is null)
        {
            Say(UpdateStep.Stopped, "no runtime is running, so there is nothing to stop");
            return ledger with { StoppedAt = ledger.StoppedAt ?? clock.GetUtcNow() };
        }

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, cancellationToken).ConfigureAwait(false);
        if (exit != ExitCodes.Success)
        {
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
        OnDisk(
            "keeping the installed executable",
            update.Previous,
            $"Make sure {update.Root} is a directory this account can write to, then run `jason update apply` again.",
            () => Directory.CreateDirectory(update.Previous));

        if (File.Exists(ledger.InstallPath))
        {
            Directory.CreateDirectory(update.Applier);

            // Unless the copy is the program doing the copying. After a kill between this step and the rename,
            // the way out is to run that copy — and an executable cannot be written over while it is running,
            // on Windows at all and on any platform to no purpose: it is already the file it would be copied to.
            var copy = Path.Combine(update.Applier, ReleaseAssets.ExecutableName);
            if (!Same(copy, Environment.ProcessPath))
            {
                File.Copy(ledger.InstallPath, copy, overwrite: true);
            }

            SameVolume(ledger.InstallPath, ledger.PreviousPath);
            OnDisk(
                "moving the installed executable aside",
                ledger.InstallPath,
                $"Nothing has been replaced yet. Make sure this account may write to {Path.GetDirectoryName(ledger.InstallPath)}, then run `jason update apply` again.",
                () => File.Move(ledger.InstallPath, ledger.PreviousPath, overwrite: true));
            Say(UpdateStep.Kept, "the installed executable is put aside");
        }

        return ledger;
    }

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
            OnDisk(
                "putting the new executable in place",
                ledger.InstallPath,
                $"There is nothing at the install path until this succeeds: run `jason update apply` again, from {Path.Combine(update.Applier, ReleaseAssets.ExecutableName)} if `jason` is no longer on your PATH, or put {ledger.PreviousPath} back by hand.",
                () => File.Move(ledger.StagedPath, ledger.InstallPath));
            Say(UpdateStep.Swapped, $"{ledger.ToVersion} is at the install path");
        }

        return ledger;
    }

    private async Task<UpdateLedger> StartAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        if (Descriptor() is not null && await RunningAttemptsAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            Say(UpdateStep.Started, "a runtime is already answering");
            return ledger;
        }

        var processes = env.Processes ?? RuntimeProcessControl.Instance;

        // The install path, and never this process: the applier runs from a copy of itself under the data
        // directory, so "start me again" would start the old build from the wrong place.
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
        Say(UpdateStep.Complete, $"{ledger.FromVersion} → {ledger.ToVersion}");
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
        if (Descriptor() is null)
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
    private void Say(UpdateStep step, string what) =>
        Steps.Add(string.Create(CultureInfo.InvariantCulture, $"{step.ToString().ToLowerInvariant()}: {what}"));
}
