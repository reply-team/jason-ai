using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The claim resolves a provider operation by the channel its call names, where the operation's document names the
/// argument a route reads its channel from: no new check, the same order, and the scope that won recorded where the claim
/// records one — on the approval a send waits on, and on the attempt a read runs as.
/// </summary>
public partial class ClaimerTests
{
    private const string Mailer = "mailer";
    private const string Social = "social";

    /// <summary>
    /// A send on LinkedIn goes to the plugin the LinkedIn route names, and an email of the same operation to the plugin
    /// the operation's route names; the approval each waits on records the plugin, the account and the scope that won.
    /// </summary>
    [Theory]
    [InlineData(true, "linkedin", Social, "sha256:social", RouteScope.CampaignChannel)]
    [InlineData(true, "email", Mailer, "sha256:mailer", RouteScope.CampaignOperation)]
    [InlineData(false, "linkedin", Social, "sha256:social", RouteScope.GlobalChannel)]
    [InlineData(false, "email", Mailer, "sha256:mailer", RouteScope.GlobalOperation)]
    public async Task A_send_is_routed_by_its_channel_and_its_approval_records_the_scope_that_won(
        bool atCampaign, string channel, string plugin, string identity, RouteScope scope)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = Sending(db, campaign, channel);
        await db.SaveChangesAsync(Ct);

        var set = BySendChannel("message.send", Mailer, "linkedin", Social);
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Mailer, ["message.send"]),
            routes: snapshot => Routes(snapshot, campaign.PublicId, atCampaign ? set : null, atCampaign ? null : set),
            others: [TestPlugins.Loaded(Social, ["message.send"])]);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((plugin, identity, scope), (approval.PluginId, approval.BindingIdentity, approval.RouteScope));
    }

    /// <summary>
    /// A list of sending accounts is routed by the channel it filters on: with the filter, to the plugin that would send on
    /// that channel, which is what a manager composing a send needs to read; without it, by the operation's own route, as
    /// before. The attempt records the scope that won.
    /// </summary>
    [Theory]
    [InlineData("linkedin", Social, RouteScope.CampaignChannel)]
    [InlineData(null, Mailer, RouteScope.CampaignOperation)]
    public async Task A_list_of_senders_is_routed_by_the_channel_it_filters_on(string? filter, string plugin, RouteScope scope)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var arguments = new JsonObject { ["owners"] = "own", ["limit"] = 20, ["offset"] = 0 };
        if (filter is not null)
        {
            arguments["sender_channel"] = filter;
        }

        var item = WorkItemFactory.NewProviderOp(campaign, "sender.list", Noon);
        item.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Mailer, ["sender.list"]),
            routes: snapshot => Routes(snapshot, campaign.PublicId, BySendChannel("sender.list", Mailer, "linkedin", Social), null),
            others: [TestPlugins.Loaded(Social, ["sender.list"])]);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal((plugin, scope), (work.Plan!.Plugin.Manifest.Id, work.Plan.Scope));
        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((plugin, scope), (attempt.Provenance!.PluginId, attempt.Provenance.RouteScope));
    }

    /// <summary>
    /// An operation whose document names no argument to route by is routed by its operation alone, whatever channel its
    /// arguments name and whatever channel route a snapshot holds under it: an enrolment's channel is the address the person
    /// is found by, and routing it would part one person's record at the provider by address type.
    /// </summary>
    [Fact]
    public async Task An_operation_that_names_no_route_channel_ignores_the_channel_its_call_names()
    {
        Assert.Null(OperationCatalog.Find("campaign.enroll")!.RouteChannelArgument);
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        Enrolment(db, campaign);
        await db.SaveChangesAsync(Ct);

        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Mailer, ["campaign.enroll"]),
            routes: snapshot => Routes(snapshot, campaign.PublicId, BySendChannel("campaign.enroll", Mailer, "email", Social), null),
            others: [TestPlugins.Loaded(Social, ["campaign.enroll"])]);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((Mailer, RouteScope.CampaignOperation), (approval.PluginId, approval.RouteScope));
    }

    /// <summary>A set whose route for <paramref name="operation"/> names one plugin and whose route for one channel of it another.</summary>
    private static RouteSet BySendChannel(string operation, string byOperation, string channel, string byChannel) =>
        new(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [operation] = new(byOperation, new JsonObject { ["seat"] = byOperation }, "sha256:" + byOperation) },
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
            {
                [operation] = new Dictionary<string, Route>(StringComparer.Ordinal)
                {
                    [channel] = new(byChannel, new JsonObject { ["seat"] = byChannel }, "sha256:" + byChannel),
                },
            });

    private static RouteSnapshot Routes(string pluginSnapshotId, string campaignId, RouteSet? campaign, RouteSet? global)
    {
        var campaigns = new Dictionary<string, RouteSet>(StringComparer.Ordinal);
        if (campaign is not null)
        {
            campaigns[campaignId] = campaign;
        }

        return new RouteSnapshot(PublicId.New(RouteSnapshot.IdPrefix), Noon, pluginSnapshotId, global ?? RouteSet.Empty, campaigns);
    }

    /// <summary>A send to Ada on <paramref name="channel"/>, written as text, with nothing wrong with it but that a person must approve it.</summary>
    private static WorkItem Sending(JasonDbContext db, Campaign campaign, string channel)
    {
        var email = channel == "email";
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(email
            ? new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true }
            : new ContactChannel { Channel = "linkedin", Value = "https://www.linkedin.com/in/ada-example", IsPrimary = true });
        db.Contacts.Add(contact);

        var message = email
            ? new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." }
            : new JsonObject { ["format"] = "linkedin_message", ["body"] = "Hello, Ada." };
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = channel,
                ["sender"] = new JsonObject
                {
                    ["external_id"] = "snd-1",
                    ["address"] = email ? "marta@example.com" : "https://www.linkedin.com/in/marta-owner",
                },
                ["message"] = message,
            },
        };
        db.WorkItems.Add(item);
        return item;
    }
}
