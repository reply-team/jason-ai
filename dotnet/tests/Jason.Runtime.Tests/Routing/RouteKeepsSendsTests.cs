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
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A route is where a binding is kept, and a cancel and a status read of a send go only to the plugin and account the send
/// ran under. So while a send of a campaign that succeeded under that plugin and account is held at the provider for a
/// moment still to come, and no cancel of it has succeeded, a route change that would take the last route naming that
/// plugin and account away from it is refused, naming the send, whichever door it comes through: <c>route.set</c>,
/// <c>route.unset</c> or a reload. The same change goes through once the send is cancelled, once its moment has passed by
/// the runtime's own clock, when its result says the provider cannot cancel it, and while another route still names the
/// plugin and account. Every moment here is read against the runtime's clock, which each test holds at noon and moves
/// itself.
/// </summary>
public class RouteKeepsSendsTests
{
    /// <summary>A plugin that sends, cancels and reads sends, and reads its accounts' events, so a channel route may name it.</summary>
    private const string Mailer = "keeping-mailer";

    /// <summary>A second such plugin, for the route to move to.</summary>
    private const string Social = "keeping-social";

    private const string Recipient = "ada@example.test";

    private const string SenderAddress = "owner@example.test";

    private const string Subject = "A quiet word about the pilot";

    private const string Body = "Ada, the pilot numbers are in.";

