using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>decision</c> verb group: the questions a running role could not answer for itself, waiting on a
/// person. Two of the verbs are read by whoever is asking what is waiting, one is typed by the role that asks,
/// and one by the person who answers — and nothing here decides anything on anybody's behalf.
/// </summary>
public static class DecisionCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var decision = new Command("decision", "See the questions waiting on a person, read one, raise one as a running role, and answer one.");
        decision.Subcommands.Add(List(env, actor));
        decision.Subcommands.Add(Get(env, actor));
        decision.Subcommands.Add(Raise(env, actor));
        decision.Subcommands.Add(Answer(env, actor));
        return decision;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "What is waiting on a person. Pending unless you ask for something else.");
        var status = new Option<string?>("--status") { Description = "pending (the default), answered or cancelled." };
        var campaign = new Option<string?>("--campaign") { Description = "Only questions about one campaign's work." };
        var workItem = new Option<string?>("--work-item") { Description = "Only questions raised about one work item." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(status);
        command.Options.Add(campaign);
        command.Options.Add(workItem);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("status", parseResult.GetValue(status))
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("work_item_id", parseResult.GetValue(workItem))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.DecisionList, body, new RunOptions(parseResult.GetValue(human), DecisionRenderers.DecisionList), cancellationToken);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "One question: what is asked, the answers the asker named, what to read first, and the answer if there is one.");
        var id = DecisionId();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty().Set("decision_id", parseResult.GetValue(id));

            return OperationRunner.RunAsync(
                env, Operations.DecisionGet, body, new RunOptions(parseResult.GetValue(human), DecisionRenderers.Decision), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The verb a running role asks with. It is fenced the way a result is: the work item and the attempt
    /// together are the claim, so the actor is checked for its spelling and never sent — the runtime already
    /// knows who holds that attempt, and a claim beside it would only be something to disagree with it.
    /// </summary>
    /// <remarks>
    /// An option is typed as its label alone. The API lets an option carry a detail as well, but a role typing
    /// one command has no good way to spell two texts into one value, so this version offers the label only
    /// and the question itself is where the reasoning goes.
    /// </remarks>
    private static Command Raise(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("raise", "Ask a person a question you cannot answer yourself, from the attempt you were launched for.");
        var id = new Argument<string>("work-item-id") { Description = "The work item you are running, as wi_…." };
        var attempt = new Option<string>("--attempt")
        {
            Description = "The attempt you are asking from, as att_… — the fencing token from the launch envelope.",
            Required = true,
        };
        var question = new Option<string>("--question")
        {
            Description = "What you need decided, in words a person can answer.",
            Required = true,
        };
        var option = VerbOptions.Repeatable("--option", "An answer you are offering, by label. Repeat the option for more than one; an answer may then name one of them.");
        var reference = VerbOptions.Repeatable(
            "--reference",
            "Something to read before deciding, as kind:id — the kind is work_item, attempt, journal_entry, report or approval. Repeat the option for more than one.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(attempt);
        command.Options.Add(question);
        command.Options.Add(option);
        command.Options.Add(reference);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("attempt_id", parseResult.GetValue(attempt))
                .Set("question", parseResult.GetValue(question))
                .Set("options", Options(parseResult.GetValue(option)))
                .Set("references", References(parseResult.GetValue(reference)))
                .Set("reason", parseResult.GetValue(reason));

            return OperationRunner.RunAsync(
                env, Operations.DecisionRaise, body, new RunOptions(parseResult.GetValue(human), DecisionRenderers.Decision), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The person's verb. The answer takes the actor the whole CLI carries, and the runtime refuses an answer
    /// that does not name a person — so <c>--actor human:&lt;id&gt;</c> is not optional here in practice, and a
    /// role holding an attempt cannot answer its own question by leaving it out.
    /// </summary>
    private static Command Answer(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("answer", "Answer a question, as the person you are. The review that continues the work follows from it.");
        var id = DecisionId();
        var answer = new Option<string>("--answer")
        {
            Description = "Your answer, in words. It is what the role reads next time.",
            Required = true,
        };
        var option = new Option<string?>("--option") { Description = "The label of the offered answer you are choosing, where the question named any." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(answer);
        command.Options.Add(option);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("decision_id", parseResult.GetValue(id))
                .Set("answer", parseResult.GetValue(answer))
                .Set("option", parseResult.GetValue(option))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(
                env, Operations.DecisionAnswer, body, new RunOptions(parseResult.GetValue(human), DecisionRenderers.Decision), cancellationToken);
        });

        return command;
    }

    /// <summary>One object per label, in the order they were typed; the label is what an answer records.</summary>
    private static JsonArray? Options(string[]? labels) =>
        labels is { Length: > 0 }
            ? new JsonArray([.. labels.Select(label => (JsonNode?)new JsonObject { ["label"] = label })])
            : null;

    /// <summary>
    /// <c>kind:id</c>, once per reference, split at the first colon only so an id is never cut where the CLI
    /// happened to look. A value with no colon, or nothing on one side of it, is not a reference in any
    /// vocabulary and is refused here rather than sent: the runtime would refuse it too, but as a validation
    /// error about a request, when what went wrong was the typing. Which kinds exist is the runtime's business,
    /// so the kind travels as typed.
    /// </summary>
    private static JsonArray? References(string[]? values)
    {
        if (values is not { Length: > 0 })
        {
            return null;
        }

        var references = new JsonArray();
        foreach (var value in values)
        {
            var split = value.IndexOf(':', StringComparison.Ordinal);
            if (split <= 0 || split == value.Length - 1)
            {
                throw new UsageException($"--reference must be written as kind:id — for example work_item:wi_…; '{value}' is not.");
            }

            references.Add(new JsonObject
            {
                ["kind"] = value[..split],
                ["id"] = value[(split + 1)..],
            });
        }

        return references;
    }

    private static Argument<string> DecisionId() => new("decision-id") { Description = "The question, as decision list shows it: dec_…." };
}
