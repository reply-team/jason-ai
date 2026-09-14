using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>campaign</c> verb group: one verb per campaign operation, named the same and carrying the same
/// fields. The campaign id is positional, the request body may come from a file or standard input, and the
/// scalar options are laid over it.
/// </summary>
public static class CampaignCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var campaign = new Command("campaign", "Create campaigns, describe them in their context and give them contacts.");
        campaign.Subcommands.Add(Create(env, actor));
        campaign.Subcommands.Add(Get(env, actor));
        campaign.Subcommands.Add(List(env, actor));
        campaign.Subcommands.Add(Update(env, actor));
        campaign.Subcommands.Add(Transition("start", "Start a draft campaign, or resume a paused one.", Operations.CampaignStart, env, actor));
        campaign.Subcommands.Add(Transition("pause", "Pause an active campaign.", Operations.CampaignPause, env, actor));
        campaign.Subcommands.Add(Transition("archive", "Archive a campaign. Archiving is final.", Operations.CampaignArchive, env, actor));
        campaign.Subcommands.Add(UpdateContext(env, actor));
        campaign.Subcommands.Add(AddContacts(env, actor));
        campaign.Subcommands.Add(RemoveContacts(env, actor));
        campaign.Subcommands.Add(ListContacts(env, actor));
        return campaign;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("create", "Create a campaign.");
        var name = new Option<string?>("--name") { Description = "The campaign name." };
        var context = new Option<string?>("--context") { Description = "The initial campaign context, as a JSON object." };
        var file = VerbOptions.File("The object holds name and context.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(name);
        command.Options.Add(context);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("name", parseResult.GetValue(name))
                .Set("context", VerbOptions.Object(parseResult.GetValue(context), "--context"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.CampaignCreate, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "Show one campaign with its context.");
        var id = CampaignId();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty().Set("campaign_id", parseResult.GetValue(id));
            return OperationRunner.RunAsync(env, Operations.CampaignGet, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List campaigns. Archived ones appear only when asked for by status.");
        var status = new Option<string?>("--status") { Description = "Only campaigns in this status: draft, active, paused or archived." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(status);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("status", parseResult.GetValue(status))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.CampaignList, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.CampaignList), cancellationToken);
        });

        return command;
    }

    private static Command Update(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update", "Change a campaign attribute. Absent options leave their field alone.");
        var id = CampaignId();
        var name = new Option<string?>("--name") { Description = "The new campaign name." };
        var file = VerbOptions.File("The object holds the fields to patch (name).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(name);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("name", parseResult.GetValue(name))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.CampaignUpdate, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Transition(string verb, string description, string operation, CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(verb, description);
        var id = CampaignId();
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
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, operation, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command UpdateContext(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update-context", "Edit top-level keys of the campaign context. Keys not named stay as they are.");
        var id = CampaignId();
        var set = new Option<string?>("--set") { Description = "Top-level keys to write, as a JSON object." };
        var unset = VerbOptions.Repeatable("--unset", "A top-level key to remove. Repeat the option for more than one.");
        var file = VerbOptions.File("The object holds set and unset.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(set);
        command.Options.Add(unset);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("set", VerbOptions.Object(parseResult.GetValue(set), "--set"))
                .Set("unset", VerbOptions.Strings(parseResult.GetValue(unset)))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.CampaignUpdateContext, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Campaign), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command AddContacts(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("add-contacts", "Add contacts to a campaign, creating or matching them as the import asks.");
        var id = CampaignId();
        var file = new Option<string>("--file")
        {
            Description = "The contacts as JSON — a path, or - for standard input. Either an array of items, or an object holding contacts and match_by. "
                + "An item is a contact payload (first_name, last_name, company, title, time_zone, channels, custom) or {\"contact_id\": \"cnt_…\"}.",
            Required = true,
        };
        var matchBy = new Option<string?>("--match-by") { Description = "Deduplicate on an existing contact by this key: a channel name such as email, or custom:<field>." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(file);
        command.Options.Add(matchBy);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), "contacts", cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("match_by", parseResult.GetValue(matchBy))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.CampaignAddContacts, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Batch), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command RemoveContacts(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("remove-contacts", "Remove contacts from a campaign. They stay on record as excluded.");
        var id = CampaignId();
        var contact = VerbOptions.Repeatable("--contact", "A contact to remove, as cnt_…. Repeat the option for more than one.");
        var file = VerbOptions.File("The object holds contact_ids.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(contact);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("campaign_id", parseResult.GetValue(id))
                .Set("contact_ids", VerbOptions.Strings(parseResult.GetValue(contact)))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.CampaignRemoveContacts, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.RemoveBatch), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command ListContacts(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list-contacts", "List the contacts of a campaign. Excluded ones appear only when asked for by state.");
        var id = CampaignId();
        var state = new Option<string?>("--state") { Description = "Only members in this state: enrolled, paused, finished or excluded." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(state);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(id))
                .Set("state", parseResult.GetValue(state))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.CampaignListContacts, body, new RunOptions(parseResult.GetValue(human), CampaignRenderers.Members), cancellationToken);
        });

        return command;
    }

    private static Argument<string> CampaignId() => new("campaign-id") { Description = "The campaign, as cmp_…." };
}
