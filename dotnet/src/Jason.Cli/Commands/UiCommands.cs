using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Cli.Skills;
using Jason.Cli.Ui;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>ui</c> verb group: the browser UI the runtime serves. Open it (a one-time link, paired once and then
/// bookmarked), see which browsers are paired, and unpair one. Only <c>install</c> changes the installation's files: it
/// places the release's build as the default UI, and it is the one verb here that is no operation.
/// </summary>
public static class UiCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var ui = new Command("ui", "The browser UI: place this release's build, open it, see which browsers are paired with this runtime, and unpair one.");
        ui.Subcommands.Add(Install(env, actor));
        ui.Subcommands.Add(Open(env, actor));
        ui.Subcommands.Add(Tokens(env, actor));
        ui.Subcommands.Add(Revoke(env, actor));
        return ui;
    }

    /// <summary>
    /// The UI the way the role skills arrive: one command places it, and the install guide, an update and
    /// <c>jason status</c>'s repair all run that command. The runtime picks its build on every request, so the next one
    /// serves what this placed, with no restart.
    /// </summary>
    private static Command Install(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "install",
            "Place this release's UI as the default build: downloaded from the release, checked against its checksums.txt, and swapped in whole. A build of your own in ui/custom is never touched, and is still served first.");
        var feed = new Option<string?>("--feed")
        {
            Description = "Read a different release: the https address of its manifest.json, or http on this machine. Default: the public release feed.",
        };
        var human = VerbOptions.Human();
        command.Options.Add(feed);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return InstallAsync(env, parseResult.GetValue(feed), parseResult.GetValue(human), cancellationToken);
        });
        return command;
    }

    private static async Task<int> InstallAsync(CliEnvironment env, string? feed, bool human, CancellationToken cancellationToken)
    {
        var built = SkillsSource.BuildVersion(env);
        if (!SemanticVersion.TryParse(built, out var version))
        {
            env.Error.WriteLine($"This build reports its version as '{built}', which is not a version, so it has no release to fetch a UI from.");
            return ExitCodes.ApiError;
        }

        // No development build is ever published, so the public feed is not asked for one; a feed named here can carry
        // one, which is how a local build is tried and how the tests run.
        if (feed is null && version.PreRelease == "dev")
        {
            env.Error.WriteLine(
                $"This build, {built}, is not a release, so no UI is published for it. Unzip a build of the UI ({UiAssetLayout.AssetName}, which `npm run package` in ui/ makes and every CI run uploads) into {env.Paths.UiCustomDirectory}, or name a release that carries one with --feed.");
            return ExitCodes.ApiError;
        }

        UiAssetInstalled placed;
        try
        {
            placed = await UiInstaller.PlaceAsync(env, UpdateCommands.Address(feed), version, ApiVersion.Current, cancellationToken).ConfigureAwait(false);
        }
        catch (UiInstallException refused)
        {
            env.Error.WriteLine(refused.Message);
            return ExitCodes.ApiError;
        }

        env.Out.WriteLine(human
            ? $"Placed the UI {placed.Manifest.Version} in {placed.Directory}."
            : JsonSerializer.Serialize(new UiInstallResponse(placed.Manifest.Version, placed.Manifest.ApiVersion, placed.Directory, placed.Digest, placed.FileCount), JasonJson.Options));
        return ExitCodes.Success;
    }

    private static Command Open(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("open", "Open the UI in your browser. The link pairs this browser once, within a minute; after that a bookmark of the page works.");
        var approval = new Option<string?>("--approval") { Description = "Open on one approval, as approval list shows its id." };
        var question = new Option<string?>("--question") { Description = "Open on one question, as decision list shows its id." };
        var noOpen = new Option<bool>("--no-open") { Description = "Print the link and open nothing; for an agent handing it to a person." };
        var human = VerbOptions.Human();
        command.Options.Add(approval);
        command.Options.Add(question);
        command.Options.Add(noOpen);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("approval_id", parseResult.GetValue(approval))
                .Set("decision_id", parseResult.GetValue(question));
            return RunOpenAsync(env, body, parseResult.GetValue(noOpen), parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The one verb that does something after the answer: it hands the link to the browser. Stdout stays the exact
    /// response; whether a browser opened is said on stderr, where diagnostics go.
    /// </summary>
    internal static async Task<int> RunOpenAsync(CliEnvironment env, JsonObject body, bool noOpen, bool human, CancellationToken cancellationToken)
    {
        var (exit, response) = await OperationRunner.SendAsync(env, Operations.UiOpen, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exit;
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(OperationRunner.ErrorText(response));
            return ExitCodes.ApiError;
        }

        var opened = RenderText.Read<UiOpenResponse>(response.Body);
        env.Out.WriteLine(human && opened?.Url is not null ? UiRenderers.Opened(opened) : response.Body);
        if (opened?.Url is null)
        {
            return ExitCodes.Success;
        }

        var handed = !noOpen && env.Opener is { } opener && opener.Open(opened.Url);
        env.Error.WriteLine(handed
            ? "Opened in your default browser. The link works once, within a minute."
            : $"Open this link in your browser within a minute (it works once): {opened.Url}");
        return ExitCodes.Success;
    }

    private static Command Tokens(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("tokens", "The browsers paired with this runtime: when each paired and when it was last used.");
        var human = VerbOptions.Human();
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return OperationRunner.RunAsync(env, Operations.UiTokens, RequestBody.Empty(), new RunOptions(parseResult.GetValue(human), UiRenderers.Tokens), cancellationToken);
        });
        return command;
    }

    private static Command Revoke(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("revoke", "Unpair a browser: its token stops working from the next request on.");
        var id = new Argument<string?>("token-id") { Description = "The token, as ui tokens shows it.", Arity = ArgumentArity.ZeroOrOne };
        var all = new Option<bool>("--all") { Description = "Every paired browser, including the one you are reading this in." };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(all);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var tokenId = parseResult.GetValue(id);
            var every = parseResult.GetValue(all);
            if (every == (tokenId is not null))
            {
                env.Error.WriteLine("Say which: a token id, or --all.");
                return Task.FromResult(ExitCodes.Usage);
            }

            var body = RequestBody.Empty().Set("token_id", tokenId).Set("all", every ? JsonValue.Create(true) : null);
            return OperationRunner.RunAsync(env, Operations.UiRevoke, body, new RunOptions(parseResult.GetValue(human), UiRenderers.Revoked), cancellationToken);
        });
        return command;
    }
}
