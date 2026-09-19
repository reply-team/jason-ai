using System.CommandLine;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
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
            "Install the newest release: download it, stop this machine's runtime, put the new executable in place and start it again.");
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
            applier = new UpdateApplier(env, new UpdatePaths(env.Paths), TimeProvider.System, UpdateApplier.ResolveInstallPath());
            var ledger = await applier.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
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
    }

    private static Command RollBack(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "rollback",
            "Put the version this machine updated from back: the executable it replaced, and - if that update migrated the database - the backup it wrote.");
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

    private static Uri Address(string? feed) => feed is null
        ? UpdateFeed.Default
        : Uri.TryCreate(feed, UriKind.Absolute, out var named) ? named : throw new UsageException("--feed must be an absolute address.");

    public static async Task<int> CheckAsync(CliEnvironment env, string? feed, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var address = Address(feed);

        // The handler the rest of the CLI sends through, so a test that substitutes it sees this request too —
        // and sees that no other is made.
        using var client = env.HttpHandler is null ? new HttpClient() : new HttpClient(env.HttpHandler, disposeHandler: false);
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
        var answer = new UpdateCheckResponse(
            running.ToString(),
            manifest.Version.ToString(),
            manifest.IsNewerThan(running),
            DateTimeOffset.UtcNow,
            manifest.ReleaseNotesUrl);

        env.Out.WriteLine(human ? Sentence(answer) : JsonSerializer.Serialize(answer, JasonJson.Options));
        return ExitCodes.Success;
    }

    private static string Sentence(UpdateCheckResponse answer)
    {
        if (!answer.Available)
        {
            return $"Up to date: this is {answer.Current}, and the newest release is {answer.Latest}.";
        }

        var notes = answer.ReleaseNotesUrl is null ? string.Empty : $" Release notes: {answer.ReleaseNotesUrl}";
        return $"A newer version is available: {answer.Latest} (this is {answer.Current}).{notes}";
    }
}
