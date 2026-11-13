using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// What a person or a client is shown of a channel route: <c>route.resolve</c> and <c>operation.describe</c> take the
/// channel a call names and answer the plugin that call reaches, <c>route.list</c> shows the global channel routes beside
/// the campaign's, and the counts the runtime reports of its routes, in <c>system.info</c> and in the reload's line,
/// include them.
/// </summary>
public class ChannelRouteSurfaceTests
{
    private const string Mailer = "channel-mailer";

    private const string Social = "channel-social";

    private const string Send = "message.send";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolve_with_a_channel_answers_the_route_a_call_on_that_channel_reaches_and_names_the_channel()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Mailer);
        await SetAsync(api, campaign, Send, "linkedin", Social);

        var onLinkedin = await ResolveAsync(api, campaign, Send, "linkedin");
        var onEmail = await ResolveAsync(api, campaign, Send, "email");
        var bare = await ResolveAsync(api, campaign, Send, channel: null);

        Assert.Equal((Social, RouteScope.CampaignChannel, "linkedin"), (onLinkedin.PluginId, onLinkedin.Scope, onLinkedin.Channel));
        Assert.Equal((Mailer, RouteScope.CampaignOperation, "email"), (onEmail.PluginId, onEmail.Scope, onEmail.Channel));
        Assert.Equal((Mailer, RouteScope.CampaignOperation, null), (bare.PluginId, bare.Scope, bare.Channel));
    }

    [Fact]
    public async Task Resolve_with_a_channel_reaches_a_global_channel_route_when_the_campaign_names_nothing()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await TestRoutes.WriteGlobalAsync(api, Routes((Send, null, Mailer), (Send, "linkedin", Social)), Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var onLinkedin = await ResolveAsync(api, campaign, Send, "linkedin");
        var onEmail = await ResolveAsync(api, campaign, Send, "email");

        Assert.Equal((Social, RouteScope.GlobalChannel), (onLinkedin.PluginId, onLinkedin.Scope));
        Assert.Equal((Mailer, RouteScope.GlobalOperation), (onEmail.PluginId, onEmail.Scope));
    }

    /// <summary>
    /// A channel no route could name is refused as the request it is, never answered with a server error and never with
    /// the operation's own route in its place: a client that mistyped a channel would read that route as the channel's.
    /// </summary>
    [Theory]
    [InlineData(Send, "sms", "channel_unknown")]
    [InlineData("campaign.get", "email", "channel_not_routable")]
    [InlineData(Send, " ", "blank")]
    public async Task Resolve_refuses_a_channel_the_operation_does_not_route_by(string operation, string channel, string code)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Mailer);

        var error = await api.PostErrorAsync(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation, channel },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("channel", code), (detail.Field, detail.Code));
    }

    [Fact]
    public async Task Resolve_with_a_channel_that_no_route_reaches_says_which_channel_had_none()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = Send, channel = "linkedin" },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("no_route", error.Code);
        Assert.Contains("'linkedin'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_with_a_channel_describes_the_plugin_that_channel_goes_to()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Mailer);
        await SetAsync(api, campaign, Send, "linkedin", Social);

        var onLinkedin = await api.PostOkAsync<OperationDescriptionDto>(Operations.OperationDescribe, new { campaign_id = campaign, operation = Send, channel = "linkedin" }, Ct);
        var bare = await api.PostOkAsync<OperationDescriptionDto>(Operations.OperationDescribe, new { campaign_id = campaign, operation = Send }, Ct);

        Assert.Equal((Social, "linkedin"), (onLinkedin.PluginId, onLinkedin.Channel));
        Assert.Equal((Mailer, null), (bare.PluginId, bare.Channel));
    }

    [Fact]
    public async Task Describe_refuses_a_channel_the_operation_does_not_route_by()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, channel: null, Mailer);

        var error = await api.PostErrorAsync(
            Operations.OperationDescribe,
            new { campaign_id = campaign, operation = "campaign.get", channel = "email" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(("channel", "channel_not_routable"), (Assert.Single(error.Details!).Field, error.Details![0].Code));
    }

    [Fact]
    public async Task The_route_list_shows_the_global_channel_routes_beside_a_campaigns()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await TestRoutes.WriteGlobalAsync(api, Routes((Send, "email", Mailer), (Send, "linkedin", Social)), Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        await SetAsync(api, campaign, "sender.list", "linkedin", Social);

        var listed = await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { campaign_id = campaign }, Ct);

        Assert.Equal(["email", "linkedin"], listed.Global.Channels![Send].Keys);
        Assert.Equal(Mailer, listed.Global.Channels[Send]["email"].PluginId);
        Assert.Empty(listed.Global.Operations);
        Assert.Equal(Social, Assert.Single(listed.Campaigns).Channels!["sender.list"]["linkedin"].PluginId);
    }

    /// <summary>
    /// The runtime counts a channel route among the routes of the scope it is in, and says on its own how many name a
    /// channel; a runtime older than channel routes says no such number, which is how a client learns it would ignore one.
    /// </summary>
    [Fact]
    public async Task System_info_counts_channel_routes_among_the_routes_of_their_scope_and_on_their_own()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        var empty = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        await TestRoutes.WriteGlobalAsync(api, Routes((Send, null, Mailer), (Send, "email", Mailer), (Send, "linkedin", Social)), Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        await SetAsync(api, campaign, Send, channel: null, Mailer);
        await SetAsync(api, campaign, Send, "linkedin", Social);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.Equal(0, empty.Routes.ChannelRouteCount);
        Assert.Equal(3, info.Routes.GlobalOverrideCount);
        Assert.Equal(2, info.Routes.CampaignRouteCount);
        Assert.Equal(3, info.Routes.ChannelRouteCount);
    }

    [Fact]
    public async Task The_reloads_line_counts_channel_routes_among_the_routes_of_their_scope()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Send, "linkedin", Social);
        await TestRoutes.WriteGlobalAsync(api, Routes((Send, "email", Mailer)), Ct);

        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var lines = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);
        var routes = lines.Items.OrderByDescending(line => line.Id).First().New!["routes"]!;
        Assert.Equal(1, (int?)routes["override_count"]);
        Assert.Equal(1, (int?)routes["campaign_route_count"]);
    }

    private static Task<RouteResolutionDto> ResolveAsync(RuntimeApiFixture api, string campaign, string operation, string? channel) =>
        api.PostOkAsync<RouteResolutionDto>(Operations.RouteResolve, new { campaign_id = campaign, operation, channel }, Ct);

    private static Task<RoutesDto> SetAsync(RuntimeApiFixture api, string campaign, string operation, string? channel, string plugin) =>
        api.PostOkAsync<RoutesDto>(Operations.RouteSet, new { campaign_id = campaign, operation, channel, plugin }, Ct);

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed by channel" }, Ct)).Id;

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

    /// <summary>Two providers that send and read their accounts' events, so a channel route may name either.</summary>
    private static Task<RuntimeApiFixture> StartAsync() =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                Install(paths, Mailer);
                Install(paths, Social);
            });

    private static void Install(JasonPaths paths, string id) =>
        TestPlugins.Write(
            paths,
            id,
            $"manifest_version: 1\nid: {id}\nversion: 1.0.0\nkind: provider\ncontracts:\n  protocol: [1]\n  operations: [1]\n  events: [1]\noperations: [message.send, sender.list, campaign.get]\n",
            "export function invoke() { return { result: {} }; }");
}
