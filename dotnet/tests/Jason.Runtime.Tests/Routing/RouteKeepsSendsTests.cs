using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A route is where a binding is kept, and a cancel and a status read of a send go only to the plugin and account the send
/// ran under. So while a send of a campaign that succeeded under that plugin and account is held at the provider for a
/// moment still to come, and no cancel of it has succeeded, a route change that would take the last route naming that
/// plugin and account away from it is refused, naming the send, whichever door it comes through: <c>route.set</c>,
/// <c>route.unset</c> or a reload. The same change goes through once the send is cancelled, once its moment has passed,
/// and while another route still names the plugin and account.
/// </summary>
public class RouteKeepsSendsTests
{
    /// <summary>A plugin that sends, cancels and reads sends, and reads its accounts' events, so a channel route may name it.</summary>
    private const string Mailer = "keeping-mailer";

    /// <summary>A second such plugin, for the route to move to.</summary>
    private const string Social = "keeping-social";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A moment two days from now, written as the contract's pattern writes one.</summary>
    private static string Later => Moment(DateTimeOffset.UtcNow.AddDays(2));

    [Fact]
    public async Task Moving_the_only_route_off_a_deferred_send_s_account_is_refused_naming_the_send()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        var send = await SentAsync(api, campaign, Mailer, "email", Later);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        var moved = await api.PostErrorAsync(Operations.RouteSet, Set(campaign, Social), HttpStatusCode.Conflict, Ct);
        var unset = await api.PostErrorAsync(Operations.RouteUnset, new { campaign_id = campaign }, HttpStatusCode.Conflict, Ct);

        foreach (var refused in new[] { moved, unset })
        {
            Assert.Equal((RouteProblemCodes.OrphansSend, false), (refused.Code, refused.Retryable));
            var detail = Assert.Single(refused.Details!);
            Assert.Equal((RouteActivator.SendField(campaign, send), RouteProblemCodes.OrphansSend), (detail.Field, detail.Code));
            Assert.Contains(send, detail.Message, StringComparison.Ordinal);
            Assert.Contains(campaign, detail.Message, StringComparison.Ordinal);
            Assert.Contains("message.cancel", detail.Message, StringComparison.Ordinal);
        }

