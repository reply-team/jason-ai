using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>approval</c> verb group: work the runtime will not perform without a person's word. Nothing here
/// decides anything on somebody's behalf — the decision carries the actor it was made by.
/// </summary>
public static class ApprovalCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var approval = new Command(
            "approval",
            "See what is waiting for a person's decision, read what it would do, and decide it; or decide in advance for one category of verdict in a campaign, or for the searches under one of its audiences.");
        approval.Subcommands.Add(List(env, actor));
        approval.Subcommands.Add(Get(env, actor));
        approval.Subcommands.Add(Decide(
            env,
            actor,
            "approve",
            Operations.ApprovalApprove,
            "Approve it. The work goes back into the queue exactly as it was. Approving a search under an audience other than the one the campaign's allowance names takes the allowance back in the same act; --allow sets one for this search's audience instead.",
            allows: true));
        approval.Subcommands.Add(Decide(env, actor, "reject", Operations.ApprovalReject, "Reject it. The work ends, carrying your reason.", allows: false));
        approval.Subcommands.Add(SetStanding(env, actor));
        approval.Subcommands.Add(UnsetStanding(env, actor));
        approval.Subcommands.Add(ListStanding(env, actor));
        return approval;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "What is waiting for a decision. Pending unless you ask for something else.");
        var status = Choice<ApprovalStatus>.Only(VerbOptions.Repeatable("--status", "pending (the default), approved, rejected, superseded or cancelled; repeat it to list several at once."));
        var campaign = new Option<string?>("--campaign") { Description = "Only decisions about one campaign's work." };
        var workItem = new Option<string?>("--work-item") { Description = "Only decisions about one work item." };
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
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var statuses = parseResult.GetValue(status) ?? [];
            var body = RequestBody.Empty()
                .Set("status", statuses.Length == 1 ? statuses[0] : null)
                .Set("statuses", statuses.Length > 1 ? new JsonArray([.. statuses.Select(s => JsonValue.Create(s))]) : null)
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("work_item_id", parseResult.GetValue(workItem))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.ApprovalList, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.ApprovalList), cancellationToken);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "One decision: what it would do, to whom, through which account.");
        var id = new Argument<string>("approval-id") { Description = "The approval, as approval list shows it." };
        var human = VerbOptions.Human();
        var prompt = VerbOptions.Prompt();
        command.Arguments.Add(id);
        command.Options.Add(human);
        command.Options.Add(prompt);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            VerbOptions.OneRendering(parseResult.GetValue(human), parseResult.GetValue(prompt));
            var body = RequestBody.Empty().Set("approval_id", parseResult.GetValue(id));

            return OperationRunner.RunAsync(
                env,
                Operations.ApprovalGet,
                body,
                new RunOptions(parseResult.GetValue(human), ApprovalRenderers.Approval)
                {
                    Prompt = parseResult.GetValue(prompt),
                    RenderPromptAsync = PromptRenderers.Approval(env),
                },
                cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// A person's word in advance, for one campaign and one category of verdict: the drafts that answer such a verdict go
    /// without being asked each time. It is a decision, so it names the person and takes no default actor.
    /// </summary>
    private static Command SetStanding(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "set-standing",
            "Let the drafts answering one category of verdict in a campaign go without being asked each time; or, with --operation audience.search, "
            + "let the searches under one of its audiences go until the people they ask for reach a ceiling, the campaign's allowance. "
            + "Set again, it replaces what was set and its count starts again. "
            + ActorOption.NoDefault + " " + ActorOption.RefusedInsideARun);
        var id = new Argument<string>("campaign-id") { Description = "The campaign, as cmp_...." };
        var category = new Option<string?>("--category")
        {
            Description = "The verdict's category, exactly as verdicts are recorded — for example interested. Required unless --operation is audience.search.",
        };
        var operation = VerbOptions.Repeatable(
            "--operation",
            "conversation.reply or message.send; repeat it to name both. conversation.reply alone when absent. audience.search alone sets the allowance.");
        var expiresAt = new Option<string?>("--expires-at")
        {
            Description = "When it stops, as ISO-8601 UTC — for example 2026-12-01T00:00:00Z. It never stops when absent.",
        };
        var maxReleases = new Option<int?>("--max-releases") { Description = "How many drafts it may let go, from 1 to 10000. No limit when absent." };
        var audience = new Option<int?>("--audience") { Description = "The allowance's audience, by the number the campaign knows it by. Required with audience.search." };
        var maxPeople = new Option<int?>("--max-people")
        {
            Description = "The allowance's ceiling: how many people its searches may ask for, added up, from 1 to 100000. Required with audience.search.",
        };
        var maxPerSearch = new Option<int?>("--max-per-search") { Description = "The most people one search under the allowance may ask for, from 1 to 1000. No cap when absent." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(category);
        command.Options.Add(operation);
        command.Options.Add(expiresAt);
        command.Options.Add(maxReleases);
        command.Options.Add(audience);
        command.Options.Add(maxPeople);
        command.Options.Add(maxPerSearch);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var operations = parseResult.GetValue(operation);
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("category", parseResult.GetValue(category))
                .Set("operations", operations is { Length: > 0 } ? new JsonArray([.. operations.Select(o => JsonValue.Create(o))]) : null)
                .Set("audience", parseResult.GetValue(audience))
                .Set("max_people", parseResult.GetValue(maxPeople))
                .Set("max_per_search", parseResult.GetValue(maxPerSearch))
                .Set("expires_at", parseResult.GetValue(expiresAt))
                .Set("max_releases", parseResult.GetValue(maxReleases))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "approval set-standing", "a standing approval is a person's word given in advance");

            return OperationRunner.RunAsync(
                env, Operations.ApprovalSetStanding, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.StandingApproval), cancellationToken);
        });

        return command;
    }

    /// <summary>Takes a standing approval back. Any named person may; the drafts already let go stay let go.</summary>
    private static Command UnsetStanding(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "unset-standing",
            "Take back the standing approval for one category of verdict in a campaign, or with --operation audience.search its allowance; "
            + "what it covered waits for a person from now on. "
            + ActorOption.NoDefault + " " + ActorOption.RefusedInsideARun);
        var id = new Argument<string>("campaign-id") { Description = "The campaign, as cmp_...." };
        var category = new Option<string?>("--category") { Description = "The verdict's category the standing approval was set for." };
        var operation = new Option<string?>("--operation") { Description = "audience.search, to take back the campaign's allowance instead of a category's standing approval." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(category);
        command.Options.Add(operation);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("category", parseResult.GetValue(category))
                .Set("operation", parseResult.GetValue(operation))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "approval unset-standing", "a standing approval is taken back on a person's word");

            return OperationRunner.RunAsync(
                env, Operations.ApprovalUnsetStanding, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.StandingApproval), cancellationToken);
        });

        return command;
    }

    private static Command ListStanding(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list-standing",
            "The standing approvals set for a campaign, by category, and its allowance for searches: what each covers, its limits, how much it let go — for the allowance, the people its searches asked for — and whether it is still in force.");
        var id = new Argument<string>("campaign-id") { Description = "The campaign, as cmp_...." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.ApprovalListStanding, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.StandingApprovalList), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Both decisions take the same three things. The actor is the one the whole CLI carries, but not its default: a
    /// decision is a person's word, so one that names nobody is refused here, before the runtime would refuse it. An
    /// approval may also carry an allowance for the audience of the search it approves.
    /// </summary>
    private static Command Decide(CliEnvironment env, Option<string?> actor, string verb, string operation, string description, bool allows)
    {
        var command = new Command(verb, $"{description} {ActorOption.NoDefault} {ActorOption.RefusedInsideARun}");
        var id = new Argument<string>("approval-id") { Description = "The approval, as approval list shows it." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        var allow = new Option<int?>("--allow")
        {
            Description = "Approve a search and allow up to this many more people under its audience, from 1 to 100000: the campaign's allowance, set in the same act.",
        };
        var allowPerSearch = new Option<int?>("--allow-per-search") { Description = "With --allow: the most people one search under it may ask for, from 1 to 1000." };
        var allowExpiresAt = new Option<string?>("--allow-expires-at") { Description = "With --allow: when the allowance stops, as ISO-8601 UTC. It never stops when absent." };
        command.Arguments.Add(id);
        command.Options.Add(reason);
        if (allows)
        {
            command.Options.Add(allow);
            command.Options.Add(allowPerSearch);
            command.Options.Add(allowExpiresAt);
        }

        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var allowed = allows ? parseResult.GetValue(allow) : null;
            if (allows && allowed is null && (parseResult.GetValue(allowPerSearch) is not null || parseResult.GetValue(allowExpiresAt) is not null))
            {
                throw new UsageException("--allow-per-search and --allow-expires-at shape the allowance --allow sets; give --allow with them, or leave them out.");
            }

            var body = RequestBody.Empty()
                .Set("approval_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .Set(
                    "allow",
                    allowed is { } most
                        ? new JsonObject
                        {
                            ["max_people"] = most,
                            ["max_per_search"] = parseResult.GetValue(allowPerSearch),
                            ["expires_at"] = parseResult.GetValue(allowExpiresAt),
                        }
                        : null)
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, $"approval {verb}", "an approval is decided on a person's word");

            return OperationRunner.RunAsync(
                env, operation, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.Approval), cancellationToken);
        });

        return command;
    }
}
