using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>journal</c> verb group. The chronicle is append-only: there is a verb to add an entry and a verb to
/// read entries back, and none to change one.
/// </summary>
public static class JournalCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var journal = new Command("journal", "Append to and read the campaign chronicle.");
        journal.Subcommands.Add(Append(env, actor));
        journal.Subcommands.Add(List(env, actor));
        return journal;
    }

    private static Command Append(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("append", "Record what happened. Kinds the runtime writes itself are refused.");
        var id = new Argument<string>("campaign-id") { Description = "The campaign the entry belongs to, as cmp_…." };
        var kind = new Option<string>("--kind")
        {
            Description = "What kind of entry this is, in snake_case — for example decision, observation or plan_revision.",
            Required = true,
        };
        var key = new Option<string?>("--key") { Description = "What the entry is about, when the kind alone does not say it." };
        var newValue = new Option<string?>("--new") { Description = "What was recorded, as any JSON value." };
        var file = VerbOptions.File("The object holds the entry fields (kind, key, new).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(kind);
        command.Options.Add(key);
        command.Options.Add(newValue);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("kind", parseResult.GetValue(kind))
                .Set("key", parseResult.GetValue(key))
                .Set("new", VerbOptions.Json(parseResult.GetValue(newValue), "--new"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.JournalAppend, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.Entry), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "Read the chronicle, newest entry first.");
        var campaign = new Option<string?>("--campaign") { Description = "Only entries of this campaign, as cmp_…." };
        var kind = new Option<string?>("--kind") { Description = "Only entries of this kind." };
        var since = new Option<string?>("--since") { Description = "Only entries at or after this moment, as ISO-8601 UTC — for example 2026-09-14T10:00:00Z." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(kind);
        command.Options.Add(since);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("kind", parseResult.GetValue(kind))
                .Set("since", parseResult.GetValue(since))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.JournalList, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.EntryList), cancellationToken);
        });

        return command;
    }
}
