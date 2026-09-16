using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>route</c> verb group: where a campaign's provider work goes. Reading asks the active snapshot what a
/// claim would decide; writing changes one campaign's mind and activates a new route snapshot on the spot.
/// <para>
/// There is deliberately no global <c>route set</c>. A global route is a setting, frozen by the same explicit
/// act that freezes the plugins it names — so the only way to write one is to edit the settings file and
/// reload, and asking for one here says exactly that rather than failing obscurely.
/// </para>
/// </summary>
public static class RouteCommands
{
    /// <summary>
    /// What an operator reaching for a global route is told. It is the documentation for the one place they
    /// will reach for the wrong thing, so it says where a global route lives and what activates it.
    /// </summary>
    private const string NoGlobalWrite =
        "--campaign is required: only a campaign's routes are written through the API. "
            + "A global route is written under \"Routes\" in settings.json and activated with 'jason plugin reload'.";

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var route = new Command("route", "Show and change where a campaign's provider work goes.");
        route.Subcommands.Add(Resolve(env, actor));
        route.Subcommands.Add(List(env, actor));
        route.Subcommands.Add(Set(env, actor));
        route.Subcommands.Add(Unset(env, actor));
        return route;
    }

    private static Command Resolve(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("resolve", "Say which plugin would perform one operation for one campaign, and why it could not.");
        var campaign = Campaign("The campaign whose work is being asked about.", required: true);
        var operation = Operation("The canonical operation, such as campaign.get.", required: true);
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(operation);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation));

            return OperationRunner.RunAsync(env, Operations.RouteResolve, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Resolution), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List every route, or the global set plus one campaign's.");
        var campaign = Campaign("Only this campaign's routes; the global set is answered either way.", required: false);
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty().Set("campaign_id", parseResult.GetValue(campaign));

            return OperationRunner.RunAsync(env, Operations.RouteList, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Routes), cancellationToken);
        });

        return command;
    }

    private static Command Set(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("set", "Send one campaign's work — or one of its operations — to a plugin.");
        var campaign = Campaign("The campaign whose work is being routed.", required: false);
        var operation = Operation("The operation to override; leave it out for the campaign's default route.", required: false);
        var plugin = new Option<string?>("--plugin")
        {
            Description = "The plugin that performs the work.",
            Required = true,
        };
        var binding = new Option<string?>("--binding")
        {
            Description = "Which account, workspace or mailbox of the plugin to work through, as a JSON object. It never carries a credential.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(operation);
        command.Options.Add(plugin);
        command.Options.Add(binding);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequireCampaign(parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation))
                .Set("plugin", parseResult.GetValue(plugin))
                .Set("binding", VerbOptions.Object(parseResult.GetValue(binding), "--binding"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(env, Operations.RouteSet, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Routes), cancellationToken);
        });

        return command;
    }

    private static Command Unset(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("unset", "Take one campaign route away, and with it whatever it was hiding.");
        var campaign = Campaign("The campaign whose route is being removed.", required: false);
        var operation = Operation("The operation override to remove; leave it out for the campaign's default route.", required: false);
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(operation);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequireCampaign(parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(env, Operations.RouteUnset, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Routes), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The body a writer starts from. <c>--campaign</c> is a checked option rather than a required one, because
    /// "you left out an option" is not the answer somebody reaching for a global route needs to read.
    /// </summary>
    private static JsonObject RequireCampaign(string? campaign) =>
        string.IsNullOrWhiteSpace(campaign)
            ? throw new UsageException(NoGlobalWrite)
            : RequestBody.Empty().Set("campaign_id", campaign);

    private static Option<string?> Campaign(string description, bool required) => new("--campaign")
    {
        Description = description,
        Required = required,
    };

    private static Option<string?> Operation(string description, bool required) => new("--operation")
    {
        Description = description,
        Required = required,
    };
}
