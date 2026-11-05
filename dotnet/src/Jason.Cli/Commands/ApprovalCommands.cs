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

        var approval = new Command("approval", "See what is waiting for a person's decision, read what it would do, and decide it; or decide in advance for one category of verdict in a campaign.");
        approval.Subcommands.Add(List(env, actor));
        approval.Subcommands.Add(Get(env, actor));
        approval.Subcommands.Add(Decide(env, actor, "approve", Operations.ApprovalApprove, "Approve it. The work goes back into the queue exactly as it was."));
        approval.Subcommands.Add(Decide(env, actor, "reject", Operations.ApprovalReject, "Reject it. The work ends, carrying your reason."));
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
            "Let the drafts answering one category of verdict in a campaign go without being asked each time. Set again, it replaces what was set and its count starts again. "
            + ActorOption.NoDefault + " " + ActorOption.RefusedInsideARun);
        var id = new Argument<string>("campaign-id") { Description = "The campaign, as cmp_...." };
        var category = new Option<string>("--category")
        {
            Description = "The verdict's category, exactly as verdicts are recorded — for example interested.",
            Required = true,
        };
        var operation = VerbOptions.Repeatable(
            "--operation",
            "conversation.reply or message.send; repeat it to name both. conversation.reply alone when absent.");
        var expiresAt = new Option<string?>("--expires-at")
        {
            Description = "When it stops, as ISO-8601 UTC — for example 2026-12-01T00:00:00Z. It never stops when absent.",
        };
        var maxReleases = new Option<int?>("--max-releases") { Description = "How many drafts it may let go, from 1 to 10000. No limit when absent." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(category);
        command.Options.Add(operation);
        command.Options.Add(expiresAt);
        command.Options.Add(maxReleases);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var operations = parseResult.GetValue(operation);
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("category", parseResult.GetValue(category))
                .Set("operations", operations is { Length: > 0 } ? new JsonArray([.. operations.Select(o => JsonValue.Create(o))]) : null)
                .Set("expires_at", parseResult.GetValue(expiresAt))
                .Set("max_releases", parseResult.GetValue(maxReleases))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));
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
            "Take back the standing approval for one category of verdict in a campaign; drafts from now on wait for a person. "
            + ActorOption.NoDefault + " " + ActorOption.RefusedInsideARun);
        var id = new Argument<string>("campaign-id") { Description = "The campaign, as cmp_...." };
        var category = new Option<string>("--category") { Description = "The verdict's category the standing approval was set for.", Required = true };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(category);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("category", parseResult.GetValue(category))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));
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
            "The standing approvals set for a campaign, by category: what each covers, its limits, how many drafts it let go and whether it is still in force.");
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
            ActorOption.Parse(parseResult.GetValue(actor));
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
    /// decision is a person's word, so one that names nobody is refused here, before the runtime would refuse it.
    /// </summary>
    private static Command Decide(CliEnvironment env, Option<string?> actor, string verb, string operation, string description)
    {
        var command = new Command(verb, $"{description} {ActorOption.NoDefault} {ActorOption.RefusedInsideARun}");
        var id = new Argument<string>("approval-id") { Description = "The approval, as approval list shows it." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("approval_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));
            ActorOption.Named(body, $"approval {verb}", "an approval is decided on a person's word");

            return OperationRunner.RunAsync(
                env, operation, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.Approval), cancellationToken);
        });

        return command;
    }
}
