using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Cli.Link;
using Jason.Cli.Skills;
using Jason.Cli.Ui;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>ui</c> verb group. <c>open</c> opens Jason Desktop at a place by the desktop's own link and asks the runtime
/// nothing; <c>install</c> places the release's build, which Jason Desktop draws, and asks the runtime nothing either;
/// <c>pair</c>, <c>tokens</c> and <c>revoke</c> are the runtime's three <c>ui</c> operations: a one-time link that pairs a
/// page, the pages paired, and an unpairing. Jason Desktop's window pairs through <c>pair</c>, and so does a web UI of
/// one's own, which <c>open --web</c> opens in a browser.
/// </summary>
public static class UiCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var ui = new Command("ui", "Jason Desktop and the UI it draws: open Jason Desktop at what waits or at one item, place this release's UI build, and pair a page with this runtime, see which browsers are paired, and unpair one.");
        ui.Subcommands.Add(Install(env, actor));
        ui.Subcommands.Add(Open(env, actor));
        ui.Subcommands.Add(Pair(env, actor));
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
        if (feed is null && version.IsDevelopment)
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
        var command = new Command("open", "Open Jason Desktop at what waits, or at one approval or question, by the desktop's own jason-desktop: link; never a browser. It asks the runtime nothing. Where no Jason Desktop that takes its links is installed for this account it opens nothing and refuses with desktop_not_installed; on Linux, where the system would hand its link to another program or a menu entry that is gone, or could not start Jason Desktop's entry, with desktop_links_elsewhere. Prints the address on stdout; exits 0 opened (or handed to the system and not yet answered, said on stderr), 1 refused, 2 usage, and 3 only under --web, which asks the runtime what ui pair asks.");
        var approval = new Option<string?>("--approval") { Description = "Open at one approval, by its id as approval list shows it." };
        var question = new Option<string?>("--question") { Description = "Open at one question, by its id as decision list shows it." };
        var noOpen = new Option<bool>("--no-open") { Description = "Print the address and open nothing; for an agent handing it to a person." };
        var web = new Option<bool>("--web") { Description = "Open a web UI of your own instead, a build in the custom UI directory (ui/custom), in your browser: the link pairs that browser once, within a minute, as ui pair's does. Refused, asking and opening nothing, where no build of your own is installed." };
        var human = VerbOptions.Human();
        command.Options.Add(approval);
        command.Options.Add(question);
        command.Options.Add(noOpen);
        command.Options.Add(web);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var approvalId = parseResult.GetValue(approval);
            var questionId = parseResult.GetValue(question);
            if (approvalId is not null && questionId is not null)
            {
                env.Error.WriteLine("Say one place: --approval or --question, not both.");
                return Task.FromResult(ExitCodes.Usage);
            }

            if (NoItem(approvalId, questionId) is { } refused)
            {
                env.Out.WriteLine(CliErrors.Serialize(refused.Code, refused.Message, retryable: false));
                env.Error.WriteLine(refused.Message);
                return Task.FromResult(ExitCodes.ApiError);
            }

            if (parseResult.GetValue(web))
            {
                var body = RequestBody.Empty().Set("approval_id", approvalId).Set("decision_id", questionId);
                return OpenWebAsync(env, body, parseResult.GetValue(noOpen), parseResult.GetValue(human), cancellationToken);
            }

            var target = approvalId is not null ? new AttentionTarget(AttentionAct.Open, AttentionKind.Approval, approvalId)
                : questionId is not null ? new AttentionTarget(AttentionAct.Open, AttentionKind.Question, questionId)
                : new AttentionTarget(AttentionAct.List, null, null);
            return Task.FromResult(OpenDesktop(env, DesktopLinks.Address(target)!, parseResult.GetValue(noOpen), parseResult.GetValue(human)));
        });

        return command;
    }

    /// <summary>
    /// The refusal for an id that is not one of its kind in the shape the runtime gives it, or null: the address is made
    /// from the id and the runtime is not asked, so text that names no item is refused here, by the code and exit the
    /// runtime refuses an id it does not know with, as every other verb that takes an id ends on one.
    /// </summary>
    private static DesktopRefusal? NoItem(string? approvalId, string? questionId) =>
        approvalId is not null && !PublicId.IsWellFormed(AttentionLinks.ApprovalPrefix, approvalId)
            ? new("approval_not_found", $"--approval names no approval: an approval's id is {AttentionLinks.ApprovalPrefix}_ and 26 letters and digits, as approval list shows it.")
        : questionId is not null && !PublicId.IsWellFormed(AttentionLinks.QuestionPrefix, questionId)
            ? new("decision_not_found", $"--question names no question: a question's id is {AttentionLinks.QuestionPrefix}_ and 26 letters and digits, as decision list shows it.")
        : null;

    /// <summary>
    /// Jason Desktop at <paramref name="address"/>: the address on stdout where the desktop takes it, compact JSON or with
    /// --human the address alone; where it does not, or the opener refused it, the refusal's envelope, as every local
    /// refusal of the CLI's is said.
    /// </summary>
    private static int OpenDesktop(CliEnvironment env, string address, bool noOpen, bool human)
    {
        if (DesktopLinks.Open(env, address, handOn: !noOpen) is { } refused)
        {
            env.Out.WriteLine(CliErrors.Serialize(refused.Code, refused.Message, retryable: false));
            env.Error.WriteLine(refused.Message);
            return ExitCodes.ApiError;
        }

        env.Out.WriteLine(human ? address : JsonSerializer.Serialize(new DesktopAddress(address), JasonJson.Options));
        return ExitCodes.Success;
    }

    /// <summary>
    /// A web UI of one's own: only where the custom directory holds a build the runtime serves first (an index, and a
    /// manifest <see cref="UiManifest.Read"/> takes), the link <c>ui pair</c> prints handed to the browser. The release's
    /// own build is never opened in a browser, so with no such build nothing is asked or opened: a custom directory the
    /// runtime refuses is served the release's build in its place. Stdout stays the exact response; whether a browser opened
    /// is said on stderr.
    /// </summary>
    private static async Task<int> OpenWebAsync(CliEnvironment env, JsonObject body, bool noOpen, bool human, CancellationToken cancellationToken)
    {
        var custom = env.Paths.UiCustomDirectory;
        if (!ServedFirst(custom))
        {
            var message = $"No web UI of your own is installed ({custom}); Jason runs in Jason Desktop.";
            env.Out.WriteLine(CliErrors.Serialize(CliErrors.CustomUiNotInstalled, message, retryable: false));
            env.Error.WriteLine(message);
            return ExitCodes.ApiError;
        }

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

    /// <summary>Whether the runtime serves <paramref name="custom"/> before the release's build, by the rule it chooses by.</summary>
    private static bool ServedFirst(string custom)
    {
        try
        {
            return File.Exists(Path.Combine(custom, UiAssetLayout.IndexFile)) && UiManifest.Read(custom) is not null;
        }
        catch (Exception exception) when (exception is UiManifestException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The runtime's <c>ui.open</c>: a link that pairs one page with this runtime, once, within a minute, printed and never
    /// opened. Jason Desktop loads it into its own window, and a web UI of one's own is paired by it.
    /// </summary>
    private static Command Pair(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("pair", "Print a link that pairs one page of the UI with this runtime, once, within a minute, and open nothing: Jason Desktop pairs its own window with it, and so does a web UI of your own.");
        var approval = new Option<string?>("--approval") { Description = "Pair at the address of one approval, #/attention/approvals/<id>, as approval list shows its id." };
        var question = new Option<string?>("--question") { Description = "Pair at the address of one question, #/attention/questions/<id>, as decision list shows its id." };
        var human = VerbOptions.Human();
        command.Options.Add(approval);
        command.Options.Add(question);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("approval_id", parseResult.GetValue(approval))
                .Set("decision_id", parseResult.GetValue(question));
            return OperationRunner.RunAsync(env, Operations.UiOpen, body, new RunOptions(parseResult.GetValue(human), PairRendered), cancellationToken);
        });
        return command;
    }

    private static string? PairRendered(string json) => RenderText.Read<UiOpenResponse>(json) is { Url: not null } opened ? UiRenderers.Opened(opened) : null;

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

/// <summary>What <c>ui open</c> prints: the Jason Desktop address it opened, or would open under <c>--no-open</c>.</summary>
public sealed record DesktopAddress(string Address);
