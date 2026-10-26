using System.CommandLine;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
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
            ActorOption.Parse(parseResult.GetValue(actor));
            return CheckAsync(env, parseResult.GetValue(feed), parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }

    private static Command Apply(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "apply",
            "Install the newest release: download it, stop this machine's runtime, put the new executable and its licence files in place, start it again, redeploy the skills this machine recorded at the new release's tag, and place the release's UI. If the skills or the UI cannot be placed the update still stands; the step's line says why, and 'jason status' names the repair.");
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
        var human = VerbOptions.Human();
        command.Options.Add(feed);
        command.Options.Add(version);
        command.Options.Add(drain);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            return ApplyAsync(
                env,
                parseResult.GetValue(feed),
                parseResult.GetValue(version),
                parseResult.GetValue(drain),
                parseResult.GetValue(human),
                cancellationToken);
        });

        return command;
    }

    public static async Task<int> ApplyAsync(
        CliEnvironment env,
        string? feed,
        string? version,
        int? drainSeconds,
        bool human,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        // Before anything at all: before the ledger is read, a byte is fetched, or the runtime is asked to drain.
        if (RefusedAsManaged(env, "apply"))
        {
            return ExitCodes.ApiError;
        }

        var request = new UpdateRequest(
            Address(feed),
            Wanted(version),
            TimeSpan.FromSeconds(drainSeconds is { } seconds and >= 0
                ? seconds
                : drainSeconds is null
                    ? DefaultDrainSeconds
                    : throw new UsageException("--drain-seconds cannot be negative.")));

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
                return await HandOverAsync(env, applier.Steps, ledger, request, human, cancellationToken).ConfigureAwait(false);
            }

            Print(env, human, applier.Steps, ledger);
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
    /// </remarks>
    private static async Task<int> HandOverAsync(
        CliEnvironment env,
        IReadOnlyList<string> steps,
        UpdateLedger ledger,
        UpdateRequest request,
        bool human,
        CancellationToken cancellationToken)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        FinishedCommand finished;
        try
        {
            finished = await processes
                .RunAsync(env.Paths, [ledger.InstallPath, "update", "apply", "--feed", request.Feed.AbsoluteUri], env.Error, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            throw NotFinished(env, ledger, $"it could not be run to finish the update: {error.Message}", error);
        }

        if (finished.ExitCode is not (ExitCodes.Success or ExitCodes.ApiError or ExitCodes.Usage or ExitCodes.RuntimeUnavailable)
            || Document(finished.StandardOutput) is not { } document)
        {
            throw NotFinished(
                env,
                ledger,
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
            "Put the version this machine updated from back: the executable it replaced with its licence files, and - if that update migrated the database - the backup it wrote.");
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
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
            ActorOption.Parse(parseResult.GetValue(actor));
            return StatusAsync(env, parseResult.GetValue(human));
        });

        return command;
    }

    public static async Task<int> RollBackAsync(CliEnvironment env, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (RefusedAsManaged(env, "rollback"))
        {
            return ExitCodes.ApiError;
        }

        var rollback = new UpdateRollback(env, new UpdatePaths(env.Paths), TimeProvider.System);
        try
        {
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

    private static void Print(CliEnvironment env, bool human, IReadOnlyList<string> steps, UpdateLedger ledger)
    {
        var answer = new UpdateApplyResponse(
            ledger.FromVersion.ToString(),
            ledger.ToVersion.ToString(),
            SnakeCase(ledger.Step),
            [.. steps]);

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
            first?.ToString());

        var passThrough = first is { } version ? UpdateApplier.PassThrough(manifest.Version, running, version, pinned: false) : null;
        env.Out.WriteLine(human ? Sentence(answer, owner, passThrough) : JsonSerializer.Serialize(answer, JasonJson.Options));
        return ExitCodes.Success;
    }

    /// <summary>What updates an installation that is Jason's own.</summary>
    private const string OwnUpgrade = "jason update apply";

    private static string Sentence(UpdateCheckResponse answer, PackageOwner? owner, string? passThrough)
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
