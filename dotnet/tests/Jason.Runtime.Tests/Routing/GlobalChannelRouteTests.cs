using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The global half of channel routes: written in the settings file under an operation, frozen by a reload like every
/// other global route, held to the checks a campaign's channel route is held to and refused with the same codes, and
/// kept when a campaign's route is written. And the rule that a channel route names a plugin that reads its accounts'
/// events, since a reply to a direct send reaches a campaign only through them: refused at a reload, of a global route
/// and of a stored campaign row alike, at <c>route.set</c>, and at a reload whose new package stops reading them while a
/// channel route names it.
/// </summary>
public class GlobalChannelRouteTests
{
    /// <summary>A plugin that sends messages and reads its accounts' events: what a channel route may name.</summary>
    private const string Mailer = "channel-mailer";

    /// <summary>A plugin that sends messages and reads no events: what a channel route may not name.</summary>
    private const string Deaf = "deaf-mailer";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_global_channel_route_in_the_settings_is_frozen_by_the_reload_beside_the_operations_own()
    {
        await using var api = await StartAsync(paths =>
        {
            Install(paths, Mailer, readsEvents: true);
            Install(paths, Deaf, readsEvents: false);
        });

        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", null, Deaf), ("message.send", "email", Mailer), ("message.send", "linkedin", Mailer)), Ct);
        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var global = api.Resolve<RouteRegistry>().Snapshot.Global;
        Assert.Equal(reloaded.RoutingSnapshotId, api.Resolve<RouteRegistry>().Snapshot.Id);
        Assert.Equal(Deaf, global.Operations["message.send"].PluginId);
        Assert.Equal(Mailer, global.ChannelRoute("message.send", "email")!.PluginId);
        Assert.Equal(Mailer, global.ChannelRoute("message.send", "linkedin")!.PluginId);
        Assert.Equal(
            [("message.send", "email"), ("message.send", "linkedin")],
            global.ChannelRoutes.Select(route => (route.Operation, route.Channel)));
    }

    /// <summary>
    /// An operation may be routed by its channels alone: the operation then has no route of its own in the global set, and
    /// a call without a channel goes on to the global default.
    /// </summary>
    [Fact]
    public async Task An_operation_routed_by_its_channels_alone_has_no_route_of_its_own()
    {
        await using var api = await StartAsync(paths => Install(paths, Mailer, readsEvents: true));

        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", "email", Mailer)), Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var global = api.Resolve<RouteRegistry>().Snapshot.Global;
        Assert.DoesNotContain("message.send", global.Operations.Keys);
        Assert.Equal(Mailer, global.ChannelRoute("message.send", "email")!.PluginId);
    }

    /// <summary>A settings file written before channel routes existed activates the global set it always did: no channel routes.</summary>
    [Fact]
    public async Task A_settings_file_without_channel_routes_activates_a_global_set_that_holds_none()
    {
        await using var api = await StartAsync(paths => Install(paths, Mailer, readsEvents: false));

        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", null, Mailer)), Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var global = api.Resolve<RouteRegistry>().Snapshot.Global;
        Assert.Equal(Mailer, global.Operations["message.send"].PluginId);
        Assert.Null(global.Channels);
        Assert.Empty(global.ChannelRoutes);
    }

    /// <summary>
    /// The two questions about the operation's document, asked of a global channel route with the codes a campaign's is
    /// answered with, and like any bad route the whole reload is rejected and both snapshots stay.
    /// </summary>
    [Theory]
    [InlineData("campaign.get", "email", RouteProblemCodes.ChannelNotRoutable)]
    [InlineData("message.send", "phone", RouteProblemCodes.ChannelUnknown)]
    public async Task A_global_channel_route_the_operation_does_not_take_rejects_the_whole_reload(string operation, string channel, string code)
    {
        await using var api = await StartAsync(paths => Install(paths, Mailer, readsEvents: true, operations: "[message.send, campaign.get]"));
        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", "email", Mailer)), Ct);
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", "email", Mailer), (operation, channel, Mailer)), Ct);
        var detail = await RefusedAsync(api);

        Assert.Equal($"Routes:Operations:{operation}:Channels:{channel}", detail.Field);
        Assert.Equal(code, detail.Code);
        await KeptAsync(api, before);
    }

