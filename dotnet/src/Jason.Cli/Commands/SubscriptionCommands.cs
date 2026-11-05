using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>subscription</c> verb group: where each subscribed journal kind stands, the records of its chains, and a
/// person's rewind of a kind's cursor. Running a chain is the runtime's own, after the fact and off the scan; nothing here
/// runs one or invokes a plugin.
/// </summary>
public static class SubscriptionCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var subscription = new Command("subscription", "Show where each subscribed journal kind stands, read the records of its chains, or hand its lines to its plugins again.");
        subscription.Subcommands.Add(List(env, actor));
        subscription.Subcommands.Add(Runs(env, actor));
        subscription.Subcommands.Add(Rewind(env, actor));
        return subscription;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list",
            "List every subscribed journal kind: its chain of plugins in order, the line its cursor stands after, its newest line, how many of its lines are due, the plugins that keep its chain from starting, and the chain in flight.");
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.SubscriptionList, body, new RunOptions(parseResult.GetValue(human), SubscriptionRenderers.Kinds), cancellationToken);
        });

        return command;
    }

    private static Command Runs(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "runs",
            "List the records of one kind's chains, the newest first: the line each was started for, how each handler ended, how the chain ended, and the envelope the last handler returned.");
        var kind = KindOption();
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(kind);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("kind", parseResult.GetValue(kind))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.SubscriptionRuns, body, new RunOptions(parseResult.GetValue(human), SubscriptionRenderers.Runs), cancellationToken);
        });

        return command;
    }

    private static Command Rewind(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "rewind",
            "Move a subscribed kind's cursor back, so its chain hands its plugins again every line of the kind from --line, or from --since on. Exactly one of the two; a target after the cursor is refused, and a chain in flight for the kind ends superseded. Only a person may rewind. "
            + ActorOption.NoDefault);
        var kind = KindOption();
        var line = new Option<string?>("--line")
        {
            Description = "The line of the kind to hand again first, by its journal id (jrn_...).",
        };
        var since = new Option<string?>("--since")
        {
            Description = "The moment from which every line of the kind is handed again, as ISO-8601 with its offset — for example 2026-08-01T00:00:00Z.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(kind);
        command.Options.Add(line);
        command.Options.Add(since);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("kind", parseResult.GetValue(kind))
                .Set("line", parseResult.GetValue(line))
                .Set("since", parseResult.GetValue(since))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));
            ActorOption.Named(body, "subscription rewind", "a subscription is rewound on a person's word");

            return OperationRunner.RunAsync(env, Operations.SubscriptionRewind, body, new RunOptions(parseResult.GetValue(human), SubscriptionRenderers.Kind), cancellationToken);
        });

        return command;
    }

    private static Option<string?> KindOption() => new("--kind")
    {
        Description = "The subscribed journal kind, as jason subscription list lists it.",
        Required = true,
    };
}
