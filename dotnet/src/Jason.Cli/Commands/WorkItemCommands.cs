using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>workitem</c> verb group: the six verbs a planner or a person uses to put work on the queue, read it
/// back and release it from a wait, the one that reviews a send before it goes, and the four an executor uses to report
/// on the attempt it was launched for.
/// The work-item id is positional, the request body may come from a file or standard input, and the scalar
/// options are laid over it.
/// </summary>
public static class WorkItemCommands
{
    /// <summary>
    /// The context key the API reserves for a provider operation's arguments. Named here the way every other
    /// field name is: the CLI composes the body the API documents and reads nothing into it.
    /// </summary>
    private const string InputKey = "input";

    /// <summary>The context key the API reserves for a creator's own doubt about a send, named here for the same reason.</summary>
    private const string AskPersonKey = "ask_person";

    /// <summary>The fields <c>--clear</c> may set back to nothing; every one of them is nullable on the API.</summary>
    private static readonly string[] Clearable =
        ["not_before", "due_at", "timeout_seconds", "heartbeat_seconds", "max_attempts", "execution_profile", "result_format"];

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var workItem = new Command("workitem", "Put work on the queue, follow it, and report on an attempt you were launched for.");
        workItem.Subcommands.Add(Create(env, actor));
        workItem.Subcommands.Add(Get(env, actor));
        workItem.Subcommands.Add(List(env, actor));
        workItem.Subcommands.Add(Count(env, actor));
        workItem.Subcommands.Add(Update(env, actor));
        workItem.Subcommands.Add(Cancel(env, actor));
        workItem.Subcommands.Add(Resume(env, actor));
        workItem.Subcommands.Add(Review(env, actor));
        workItem.Subcommands.Add(Heartbeat(env, actor));
        workItem.Subcommands.Add(SetResult(env, actor));
        workItem.Subcommands.Add(Complete(env, actor));
        workItem.Subcommands.Add(Wait(env, actor));
        return workItem;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("create", "Create a work item in a campaign.");
        var id = CampaignId();
        var kind = Choice<WorkItemKind>.Only(new Option<string?>("--kind") { Description = "What the item is: ai_role or provider_op." });
        var role = new Option<string?>("--role") { Description = "The role that does the work, for an ai_role item — for example researcher." };
        var operation = new Option<string?>("--operation") { Description = "The canonical operation to perform, for a provider_op item: one of those under docs/contracts/operations, not a Jason verb." };
        var contact = new Option<string?>("--contact") { Description = "The contact the work is about, as cnt_..., when it is about one." };
        var executionProfile = new Option<string?>("--execution-profile") { Description = "The execution profile the attempt should run under." };
        var priority = new Option<int?>("--priority") { Description = "Higher is claimed first; the runtime defaults to 0." };
        var notBefore = new Option<string?>("--not-before") { Description = "Do not start before this moment, as ISO-8601 UTC — for example 2026-09-14T10:00:00Z." };
        var dueAt = new Option<string?>("--due-at") { Description = "Expire the item if it has not started by this moment, as ISO-8601 UTC." };
        var timeout = new Option<int?>("--timeout") { Description = "The total budget of one attempt in seconds; the kind's default applies when absent." };
        var heartbeat = new Option<int?>("--heartbeat") { Description = "How often the executor must prove it is alive, in seconds; 0 turns the check off." };
        var maxAttempts = new Option<int?>("--max-attempts") { Description = "How many attempts a retriable failure is worth." };
        var context = new Option<string?>("--context") { Description = "The work item's context, as a JSON object — the brief the executor is given." };
        var input = new Option<string?>("--input")
        {
            Description = "The arguments of a provider operation, as a JSON object. Written into the context under input, the one key an operation's arguments travel in.",
        };
        var resultFormat = new Option<string?>("--result-format") { Description = "What the result should look like, as any JSON value." };
        var waitOn = VerbOptions.Repeatable(
            "--wait-on",
            "A work item of the same campaign, as wi_..., that this one waits on from its creation: it is not claimed until every one named "
            + "has ended, whichever way, or the wait's ceiling has come. Repeat the option for more than one.");
        var askPerson = Choices.Hold(
            new Option<string?>("--ask-person")
            {
                Description = "Send this to the person whatever the send mode, saying why with one code: "
                    + Choices.Spoken(DoubtCodes.All) + ". Written into the context as ask_person; only on a send a person "
                    + "decides each one of, and never changed or taken back once the item exists.",
            },
            DoubtCodes.All,
            DoubtCodes.Knows);
        var askPersonNote = new Option<string?>("--ask-person-note")
        {
            Description = "One line, at most 280 characters, saying in words why the person should look: kept beside the item, never in a "
                + "journal line, and shown to the person on the approval. Only with --ask-person.",
        };
        var idempotencyKey = VerbOptions.IdempotencyKey();
        var file = VerbOptions.File(
            "The object holds campaign_id, kind, role or operation, contact_id, execution_profile, priority, not_before, due_at, "
            + "timeout_seconds, heartbeat_seconds, max_attempts, context, result_format, wait_on, ask_person_note and idempotency_key.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(kind);
        command.Options.Add(role);
        command.Options.Add(operation);
        command.Options.Add(contact);
        command.Options.Add(executionProfile);
        command.Options.Add(priority);
        command.Options.Add(notBefore);
        command.Options.Add(dueAt);
        command.Options.Add(timeout);
        command.Options.Add(heartbeat);
        command.Options.Add(maxAttempts);
        command.Options.Add(context);
        command.Options.Add(input);
        command.Options.Add(resultFormat);
        command.Options.Add(waitOn);
        command.Options.Add(askPerson);
        command.Options.Add(askPersonNote);
        command.Options.Add(idempotencyKey);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("kind", parseResult.GetValue(kind))
                .Set("role", parseResult.GetValue(role))
                .Set("operation", parseResult.GetValue(operation))
                .Set("contact_id", parseResult.GetValue(contact))
                .Set("execution_profile", parseResult.GetValue(executionProfile))
                .Set("priority", parseResult.GetValue(priority))
                .Set("not_before", parseResult.GetValue(notBefore))
                .Set("due_at", parseResult.GetValue(dueAt))
                .Set("timeout_seconds", parseResult.GetValue(timeout))
                .Set("heartbeat_seconds", parseResult.GetValue(heartbeat))
                .Set("max_attempts", parseResult.GetValue(maxAttempts))
                .Set("context", AskPerson(body, Context(body, parseResult.GetValue(context), parseResult.GetValue(input)), parseResult.GetValue(askPerson)))
                .Set("result_format", VerbOptions.Json(parseResult.GetValue(resultFormat), "--result-format"))
                .Set("wait_on", VerbOptions.Strings(parseResult.GetValue(waitOn)))
                .Set("ask_person_note", parseResult.GetValue(askPersonNote))
                .Set("idempotency_key", parseResult.GetValue(idempotencyKey))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemCreate, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "Show one work item with its context and its attempts.");
        var id = WorkItemId();
        var snapshots = new Option<bool>("--snapshots")
        {
            Description = "Include the evidence each attempt kept: the context it was launched with, and any answer its operation refused. Left out by default: either can be megabytes.",
        };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(snapshots);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("include_snapshots", parseResult.GetValue(snapshots) ? JsonValue.Create(true) : null);