    /// <summary>R6 at the reload: a global channel route to a plugin that reads no events rejects it whole.</summary>
    [Fact]
    public async Task A_global_channel_route_to_a_plugin_that_reads_no_events_rejects_the_whole_reload()
    {
        await using var api = await StartAsync(paths =>
        {
            Install(paths, Mailer, readsEvents: true);
            Install(paths, Deaf, readsEvents: false);
        });
        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", "email", Mailer)), Ct);
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", "email", Mailer), ("message.send", "linkedin", Deaf)), Ct);
        var detail = await RefusedAsync(api);

        Assert.Equal("Routes:Operations:message.send:Channels:linkedin", detail.Field);
        Assert.Equal(RouteProblemCodes.ChannelPluginReadsNoEvents, detail.Code);
        Assert.Contains(Deaf, detail.Message, StringComparison.Ordinal);
        await KeptAsync(api, before);
    }

    /// <summary>
    /// The rule is about a channel route only: the same plugin may still take the operation's own route, which is the route
    /// a send took before channel routes existed.
    /// </summary>
    [Fact]
    public void Only_a_channel_route_needs_a_plugin_that_reads_events()
    {
        var plugins = Snapshot(TestPlugins.Loaded(Deaf, ["message.send"]), TestPlugins.Loaded(Mailer, ["message.send"], events: [1]));

        Assert.Null(RouteActivator.Check(plugins, "message.send", channel: null, Deaf, binding: null, PatternTiming.Default));
        Assert.Null(RouteActivator.Check(plugins, "message.send", "email", Mailer, binding: null, PatternTiming.Default));
        var refused = RouteActivator.Check(plugins, "message.send", "email", Deaf, binding: null, PatternTiming.Default);
        Assert.Equal(RouteProblemCodes.ChannelPluginReadsNoEvents, refused!.Code);
    }

    /// <summary>
    /// Asked after the plugin is found to perform the operation: a plugin that does not send at all is told that, rather
    /// than that it reads no events.
    /// </summary>
    [Fact]
    public void A_plugin_that_does_not_perform_the_operation_is_told_so_before_it_is_told_it_reads_no_events()
    {
        var plugins = Snapshot(TestPlugins.Loaded(Deaf, ["campaign.get"]));

        var refused = RouteActivator.Check(plugins, "message.send", "email", Deaf, binding: null, PatternTiming.Default);

        Assert.Equal(RouteProblemCodes.OperationUnsupported, refused!.Code);
    }

