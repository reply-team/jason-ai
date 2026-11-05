using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>block</c> verb group: what the provider said stands in the way of an account or a sender, what that holds,
/// and a person's word that the cause is gone. A block opens only on the provider's answer; nothing here opens one.
/// </summary>
public static class BlockCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var block = new Command("block", "See what the provider has blocked, what each block holds, and clear one once its cause is gone.");
        block.Subcommands.Add(List(env, actor));
        block.Subcommands.Add(Get(env, actor));
        block.Subcommands.Add(Clear(env, actor));
        return block;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "The provider's blocks, newest first: open blocks only unless --status asks for others.");
        var status = Choice<ProviderBlockStatus>.Only(VerbOptions.Repeatable("--status", "open (the default) or cleared; repeat it to list both."));
        var plugin = new Option<string?>("--plugin") { Description = "Only the blocks one plugin's provider reported." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(status);
        command.Options.Add(plugin);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var statuses = parseResult.GetValue(status) ?? [];
            var body = RequestBody.Empty()
                .Set("statuses", statuses.Length > 0 ? new JsonArray([.. statuses.Select(s => JsonValue.Create(s))]) : null)
                .Set("plugin_id", parseResult.GetValue(plugin))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.BlockList, body, new RunOptions(parseResult.GetValue(human), BlockRenderers.BlockList), cancellationToken);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "get",
            "One block: what is blocked and why, the link where the provider says it is cleared, whether it clears by itself, "
            + "how many items it holds and when it is tried next. workitem list --block names the items.");
        var id = BlockId();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("block_id", parseResult.GetValue(id));

            return OperationRunner.RunAsync(
                env, Operations.BlockGet, body, new RunOptions(parseResult.GetValue(human), BlockRenderers.Block), cancellationToken);
        });

        return command;
    }

    private static Command Clear(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "clear",
            "Say the cause of an open block is gone. The block is cleared under your name, each campaign it held is told, "
            + "and the next scan releases what it held; this command releases nothing itself. A clear is not a mute: if "
            + "the provider refuses again, the account or sender is blocked again as a new block, and the campaigns are "
            + "told again. --actor is required: a block is cleared on a person's word, and it names one with an id. An agent "
            + "cannot clear a block: a role or an attempt is refused with block_not_human. Only an open block can be cleared. " + ActorOption.NoDefault);
        var id = BlockId();
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("block_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "block clear", "a block is cleared on a person's word");

            return OperationRunner.RunAsync(
                env, Operations.BlockClear, body, new RunOptions(parseResult.GetValue(human), BlockRenderers.Block), cancellationToken);
        });

        return command;
    }

    private static Argument<string> BlockId() => new("block-id") { Description = "The block, as block list shows it (blk_...)." };
}
