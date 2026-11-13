using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Hosting;
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
/// moment still to come, a route change that would take the last route naming that plugin and account away from it is
/// refused, naming the send, whichever door it comes through: <c>route.set</c>, <c>route.unset</c> or a reload. Only a
/// send whose result says the provider can cancel it holds a route: the flag is read as the operation's document defines
/// it, absent as false. The moment is the provider's own where its result names one, and the arguments' otherwise. The
/// same change goes through once the provider has said it holds the send no longer (a cancel succeeded, a cancel was
/// answered that nothing was left to cancel, a status read says the send is final or can no longer be cancelled), once its
/// moment has passed by the runtime's own clock, when its result does not say it can be cancelled, and while another route
/// still names the plugin and account. Every moment here is read against the runtime's clock, which each test holds at
/// noon and moves itself.
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
            Assert.Contains("1 send(s)", refused.Message, StringComparison.Ordinal);
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
    /// The same move goes through once nothing is left to cancel or to read there. The provider has said it holds the send
    /// no longer: a cancel of it succeeded; a cancel of it failed with the answer its document declares as nothing to
    /// cancel, the send already went or the provider holds none under the reference; a status read of it failed with the
    /// answer that the provider holds no such send; a status read of it succeeded and says it went, or, the latest of them,
    /// that it can no longer be cancelled. Its moment has passed, the provider's own where its result names one, though its
    /// arguments named a later one; it names no moment at all, which the guard does not keep; or its own result does not
    /// say the provider can cancel it, whether it says false or says nothing, which the operation's document reads as
    /// false. Or another route of the campaign still names the plugin and account. A cancel that failed for any other
    /// reason, a cancel still to run, a status read that says the send is still queued and cancellable, and an older read
    /// that said it could no longer be cancelled under a later one that says it can, leave it held; so does a send the
    /// provider took for a later moment of its own with no moment in its arguments, and one whose result says the provider
    /// can cancel it, as every other row here has: the move is still refused.
    /// </summary>
    [Theory]
    [InlineData("cancelled", true)]
    [InlineData("its cancel found nothing pending", true)]
    [InlineData("its cancel found no such send", true)]
    [InlineData("a status read says it went", true)]
    [InlineData("a status read says it can no longer be cancelled", true)]
    [InlineData("a status read found no such send", true)]
    [InlineData("its moment passed", true)]
    [InlineData("the provider's own moment passed", true)]
    [InlineData("no moment", true)]
    [InlineData("another route names the account", true)]
    [InlineData("the provider cannot cancel it", true)]
    [InlineData("the provider says nothing of cancelling it", true)]
    [InlineData("its cancel failed for now", false)]
    [InlineData("its cancel is blocked", false)]
    [InlineData("its cancel is still to run", false)]
    [InlineData("a status read says it is still queued", false)]
    [InlineData("a later status read says it can be cancelled again", false)]
    [InlineData("the provider took it for a moment of its own", false)]
    [InlineData("the provider can cancel it", false)]
    public async Task The_same_move_goes_through_once_nothing_is_left_to_cancel_there(string after, bool through)
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        var send = await SentAsync(
            api.Paths,
            campaign,
            Mailer,
            "email",
            after switch
            {
                "its moment passed" => Moment(Noon.AddMinutes(-5)),
                "no moment" or "the provider took it for a moment of its own" => null,
                _ => Later,
            },
            after switch
            {
                "the provider cannot cancel it" => false,
                "the provider says nothing of cancelling it" => null,
                _ => true,
            },
            after switch
            {
                "the provider's own moment passed" => Moment(Noon.AddMinutes(-5)),
                "the provider took it for a moment of its own" => Later,
                _ => null,
            });
        switch (after)
        {
            case "cancelled":
                await CancelAsync(api, campaign, send, WorkItemStatus.Succeeded);
                break;
            case "its cancel found nothing pending":
                await CancelAsync(api, campaign, send, WorkItemStatus.Failed, "send_not_pending");
                break;
            case "its cancel found no such send":
                await CancelAsync(api, campaign, send, WorkItemStatus.Failed, "send_not_found");
                break;
            case "its cancel failed for now":
                await CancelAsync(api, campaign, send, WorkItemStatus.Failed, "provider_unavailable");
                break;
            case "its cancel is blocked":
                await CancelAsync(api, campaign, send, WorkItemStatus.Failed, "provider_blocked");
                break;
            case "its cancel is still to run":
                await CancelAsync(api, campaign, send, WorkItemStatus.Scheduled);
                break;
            case "a status read says it went":
                await StatusReadAsync(api, campaign, send, "sent", cancelable: false);
                break;
            case "a status read says it can no longer be cancelled":
                await StatusReadAsync(api, campaign, send, "queued", cancelable: false);
                break;
            case "a status read says it is still queued":
                await StatusReadAsync(api, campaign, send, "queued", cancelable: true);
                break;
            case "a later status read says it can be cancelled again":
                await StatusReadAsync(api, campaign, send, "queued", cancelable: false, finishedAt: Noon.AddMinutes(-2));
                await StatusReadAsync(api, campaign, send, "queued", cancelable: true, finishedAt: Noon.AddMinutes(-1));
                break;
            case "a status read found no such send":
                await CancelAsync(api, campaign, send, WorkItemStatus.Failed, "send_not_found", HeldSends.Status);
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
    /// The answers that let a send go are the ones the operations' documents declare: the two failures of a cancel that
    /// say nothing is left to cancel and the failure of a status read that says there is no such send, all final, and the
    /// states of a status read the document calls final, every one but the queued one.
    /// </summary>
    [Fact]
    public void The_answers_that_let_a_send_go_are_the_ones_the_operations_documents_declare()
    {
        var cancel = OperationCatalog.Find(HeldSends.Cancel)!;
        foreach (var code in HeldSends.NothingToCancel)
        {
            Assert.Equal(FailureClass.Permanent, Assert.Single(cancel.FailureCodes, declared => declared.Code == code).Class);
        }

        var status = OperationCatalog.Find(HeldSends.Status)!;
        foreach (var code in HeldSends.NothingToRead)
        {
            Assert.Equal(FailureClass.Permanent, Assert.Single(status.FailureCodes, declared => declared.Code == code).Class);
        }

        var states = status.OutputSchema["properties"]!["state"]!["enum"]!.AsArray()
            .Select(state => (string)state!)
            .ToList();
        Assert.Equal(["queued", .. HeldSends.FinalStates], states);
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
    /// The moment a refusal names as the time after which the change goes through is rounded up to its second, so a person
    /// who asks again at that second is not refused again by a fraction the answer did not show.
    /// </summary>
    [Fact]
    public async Task The_moment_a_refusal_names_is_rounded_up_to_its_second_and_a_change_at_that_second_goes_through()
    {
        var clock = new FixedClock(Noon);
        await using var api = await StartAsync(clock: clock);
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        await SentAsync(api, campaign, Mailer, "email", "2026-11-12T12:30:00.400Z");

        var refused = await api.PostErrorAsync(Operations.RouteSet, Set(campaign, Social), HttpStatusCode.Conflict, Ct);

        Assert.Contains("change the route after 2026-11-12T12:30:01Z.", Assert.Single(refused.Details!).Message, StringComparison.Ordinal);
        clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        await SetAsync(api, campaign, Social);
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
        Assert.Contains("2 send(s)", refused.Message, StringComparison.Ordinal);
        var detail = Assert.Single(refused.Details!);
        Assert.Equal((RouteActivator.SendField(campaign, latest), RouteProblemCodes.OrphansSend), (detail.Field, detail.Code));
        Assert.Contains("2 send(s)", detail.Message, StringComparison.Ordinal);
        Assert.Contains(earlier, detail.Message, StringComparison.Ordinal);
        Assert.Contains(latest, detail.Message, StringComparison.Ordinal);
        Assert.Contains("until 2026-11-14T23:45:00Z", detail.Message, StringComparison.Ordinal);
        Assert.Contains("Cancel them with message.cancel, or change the route after 2026-11-14T23:45:00Z.", detail.Message, StringComparison.Ordinal);
        foreach (var said in new[] { person, Recipient, SenderAddress, Subject, Body, "18:45:00-05:00", "09:30:00+02:00" })
        {
            Assert.DoesNotContain(said, refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(said, detail.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// More than five sends at one account: the answer and the detail count all of them, the detail names the latest five
    /// by their work items and says how many more there are, and the earlier ones are not named.
    /// </summary>
    [Fact]
    public async Task More_than_five_sends_are_counted_whole_and_the_latest_five_named()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        var sends = new List<string>();
        for (var hour = 1; hour <= 7; hour++)
        {
            sends.Add(await SentAsync(api, campaign, Mailer, "email", Moment(Noon.AddHours(hour))));
        }

        var refused = await api.PostErrorAsync(Operations.RouteSet, Set(campaign, Social), HttpStatusCode.Conflict, Ct);

        Assert.Contains("7 send(s)", refused.Message, StringComparison.Ordinal);
        var detail = Assert.Single(refused.Details!);
        Assert.Equal(RouteActivator.SendField(campaign, sends[^1]), detail.Field);
        Assert.Contains("7 send(s)", detail.Message, StringComparison.Ordinal);
        Assert.Contains("and 2 more", detail.Message, StringComparison.Ordinal);
        Assert.All(sends[2..], named => Assert.Contains(named, detail.Message, StringComparison.Ordinal));
        Assert.All(sends[..2], unnamed => Assert.DoesNotContain(unnamed, detail.Message, StringComparison.Ordinal));
    }

    /// <summary>
    /// The account is the binding's identity, not only the plugin: a route kept on the same plugin under another binding no
    /// longer names the account the send ran under, so it is refused as a move to another plugin is.
    /// </summary>
    [Fact]
    public async Task A_binding_edit_that_changes_the_account_s_identity_is_refused()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        await SetAsync(api, campaign, Mailer);
        var send = await SentAsync(api, campaign, Mailer, "email", Later);

        var refused = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, plugin = Mailer, binding = new { workspace = "east" } },
            HttpStatusCode.Conflict,
            Ct);

        Assert.Equal(RouteProblemCodes.OrphansSend, refused.Code);
        Assert.Equal(RouteActivator.SendField(campaign, send), Assert.Single(refused.Details!).Field);
        Assert.Null(api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].Default!.Binding);
    }

    /// <summary>
    /// Two campaigns holding one account by the global default: a reload that moves it is refused with one detail per
    /// campaign, each counting and naming only its own sends.
    /// </summary>
    [Fact]
    public async Task Two_campaigns_holding_one_account_are_each_named_by_a_detail_of_their_own()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(Mailer));
        var first = await CampaignAsync(api, "Deferred east");
        var second = await CampaignAsync(api, "Deferred west");
        var firstSends = new[]
        {
            await SentAsync(api, first, Mailer, "email", Moment(Noon.AddHours(1))),
            await SentAsync(api, first, Mailer, "email", Moment(Noon.AddHours(2))),
        };
        var secondSend = await SentAsync(api, second, Mailer, "email", Moment(Noon.AddHours(3)));

        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(Social), Ct);
        var refused = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal(2, refused.Details!.Count);
        var ofFirst = Assert.Single(refused.Details, detail => detail.Field == RouteActivator.SendField(first, firstSends[1]));
        var ofSecond = Assert.Single(refused.Details, detail => detail.Field == RouteActivator.SendField(second, secondSend));
        Assert.Contains("2 send(s)", ofFirst.Message, StringComparison.Ordinal);
        Assert.All(firstSends, send => Assert.Contains(send, ofFirst.Message, StringComparison.Ordinal));
        Assert.DoesNotContain(secondSend, ofFirst.Message, StringComparison.Ordinal);
        Assert.Contains("1 send(s)", ofSecond.Message, StringComparison.Ordinal);
        Assert.All(firstSends, send => Assert.DoesNotContain(send, ofSecond.Message, StringComparison.Ordinal));
        Assert.Equal(Mailer, api.Resolve<RouteRegistry>().Snapshot.Global.Default!.PluginId);
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

    /// <summary>
    /// A start is not one of the doors a change is refused at: settings edited while the runtime was stopped, moving the
    /// only route off a held send's account, are activated, and the send they leave without a route is said once in the
    /// log, by its work item, its campaign and the plugin it ran at, and by nothing it says to a person. A later change,
    /// which cannot be what stranded it, neither refuses for it nor says it again.
    /// </summary>
    [Fact]
    public async Task A_start_with_the_move_already_in_the_settings_activates_it_and_says_the_send_once_in_the_log()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        WritePlugins(dir.Paths);
        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(Mailer));
        string campaign;
        string send;
        await using (var first = await RuntimeHost.StartAsync(dir.Paths, HostOptions, Ct))
        {
            await using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
            var owner = WorkItemFactory.NewCampaign("Deferred", now: Noon.UtcDateTime);
            db.Campaigns.Add(owner);
            await db.SaveChangesAsync(Ct);
            campaign = owner.PublicId;
            send = await SentAsync(dir.Paths, campaign, Mailer, "email", Later);
        }

        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(Social));
        await using (var second = await RuntimeHost.StartAsync(dir.Paths, HostOptions, Ct))
        {
            Assert.Equal(Social, second.Services.GetRequiredService<RouteRegistry>().Snapshot.Global.Default!.PluginId);
            using var http = new HttpClient { BaseAddress = second.BaseUrl };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Token);
            using var moved = await http.PostAsJsonAsync(Operations.Route(Operations.RouteSet), Set(campaign, Social), JasonJson.Options, Ct);
            Assert.True(moved.StatusCode == HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        }

        var said = Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Contains(send, StringComparison.Ordinal))
            .ToList();
        var line = JsonNode.Parse(Assert.Single(said))!;
        Assert.Equal("Warning", (string?)line["@l"]);
        Assert.Equal((campaign, Mailer), ((string?)line["CampaignId"], (string?)line["PluginId"]));
        Assert.Contains("Put such a route back", (string?)line["@mt"], StringComparison.Ordinal);
        foreach (var personal in new[] { Recipient, SenderAddress, Subject, Body })
        {
            Assert.DoesNotContain(personal, said[0], StringComparison.Ordinal);
        }
    }

    private static string Moment(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static object Set(string campaign, string plugin, string? operation = null, string? channel = null) =>
        new { campaign_id = campaign, operation, channel, plugin };

    private static async Task SetAsync(RuntimeApiFixture api, string campaign, string plugin, string? operation = null, string? channel = null)
    {
        var (status, body) = await api.PostAsync(Operations.RouteSet, Set(campaign, plugin, operation, channel), Ct);
        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api, string name = "Deferred") =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = name }, Ct)).Id;

    private static JasonDbContext Open(RuntimeApiFixture api) => new(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));

    private static Task<string> SentAsync(RuntimeApiFixture api, string campaign, string plugin, string channel, string? sendAfter) =>
        SentAsync(api.Paths, campaign, plugin, channel, sendAfter);

    /// <summary>
    /// A send of the campaign on <paramref name="channel"/> to a person, which the provider took with its reference, under
    /// <paramref name="plugin"/> and no binding, as its attempt's provenance keeps it, for the moment
    /// <paramref name="sendAfter"/> names, or none; its result says the provider can cancel it unless
    /// <paramref name="cancelable"/> says otherwise, and says nothing about it where that is null, and names the moment the
    /// provider took it for where <paramref name="acceptedFor"/> is given. Answers its work item.
    /// </summary>
    private static async Task<string> SentAsync(
        JasonPaths paths, string campaign, string plugin, string channel, string? sendAfter, bool? cancelable = true, string? acceptedFor = null)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
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

        var outcome = new JsonObject
        {
            ["contact_id"] = contact.PublicId,
            ["status"] = "accepted",
            ["reference"] = "send-1001",
            ["accepted_for"] = acceptedFor,
        };
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
        return await db.WorkItems.Where(item => item.Operation == "message.send" && item.PublicId == send).Select(item => item.Contact!.PublicId).SingleAsync(Ct);
    }

    /// <summary>
    /// A cancel of <paramref name="send"/>, or another <paramref name="operation"/> naming it, that stands at
    /// <paramref name="status"/>, failed with <paramref name="code"/> where one is given.
    /// </summary>
    private static async Task CancelAsync(
        RuntimeApiFixture api, string campaign, string send, WorkItemStatus status, string? code = null, string operation = HeldSends.Cancel)
    {
        await using var db = Open(api);
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, operation, Noon.UtcDateTime);
        item.Context = new JsonObject { [Jason.Runtime.WorkItems.WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        item.Status = status;
        if (status is WorkItemStatus.Succeeded or WorkItemStatus.Failed)
        {
            item.FinishedAt = Noon.UtcDateTime;
        }

        if (code is not null)
        {
            item.LastError = new AttemptErrorDto(code, "The provider answered.", Retriable: false);
        }

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// A status read of <paramref name="send"/> that succeeded at <paramref name="finishedAt"/>, or noon, saying
    /// <paramref name="state"/> and <paramref name="cancelable"/>.
    /// </summary>
    private static async Task StatusReadAsync(
        RuntimeApiFixture api, string campaign, string send, string state, bool cancelable, DateTimeOffset? finishedAt = null)
    {
        await using var db = Open(api);
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, HeldSends.Status, Noon.UtcDateTime);
        item.Context = new JsonObject { [Jason.Runtime.WorkItems.WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = (finishedAt ?? Noon).UtcDateTime;
        item.Result = new JsonObject { ["state"] = state, ["channel"] = "email", ["cancelable"] = cancelable };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Two plugins that send, cancel and read sends, and read their accounts' events, so either may take a channel route,
    /// and whose binding may name a workspace.
    /// </summary>
    private static void WritePlugins(JasonPaths paths)
    {
        foreach (var id in new[] { Mailer, Social })
        {
            TestPlugins.Write(
                paths,
                id,
                $"manifest_version: 1\nid: {id}\nversion: 1.0.0\nkind: provider\ncontracts:\n  protocol: [1]\n  operations: [1]\n  events: [1]\noperations: [message.send, message.cancel, message.status, campaign.get]\nbinding:\n  type: object\n  properties:\n    workspace:\n      type: string\n",
                "export function invoke() { return { result: {} }; }");
        }
    }

    private static RuntimeHostOptions HostOptions => TestRuntimeOptions.Quiet with
    {
        Clock = new FixedClock(Noon),
        ConfigureServices = services =>
        {
            TestRuntimeOptions.TurnTheOutsideOff(services);
            services.AddSingleton(TestPlugins.SearchPath);
        },
    };

    /// <summary>The two plugins, and the runtime's clock held at noon, or the one a test passes to move it.</summary>
    private static Task<RuntimeApiFixture> StartAsync(string? routes = null, FixedClock? clock = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                WritePlugins(paths);
                if (routes is not null)
                {
                    TestRoutes.WriteGlobal(paths, routes);
                }
            },
            clock: clock ?? new FixedClock(Noon),
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));
}
