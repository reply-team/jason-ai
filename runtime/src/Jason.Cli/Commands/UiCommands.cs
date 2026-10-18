using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>ui</c> verb group: the browser UI the runtime serves. Open it (a one-time link, paired once and then
/// bookmarked), see which browsers are paired, and unpair one. Nothing here changes the installation's files.
/// </summary>
public static class UiCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var ui = new Command("ui", "The browser UI: open it, see which browsers are paired with this runtime, and unpair one.");
        ui.Subcommands.Add(Open(env, actor));
        ui.Subcommands.Add(Tokens(env, actor));
        ui.Subcommands.Add(Revoke(env, actor));
        return ui;
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
    private static async Task<int> RunOpenAsync(CliEnvironment env, JsonObject body, bool noOpen, bool human, CancellationToken cancellationToken)
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
