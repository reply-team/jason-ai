using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A campaign's route may name a channel under its operation: a row of its own, set and taken away on its own beside the
/// operation's own route, checked by the same checks in the same order plus the two about the channel, recorded in the
/// chronicle under its operation with its channel said beside the route, and frozen by every activation into the
/// campaign's set, where the resolver answers it for the calls on that channel.
/// </summary>
public class ChannelRouteStorageTests
{
    private const string Mailer = "mailer";

    private const string Social = "social";

    private const string Send = "message.send";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_channel_route_is_set_beside_the_operations_own_route_and_each_answers_its_own_calls()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        await SetAsync(api, campaign, Send, channel: null, Mailer);
        var answered = await SetAsync(api, campaign, Send, "linkedin", Social, reason: "invitations and messages on one account");

        var listed = Assert.Single(answered.Campaigns);
        Assert.Equal(Mailer, listed.Operations[Send].PluginId);
        Assert.Equal(Social, listed.Channels![Send]["linkedin"].PluginId);
        Assert.Equal(["linkedin"], listed.Channels[Send].Keys);
        Assert.Empty(answered.Global.Channels!);

        var routes = api.Resolve<RouteRegistry>().Snapshot;
        Assert.Equal(answered.RoutingSnapshotId, routes.Id);
        Assert.Equal((Social, RouteScope.CampaignChannel), Won(routes, campaign, "linkedin"));
        Assert.Equal((Mailer, RouteScope.CampaignOperation), Won(routes, campaign, "email"));
        Assert.Equal((Mailer, RouteScope.CampaignOperation), Won(routes, campaign, channel: null));

