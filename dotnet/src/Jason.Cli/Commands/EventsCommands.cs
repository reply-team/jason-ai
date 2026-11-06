using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>events</c> verb group: the record of the accounts whose events Jason reads, and a person's request to read a
/// span of one again. Reading itself is the runtime's own, on its cadence; nothing here reads an account.
/// </summary>
public static class EventsCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var events = new Command("events", "Show the accounts whose events Jason reads, read a span of one again, or have one read sooner.");
        events.Subcommands.Add(Sources(env, actor));
        events.Subcommands.Add(Backfill(env, actor));
        events.Subcommands.Add(Signal(env, actor));
        return events;
    }

    private static Command Sources(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "sources",
            "List every account whose events are read: whether a route binds it, its last read and how it ended, whether it is healthy, when it is read next, the latest span nobody will read, and the spans asked for again. Never a cursor.");
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.EventsSources, body, new RunOptions(parseResult.GetValue(human), EventsRenderers.Sources), cancellationToken);
        });

        return command;
    }

    private static Command Backfill(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "backfill",
            "Read a span of one account's events again: every event at or after --from and before --to, at most 366 days back. It reads and sends nothing, and leaves the account's own reading where it stands; what it finds wakes the campaigns it concerns that were live when it happened and were not told.");
        var source = new Option<string?>("--source")
        {
            Description = "The account, by the id jason events sources lists (evs_...).",
            Required = true,
        };
        var from = new Option<string?>("--from")
        {
            Description = "Where the span begins, as ISO-8601 UTC — for example 2026-08-01T00:00:00Z.",
            Required = true,
        };
        var to = new Option<string?>("--to")
        {
            Description = "Where the span ends, not included, as ISO-8601 UTC; no later than now.",
            Required = true,
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(source);
        command.Options.Add(from);
        command.Options.Add(to);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("source", parseResult.GetValue(source))
                .Set("from", parseResult.GetValue(from))
                .Set("to", parseResult.GetValue(to))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return OperationRunner.RunAsync(env, Operations.EventsBackfill, body, new RunOptions(parseResult.GetValue(human), EventsRenderers.Backfill), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// A nudge from a program that receives a provider's webhooks: the account is read at the next scan, never within a
    /// minute of its last read. It sends the account and nothing else — the global actor is checked, as every verb checks
    /// it, and not sent — so nothing a webhook carried reaches Jason.
    /// </summary>
    private static Command Signal(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "signal",
            "Have one account read sooner: at the next scan, never within a minute of its last read. For a program that receives a provider's webhooks; it sends the account and nothing else.");
        var source = new Option<string?>("--source")
        {
            Description = "The account, by the id jason events sources lists (evs_...).",
            Required = true,
        };
        var human = VerbOptions.Human();
        command.Options.Add(source);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("source", parseResult.GetValue(source));

            return OperationRunner.RunAsync(env, Operations.EventsSignal, body, new RunOptions(parseResult.GetValue(human), EventsRenderers.Signal), cancellationToken);
        });

        return command;
    }
}
