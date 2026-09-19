using System.Globalization;
using System.Text.Json;
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
                $"An update to {ledger.ToVersion} is in flight at '{ledger.Step}'. Finish it with `jason update apply`, "
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
    public static string ResolveInstallPath()
    {
        var self = SelfExecutable.Command;
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
        var address = request.Version is { } pinned ? UpdateFeed.PinnedFor(request.Feed, pinned) : request.Feed;
        return await new UpdateFeed(client).ReadAsync(address, cancellationToken).ConfigureAwait(false);
    }

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
        var manifest = await ReadFeedAsync(request with { Version = ledger.ToVersion }, cancellationToken).ConfigureAwait(false);
        if (manifest.Version != ledger.ToVersion)
        {
            throw new UpdateException(
                UpdateCodes.ArtifactUnexpected,
                $"The feed now offers {manifest.Version}, and this update is for {ledger.ToVersion}.");
        }

        using var client = env.HttpHandler is null ? new HttpClient() : new HttpClient(env.HttpHandler, disposeHandler: false);
        var staged = await new UpdateStager(client)
            .StageAsync(manifest, ReleaseAssets.CurrentRid!, request.Feed, update, cancellationToken)
            .ConfigureAwait(false);

        Say($"staged {ledger.ToVersion}");
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
            Say("no runtime is running, so there is nothing to drain");
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
                Say("drained");
                return ledger;
            }

            if (request.Drain == TimeSpan.Zero || clock.GetUtcNow() >= deadline)
            {
                // Said rather than hidden: what is still running keeps its lease, and the next runtime will
                // finish it or the enforcer will take it back. Either way a person should know it happened.
                Say($"drain gave up with {running} attempt(s) still running; they keep their leases");
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
            Say("no runtime is running, so there is nothing to stop");
            return ledger with { StoppedAt = ledger.StoppedAt ?? clock.GetUtcNow() };
        }

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, cancellationToken).ConfigureAwait(false);
        if (exit != ExitCodes.Success)
        {
            throw new UpdateException(UpdateCodes.RuntimeUnreachable, "The runtime would not stop, so nothing was replaced.");
        }

        Say("stopped");
        return ledger with { StoppedAt = clock.GetUtcNow() };
    }

    /// <summary>Moves the installed executable aside. Doing it twice is doing it once: the second finds it gone.</summary>
    private UpdateLedger Keep(UpdateLedger ledger)
    {
        Directory.CreateDirectory(update.Previous);
        if (File.Exists(ledger.InstallPath))
        {
            SameVolume(ledger.InstallPath, ledger.PreviousPath);
            File.Move(ledger.InstallPath, ledger.PreviousPath, overwrite: true);
            Say("kept the installed executable");
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
            File.Move(ledger.StagedPath, ledger.InstallPath);
            Say($"swapped in {ledger.ToVersion}");
        }

        return ledger;
    }

    private async Task<UpdateLedger> StartAsync(UpdateLedger ledger, CancellationToken cancellationToken)
    {
        if (Descriptor() is not null && await RunningAttemptsAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            Say("a runtime is already answering");
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
                Say($"started {ledger.ToVersion}");
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

        Say($"healthy: {info.RuntimeVersion}, {info.Database.AppliedMigrations.Count} migrations applied");
        return ledger with
        {
            BackupFile = info.Database.BackupFile,
            NewlyApplied = info.Database.NewlyApplied,
        };
    }

    private UpdateLedger Done(UpdateLedger ledger)
    {
        Say($"{ledger.FromVersion} → {ledger.ToVersion}");
        return ledger;
    }

    /// <summary>A rename is atomic; a copy across volumes is not, and a half-copied executable is the one state this avoids.</summary>
    private static void SameVolume(string from, string to)
    {
        if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
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

    private void Say(string what) => Steps.Add(string.Create(CultureInfo.InvariantCulture, $"{what}"));
}
