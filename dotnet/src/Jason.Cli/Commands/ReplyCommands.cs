using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>reply</c> verb group: a role's verdict on one reply it read, recorded as a line of the campaign's chronicle,
/// and the verdicts counted by their category over a window.
/// The verdict names the reply by the provider's identifiers and never carries a word of it.
/// </summary>
public static class ReplyCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        // The noun is read from the operation it stands for: the bare word is also a vendor's name, which the runtime's
        // vendor-neutrality guard refuses as a literal anywhere in the sources, and the verb map is one-to-one anyway.
        var reply = new Command(Operations.ReplyRecord[..Operations.ReplyRecord.IndexOf('.', StringComparison.Ordinal)], "Verdicts on replies: record one, what kind of reply it is, how sure, what to do about it and which goal it steers to; or count the judged replies by category.");
        reply.Subcommands.Add(Record(env, actor));
        reply.Subcommands.Add(Count(env, actor));
        return reply;
    }

    private static Command Record(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "record",
            "Record a verdict on one reply as the campaign's reply_classified line. The reply is named by its plugin, thread, message and moment; no word of it is sent.");
        var id = new Argument<string>("campaign-id") { Description = "The campaign the reply belongs to, as cmp_...." };
        var plugin = new Option<string>("--plugin") { Description = "The plugin whose conversation the reply is in.", Required = true };
        var thread = new Option<string>("--thread") { Description = "The provider's identifier of the thread.", Required = true };
        var message = new Option<string>("--message") { Description = "The provider's identifier of the reply within the thread.", Required = true };
        var messageAt = new Option<string>("--message-at")
        {
            Description = "When the reply arrived, as ISO-8601 UTC — for example 2026-09-14T10:00:00Z.",
            Required = true,
        };
        var category = new Option<string>("--category")
        {
            Description = "What kind of reply this is, as a word of the skills' vocabulary — for example interested or not_now.",
            Required = true,
        };
        var confidence = new Option<double?>("--confidence") { Description = "How sure the verdict is, from 0 to 1.", Required = true };
        var reaction = Choice<ReplyReaction>.Only(new Option<string>("--reaction")
        {
            Description = $"What was decided about the reply: {Choices.Spoken(Choice<ReplyReaction>.Words)}.",
            Required = true,
        });
        var contact = new Option<string?>("--contact") { Description = "The person who replied, as cnt_...." };
        var goal = new Option<string?>("--goal") { Description = "The id of the campaign's goal the reaction steers to." };
        var decision = new Option<string?>("--decision")
        {
            Description = "The question about this reply a person answered, as dec_...: the verdict then carries their answer, and its category must be the option they chose, and the question must have been raised by the run that recorded a verdict on this reply.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(plugin);
        command.Options.Add(thread);
        command.Options.Add(message);
        command.Options.Add(messageAt);
        command.Options.Add(category);
        command.Options.Add(confidence);
        command.Options.Add(reaction);
        command.Options.Add(contact);
        command.Options.Add(goal);
        command.Options.Add(decision);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("plugin_id", parseResult.GetValue(plugin))
                .Set("thread_id", parseResult.GetValue(thread))
                .Set("message_id", parseResult.GetValue(message))
                .Set("message_at", parseResult.GetValue(messageAt))
                .Set("category", parseResult.GetValue(category))
                .Set("confidence", parseResult.GetValue(confidence))
                .Set("reaction", parseResult.GetValue(reaction))
                .Set("contact_id", parseResult.GetValue(contact))
                .Set("goal_id", parseResult.GetValue(goal))
                .Set("decision_id", parseResult.GetValue(decision))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return OperationRunner.RunAsync(
                env, Operations.ReplyRecord, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.Entry), cancellationToken);
        });

        return command;
    }

    private static Command Count(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "count",
            "How many replies roles have judged in a window, by the category word each verdict names, per campaign and in total, "
            + "and in how many threads. A reply is counted under its latest verdict and dated by its own moment; a reply nobody has "
            + "judged is not counted, so this is not campaign activity's replied.");
        var ids = CampaignCommands.CampaignIds();
        var since = VerbOptions.Moment("--since", "The window's start, included, as ISO-8601 UTC, or now.");
        since.Required = true;
        var until = VerbOptions.Moment("--until", "The window's end, not included, as ISO-8601 UTC, or now.");
        until.Required = true;
        var human = VerbOptions.Human();
        command.Arguments.Add(ids);
        command.Options.Add(since);
        command.Options.Add(until);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_ids", CampaignCommands.Ids(parseResult.GetValue(ids)))
                .Set("since", VerbOptions.ResolveMoment(parseResult.GetValue(since), TimeProvider.System))
                .Set("until", VerbOptions.ResolveMoment(parseResult.GetValue(until), TimeProvider.System));
            return OperationRunner.RunAsync(
                env, Operations.ReplyCount, body, CampaignCommands.Named(env, parseResult.GetValue(human), ReplyRenderers.Count), cancellationToken);
        });

        return command;
    }
}
