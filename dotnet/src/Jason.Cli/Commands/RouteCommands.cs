using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

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
        var channel = Channel("The channel the call names for its route, such as linkedin for message.send; leave it out to ask about a call that names none.");
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(operation);
        command.Options.Add(channel);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation))
                .Set("channel", parseResult.GetValue(channel));

            if (await RefusedForAnOlderRuntimeAsync(env, parseResult.GetValue(channel), cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            return await OperationRunner.RunAsync(env, Operations.RouteResolve, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Resolution), cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List every route, or the global set plus one campaign's. A route by channel is listed beside the operation's own and reads \"<operation> on <channel>\".");
        var campaign = Campaign("Only this campaign's routes; the global set is answered either way.", required: false);
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty().Set("campaign_id", parseResult.GetValue(campaign));

            return OperationRunner.RunAsync(env, Operations.RouteList, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Routes), cancellationToken);
        });

        return command;
    }

    private static Command Set(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("set", "Send one campaign's work — or one of its operations — to a plugin. " + ActorOption.NoDefaultForAnAct);
        var campaign = Campaign("The campaign whose work is being routed.", required: false);
        var operation = Operation("The operation to override; leave it out for the campaign's default route.", required: false);
        var channel = Channel(
            "Route one channel of the operation alone, such as linkedin for message.send, beside the operation's own route and never in its place. "
                + "Needs --operation, and an operation whose document lets a route name a channel.");
        var plugin = new Option<string?>("--plugin")
        {
            Description = "The plugin that performs the work.",
            Required = true,
        };
        var binding = new Option<string?>("--binding")
        {
            Description = "Which account, workspace or mailbox of the plugin to work through, as a JSON object. "
                + "It never carries a credential. A set writes the whole route, so leaving this out clears the "
                + "binding the route had rather than keeping it.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(operation);
        command.Options.Add(channel);
        command.Options.Add(plugin);
        command.Options.Add(binding);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = RequireCampaign(parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation))
                .Set("channel", parseResult.GetValue(channel))
                .Set("plugin", parseResult.GetValue(plugin))
                .Set("binding", VerbOptions.Object(parseResult.GetValue(binding), "--binding"))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "route set", "the CLI will not put this account's name on a change of route by itself", $"name who does it, e.g. {ActorOption.Name} human:<Name>");

            if (await RefusedForAnOlderRuntimeAsync(env, parseResult.GetValue(channel), cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            return await OperationRunner.RunAsync(env, Operations.RouteSet, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Routes), cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static Command Unset(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("unset", "Take one campaign route away, and with it whatever it was hiding. " + ActorOption.NoDefaultForAnAct);
        var campaign = Campaign("The campaign whose route is being removed.", required: false);
        var operation = Operation("The operation override to remove; leave it out for the campaign's default route.", required: false);
        var channel = Channel("Remove the route of this channel of the operation alone, leaving the operation's own route; needs --operation.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(campaign);
        command.Options.Add(operation);
        command.Options.Add(channel);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = RequireCampaign(parseResult.GetValue(campaign))
                .Set("operation", parseResult.GetValue(operation))
                .Set("channel", parseResult.GetValue(channel))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "route unset", "the CLI will not put this account's name on taking a route away by itself", $"name who does it, e.g. {ActorOption.Name} human:<Name>");

            if (await RefusedForAnOlderRuntimeAsync(env, parseResult.GetValue(channel), cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            return await OperationRunner.RunAsync(env, Operations.RouteUnset, body, new RunOptions(parseResult.GetValue(human), RouteRenderers.Routes), cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// A request that names a channel, asked of a runtime that does not know channels: it ignores the member it does not
    /// read, so a write would replace, or take away, the operation's own route, the very route the channel was meant to
    /// leave alone, and a read would answer for the operation's own route as though it were the channel's. So the running
    /// runtime is asked, by the <c>system.info</c> read every client already makes, whether it counts channel routes, and
    /// a request is sent only to one that says it does. Where that cannot be read, or the answer has no routes section or
    /// no such count, the request is not sent on a guess: it is refused as <c>runtime_too_old</c>, exit 1, and the person
    /// is told to restart or update the runtime. A runtime that cannot be reached at all is reported as every verb reports
    /// it. A request that names no channel asks nothing first. Null is clear to send. A runtime restarted between the read and the request is not covered; nothing short of a member the server checks would cover it.
    /// </summary>
    internal static async Task<int?> RefusedForAnOlderRuntimeAsync(CliEnvironment env, string? channel, CancellationToken cancellationToken)
    {
        if (channel is null)
        {
            return null;
        }

        var (exit, response) = await OperationRunner.SendAsync(env, Operations.SystemInfo, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exit;
        }

        var info = response.IsSuccess ? Info(response.Body) : null;
        if (info?.Routes?.ChannelRouteCount is not null)
        {
            return null;
        }

        var running = info is null ? "The runtime that is running could not be asked what it knows" : $"The runtime that is running ({info.RuntimeVersion}) does not know channel routes";
        env.Out.WriteLine(CliErrors.Serialize(
            CliErrors.RuntimeTooOld,
            $"{running}, so it would ignore --channel and answer or change the operation's own route instead. Nothing was sent. Restart the runtime if it was updated without a restart, or update Jason.",
            retryable: false));
        return ExitCodes.ApiError;
    }

    private static SystemInfoResponse? Info(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<SystemInfoResponse>(body, JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
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

    private static Option<string?> Channel(string description) => new("--channel")
    {
        Description = description,
    };

    private static Option<string?> Operation(string description, bool required) => new("--operation")
    {
        Description = description,
        Required = required,
    };
}