    /// <summary>The moment the runtime's clock stands at when each test starts.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A moment two days after noon, written as the contract's pattern writes one.</summary>
    private static string Later => Moment(Noon.AddDays(2));

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
    /// moment has passed, it named no moment at all, or its own result says the provider cannot cancel it; and while another
    /// route of the campaign still names the plugin and account. A cancel that failed leaves the send held, and so does a
    /// result that says the provider can cancel it: the move is still refused. A result that says nothing either way holds
    /// too, as every other row here shows.
    /// </summary>
    [Theory]
    [InlineData("cancelled", true)]
    [InlineData("its moment passed", true)]
    [InlineData("no moment", true)]
    [InlineData("another route names the account", true)]
    [InlineData("the provider cannot cancel it", true)]
    [InlineData("its cancel failed", false)]
    [InlineData("the provider can cancel it", false)]
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
                "its moment passed" => Moment(Noon.AddMinutes(-5)),
                "no moment" => null,
                _ => Later,
            },
            after switch
            {
                "the provider cannot cancel it" => false,
                "the provider can cancel it" => true,
                _ => null,
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
    /// The guard lets go by itself: no cancel is made and nothing is written, and the same <c>route.set</c> that was refused
    /// while the send's moment was still to come goes through once the runtime's clock, the one the activator reads, has
    /// passed it.
    /// </summary>
    [Fact]
    public async Task The_guard_lets_go_by_itself_once_the_runtime_s_clock_passes_the_moment()
    {
        var clock = new FixedClock(Noon);
        await using var api = await StartAsync(clock: clock);
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        await SentAsync(api, campaign, Mailer, "email", Moment(Noon.AddMinutes(30)));

        Assert.Equal(RouteProblemCodes.OrphansSend, (await api.PostErrorAsync(Operations.RouteSet, Set(campaign, Social), HttpStatusCode.Conflict, Ct)).Code);
        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(RouteProblemCodes.OrphansSend, (await api.PostErrorAsync(Operations.RouteSet, Set(campaign, Social), HttpStatusCode.Conflict, Ct)).Code);

        clock.Advance(TimeSpan.FromMinutes(2));
        await SetAsync(api, campaign, Social);

        Assert.Equal(Social, api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].Default!.PluginId);
        await using var db = Open(api);
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == "message.cancel", Ct));
    }

    /// <summary>
    /// The sends of one campaign held at one account are one problem: it says how many hold the route, the latest moment
    /// among them, in UTC, and the two ways out (cancel them, or change the route after that moment), and names them by
    /// their work items. It says nothing a send says to a person: not who it is to, not the sender's address, not the
    /// subject or the text, and not a moment as its arguments wrote it.
    /// </summary>
    [Fact]
    public async Task The_refusal_counts_the_sends_names_the_latest_moment_and_the_ways_out_and_nothing_a_send_says()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        var earlier = await SentAsync(api, campaign, Mailer, "email", "2026-11-13T09:30:00+02:00");
        var latest = await SentAsync(api, campaign, Mailer, "email", "2026-11-14T18:45:00-05:00");
        var person = await PersonOfAsync(api, latest);

        var refused = await api.PostErrorAsync(Operations.RouteSet, Set(campaign, Social), HttpStatusCode.Conflict, Ct);

        Assert.Equal(RouteProblemCodes.OrphansSend, refused.Code);
        var detail = Assert.Single(refused.Details!);
        Assert.Equal((RouteActivator.SendField(campaign, latest), RouteProblemCodes.OrphansSend), (detail.Field, detail.Code));
        Assert.Contains("2 send(s)", detail.Message, StringComparison.Ordinal);
        Assert.Contains(earlier, detail.Message, StringComparison.Ordinal);
        Assert.Contains(latest, detail.Message, StringComparison.Ordinal);
        Assert.Contains("until 2026-11-14T23:45:00Z", detail.Message, StringComparison.Ordinal);
        Assert.Contains("Cancel them with message.cancel, or change the route after 2026-11-14T23:45:00Z.", detail.Message, StringComparison.Ordinal);
        foreach (var said in new[] { person, Recipient, SenderAddress, Subject, Body, "18:45:00-05:00", "09:30:00+02:00" })
        {
            Assert.DoesNotContain(said, detail.Message, StringComparison.Ordinal);
        }
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

    /// <summary>
    /// The same reload goes through once the runtime's clock has passed the send's moment: nothing is held there to cancel
    /// any more, and no cancel was made.
    /// </summary>
    [Fact]
    public async Task The_same_reload_goes_through_once_the_send_s_moment_has_passed()
    {
        var clock = new FixedClock(Noon);
        await using var api = await StartAsync(TestRoutes.GlobalDefault(Mailer), clock);
        var campaign = await CampaignAsync(api);
        await SentAsync(api, campaign, Mailer, "email", Moment(Noon.AddHours(1)));
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(Social), Ct);
        Assert.Equal("plugin_reload_rejected", (await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct)).Code);

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
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
    /// A send of the campaign on <paramref name="channel"/> to a person, which the provider took with its reference, under
    /// <paramref name="plugin"/> and no binding, as its attempt's provenance keeps it, for the moment
    /// <paramref name="sendAfter"/> names, or none; its result says whether the provider can cancel it where
    /// <paramref name="cancelable"/> is not null, and nothing about it otherwise. Answers its work item.
    /// </summary>
    private static async Task<string> SentAsync(RuntimeApiFixture api, string campaign, string plugin, string channel, string? sendAfter, bool? cancelable = null)
    {
        await using var db = Open(api);
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var now = Noon.UtcDateTime;
        var contact = new Contact { PublicId = Jason.Contracts.Ids.PublicId.New("cnt"), FirstName = "Ada", CreatedAt = now, UpdatedAt = now };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = Recipient, IsPrimary = true });
        db.Contacts.Add(contact);
        var arguments = new JsonObject
        {
            ["channel"] = channel,
            ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = SenderAddress },
            ["message"] = channel == "email"
                ? new JsonObject { ["format"] = "email", ["subject"] = Subject, ["body"] = Body }
                : new JsonObject { ["format"] = "linkedin_message", ["body"] = Body },
        };
        if (sendAfter is not null)
        {
            arguments["send_after"] = sendAfter;
        }

        var outcome = new JsonObject { ["contact_id"] = contact.PublicId, ["status"] = "accepted", ["reference"] = "send-1001" };
        if (cancelable is { } can)
        {
            outcome["cancelable"] = can;
        }

        var item = WorkItemFactory.NewProviderOp(owner, "message.send", now);
        item.Context = new JsonObject { [Jason.Runtime.WorkItems.WorkItemService.InputKey] = arguments };
        item.Contact = contact;
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = now;
        item.Result = new JsonObject { ["items"] = new JsonArray(outcome) };
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

    /// <summary>The public id of the person <paramref name="send"/> is to.</summary>
    private static async Task<string> PersonOfAsync(RuntimeApiFixture api, string send)
    {
        await using var db = Open(api);
        return await db.WorkItems.Where(item => item.PublicId == send).Select(item => item.Contact!.PublicId).SingleAsync(Ct);
    }

    /// <summary>A cancel of <paramref name="send"/> that ended as <paramref name="status"/>.</summary>
    private static async Task CancelAsync(RuntimeApiFixture api, string campaign, string send, WorkItemStatus status)
    {
        await using var db = Open(api);
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, "message.cancel", Noon.UtcDateTime);
        item.Context = new JsonObject { [Jason.Runtime.WorkItems.WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        item.Status = status;
        item.FinishedAt = Noon.UtcDateTime;
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Two plugins that send, cancel and read sends, and read their accounts' events, so either may take a channel route;
    /// the runtime's clock held at noon, or the one a test passes to move it.
    /// </summary>
    private static Task<RuntimeApiFixture> StartAsync(string? routes = null, FixedClock? clock = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                foreach (var id in new[] { Mailer, Social })
                {
                    TestPlugins.Write(
                        paths,
                        id,
                        $"manifest_version: 1\nid: {id}\nversion: 1.0.0\nkind: provider\ncontracts:\n  protocol: [1]\n  operations: [1]\n  events: [1]\noperations: [message.send, message.cancel, message.status, campaign.get]\n",
                        "export function invoke() { return { result: {} }; }");
                }

                if (routes is not null)
                {
                    TestRoutes.WriteGlobal(paths, routes);
                }
            },
            clock: clock ?? new FixedClock(Noon),
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));
}
