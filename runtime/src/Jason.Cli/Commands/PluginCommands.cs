using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>plugin</c> verb group: what the runtime has loaded, and the one explicit act that replaces it.
/// Installing a plugin is copying its directory into the plugins directory — nothing here writes files, and
/// a reload that finds a broken package changes nothing.
/// </summary>
public static class PluginCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var plugin = new Command("plugin", "Show the plugins the runtime has loaded, and load them again from disk.");
        plugin.Subcommands.Add(List(env, actor));
        plugin.Subcommands.Add(Reload(env, actor));
        return plugin;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List the active snapshot: every plugin with its digest, capabilities and problems.");
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));

            return OperationRunner.RunAsync(
                env,
                Operations.PluginList,
                RequestBody.Empty(),
                new RunOptions(parseResult.GetValue(human), PluginRenderers.Registry),
                cancellationToken);
        });

        return command;
    }

    private static Command Reload(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("reload", "Read every package again and swap the snapshot, or keep the old one and say what is wrong.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(
                env,
                Operations.PluginReload,
                body,
                new RunOptions(parseResult.GetValue(human), PluginRenderers.Reload),
                cancellationToken);
        });

        return command;
    }
}
