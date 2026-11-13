using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The exchanges a chat's card for a connection reads to learn which plugin a campaign's enrolment would go through:
/// <c>route.resolve</c> for <c>campaign.enroll</c>, answered by a global default or by a campaign's own route, and refused
/// for a campaign nothing routes; and the same read for a call on a channel, with the list that shows the route that call
/// reaches beside the campaign's other routes. Each runtime resolves two campaigns, so the second one is the id <c>cmp_2</c> a screen's
/// fixtures call their draft campaign; the first is recorded as the exchange it is.
/// </summary>
public class RouteResolveExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Enroll = "campaign.enroll";

    [Fact]
    public async Task A_global_default_to_reply_routes_a_campaign_with_no_route_of_its_own_to_reply()
    {
        await using var api = await StartAsync(globalDefault: "reply");
        var first = await CampaignAsync(api, "First");
        var second = await CampaignAsync(api, "Second");
        await TestRoutes.SetCampaignAsync(api, first, operation: null, "other-tool", binding: null, Ct);

        var own = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "own-route", new { campaign_id = first, operation = Enroll }, Ct);
        var inherited = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "global-default", new { campaign_id = second, operation = Enroll }, Ct);

        Assert.Equal("other-tool", own.PluginId);
        Assert.Equal("reply", inherited.PluginId);
        Assert.Equal(RouteScope.GlobalDefault, inherited.Scope);
    }

    [Fact]
    public async Task A_global_default_to_another_plugin_routes_a_campaign_there()
    {
        await using var api = await StartAsync(globalDefault: "other-tool");
        var first = await CampaignAsync(api, "First");
        var second = await CampaignAsync(api, "Second");

        await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "elsewhere-first", new { campaign_id = first, operation = Enroll }, Ct);
        var resolved = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "elsewhere", new { campaign_id = second, operation = Enroll }, Ct);

        Assert.Equal("other-tool", resolved.PluginId);
    }

    [Fact]
    public async Task A_campaign_nothing_routes_is_refused_as_no_route()
    {
        await using var api = await StartAsync(globalDefault: null);
        var first = await CampaignAsync(api, "First");
        var second = await CampaignAsync(api, "Second");

        await api.RecordErrorAsync(Operations.RouteResolve, "unrouted-first", new { campaign_id = first, operation = Enroll }, HttpStatusCode.NotFound, Ct);
        var error = await api.RecordErrorAsync(Operations.RouteResolve, "no-route", new { campaign_id = second, operation = Enroll }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("no_route", error.Code);
    }

    /// <summary>
    /// A campaign that sends email through one plugin and LinkedIn messages through another: a call on a channel resolves
    /// to the channel's route, a call on any other to the operation's own, and the list names both.
    /// </summary>
    [Fact]
    public async Task A_channel_route_answers_the_calls_on_its_channel_and_the_list_shows_it_beside_the_operations_own()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(paths, "reply", Sender("reply"), "export function invoke() {}");
            TestPlugins.Write(paths, "other-tool", Sender("other-tool"), "export function invoke() {}");
        });
        var campaign = await CampaignAsync(api, "First");
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, new { campaign_id = campaign, operation = "message.send", plugin = "reply" }, Ct);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, new { campaign_id = campaign, operation = "message.send", channel = "linkedin", plugin = "other-tool" }, Ct);

        var resolved = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "on-a-channel", new { campaign_id = campaign, operation = "message.send", channel = "linkedin" }, Ct);
        var listed = await api.RecordAsync<RoutesDto>(Operations.RouteList, "channel-routes", new { campaign_id = campaign }, Ct);

        Assert.Equal(("other-tool", RouteScope.CampaignChannel, "linkedin"), (resolved.PluginId, resolved.Scope, resolved.Channel));
        var routes = Assert.Single(listed.Campaigns);
        Assert.Equal("reply", routes.Operations["message.send"].PluginId);
        Assert.Equal("other-tool", routes.Channels!["message.send"]["linkedin"].PluginId);
    }

    /// <summary>A package that sends messages and reads its accounts' events: what a channel route may name.</summary>
    private static string Sender(string id) =>
        $"""
        manifest_version: 1
        id: {id}
        version: 1.0.0
        kind: provider
        contracts:
          protocol: [1]
          operations: [1]
          events: [1]
        operations: [message.send]

        """;

    private static async Task<string> CampaignAsync(RuntimeApiFixture api, string name) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = name }, Ct)).Id;

    private static Task<RuntimeApiFixture> StartAsync(string? globalDefault) =>
        RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(paths, "reply", TestPlugins.Manifest("reply", $"[{Enroll}]"), "export function invoke() {}");
            TestPlugins.Write(paths, "other-tool", TestPlugins.Manifest("other-tool", $"[{Enroll}]"), "export function invoke() {}");
            if (globalDefault is not null)
            {
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(globalDefault));
            }
        });
}
