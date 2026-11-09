using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Cli.Plugins;
using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>plugin</c> verb group: what the runtime has loaded, the one explicit act that replaces it, and the one verb that
/// is no operation. <c>install</c> places a package the release publishes in the plugins directory — the only verb here that
/// writes files, and the eighth name in this client with no operation behind it — and then asks for the same reload
/// <c>reload</c> asks for. A package of a person's own is still a directory copied in by hand, and a reload that finds a
/// broken package changes nothing.
/// </summary>
public static class PluginCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var plugin = new Command("plugin", "Place a plugin package the release publishes, show the plugins the runtime has loaded, and load them again from disk.");
        plugin.Subcommands.Add(Install(env, actor));
        plugin.Subcommands.Add(List(env, actor));
        plugin.Subcommands.Add(Reload(env, actor));
        return plugin;
    }

    /// <summary>
    /// The verb's help is its contract, published where it is used: what it fetches, what it checks, what it never touches.
    /// </summary>
    private static Command Install(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "install",
            "Place a plugin package this release publishes in the plugins directory, then ask the runtime to load it. "
            + "It downloads the package the release's manifest.json names for the id, from the release's own directory, and "
            + "checks it twice: against its line in the release's checksums.txt, and, unpacked, against the content digest the "
            + "manifest gives. Only then is it moved in whole as plugins/<id>; on any refusal nothing is placed and exit 1 says "
            + "why. A different package already there under that id (edited or built on this machine, or another version) is "
            + "kept unless --replace says to swap it whole; the release's own package already there is left as it is. Other "
            + "packages, settings, grants and data are never touched, and no runtime is started: one that answers is asked to "
            + "reload, and otherwise the package loads at the runtime's next start. A runtime that answers and refuses the "
            + "reload leaves the package placed, and the verb exits 1 with load: rejected in its body and the runtime's refusal "
            + "under error. After a reload that answered, the body also carries the plugin's status, problems and capabilities "
            + "as the runtime answered them, and the settings file this CLI uses; with --human, each problem is named with the "
            + "runtime's words and each capability granted nothing of with the key that grants it.");
        var id = new Argument<string>("id") { Description = "The plugin's id, as the release's manifest names it." };
        var feed = new Option<string?>("--feed")
        {
            Description = "Read a different release: the https address of its manifest.json, or http on this machine. Default: the public release feed.",
        };
        var replace = new Option<bool>("--replace")
        {
            Description = "Swap a different package already in plugins/<id> for the release's. Without it, such a package is kept and nothing is changed.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(feed);
        command.Options.Add(replace);
        command.Options.Add(reason);
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var by = ActorOption.Default(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env.AccountName);
            var named = parseResult.GetValue(id)!;
            if (!PluginIds.IsWellFormed(named))
            {
                throw new UsageException($"'{named}' is not a plugin's id: a lowercase letter, then lowercase letters, digits or dashes.");
            }

            return InstallAsync(
                env,
                new InstallRequest(named, parseResult.GetValue(feed), parseResult.GetValue(replace), parseResult.GetValue(reason), by, parseResult.GetValue(human)),
                cancellationToken);
        });
        return command;
    }

    private sealed record InstallRequest(string Id, string? Feed, bool Replace, string? Reason, JsonObject? Actor, bool Human);

    private static async Task<int> InstallAsync(CliEnvironment env, InstallRequest request, CancellationToken cancellationToken)
    {
        var built = SkillsSource.BuildVersion(env);
        if (!SemanticVersion.TryParse(built, out var version))
        {
            env.Error.WriteLine($"This build reports its version as '{built}', which is not a version, so it has no release to fetch a plugin package from.");
            return ExitCodes.ApiError;
        }

        // No development build is ever published, so the public feed is not asked for one; a feed named here can carry
        // one, which is how a local build is tried and how the tests run.
        if (request.Feed is null && version.IsDevelopment)
        {
            env.Error.WriteLine(
                $"This build, {built}, is not a release, so no plugin package is published for it. Copy a package's directory into {env.Paths.PluginsDirectory} and run `jason plugin reload`, or name a release that carries one with --feed.");
            return ExitCodes.ApiError;
        }

        PluginPlaced placed;
        try
        {
            placed = await PluginInstaller.InstallAsync(env, UpdateCommands.Address(request.Feed), version, request.Id, request.Replace, cancellationToken).ConfigureAwait(false);
        }
        catch (PluginInstallException refused)
        {
            env.Error.WriteLine(refused.InPlaceDiffers && !request.Replace
                ? $"{refused.Message} --replace replaces it with the release's."
                : refused.Message);
            return ExitCodes.ApiError;
        }

        var (load, exit, refusal, loaded) = await LoadAsync(env, placed, request, cancellationToken).ConfigureAwait(false);
        var settingsFile = env.Paths.UserSettingsFile;
        env.Out.WriteLine(request.Human
            ? RenderText.Lines(
                new[]
                {
                    $"{Said(placed)} {Said(load)}",
                    loaded is null ? null : PluginRenderers.Problems(loaded),
                    loaded is null ? null : PluginRenderers.Ungranted(loaded.Id, loaded.Capabilities, settingsFile),
                }.OfType<string>())
            : JsonSerializer.Serialize(
                new PluginInstallResponse(placed.Id, placed.Version.ToString(), placed.Directory, placed.Digest, placed.FileCount, placed.Placement, load, refusal)
                {
                    Status = loaded?.Status,
                    Problems = loaded?.Problems,
                    Capabilities = loaded?.Capabilities,
                    SettingsFile = load == PluginLoad.Activated ? settingsFile : null,
                },
                JasonJson.Options));
        return exit;
    }

    /// <summary>
    /// The reload <c>plugin reload</c> asks for, and its three answers. A runtime that answers and refuses is the verb's
    /// refusal: the package stays placed, and the runtime's envelope comes back to go in the body. A runtime that refuses
    /// the descriptor's token, or none that answers, is no failure of this verb: the package is placed, the runtime reads it
    /// when it starts, and starting one is the person's to decide. A reload performed answers the registry, and the placed
    /// plugin is read out of it as the runtime answered it.
    /// </summary>
    private static async Task<(PluginLoad Load, int Exit, ErrorBody? Refusal, PluginDto? Loaded)> LoadAsync(CliEnvironment env, PluginPlaced placed, InstallRequest request, CancellationToken cancellationToken)
    {
        var body = RequestBody.Empty()
            .Set("reason", request.Reason)
            .SetActor(request.Actor);

        // The envelope of a runtime that could not be asked is read here, not printed: it is an answer to this verb.
        using var unasked = new StringWriter();
        var (_, response) = await OperationRunner.SendAsync(env with { Out = unasked }, Operations.PluginReload, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            env.Error.WriteLine(Envelope(unasked.ToString())?.Code == CliErrors.Unauthorized
                ? $"The runtime at the descriptor's address refused the descriptor's capability token, so {placed.Id} loads at the runtime's next start."
                : $"No runtime answered, so {placed.Id} loads at the runtime's next start, or at `jason plugin reload` once one runs; then `jason plugin list --human` says what it asks for and what it is granted.");
            return (PluginLoad.AtNextStart, ExitCodes.Success, null, null);
        }

        if (!response.IsSuccess)
        {
            var refusal = OperationRunner.ErrorText(response);
            env.Error.WriteLine($"{placed.Id} is placed, and the runtime refused to reload: {refusal}");
            return (PluginLoad.Rejected, ExitCodes.ApiError, Envelope(refusal), null);
        }

        // The runtime answers a reload it performs with 200 and a reload it refuses with an error envelope; there is no
        // third answer to read.
        var registry = RenderText.Read<PluginRegistryDto>(response.Body);
        var loaded = registry?.Plugins?.FirstOrDefault(plugin => string.Equals(plugin.Id, placed.Id, StringComparison.Ordinal));
        return (PluginLoad.Activated, ExitCodes.Success, null, loaded);
    }

    private static ErrorBody? Envelope(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Said(PluginPlaced placed) => placed.Placement switch
    {
        PluginPlacement.AlreadyInPlace => $"{placed.Id} {placed.Version} was already in {placed.Directory}.",
        PluginPlacement.Replaced => $"Replaced what was in {placed.Directory} with {placed.Id} {placed.Version}.",
        _ => $"Placed {placed.Id} {placed.Version} in {placed.Directory}.",
    };

    private static string Said(PluginLoad load) => load switch
    {
        PluginLoad.Activated => "The runtime has loaded it.",
        PluginLoad.AtNextStart => "It loads at the runtime's next start.",
        _ => "The runtime did not load it.",
    };

    /// <summary>What <c>--human</c> says under what <c>list</c> and <c>reload</c> always said: what each plugin still needs.</summary>
    private const string HumanNeeds =
        "With --human, each unavailable plugin's problems are named with the runtime's words, and each capability a plugin "
        + "asks for and is granted nothing of is named with the key that grants it in the settings file this CLI uses.";

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list",
            "List the active snapshot: every plugin with its digest, capabilities and problems. " + HumanNeeds);
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());

            return OperationRunner.RunAsync(
                env,
                Operations.PluginList,
                RequestBody.Empty(),
                new RunOptions(parseResult.GetValue(human), json => PluginRenderers.Registry(json, env.Paths.UserSettingsFile)),
                cancellationToken);
        });

        return command;
    }

    private static Command Reload(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "reload",
            "Read every package again and swap the snapshot, or keep the old one and say what is wrong. " + HumanNeeds);
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return OperationRunner.RunAsync(
                env,
                Operations.PluginReload,
                body,
                new RunOptions(parseResult.GetValue(human), json => PluginRenderers.Reload(json, env.Paths.UserSettingsFile)),
                cancellationToken);
        });

        return command;
    }
}