        Assert.Same(before, api.Resolve<RouteRegistry>().Snapshot);
        await using var db = Open(api);
        Assert.Equal(Mailer, (await db.CampaignRoutes.AsNoTracking().SingleAsync(Ct)).PluginId);
    }

    /// <summary>
    /// The same move goes through once nothing is left to cancel or to read there: a cancel of the send has succeeded, its
    /// moment has passed, or it named no moment at all; and while another route of the campaign still names the plugin and
    /// account. A cancel that failed leaves the send held, so the move is still refused.
    /// </summary>
    [Theory]
    [InlineData("cancelled", true)]
    [InlineData("its moment passed", true)]
    [InlineData("no moment", true)]
    [InlineData("another route names the account", true)]
    [InlineData("its cancel failed", false)]
    public async Task The_same_move_goes_through_once_nothing_is_left_to_cancel_there(string after, bool through)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        var send = await SentAsync(
            api,
            campaign,
            Mailer,
            "email",
            after switch
            {
                "its moment passed" => Moment(DateTimeOffset.UtcNow.AddMinutes(-5)),
                "no moment" => null,
                _ => Later,
            });
        switch (after)
        {
            case "cancelled":
                await CancelAsync(api, campaign, send, WorkItemStatus.Succeeded);
                break;
            case "its cancel failed":
                await CancelAsync(api, campaign, send, WorkItemStatus.Failed);
                break;
            case "another route names the account":
                await SetAsync(api, campaign, Mailer, "campaign.get");
                break;
        }

        var (status, body) = await api.PostAsync(Operations.RouteSet, Set(campaign, Social), Ct);

        Assert.True(through ? status == HttpStatusCode.OK : status == HttpStatusCode.Conflict, $"route.set answered {(int)status}: {body}");
        Assert.Equal(through ? Social : Mailer, api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].Default!.PluginId);
    }

    /// <summary>
    /// A LinkedIn send that went by a channel route: taking that route away, or moving it to the other plugin, is refused
    /// while the send waits for its moment, though the campaign's other routes go on naming another plugin.
    /// </summary>
    [Fact]
    public async Task Taking_away_the_channel_route_a_deferred_send_went_by_is_refused()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        await SetAsync(api, campaign, Social, "message.send", "linkedin");
        var send = await SentAsync(api, campaign, Social, "linkedin", Later);

        var unset = await api.PostErrorAsync(
            Operations.RouteUnset, new { campaign_id = campaign, operation = "message.send", channel = "linkedin" }, HttpStatusCode.Conflict, Ct);
        var moved = await api.PostErrorAsync(
            Operations.RouteSet, Set(campaign, Mailer, "message.send", "linkedin"), HttpStatusCode.Conflict, Ct);

        foreach (var refused in new[] { unset, moved })
        {
            Assert.Equal(RouteProblemCodes.OrphansSend, refused.Code);
            Assert.Equal(RouteActivator.SendField(campaign, send), Assert.Single(refused.Details!).Field);
        }

        Assert.Equal(Social, api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].ChannelRoute("message.send", "linkedin")!.PluginId);

        // The campaign's own operations go on moving freely: a send of the mailer's is not what holds them.
        await SetAsync(api, campaign, Mailer, "campaign.get");
    }

    /// <summary>
    /// The global half at a reload: settings that move the global default, the only route naming the account a deferred
    /// send ran under, reject the reload whole, naming the send, and the routes in force stay active.
    /// </summary>
    [Fact]
    public async Task A_reload_carrying_the_move_is_refused_and_the_routes_in_force_stay_active()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(Mailer));
        var campaign = await CampaignAsync(api);
        var send = await SentAsync(api, campaign, Mailer, "email", Later);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(Social), Ct);
        var refused = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", refused.Code);
        var detail = Assert.Single(refused.Details!);
        Assert.Equal((RouteActivator.SendField(campaign, send), RouteProblemCodes.OrphansSend), (detail.Field, detail.Code));
        Assert.Contains(send, detail.Message, StringComparison.Ordinal);

        Assert.Same(before, api.Resolve<RouteRegistry>().Snapshot);
        Assert.Equal(Mailer, before.Global.Default!.PluginId);
        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.False(registry.Activated);
        var recorded = Assert.Single(registry.LastReload!.Routes);
        Assert.Equal((RouteActivator.SendField(campaign, send), RouteProblemCodes.OrphansSend), (recorded.Route, recorded.Code));
    }

    /// <summary>The same reload goes through once the send's moment has passed: nothing is held there to cancel any more.</summary>
    [Fact]
    public async Task The_same_reload_goes_through_once_the_send_s_moment_has_passed()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(Mailer));
        var campaign = await CampaignAsync(api);
        await SentAsync(api, campaign, Mailer, "email", Moment(DateTimeOffset.UtcNow.AddSeconds(-1)));

        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(Social), Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        Assert.Equal(Social, api.Resolve<RouteRegistry>().Snapshot.Global.Default!.PluginId);
    }

    private static string Moment(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static object Set(string campaign, string plugin, string? operation = null, string? channel = null) =>
        new { campaign_id = campaign, operation, channel, plugin };

    private static async Task SetAsync(RuntimeApiFixture api, string campaign, string plugin, string? operation = null, string? channel = null)
    {
        var (status, body) = await api.PostAsync(Operations.RouteSet, Set(campaign, plugin, operation, channel), Ct);
        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Deferred" }, Ct)).Id;

    private static JasonDbContext Open(RuntimeApiFixture api) => new(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));

    /// <summary>
    /// A send of the campaign on <paramref name="channel"/> the provider took with its reference, under
    /// <paramref name="plugin"/> and no binding, as its attempt's provenance keeps it, for the moment
    /// <paramref name="sendAfter"/> names, or none. Answers its work item.
    /// </summary>
    private static async Task<string> SentAsync(RuntimeApiFixture api, string campaign, string plugin, string channel, string? sendAfter)
    {
        await using var db = Open(api);
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var now = DateTime.UtcNow;
        var arguments = new JsonObject
        {
            ["channel"] = channel,
            ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "owner@example.test" },
            ["message"] = new JsonObject { ["format"] = channel == "email" ? "email" : "linkedin_message", ["body"] = "Hello." },
        };
        if (sendAfter is not null)
        {
            arguments["send_after"] = sendAfter;
        }

        var item = WorkItemFactory.NewProviderOp(owner, "message.send", now);
        item.Context = new JsonObject { [Jason.Runtime.WorkItems.WorkItemService.InputKey] = arguments };
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = now;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, now);
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            "message.send", 1, null, null, RouteScope.CampaignDefault, BindingIdentity.Of(null),
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = "send-1001" });
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return item.PublicId;
    }

    /// <summary>A cancel of <paramref name="send"/> that ended as <paramref name="status"/>.</summary>
    private static async Task CancelAsync(RuntimeApiFixture api, string campaign, string send, WorkItemStatus status)
    {
        await using var db = Open(api);
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, "message.cancel", DateTime.UtcNow);
        item.Context = new JsonObject { [Jason.Runtime.WorkItems.WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        item.Status = status;
        item.FinishedAt = DateTime.UtcNow;
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Two plugins that send, cancel and read sends, and read their accounts' events, so either may take a channel route.</summary>
    private static Task<RuntimeApiFixture> StartAsync(string? routes = null) =>
        RouteActivationTests.StartAsync(
            routes,
            paths =>
            {
                foreach (var id in new[] { Mailer, Social })
                {
                    TestPlugins.Write(
                        paths,
                        id,
                        $"manifest_version: 1\nid: {id}\nversion: 1.0.0\nkind: provider\ncontracts:\n  protocol: [1]\n  operations: [1]\n  events: [1]\noperations: [message.send, message.cancel, message.status, campaign.get]\n",
                        "export function invoke() { return { result: {} }; }");
                }
            });
}
