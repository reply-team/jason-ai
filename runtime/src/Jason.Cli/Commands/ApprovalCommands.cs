using System.CommandLine;
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

        var approval = new Command("approval", "See what is waiting for a person's decision, read what it would do, and decide it.");
        approval.Subcommands.Add(List(env, actor));
        approval.Subcommands.Add(Get(env, actor));
        approval.Subcommands.Add(Decide(env, actor, "approve", Operations.ApprovalApprove, "Approve it. The work goes back into the queue exactly as it was."));
        approval.Subcommands.Add(Decide(env, actor, "reject", Operations.ApprovalReject, "Reject it. The work ends, carrying your reason."));
        return approval;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "What is waiting for a decision. Pending unless you ask for something else.");
        var status = new Option<string?>("--status") { Description = "pending (the default), approved, rejected, superseded or cancelled." };
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
            var body = RequestBody.Empty()
                .Set("status", parseResult.GetValue(status))
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
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty().Set("approval_id", parseResult.GetValue(id));

            return OperationRunner.RunAsync(
                env, Operations.ApprovalGet, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.Approval), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Both decisions take the same three things. The actor is the one the whole CLI carries, and the runtime
    /// refuses a decision that does not name a person — so <c>--actor</c> is not optional here in practice.
    /// </summary>
    private static Command Decide(CliEnvironment env, Option<string?> actor, string verb, string operation, string description)
    {
        var command = new Command(verb, description);
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

            return OperationRunner.RunAsync(
                env, operation, body, new RunOptions(parseResult.GetValue(human), ApprovalRenderers.Approval), cancellationToken);
        });

        return command;
    }
}
