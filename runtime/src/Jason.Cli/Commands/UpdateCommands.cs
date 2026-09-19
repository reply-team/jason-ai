using System.CommandLine;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
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
    /// <summary>A manifest is a few hundred bytes; a feed that has not answered in this long is not going to.</summary>
    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(30);

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var update = new Command("update", "Find out whether a newer version of Jason has been released.");
        update.Subcommands.Add(Check(env, actor));
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

    public static async Task<int> CheckAsync(CliEnvironment env, string? feed, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var address = feed is null
            ? UpdateFeed.Default
            : Uri.TryCreate(feed, UriKind.Absolute, out var named) ? named : throw new UsageException("--feed must be an absolute address.");

        // The handler the rest of the CLI sends through, so a test that substitutes it sees this request too —
        // and sees that no other is made.
        using var client = env.HttpHandler is null ? new HttpClient() : new HttpClient(env.HttpHandler, disposeHandler: false);
        client.Timeout = FeedTimeout;

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
