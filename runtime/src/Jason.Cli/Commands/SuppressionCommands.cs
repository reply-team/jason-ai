using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>suppression</c> verb group: the do-not-contact list. Suppression is global — an address opted out of
/// email is not opted out of LinkedIn — so the entries are named by channel and value, never by campaign.
/// </summary>
public static class SuppressionCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var suppression = new Command("suppression", "Keep the do-not-contact list.");
        suppression.Subcommands.Add(Add(env, actor));
        suppression.Subcommands.Add(Remove(env, actor));
        suppression.Subcommands.Add(List(env, actor));
        return suppression;
    }

    private static Command Add(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("add", "Suppress a channel value. Suppressing one that is already suppressed returns the existing entry.");
        var channel = Channel("The channel the value belongs to, for example email.", required: true);
        var value = Value("The channel value to suppress, such as an email address; the runtime normalizes it.", required: true);
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(channel);
        command.Options.Add(value);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("channel", parseResult.GetValue(channel))
                .Set("value", parseResult.GetValue(value))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(env, Operations.SuppressionAdd, body, new RunOptions(parseResult.GetValue(human), SuppressionRenderers.Suppression), cancellationToken);
        });

        return command;
    }

    private static Command Remove(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("remove", "Lift a suppression. Removing one that is not there is not an error.");
        var channel = Channel("The channel the value belongs to, for example email.", required: true);
        var value = Value("The channel value to stop suppressing.", required: true);
        var reason = new Option<string>("--reason")
        {
            Description = "Why the suppression is lifted; recorded in the journal. Required: lifting one is a deliberate act.",
            Required = true,
        };
        var human = VerbOptions.Human();
        command.Options.Add(channel);
        command.Options.Add(value);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("channel", parseResult.GetValue(channel))
                .Set("value", parseResult.GetValue(value))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(env, Operations.SuppressionRemove, body, new RunOptions(parseResult.GetValue(human), SuppressionRenderers.Removed), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List the do-not-contact entries.");
        var channel = Channel("Only entries of this channel.", required: false);
        var value = Value("Only entries with this value.", required: false);
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(channel);
        command.Options.Add(value);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("channel", parseResult.GetValue(channel))
                .Set("value", parseResult.GetValue(value))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.SuppressionList, body, new RunOptions(parseResult.GetValue(human), SuppressionRenderers.SuppressionList), cancellationToken);
        });

        return command;
    }

    private static Option<string?> Channel(string description, bool required) => new("--channel")
    {
        Description = description,
        Required = required,
    };

    private static Option<string?> Value(string description, bool required) => new("--value")
    {
        Description = description,
        Required = required,
    };
}
