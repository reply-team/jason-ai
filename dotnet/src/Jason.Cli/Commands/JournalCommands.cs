using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>journal</c> verb group. The chronicle is append-only: there is a verb to add an entry and a verb to
/// read entries back, and none to change one; a third reads what each kind the runtime writes itself means, and a
/// fourth, <see cref="JournalWatch"/>, follows the entries as they are written.
/// </summary>
public static class JournalCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var journal = new Command("journal", "Append to, read and follow the campaign chronicle.");
        journal.Subcommands.Add(Append(env, actor));
        journal.Subcommands.Add(List(env, actor));
        journal.Subcommands.Add(Kinds(env, actor));
        journal.Subcommands.Add(JournalWatch.Command(env, actor));
        return journal;
    }

    private static Command Append(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("append", "Record what happened. Kinds the runtime writes itself are refused.");
        var id = new Argument<string>("campaign-id") { Description = "The campaign the entry belongs to, as cmp_...." };
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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.JournalAppend, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.Entry), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "Read the chronicle, newest entry first.");
        var campaign = new Option<string?>("--campaign") { Description = "Only entries of this campaign, as cmp_...." };
        var workItem = new Option<string?>("--work-item") { Description = "Only entries about this work item, as wi_...." };
        var contact = new Option<string?>("--contact") { Description = "Only entries about this person, as cnt_...." };
        var kinds = VerbOptions.Repeatable("--kind", "Only entries of this kind; repeat it for several kinds.");
        var since = new Option<string?>("--since") { Description = "Only entries at or after this moment, as ISO-8601 UTC — for example 2026-09-14T10:00:00Z." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(workItem);
        command.Options.Add(contact);
        command.Options.Add(kinds);
        command.Options.Add(since);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("work_item_id", parseResult.GetValue(workItem))
                .Set("contact_id", parseResult.GetValue(contact))
                .Set("kinds", VerbOptions.Strings(parseResult.GetValue(kinds)))
                .Set("since", parseResult.GetValue(since))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.JournalList, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.EntryList), cancellationToken);
        });

        return command;
    }

    private static Command Kinds(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "kinds",
            "Read the kinds of line the runtime writes itself, in kind order, each as its document under docs/contracts/journal/ publishes it.");
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

            return OperationRunner.RunAsync(env, Operations.JournalKinds, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.KindList), cancellationToken);
        });

        return command;
    }
}