            return OperationRunner.RunAsync(env, Operations.WorkItemGet, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List work items: oldest first, or by due time or finish time with --order.");
        var filter = new ListingFilter(command);
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = filter.Body(parseResult)
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.WorkItemList, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItemList), cancellationToken);
        });

        return command;
    }

    private static Command Count(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("count", "Count the work items a listing with the same filters would return, to its end.");
        var filter = new ListingFilter(command);
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return OperationRunner.RunAsync(env, Operations.WorkItemCount, filter.Body(parseResult), new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItemCount), cancellationToken);
        });

        return command;
    }

    /// <summary>The filters a listing and a count share, so the two verbs can never take different ones.</summary>
    private sealed class ListingFilter
    {
        private readonly Option<string?> _campaign = new("--campaign") { Description = "Only items of this campaign, as cmp_...." };
        private readonly Option<string?> _contact = new("--contact") { Description = "Only items about this contact, as cnt_...." };
        private readonly Option<string[]> _status = Choice<WorkItemStatus>.Only(VerbOptions.Repeatable(
            "--status",
            $"Only items in this status: {Choices.Spoken(Choice<WorkItemStatus>.Words)}. Repeat the option to ask for more than one — for example failed and expired."));
        private readonly Option<string?> _kind = Choice<WorkItemKind>.Only(new Option<string?>("--kind") { Description = "Only items of this kind: ai_role or provider_op." });
        private readonly Option<string?> _role = new("--role") { Description = "Only items for this role." };
        private readonly Option<bool> _eligible = new("--eligible") { Description = "Only items the dispatcher would claim right now." };
        private readonly Option<bool> _notEligible = new("--not-eligible") { Description = "Only items the dispatcher would not claim right now." };
        private readonly Option<string?> _since = VerbOptions.Moment("--since", "Only items whose ordered time (created, due or finished, as --order says) is at or after this moment, as ISO-8601 UTC, or now. An item without that time is left out.");
        private readonly Option<string?> _until = VerbOptions.Moment("--until", "Only items whose ordered time is before this moment, as ISO-8601 UTC, or now.");
        private readonly Option<string?> _order = Choice<WorkItemListOrder>.Only(new Option<string?>("--order") { Description = "created (the default, oldest first), due (soonest first, undated last) or finished (newest first; open items are left out)." });
        private readonly Option<string?> _block = new("--block") { Description = "Only the items this provider block holds, as blk_...: those whose open wait names it." };

        public ListingFilter(Command command)
        {
            command.Options.Add(_campaign);
            command.Options.Add(_contact);
            command.Options.Add(_status);
            command.Options.Add(_kind);
            command.Options.Add(_role);
            command.Options.Add(_eligible);
            command.Options.Add(_notEligible);
            command.Options.Add(_since);
            command.Options.Add(_until);
            command.Options.Add(_order);
            command.Options.Add(_block);
        }

        public JsonObject Body(ParseResult parseResult) =>
            RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(_campaign))
                .Set("contact_id", parseResult.GetValue(_contact))
                .Set("status", VerbOptions.Strings(parseResult.GetValue(_status)))
                .Set("kind", parseResult.GetValue(_kind))
                .Set("role", parseResult.GetValue(_role))
                .Set("eligible", Eligibility(parseResult.GetValue(_eligible), parseResult.GetValue(_notEligible)))
                .Set("since", VerbOptions.ResolveMoment(parseResult.GetValue(_since), TimeProvider.System))
                .Set("until", VerbOptions.ResolveMoment(parseResult.GetValue(_until), TimeProvider.System))
                .Set("order", parseResult.GetValue(_order))
                .Set("provider_block_id", parseResult.GetValue(_block));
    }

    private static Command Update(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update", "Change a work item. Absent options leave their field alone.");
        var id = WorkItemId();
        var set = new Option<string?>("--set") { Description = "Top-level context keys to write, as a JSON object." };
        var unset = VerbOptions.Repeatable("--unset", "A top-level context key to remove. Repeat the option for more than one.");
        var notBefore = new Option<string?>("--not-before") { Description = "The new earliest start, as ISO-8601 UTC." };
        var dueAt = new Option<string?>("--due-at") { Description = "The new expiry, as ISO-8601 UTC. Moving it forward reopens an expired item." };
        var priority = new Option<int?>("--priority") { Description = "The new priority." };
        var timeout = new Option<int?>("--timeout") { Description = "The new attempt budget in seconds." };
        var heartbeat = new Option<int?>("--heartbeat") { Description = "The new heartbeat interval in seconds." };
        var maxAttempts = new Option<int?>("--max-attempts") { Description = "The new attempt limit." };
        var executionProfile = new Option<string?>("--execution-profile")
        {
            Description = "The execution profile the next attempt should run under; --clear execution_profile gives the item back to the levels below it.",
        };
        var resultFormat = new Option<string?>("--result-format") { Description = "The new result format, as any JSON value." };
        var clear = VerbOptions.Repeatable(
            "--clear",
            $"A field to set back to nothing: {string.Join(", ", Clearable)}. Repeat the option for more than one; clearing wins over a value given for the same field.");
        var file = VerbOptions.File(
            "The object holds the fields to patch (set, unset, not_before, due_at, priority, timeout_seconds, heartbeat_seconds, "
            + "max_attempts, execution_profile, result_format).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(set);
        command.Options.Add(unset);
        command.Options.Add(notBefore);
        command.Options.Add(dueAt);
        command.Options.Add(priority);
        command.Options.Add(timeout);
        command.Options.Add(heartbeat);
        command.Options.Add(maxAttempts);
        command.Options.Add(executionProfile);
        command.Options.Add(resultFormat);
        command.Options.Add(clear);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("work_item_id", parseResult.GetValue(id))
                .Set("set", VerbOptions.Object(parseResult.GetValue(set), "--set"))
                .Set("unset", VerbOptions.Strings(parseResult.GetValue(unset)))
                .Set("not_before", parseResult.GetValue(notBefore))
                .Set("due_at", parseResult.GetValue(dueAt))
                .Set("priority", parseResult.GetValue(priority))
                .Set("timeout_seconds", parseResult.GetValue(timeout))
                .Set("heartbeat_seconds", parseResult.GetValue(heartbeat))
                .Set("max_attempts", parseResult.GetValue(maxAttempts))
                .Set("execution_profile", parseResult.GetValue(executionProfile))
                .Set("result_format", VerbOptions.Json(parseResult.GetValue(resultFormat), "--result-format"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);
            Clear(body, parseResult.GetValue(clear));

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemUpdate, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Cancel(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("cancel", "Cancel a work item. A running attempt is stopped; cancelling is final.");
        var id = WorkItemId();
        var file = VerbOptions.File("The object holds the request fields (reason).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("work_item_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()), env);

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemCancel, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Resume(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "resume",
            "Release a waiting work item before what it waits for has happened. It goes back to the queue, and its next "
            + "attempt is told it was resumed by the actor named rather than woken by its work. --actor is required: a "
            + "wait is ended on somebody's word. Only a waiting item can be resumed. " + ActorOption.NoDefaultForResume);
        var id = WorkItemId();
        var file = VerbOptions.File("The object holds the request fields (reason).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("work_item_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "workitem resume", "a wait is ended on somebody's word", $"name who ends it, e.g. {ActorOption.Name} human:<Name>");

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemResume, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Review(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "review",
            "Review a send before it goes: pass the text you read, or doubt it with a code, which sends it to the person. "
            + "Give the review_hash workitem get answered; a send edited since is refused, and read again. "
            + "Nobody reviews a send they made. Inside a run the review is the run's own; outside one, the person who reviews "
            + "names themselves. There is no --reason: a reason is journaled, and the doubt's sentence never is, so it goes as --note. "
            + ActorOption.NoDefault);
        var id = WorkItemId();
        var reviewHash = new Option<string>("--review-hash")
        {
            Description = "The send's review_hash as workitem get answered it: the review is bound to that text.",
            Required = true,
        };
        var result = Choice<ReviewResult>.Only(new Option<string>("--result")
        {
            Description = "passed, or doubts, which sends the send to the person.",
            Required = true,
        });
        var code = Choices.Hold(
            new Option<string?>("--code") { Description = "Why the review doubts, with --result doubts: " + Choices.Spoken(DoubtCodes.All) + "." },
            DoubtCodes.All,
            DoubtCodes.Knows);
        var note = new Option<string?>("--note")
        {
            Description = "With --result doubts, one line, at most 280 characters, saying in words what is wrong: shown to the person on "
                + "the approval, and never in a journal line.",
        };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(reviewHash);
        command.Options.Add(result);
        command.Options.Add(code);
        command.Options.Add(note);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("review_hash", parseResult.GetValue(reviewHash))
                .Set("result", parseResult.GetValue(result))
                .Set("code", parseResult.GetValue(code))
                .Set("note", parseResult.GetValue(note))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "workitem review", "a review is a run's own or a named person's");

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemReview, body, new RunOptions(parseResult.GetValue(human), JournalRenderers.Entry), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Heartbeat(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("heartbeat", "Prove the attempt you were launched for is still alive.");
        var id = WorkItemId();
        var attempt = AttemptOption();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(attempt);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("attempt_id", parseResult.GetValue(attempt));

            return OperationRunner.RunAsync(env, Operations.WorkItemHeartbeat, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.Heartbeat), cancellationToken);
        });

        return command;
    }

    private static Command SetResult(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("set-result", "Record the result of the attempt you were launched for, without finishing it.");
        var id = WorkItemId();
        var attempt = AttemptOption();
        var result = ResultOption();
        var resultFile = ResultFileOption();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(attempt);
        command.Options.Add(result);
        command.Options.Add(resultFile);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var value = await ResultAsync(env, parseResult.GetValue(result), parseResult.GetValue(resultFile), required: true, cancellationToken).ConfigureAwait(false);
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("attempt_id", parseResult.GetValue(attempt))
                .Set("result", value, onlyIfNotNull: false);

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemSetResult, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Complete(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("complete", "Finish the attempt you were launched for, one way or the other.");
        var id = WorkItemId();
        var attempt = AttemptOption();
        var status = Choice<CompletionStatus>.Only(new Option<string>("--status")
        {
            Description = "How the attempt ended: succeeded or failed.",
            Required = true,
        });
        var result = ResultOption();
        var resultFile = ResultFileOption();
        var error = new Option<string?>("--error") { Description = "Why the attempt failed, as a JSON object holding code, message and details." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(attempt);
        command.Options.Add(status);
        command.Options.Add(result);
        command.Options.Add(resultFile);
        command.Options.Add(error);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var value = await ResultAsync(env, parseResult.GetValue(result), parseResult.GetValue(resultFile), required: false, cancellationToken).ConfigureAwait(false);
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("attempt_id", parseResult.GetValue(attempt))
                .Set("status", parseResult.GetValue(status))
                .Set("result", value)
                .Set("error", VerbOptions.Object(parseResult.GetValue(error), "--error"))
                .Set("reason", parseResult.GetValue(reason));

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemComplete, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Wait(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "wait",
            "End the attempt you were launched for by waiting: for work items it raised to end, for a moment, or for "
            + "both. The item is released when they have ended — all of them, or any one with --any — or when the moment "
            + "comes, whichever is first, and its next attempt is told why. A role's work only; a check-in never waits.");
        var id = WorkItemId();
        var attempt = AttemptOption();
        var on = VerbOptions.Repeatable(
            "--on",
            "A work item this attempt raised, as wi_..., to wait for. Repeat the option for each one, at most 100; leave it "
            + "out to wait for --until alone.");
        var all = new Option<bool>("--all") { Description = "Release when every item named with --on has ended. The default when items are named." };
        var any = new Option<bool>("--any") { Description = "Release when any one item named with --on has ended." };
        var until = new Option<string?>("--until")
        {
            Description = "Release at this moment at the latest, as ISO-8601 UTC — for example 2026-09-14T10:00:00Z. Required when no item is named.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(attempt);
        command.Options.Add(on);
        command.Options.Add(all);
        command.Options.Add(any);
        command.Options.Add(until);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("attempt_id", parseResult.GetValue(attempt))
                .Set("on", VerbOptions.Strings(parseResult.GetValue(on)))
                .Set("mode", Mode(parseResult.GetValue(all), parseResult.GetValue(any)))
                .Set("until", parseResult.GetValue(until))
                .Set("reason", parseResult.GetValue(reason));

            return OperationRunner.RunAsync(env, Operations.WorkItemWait, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken);
        });

        return command;
    }

    /// <summary>Both flags at once asks for two releases, and the CLI can tell without the runtime; neither leaves the runtime's default.</summary>
    private static string? Mode(bool all, bool any)
    {
        if (all && any)
        {
            throw new UsageException("--all and --any ask for opposite releases; give at most one.");
        }

        return all ? "all" : any ? "any" : null;
    }

    /// <summary>
    /// The context the item starts with, and a provider operation's arguments written into the one key that
    /// carries them. The CLI reads nothing about what an argument means — that is the contract's business — but
    /// it refuses to write <c>input</c> twice: choosing silently between two of them would decide for the caller
    /// what the operation does.
    /// </summary>
    private static JsonObject? Context(JsonObject body, string? context, string? input)
    {
        var given = VerbOptions.Object(context, "--context");
        var arguments = VerbOptions.Object(input, "--input");
        if (arguments is null)
        {
            return given;
        }

        var carrier = given ?? body["context"] switch
        {
            null => [],
            JsonObject existing => existing,
            _ => throw new UsageException("--input writes into the context, and the body carries a context that is not a JSON object."),
        };

        if (carrier.ContainsKey(InputKey))
        {
            throw new UsageException($"--input and the context both carry '{InputKey}'; give the operation's arguments once.");
        }

        carrier[InputKey] = arguments;
        return carrier;
    }

    /// <summary>
    /// Lays <c>--ask-person</c> into the context as the reserved key, over the context the other options made or the body
    /// carried; a context that already carries the key is told to say it once.
    /// </summary>
    private static JsonObject? AskPerson(JsonObject body, JsonObject? context, string? code)
    {
        if (code is null)
        {
            return context;
        }

        var carrier = context ?? body["context"] switch
        {
            null => [],
            JsonObject existing => existing,
            _ => throw new UsageException("--ask-person writes into the context, and the body carries a context that is not a JSON object."),
        };

        if (carrier.ContainsKey(AskPersonKey))
        {
            throw new UsageException($"--ask-person and the context both carry '{AskPersonKey}'; give the doubt once.");
        }

        carrier[AskPersonKey] = new JsonObject { ["code"] = code };
        return carrier;
    }

    /// <summary>Both flags at once asks two contradictory questions, and the CLI can tell without the runtime.</summary>
    private static JsonNode? Eligibility(bool eligible, bool notEligible)
    {
        if (eligible && notEligible)
        {
            throw new UsageException("--eligible and --not-eligible ask for opposite things; give at most one.");
        }

        return eligible || notEligible ? JsonValue.Create(eligible) : null;
    }

    /// <summary>Writes an explicit null for every named field, so the runtime reads the patch as "set this back to nothing".</summary>
    private static void Clear(JsonObject body, string[]? fields)
    {
        foreach (var field in fields ?? [])
        {
            if (!Array.Exists(Clearable, clearable => string.Equals(clearable, field, StringComparison.Ordinal)))
            {
                throw new UsageException($"--clear '{field}' is not a field that can be cleared; give one of {string.Join(", ", Clearable)}.");
            }

            body.Set(field, null, onlyIfNotNull: false);
        }
    }

    /// <summary>
    /// The result of an attempt, from the command line or from a file. A result is any JSON value, not only an
    /// object, so it does not travel through the <c>--file</c> body reader.
    /// </summary>
    private static async Task<JsonNode?> ResultAsync(CliEnvironment env, string? inline, string? path, bool required, CancellationToken cancellationToken)
    {
        if (inline is not null && path is not null)
        {
            throw new UsageException("--result and --result-file give the result twice; use one of them.");
        }

        if (inline is not null)
        {
            return VerbOptions.Json(inline, "--result");
        }

        if (path is null)
        {
            return required ? throw new UsageException("The result is missing; give --result <json> or --result-file <path|->.") : null;
        }

        var (text, source) = await RequestFile.ReadAsync(env, "--result-file", path, namesOption: true, cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new UsageException($"{source} does not contain valid JSON (line {ex.LineNumber ?? 0}, position {ex.BytePositionInLine ?? 0}).");
        }
    }

    private static Option<string> AttemptOption() => new("--attempt")
    {
        Description = "The attempt you are reporting for, as att_... — the fencing token from the launch envelope.",
        Required = true,
    };

    private static Option<string?> ResultOption() => new("--result") { Description = "The result, as any JSON value." };

    private static RequestFileOption ResultFileOption() => new("--result-file")
    {
        Description = "The result as JSON — a path, or - for standard input. The file holds the result itself, not a request body.",
    };

    private static Argument<string> WorkItemId() => new("work-item-id") { Description = "The work item, as wi_...." };

    private static Argument<string> CampaignId() => new("campaign-id") { Description = "The campaign the work belongs to, as cmp_...." };
}
