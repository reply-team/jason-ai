using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>workitem</c> verb group: the five verbs a planner uses to put work on the queue and read it back,
/// and the three an executor uses to report on the attempt it was launched for. The work-item id is
/// positional, the request body may come from a file or standard input, and the scalar options are laid
/// over it.
/// </summary>
public static class WorkItemCommands
{
    /// <summary>The fields <c>--clear</c> may set back to nothing; every one of them is nullable on the API.</summary>
    private static readonly string[] Clearable =
        ["not_before", "due_at", "timeout_seconds", "heartbeat_seconds", "max_attempts", "result_format"];

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var workItem = new Command("workitem", "Put work on the queue, follow it, and report on an attempt you were launched for.");
        workItem.Subcommands.Add(Create(env, actor));
        workItem.Subcommands.Add(Get(env, actor));
        workItem.Subcommands.Add(List(env, actor));
        workItem.Subcommands.Add(Update(env, actor));
        workItem.Subcommands.Add(Cancel(env, actor));
        workItem.Subcommands.Add(Heartbeat(env, actor));
        workItem.Subcommands.Add(SetResult(env, actor));
        workItem.Subcommands.Add(Complete(env, actor));
        return workItem;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("create", "Create a work item in a campaign.");
        var id = CampaignId();
        var kind = new Option<string?>("--kind") { Description = "What the item is: ai_role or provider_op." };
        var role = new Option<string?>("--role") { Description = "The role that does the work, for an ai_role item — for example researcher." };
        var operation = new Option<string?>("--operation") { Description = "The provider operation to perform, for a provider_op item." };
        var contact = new Option<string?>("--contact") { Description = "The contact the work is about, as cnt_…, when it is about one." };
        var executionProfile = new Option<string?>("--execution-profile") { Description = "The execution profile the attempt should run under." };
        var priority = new Option<int?>("--priority") { Description = "Higher is claimed first; the runtime defaults to 0." };
        var notBefore = new Option<string?>("--not-before") { Description = "Do not start before this moment, as ISO-8601 UTC — for example 2026-09-14T10:00:00Z." };
        var dueAt = new Option<string?>("--due-at") { Description = "Expire the item if it has not started by this moment, as ISO-8601 UTC." };
        var timeout = new Option<int?>("--timeout") { Description = "The total budget of one attempt in seconds; the kind's default applies when absent." };
        var heartbeat = new Option<int?>("--heartbeat") { Description = "How often the executor must prove it is alive, in seconds; 0 turns the check off." };
        var maxAttempts = new Option<int?>("--max-attempts") { Description = "How many attempts a retriable failure is worth." };
        var context = new Option<string?>("--context") { Description = "The work item's context, as a JSON object — the brief the executor is given." };
        var resultFormat = new Option<string?>("--result-format") { Description = "What the result should look like, as any JSON value." };
        var file = VerbOptions.File(
            "The object holds campaign_id, kind, role or operation, contact_id, execution_profile, priority, not_before, due_at, "
            + "timeout_seconds, heartbeat_seconds, max_attempts, context and result_format.");
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
        command.Options.Add(resultFormat);
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
                .Set("context", VerbOptions.Object(parseResult.GetValue(context), "--context"))
                .Set("result_format", VerbOptions.Json(parseResult.GetValue(resultFormat), "--result-format"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

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
            Description = "Include the context each attempt was launched with. Left out by default: ten attempts of a large context is a lot to read.",
        };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(snapshots);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("work_item_id", parseResult.GetValue(id))
                .Set("include_snapshots", parseResult.GetValue(snapshots) ? JsonValue.Create(true) : null);

            return OperationRunner.RunAsync(env, Operations.WorkItemGet, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List work items, oldest first.");
        var campaign = new Option<string?>("--campaign") { Description = "Only items of this campaign, as cmp_…." };
        var contact = new Option<string?>("--contact") { Description = "Only items about this contact, as cnt_…." };
        var status = VerbOptions.Repeatable("--status", "Only items in this status. Repeat the option to ask for more than one — for example failed and expired.");
        var kind = new Option<string?>("--kind") { Description = "Only items of this kind: ai_role or provider_op." };
        var role = new Option<string?>("--role") { Description = "Only items for this role." };
        var eligible = new Option<bool>("--eligible") { Description = "Only items the dispatcher would claim right now." };
        var notEligible = new Option<bool>("--not-eligible") { Description = "Only items the dispatcher would not claim right now." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(contact);
        command.Options.Add(status);
        command.Options.Add(kind);
        command.Options.Add(role);
        command.Options.Add(eligible);
        command.Options.Add(notEligible);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("contact_id", parseResult.GetValue(contact))
                .Set("status", VerbOptions.Strings(parseResult.GetValue(status)))
                .Set("kind", parseResult.GetValue(kind))
                .Set("role", parseResult.GetValue(role))
                .Set("eligible", Eligibility(parseResult.GetValue(eligible), parseResult.GetValue(notEligible)))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.WorkItemList, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItemList), cancellationToken);
        });

        return command;
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
        var resultFormat = new Option<string?>("--result-format") { Description = "The new result format, as any JSON value." };
        var clear = VerbOptions.Repeatable(
            "--clear",
            $"A field to set back to nothing: {string.Join(", ", Clearable)}. Repeat the option for more than one; clearing wins over a value given for the same field.");
        var file = VerbOptions.File("The object holds the fields to patch (set, unset, not_before, due_at, priority, timeout_seconds, heartbeat_seconds, max_attempts, result_format).");
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
                .Set("result_format", VerbOptions.Json(parseResult.GetValue(resultFormat), "--result-format"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));
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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.WorkItemCancel, body, new RunOptions(parseResult.GetValue(human), WorkItemRenderers.WorkItem), cancellationToken)
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
        var status = new Option<string>("--status")
        {
            Description = "How the attempt ended: succeeded or failed.",
            Required = true,
        };
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
            ActorOption.Parse(parseResult.GetValue(actor));
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

        var isStdin = string.Equals(path, "-", StringComparison.Ordinal);
        var text = isStdin
            ? await (env.In ?? TextReader.Null).ReadToEndAsync(cancellationToken).ConfigureAwait(false)
            : await ReadAsync(path, cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            var source = isStdin ? "Standard input" : $"'{path}'";
            throw new UsageException($"{source} does not contain valid JSON (line {ex.LineNumber ?? 0}, position {ex.BytePositionInLine ?? 0}).");
        }
    }

    private static async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new UsageException($"--result-file: no such file: '{path}'.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new UsageException($"--result-file: no such file: '{path}'.");
        }
        catch (IOException ex)
        {
            throw new UsageException($"--result-file '{path}' could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UsageException($"--result-file '{path}' could not be read: {ex.Message}");
        }
    }

    private static Option<string> AttemptOption() => new("--attempt")
    {
        Description = "The attempt you are reporting for, as att_… — the fencing token from the launch envelope.",
        Required = true,
    };

    private static Option<string?> ResultOption() => new("--result") { Description = "The result, as any JSON value." };

    private static Option<string?> ResultFileOption() => new("--result-file")
    {
        Description = "The result as JSON — a path, or - for standard input. The file holds the result itself, not a request body.",
    };

    private static Argument<string> WorkItemId() => new("work-item-id") { Description = "The work item, as wi_…." };

    private static Argument<string> CampaignId() => new("campaign-id") { Description = "The campaign the work belongs to, as cmp_…." };
}