        var lines = await LinesAsync(api, campaign);
        var channelLine = lines.Single(line => line.Reason is not null);
        Assert.Equal(Send, channelLine.Key);
        Assert.Null(channelLine.Old);
        Assert.Equal(Social, (string?)channelLine.New!["plugin"]);
        Assert.Equal("linkedin", (string?)channelLine.New["channel"]);
        var operationLine = lines.Single(line => line.Reason is null);
        Assert.Equal(Send, operationLine.Key);
        Assert.False(operationLine.New!.AsObject().ContainsKey("channel"));
    }

    [Fact]
    public async Task Setting_a_channel_route_again_moves_that_route_and_the_line_names_its_channel_before_and_after()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, "linkedin", Social);

        await SetAsync(api, campaign, Send, "linkedin", Mailer, reason: "one account for both");

        Assert.Equal((Mailer, RouteScope.CampaignChannel), Won(api.Resolve<RouteRegistry>().Snapshot, campaign, "linkedin"));
        var moved = (await LinesAsync(api, campaign)).Single(line => line.Reason is not null);
        Assert.Equal(Send, moved.Key);
        Assert.Equal((Social, "linkedin"), ((string?)moved.Old!["plugin"], (string?)moved.Old["channel"]));
        Assert.Equal((Mailer, "linkedin"), ((string?)moved.New!["plugin"], (string?)moved.New["channel"]));
        Assert.Equal(1, await CountAsync(api, campaign));
    }

    /// <summary>
    /// Taking a channel route away leaves its operation's own route and the other channels' routes; taking the operation's
    /// own route away leaves its channel routes, and a call on another channel then goes where a call with no route of its
    /// own goes — here the global default.
    /// </summary>
    [Fact]
    public async Task A_channel_route_and_its_operations_own_route_are_taken_away_each_on_its_own()
    {
        await using var api = await StartAsync(globalDefault: Mailer);
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Social);
        await SetAsync(api, campaign, Send, "linkedin", Social);
        await SetAsync(api, campaign, Send, "email", Social);

        await UnsetAsync(api, campaign, Send, "linkedin", reason: "back to the operation's route");

        var afterChannel = api.Resolve<RouteRegistry>().Snapshot;
        Assert.Equal((Social, RouteScope.CampaignOperation), Won(afterChannel, campaign, "linkedin"));
        Assert.Equal((Social, RouteScope.CampaignChannel), Won(afterChannel, campaign, "email"));
        var removal = (await LinesAsync(api, campaign)).Single(line => line.Reason is not null);
        Assert.Equal(Send, removal.Key);
        Assert.Null(removal.New);
        Assert.Equal((Social, "linkedin"), ((string?)removal.Old!["plugin"], (string?)removal.Old["channel"]));

        await UnsetAsync(api, campaign, Send, channel: null);

        var afterOperation = api.Resolve<RouteRegistry>().Snapshot;
        Assert.Equal((Social, RouteScope.CampaignChannel), Won(afterOperation, campaign, "email"));
        Assert.Equal((Mailer, RouteScope.GlobalDefault), Won(afterOperation, campaign, "linkedin"));
        Assert.Equal(1, await CountAsync(api, campaign));
    }

    [Fact]
    public async Task Taking_away_a_channel_route_that_is_not_there_changes_nothing()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Mailer);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        var after = await UnsetAsync(api, campaign, Send, "linkedin");

        Assert.Equal(before.Id, after.RoutingSnapshotId);
        Assert.Single(await LinesAsync(api, campaign));
        Assert.Equal(1, await CountAsync(api, campaign));
    }

    /// <summary>
    /// The channel is checked against the operation's document before anything about the plugin, and before the row: an
    /// operation that names no argument a route reads its channel from takes no channel route, and an operation that does
    /// takes only a channel that argument allows. The detail names the route the way it was written, channel included.
    /// </summary>
    [Theory]
    [InlineData("campaign.get", "email", RouteProblemCodes.ChannelNotRoutable)]
    [InlineData(Send, "sms", RouteProblemCodes.ChannelUnknown)]
    [InlineData(Send, "Email", RouteProblemCodes.ChannelUnknown)]
    public async Task A_channel_the_operation_does_not_route_by_is_refused_before_the_row_is_written(string operation, string channel, string code)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation, channel, plugin = TestPlugins.OtherProviderId },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(($"campaign:{campaign}/routes/{operation}/{channel}", code), (detail.Field, detail.Code));
        Assert.Equal(0, await CountAsync(api, campaign));
    }

    /// <summary>
    /// A channel that is there and blank — what an empty variable or field sends — is refused at both doors rather than
    /// read as no channel, which would replace the operation's own route at a set and take it away at an unset.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task A_blank_channel_is_refused_at_set_and_at_unset_and_the_operations_own_route_stays(string blank)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Mailer);
        var before = api.Resolve<RouteRegistry>().Snapshot.Id;

        var set = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = Send, channel = blank, plugin = Social },
            HttpStatusCode.BadRequest,
            Ct);
        var unset = await api.PostErrorAsync(
            Operations.RouteUnset,
            new { campaign_id = campaign, operation = Send, channel = blank },
            HttpStatusCode.BadRequest,
            Ct);

        foreach (var error in new[] { set, unset })
        {
            Assert.Equal("validation_failed", error.Code);
            var detail = Assert.Single(error.Details!);
            Assert.Equal(("channel", "blank"), (detail.Field, detail.Code));
        }

        var routes = api.Resolve<RouteRegistry>().Snapshot;
        Assert.Equal(before, routes.Id);
        Assert.Equal((Mailer, RouteScope.CampaignOperation), Won(routes, campaign, channel: null));
        Assert.Equal(1, await CountAsync(api, campaign));
        Assert.Single(await LinesAsync(api, campaign));
    }

    /// <summary>A channel written with spaces around it names the channel inside them, at a set and at an unset alike.</summary>
    [Fact]
    public async Task A_padded_channel_names_the_channel_inside_its_spaces()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        await SetAsync(api, campaign, Send, " linkedin ", Social);

        Assert.Equal((Social, RouteScope.CampaignChannel), Won(api.Resolve<RouteRegistry>().Snapshot, campaign, "linkedin"));
        Assert.Equal("linkedin", (string?)(await LinesAsync(api, campaign)).Single().New!["channel"]);

        await UnsetAsync(api, campaign, Send, " linkedin ");

        Assert.Equal(0, await CountAsync(api, campaign));
    }

    /// <summary>
    /// An unset asks of the channel what a set asks, before "not there" is considered: a channel the operation's document
    /// would never let a route name is refused, named as a set names it, rather than answered "nothing to take away" while
    /// the route the caller meant stays.
    /// </summary>
    [Theory]
    [InlineData(Send, "LinkedIn", RouteProblemCodes.ChannelUnknown)]
    [InlineData(Send, "sms", RouteProblemCodes.ChannelUnknown)]
    [InlineData("campaign.get", "email", RouteProblemCodes.ChannelNotRoutable)]
    public async Task An_unset_of_a_channel_no_route_could_name_is_refused_and_the_route_meant_stays(string operation, string channel, string code)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, "linkedin", Social);
        var before = api.Resolve<RouteRegistry>().Snapshot.Id;

        var error = await api.PostErrorAsync(
            Operations.RouteUnset,
            new { campaign_id = campaign, operation, channel },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal((RouteActivator.CampaignField(campaign, operation, channel), code), (detail.Field, detail.Code));
        var routes = api.Resolve<RouteRegistry>().Snapshot;
        Assert.Equal(before, routes.Id);
        Assert.Equal((Social, RouteScope.CampaignChannel), Won(routes, campaign, "linkedin"));
        Assert.Equal(1, await CountAsync(api, campaign));
    }

    /// <summary>The same checks as an operation's own route, in the same order: a plugin that does not perform the operation.</summary>
    [Fact]
    public async Task A_channel_route_to_a_plugin_that_does_not_perform_the_operation_is_refused_as_an_operation_route_is()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = Send, channel = "linkedin", plugin = TestPlugins.OtherProviderId },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal((RouteActivator.CampaignField(campaign, Send, "linkedin"), RouteProblemCodes.OperationUnsupported), (detail.Field, detail.Code));
    }

    /// <summary>A channel is a channel of one operation: named with none — under the default — it is refused as the operation missing.</summary>
    [Fact]
    public async Task A_channel_named_without_an_operation_is_refused()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var set = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, channel = "email", plugin = Mailer },
            HttpStatusCode.BadRequest,
            Ct);
        var unset = await api.PostErrorAsync(
            Operations.RouteUnset,
            new { campaign_id = campaign, channel = "email" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.All(new[] { set, unset }, error => Assert.Equal(("operation", "required"), (Assert.Single(error.Details!).Field, error.Details![0].Code)));
        Assert.Equal(0, await CountAsync(api, campaign));
    }

    /// <summary>
    /// A reload reads every campaign's rows again and freezes the channel routes into the campaign's set beside its
    /// operation routes, each with the binding identity its route was written with; the resolver then answers them.
    /// </summary>
    [Fact]
    public async Task A_reload_freezes_each_campaigns_channel_routes_and_the_resolver_answers_them()
    {
        await using var api = await StartAsync();
        var first = await CampaignAsync(api);
        var second = await CampaignAsync(api);
        await SetAsync(api, first, Send, channel: null, Mailer);
        await SetAsync(api, first, Send, "linkedin", Social, binding: """{"seat":"one"}""");
        await SetAsync(api, second, "sender.list", "linkedin", Social);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var routes = api.Resolve<RouteRegistry>().Snapshot;
        Assert.NotEqual(before.Id, routes.Id);
        var frozen = routes.Campaigns[first].ChannelRoute(Send, "linkedin")!;
        Assert.Equal("one", (string?)frozen.Binding!["seat"]);
        Assert.Equal(before.Campaigns[first].ChannelRoute(Send, "linkedin")!.BindingIdentity, frozen.BindingIdentity);
        Assert.Equal((Social, RouteScope.CampaignChannel), Won(routes, first, "linkedin"));
        Assert.Equal((Mailer, RouteScope.CampaignOperation), Won(routes, first, "email"));
        Assert.Equal(
            new[] { ("sender.list", "linkedin", Social) },
            routes.Campaigns[second].ChannelRoutes.Select(entry => (entry.Operation, entry.Channel, entry.Route.PluginId)));
        Assert.Empty(routes.Campaigns[second].Operations);
    }

    /// <summary>
    /// A row the route check never saw — written by an older build, or straight into the table — is checked by the reload
    /// like every other route: a channel its operation does not route by refuses the whole reload, named the way the route
    /// would have been written.
    /// </summary>
    [Theory]
    [InlineData("campaign.get", "email", RouteProblemCodes.ChannelNotRoutable)]
    [InlineData(Send, "sms", RouteProblemCodes.ChannelUnknown)]
    public async Task A_stored_channel_route_its_operation_does_not_route_by_refuses_the_reload(string operation, string channel, string code)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await WriteRowAsync(api, campaign, operation, channel, Mailer);
        var before = api.Resolve<RouteRegistry>().Snapshot.Id;

        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(($"campaign:{campaign}/routes/{operation}/{channel}", code), (detail.Field, detail.Code));
        Assert.Equal(before, api.Resolve<RouteRegistry>().Snapshot.Id);
    }

    /// <summary>
    /// The route that won, named the way the operator wrote it, for each of the six scopes: a channel route under its
    /// operation and channel, as the campaign row and the setting spell them.
    /// </summary>
    [Theory]
    [InlineData(RouteScope.CampaignChannel, "campaign:cmp_A/routes/message.send/linkedin")]
    [InlineData(RouteScope.CampaignOperation, "campaign:cmp_A/routes/message.send")]
    [InlineData(RouteScope.CampaignDefault, "campaign:cmp_A/routes/default")]
    [InlineData(RouteScope.GlobalChannel, "Routes:Operations:message.send:Channels:linkedin")]
    [InlineData(RouteScope.GlobalOperation, "Routes:Operations:message.send")]
    [InlineData(RouteScope.GlobalDefault, "Routes:Default")]
    public void Each_scope_names_the_route_that_won_as_it_was_written(RouteScope scope, string field)
    {
        Assert.Equal(field, RouteService.Field("cmp_A", Send, "linkedin", scope));
    }

    /// <summary>
    /// Every scope there is has a name of its own: none falls through to another route's name, so a scope added later
    /// without one is refused here rather than reported as the global default.
    /// </summary>
    [Fact]
    public void Every_scope_has_a_name_of_its_own()
    {
        var names = Enum.GetValues<RouteScope>().Select(scope => RouteService.Field("cmp_A", Send, "linkedin", scope)).ToList();

        Assert.Equal(Enum.GetValues<RouteScope>().Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteService.Field("cmp_A", Send, "linkedin", (RouteScope)99));
    }

    private static (string Plugin, RouteScope Scope) Won(RouteSnapshot routes, string campaign, string? channel)
    {
        var resolution = RouteResolver.Resolve(routes, campaign, Send, channel)!;
        return (resolution.Route.PluginId, resolution.Scope);
    }

    private static Task<RoutesDto> SetAsync(
        RuntimeApiFixture api,
        string campaign,
        string operation,
        string? channel,
        string plugin,
        string? reason = null,
        string? binding = null) =>
        api.PostOkAsync<RoutesDto>(
            Operations.RouteSet,
            new { campaign_id = campaign, operation, channel, plugin, binding = binding is null ? null : System.Text.Json.Nodes.JsonNode.Parse(binding), reason },
            Ct);

    private static Task<RoutesDto> UnsetAsync(RuntimeApiFixture api, string campaign, string operation, string? channel, string? reason = null) =>
        api.PostOkAsync<RoutesDto>(Operations.RouteUnset, new { campaign_id = campaign, operation, channel, reason }, Ct);

    private static async Task<IReadOnlyList<JournalEntryDto>> LinesAsync(RuntimeApiFixture api, string campaign) =>
        (await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { CampaignId = campaign, Kind = JournalKinds.RoutesUpdated },
            Ct)).Items;

    /// <summary>How many route rows the campaign holds: what was written, read from the table rather than from any answer.</summary>
    private static async Task<int> CountAsync(RuntimeApiFixture api, string campaign)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        return await db.CampaignRoutes.CountAsync(route => route.Campaign!.PublicId == campaign, Ct);
    }

    /// <summary>A campaign route written straight into the row the activator reads, past every check <c>route.set</c> makes.</summary>
    private static async Task WriteRowAsync(RuntimeApiFixture api, string campaign, string operation, string channel, string plugin)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var id = await db.Campaigns.Where(c => c.PublicId == campaign).Select(c => c.Id).SingleAsync(Ct);
        db.CampaignRoutes.Add(new CampaignRoute
        {
            CampaignId = id,
            Operation = operation,
            Channel = channel,
            PluginId = plugin,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed by channel" }, Ct)).Id;

    /// <summary>
    /// Two providers that both send, so a channel of a send can go to one and the operation to the other, and the
    /// checked-in provider that does not send, for the check a channel route shares with every route.
    /// </summary>
    private static Task<RuntimeApiFixture> StartAsync(string? globalDefault = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.Write(paths, Mailer, TestPlugins.Manifest(Mailer, "[message.send, sender.list, campaign.get]"), "export function invoke() {}");
                TestPlugins.Write(paths, Social, TestPlugins.Manifest(Social, "[message.send, sender.list, campaign.get]"), "export function invoke() {}");
                TestPlugins.InstallOtherProvider(paths);
                if (globalDefault is not null)
                {
                    TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(globalDefault));
                }
            });
}
