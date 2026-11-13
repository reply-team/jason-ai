using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What follows a send goes where the send ran: a cancel and a status read of a send resolve to the plugin and the account
/// the send's own attempt recorded, while any route of the campaign or a global one still names them. No check is added
/// and none moves: this is where the resolve step looks, decided before a child exists, and the fourteenth check still
/// refuses a send whose plugin and account no route names any more.
/// </summary>
public partial class ClaimerTests
{
    private static readonly string[] Follows = ["message.send", "message.cancel", "message.status"];

    /// <summary>
    /// A campaign that sends email through one plugin and LinkedIn messages through another, with its cancels and status
    /// reads routed to the first: a cancel and a status read of each send go to the plugin and account that send ran under,
    /// so neither half of the campaign's sends is left unreachable. The attempt records where it ran and the scope that won.
    /// </summary>
    [Theory]
    [InlineData("message.cancel", "email", Mailer, RouteScope.CampaignOperation)]
    [InlineData("message.cancel", "linkedin", Social, RouteScope.CampaignChannel)]
    [InlineData("message.status", "email", Mailer, RouteScope.CampaignOperation)]
    [InlineData("message.status", "linkedin", Social, RouteScope.CampaignChannel)]
    public async Task A_cancel_or_a_status_read_goes_to_the_plugin_and_account_its_send_ran_under(
        string operation, string channel, string plugin, RouteScope scope)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var email = Sent(db, campaign, "email", Mailer, "sha256:" + Mailer);
        var linkedin = Sent(db, campaign, "linkedin", Social, "sha256:" + Social);
        var item = Following(db, campaign, operation, channel == "email" ? email : linkedin);
        await db.SaveChangesAsync(Ct);

        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Mailer, Follows),
            routes: snapshot => Routes(snapshot, campaign.PublicId, TwoPlugins(), null),
            others: [TestPlugins.Loaded(Social, Follows)]);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        Assert.Equal((plugin, "sha256:" + plugin, scope), (work.Plan!.Plugin.Manifest.Id, work.Plan.BindingIdentity, work.Plan.Scope));
        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(row => row.WorkItemId == item.Id, Ct);
        Assert.Equal((plugin, "sha256:" + plugin, scope), (attempt.Provenance!.PluginId, attempt.Provenance.BindingIdentity, attempt.Provenance.RouteScope));
    }

    /// <summary>
    /// The claim reads no <c>cancelable</c> flag: whether a send can still be taken back is the provider's to answer, by the
    /// cancel operation's own document, which fails a send that already went <c>send_not_pending</c>. So a cancel of a send
    /// whose result says false, or says nothing, is claimed and goes to the plugin and account the send ran under, as one
    /// whose result says true does; only the route guard reads the flag, to decide whether a route must be kept for it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task A_cancel_is_claimed_whatever_the_send_s_result_says_of_cancelling_it(bool? cancelable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var send = Sent(db, campaign, "linkedin", Social, "sha256:" + Social);
        var outcome = new JsonObject { ["status"] = "accepted", ["reference"] = "linkedin-1001" };
        if (cancelable is { } can)
        {
            outcome["cancelable"] = can;
        }

        send.Result = new JsonObject { ["items"] = new JsonArray(outcome) };
        var item = Following(db, campaign, "message.cancel", send);
        await db.SaveChangesAsync(Ct);

        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Mailer, Follows),
            routes: snapshot => Routes(snapshot, campaign.PublicId, TwoPlugins(), null),
            others: [TestPlugins.Loaded(Social, Follows)]);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal((item.PublicId, Social), (work.WorkItemPublicId, work.Plan!.Plugin.Manifest.Id));
        Assert.Equal("linkedin-1001", (string?)work.Plan.Input["send"]!["reference"]);
    }

    /// <summary>
    /// A send whose plugin and account no route names any more is not followed anywhere: its cancel and its status read
    /// resolve as they did before, by their own route, and the fourteenth check refuses them, permanently, as before.
    /// </summary>
    [Theory]
    [InlineData("message.cancel")]
    [InlineData("message.status")]
    public async Task A_send_no_route_names_any_more_is_refused_by_the_fourteenth_check_as_before(string operation)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var gone = Sent(db, campaign, "linkedin", Social, "sha256:retired-seat");
        var item = Following(db, campaign, operation, gone);
        await db.SaveChangesAsync(Ct);

        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Mailer, Follows),
            routes: snapshot => Routes(snapshot, campaign.PublicId, TwoPlugins(), null),
            others: [TestPlugins.Loaded(Social, Follows)]);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(row => row.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.SendRouteMismatch, stored.LastError!.Code);
    }

    /// <summary>
    /// The campaign's routes: sends by email, and every cancel and status read, through the mailer; LinkedIn messages
    /// through the social plugin by the send's channel route.
    /// </summary>
    private static RouteSet TwoPlugins()
    {
        var set = BySendChannel("message.send", Mailer, "linkedin", Social);
        var mailer = set.Operations["message.send"];
        var operations = new Dictionary<string, Route>(set.Operations, StringComparer.Ordinal)
        {
            ["message.cancel"] = mailer,
            ["message.status"] = mailer,
        };
        return set with { Operations = operations };
    }

    /// <summary>
    /// A send of this campaign on <paramref name="channel"/> that succeeded with the provider's reference, under
    /// <paramref name="plugin"/> and the account <paramref name="identity"/> names, as its attempt's provenance keeps it.
    /// </summary>
    private static WorkItem Sent(JasonDbContext db, Campaign campaign, string channel, string plugin, string identity)
    {
        var item = Sending(db, campaign, channel);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Noon;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            "message.send", 1, null, null, RouteScope.CampaignOperation, identity,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = channel + "-1001" });
        db.Attempts.Add(attempt);
        return item;
    }

    /// <summary>A cancel or a status read of <paramref name="send"/>, waiting to be claimed.</summary>
    private static WorkItem Following(JasonDbContext db, Campaign campaign, string operation, WorkItem send)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon);
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send.PublicId } };
        db.WorkItems.Add(item);
        return item;
    }
}
