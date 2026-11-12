using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The exchanges a chat's card for a connection reads to learn which plugin a campaign's enrolment would go through:
/// <c>route.resolve</c> for <c>campaign.enroll</c>, answered by a global default, answered by a campaign's own route, and
/// refused for a campaign nothing routes.
/// </summary>
public class RouteResolveExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Enroll = "campaign.enroll";

    [Fact]
    public async Task A_campaign_resolves_to_the_global_default_or_to_its_own_route()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(paths, "reply", TestPlugins.Manifest("reply", $"[{Enroll}]"), "export function invoke() {}");
            TestPlugins.Write(paths, "other-tool", TestPlugins.Manifest("other-tool", $"[{Enroll}]"), "export function invoke() {}");
            TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault("reply"));
        });
        var plain = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Plain" }, Ct)).Id;
        var own = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Elsewhere" }, Ct)).Id;
        await TestRoutes.SetCampaignAsync(api, own, operation: null, "other-tool", binding: null, Ct);

        var viaDefault = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "global-default", new { campaign_id = plain, operation = Enroll }, Ct);
        var viaOwn = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "campaign-route", new { campaign_id = own, operation = Enroll }, Ct);

        Assert.Equal("reply", viaDefault.PluginId);
        Assert.Equal(RouteScope.GlobalDefault, viaDefault.Scope);
        Assert.Equal("other-tool", viaOwn.PluginId);
    }

    [Fact]
    public async Task A_campaign_nothing_routes_is_refused_as_no_route()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths => File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Unrouted" }, Ct)).Id;

        var error = await api.RecordErrorAsync(Operations.RouteResolve, "no-route", new { campaign_id = campaign, operation = Enroll }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("no_route", error.Code);
    }
}
