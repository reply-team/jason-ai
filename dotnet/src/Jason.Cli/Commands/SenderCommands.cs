using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>sender</c> verb group: the provider's accounts a send can go from, read now for a campaign by the plugin the
/// campaign's route for <c>sender.list</c> names. Nothing is recorded: no work item, no journal line.
/// </summary>
public static class SenderCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var sender = new Command("sender", "Read which accounts a campaign's provider can send from.");
        sender.Subcommands.Add(List(env, actor));
        return sender;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list",
            "Ask the provider for one page of the accounts a send can go from: each one's channel, whether it can send now, whose it is, "
                + "its name, its address and the identifier a send names it by. It asks the provider once, and nothing is recorded.");
        var campaign = new Option<string?>("--campaign")
        {
            Description = "The campaign, as cmp_...; its route for sender.list, on the channel asked for, names the provider that answers.",
            Required = true,
        };
        var owners = new Option<string?>("--owners")
        {
            Description = "Whose accounts: own, the accounts of the user the provider is signed in to as, or team, every account the provider lists. "
                + "Required: a colleague's account is never a default.",
            Required = true,
        };
        var channel = new Option<string?>("--channel") { Description = "Only the accounts of this channel, email or linkedin; every channel when absent." };
        var limit = new Option<int?>("--limit") { Description = "How many accounts on the page, from 1 to 100; 20 when absent." };
        var offset = new Option<int?>("--offset") { Description = "Where the page starts in the provider's order; 0 when absent." };
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(owners);
        command.Options.Add(channel);
        command.Options.Add(limit);
        command.Options.Add(offset);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("owners", parseResult.GetValue(owners))
                .Set("sender_channel", parseResult.GetValue(channel))
                .Set("limit", parseResult.GetValue(limit))
                .Set("offset", parseResult.GetValue(offset));
            return OperationRunner.RunAsync(env, Operations.SenderList, body, new RunOptions(parseResult.GetValue(human), SenderRenderers.List), cancellationToken);
        });

        return command;
    }
}
