using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The exchanges a chat's card for a connection reads to learn which plugin a campaign's enrolment would go through:
/// <c>route.resolve</c> for <c>campaign.enroll</c>, answered by a global default or by a campaign's own route, and refused
/// for a campaign nothing routes. Each runtime resolves two campaigns, so the second one is the id <c>cmp_2</c> a screen's
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