    /// <summary>R6 at <c>route.set</c>: the same check refuses a campaign's channel route before its row is written.</summary>
    [Fact]
    public async Task A_campaign_channel_route_to_a_plugin_that_reads_no_events_is_refused_at_route_set()
    {
        await using var api = await StartAsync(paths => Install(paths, Deaf, readsEvents: false));
        var campaign = await CampaignAsync(api);
        var before = api.Resolve<RouteRegistry>().Snapshot.Id;

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "message.send", channel = "email", plugin = Deaf },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(RouteProblemCodes.ChannelPluginReadsNoEvents, detail.Code);
        Assert.Equal(before, api.Resolve<RouteRegistry>().Snapshot.Id);
        var listed = await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { campaign_id = campaign }, Ct);
        Assert.All(listed.Campaigns, routes => Assert.True(routes.Default is null && routes.Operations.Count == 0 && (routes.Channels is null || routes.Channels.Count == 0)));
    }

    /// <summary>
    /// R6 at the reload's campaign half: a stored campaign channel route, a row of <c>campaign_channel_routes</c>, is checked
    /// again at every reload, whoever wrote it, so a row naming a plugin that never read events refuses the reload whole as
    /// the settings' would.
    /// </summary>
    [Fact]
    public async Task A_stored_campaign_channel_route_to_a_plugin_that_reads_no_events_rejects_the_whole_reload()
    {
        await using var api = await StartAsync(paths => Install(paths, Deaf, readsEvents: false));
        var campaign = await CampaignAsync(api);
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        await using (var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile)))
        {
            var id = await db.Campaigns.Where(c => c.PublicId == campaign).Select(c => c.Id).SingleAsync(Ct);
            db.CampaignChannelRoutes.Add(new CampaignChannelRoute
            {
                CampaignId = id,
                Operation = "message.send",
                Channel = "linkedin",
                PluginId = Deaf,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        var detail = await RefusedAsync(api);

        Assert.Equal($"campaign:{campaign}/routes/message.send/linkedin", detail.Field);
        Assert.Equal(RouteProblemCodes.ChannelPluginReadsNoEvents, detail.Code);
        await KeptAsync(api, before);
    }

    /// <summary>
    /// R6 when a package changes under a route: a reload whose new package no longer reads events, while a campaign's channel
    /// route names it, is refused whole, and the package that did read them stays active with the routes that name it.
    /// </summary>
    [Fact]
    public async Task A_reload_whose_new_package_stops_reading_events_while_a_channel_route_names_it_is_refused_whole()
    {
        await using var api = await StartAsync(paths => Install(paths, Mailer, readsEvents: true));
        var campaign = await CampaignAsync(api);
        await SetChannelAsync(api, campaign, "message.send", "email", Mailer);
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        Install(api.Paths, Mailer, readsEvents: false);
        var detail = await RefusedAsync(api);

        Assert.Equal($"campaign:{campaign}/routes/message.send/email", detail.Field);
        Assert.Equal(RouteProblemCodes.ChannelPluginReadsNoEvents, detail.Code);
        await KeptAsync(api, before);
        Assert.True(api.Resolve<PluginRegistry>().Snapshot.Find(Mailer)!.Manifest.ReadsEvents);
        Assert.Equal(Mailer, api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].ChannelRoute("message.send", "email")!.PluginId);
    }

    /// <summary>
    /// A campaign's route write copies the global half from the active snapshot and checks it again; the global channel
    /// routes are part of that half, so a write to one campaign keeps them rather than quietly dropping them.
    /// </summary>
    [Fact]
    public async Task A_campaign_route_write_keeps_the_global_channel_routes()
    {
        await using var api = await StartAsync(paths =>
        {
            Install(paths, Mailer, readsEvents: true);
            Install(paths, Deaf, readsEvents: false);
        });
        await TestRoutes.WriteGlobalAsync(api, Routes(("message.send", null, Deaf), ("message.send", "email", Mailer)), Ct);
        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        var campaign = await CampaignAsync(api);

        await TestRoutes.SetCampaignAsync(api, campaign, "message.send", Deaf, binding: null, Ct);

        var routes = api.Resolve<RouteRegistry>().Snapshot;
        Assert.NotEqual(reloaded.RoutingSnapshotId, routes.Id);
        Assert.Equal(Deaf, routes.Global.Operations["message.send"].PluginId);
        Assert.Equal(Mailer, routes.Global.ChannelRoute("message.send", "email")!.PluginId);
        Assert.Equal(
            RouteScope.GlobalChannel,
            RouteResolver.Resolve(routes, PublicIdOfNoCampaign, "message.send", "email")!.Scope);
    }

    /// <summary>A campaign with no routes of its own, so a resolution reaches the global half.</summary>
    private const string PublicIdOfNoCampaign = "cmp_01ARZ3NDEKTSV4RRFFQ69G5FAV";

    /// <summary>The <c>Routes</c> section with these operation routes and channel routes; a null channel is the operation's own route.</summary>
    private static string Routes(params (string Operation, string? Channel, string Plugin)[] routes)
    {
        var operations = new JsonObject();
        foreach (var (operation, channel, plugin) in routes)
        {
            var entry = (JsonObject)(operations[operation] ??= new JsonObject());
            if (channel is null)
            {
                entry["Plugin"] = plugin;
            }
            else
            {
                var channels = (JsonObject)(entry["Channels"] ??= new JsonObject());
                channels[channel] = new JsonObject { ["Plugin"] = plugin };
            }
        }

        return new JsonObject { ["Operations"] = operations }.ToJsonString();
    }

    /// <summary>A package that sends messages, written whole so that whether it reads events is the one thing that varies.</summary>
    private static void Install(JasonPaths paths, string id, bool readsEvents, string operations = "[message.send]")
    {
        var events = readsEvents ? "  events: [1]\n" : string.Empty;
        TestPlugins.Write(
            paths,
            id,
            $"manifest_version: 1\nid: {id}\nversion: 1.0.0\nkind: provider\ncontracts:\n  protocol: [1]\n  operations: [1]\n{events}operations: {operations}\n",
            "export function invoke() { return { result: {} }; }");
    }

    private static PluginSnapshot Snapshot(params LoadedPlugin[] plugins) =>
        new("snp_built", DateTime.UnixEpoch, SnapshotSource.Reload, plugins);

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed by channel" }, Ct)).Id;

    private static async Task SetChannelAsync(RuntimeApiFixture api, string campaign, string operation, string channel, string plugin)
    {
        var (status, body) = await api.PostAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation, channel, plugin },
            Ct);
        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }

    /// <summary>The single route problem a reload was refused for.</summary>
    private static async Task<ErrorDetail> RefusedAsync(RuntimeApiFixture api)
    {
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        return Assert.Single(error.Details!);
    }

    /// <summary>Both snapshots are the ones that were active before the refused reload.</summary>
    private static async Task KeptAsync(RuntimeApiFixture api, PluginRegistryDto before)
    {
        var after = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.Equal(before.Snapshot.Id, after.Snapshot.Id);
        Assert.Equal(before.RoutingSnapshotId, after.RoutingSnapshotId);
    }

    private static Task<RuntimeApiFixture> StartAsync(Action<JasonPaths> prepare) =>
        RouteActivationTests.StartAsync(prepare: prepare);
}
