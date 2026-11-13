using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A thread is held by the provider account it was seen at. A read of a thread, and filing it — classifying, closing,
/// reopening — goes to the plugin and account the campaign's own lines say it was seen at, while a route still names them;
/// a reply is routed by its channel, as a send is; and either is refused before a child exists where its route does not
/// name where the thread was seen. A thread no line of the campaign names is routed as it always was.
/// </summary>
public partial class ClaimerTests
{
    private static readonly string[] OnAThread =
        ["conversation.get", "conversation.classify", "conversation.close", "conversation.reopen", "conversation.reply", "message.send"];

    /// <summary>
    /// A campaign that sends email through one plugin and LinkedIn messages through another, with every thread operation
    /// routed to the first: a read or a filing of a thread seen at either goes to the plugin and account it was seen at —
    /// the inbound read's attempt says the first, the events source the second — and the attempt records the scope that won.
    /// </summary>
    [Theory]
    [InlineData("conversation.get", "th-mail", Mailer, RouteScope.CampaignOperation)]
    [InlineData("conversation.get", "th-social", Social, RouteScope.CampaignChannel)]
    [InlineData("conversation.classify", "th-social", Social, RouteScope.CampaignChannel)]
    [InlineData("conversation.close", "th-social", Social, RouteScope.CampaignChannel)]
    [InlineData("conversation.reopen", "th-social", Social, RouteScope.CampaignChannel)]
    [InlineData("conversation.close", "th-mail", Mailer, RouteScope.CampaignOperation)]
    public async Task A_read_or_a_filing_of_a_thread_goes_to_the_plugin_and_account_it_was_seen_at(
        string operation, string thread, string plugin, RouteScope scope)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        await SeenByInboundReadAsync(db, campaign, "th-mail", Mailer, "sha256:" + Mailer);
        await SeenByEventsAsync(db, campaign, "th-social", Social, "sha256:" + Social);
        var item = OnThread(db, campaign, operation, thread);
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        Assert.Equal((plugin, "sha256:" + plugin, scope), (work.Plan!.Plugin.Manifest.Id, work.Plan.BindingIdentity, work.Plan.Scope));
        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(row => row.WorkItemId == item.Id, Ct);
        Assert.Equal((plugin, "sha256:" + plugin, scope), (attempt.Provenance!.PluginId, attempt.Provenance.BindingIdentity, attempt.Provenance.RouteScope));
    }

    /// <summary>
    /// A thread seen at two accounts over time is read at the newer one, though the read's own route names the older.
    /// </summary>
    [Fact]
    public async Task A_thread_seen_at_two_accounts_over_time_is_read_at_the_newest()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        await SeenByInboundReadAsync(db, campaign, "th-both", Mailer, "sha256:" + Mailer);
        await SeenByEventsAsync(db, campaign, "th-both", Social, "sha256:" + Social);
        OnThread(db, campaign, "conversation.get", "th-both");
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal((Social, "sha256:" + Social), (work.Plan!.Plugin.Manifest.Id, work.Plan.BindingIdentity));
    }

    /// <summary>
    /// A thread whose plugin and account no route names any more is not followed anywhere: the work resolves by its own
    /// route, and the thread's check refuses it, permanently, before a child exists. The same is true of an account the
    /// route's plugin holds beside the one the thread was seen at.
    /// </summary>
    [Theory]
    [InlineData("conversation.get", Social, "sha256:retired-seat")]
    [InlineData("conversation.close", Social, "sha256:retired-seat")]
    [InlineData("conversation.get", Mailer, "sha256:another-mailbox")]
    public async Task A_thread_seen_where_no_route_names_any_more_is_refused_before_a_child_exists(string operation, string plugin, string identity)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        await SeenByEventsAsync(db, campaign, "th-gone", plugin, identity);
        var item = OnThread(db, campaign, operation, "th-gone");
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(row => row.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal((AttemptErrors.ThreadRouteMismatch, (FailureClass?)FailureClass.Permanent), (stored.LastError!.Code, stored.LastError.Class));
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(row => row.WorkItemId == item.Id, Ct);
        Assert.Null(attempt.StartedAt);
        Assert.Equal((Mailer, RouteScope.CampaignOperation), (attempt.Provenance!.PluginId, attempt.Provenance.RouteScope));
    }

    /// <summary>A thread no line of the campaign names is one the runtime knows nothing about: routed by the work's own route, as before.</summary>
    [Theory]
    [InlineData("conversation.get")]
    [InlineData("conversation.close")]
    public async Task A_thread_the_campaign_s_lines_do_not_name_is_routed_as_before(string operation)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var other = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.AddRange(campaign, other);
        await db.SaveChangesAsync(Ct);

        // Another campaign's line is not this one's record.
        await SeenByEventsAsync(db, other, "th-elsewhere", Social, "sha256:" + Social);
        var item = OnThread(db, campaign, operation, "th-elsewhere");
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal((item.PublicId, Mailer, RouteScope.CampaignOperation), (work.WorkItemPublicId, work.Plan!.Plugin.Manifest.Id, work.Plan.Scope));
    }

    /// <summary>
    /// A reply is routed by the channel it goes on: on LinkedIn to the plugin the channel route names, which holds the
    /// LinkedIn thread, and on email by the operation's own route, which holds the email thread. Either is parked for the
    /// person's approval under the route that won; so is a reply to a thread the campaign's lines do not name.
    /// </summary>
    [Theory]
    [InlineData("linkedin", "th-social", Social, RouteScope.CampaignChannel)]
    [InlineData("email", "th-mail", Mailer, RouteScope.CampaignOperation)]
    [InlineData("linkedin", "th-unknown", Social, RouteScope.CampaignChannel)]
    public async Task A_reply_goes_by_its_channel_to_the_route_that_holds_its_thread(string channel, string thread, string plugin, RouteScope scope)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        await SeenByInboundReadAsync(db, campaign, "th-mail", Mailer, "sha256:" + Mailer);
        await SeenByEventsAsync(db, campaign, "th-social", Social, "sha256:" + Social);
        var item = Replying(db, campaign, channel, thread);
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(row => row.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, stored.Status);
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(row => row.WorkItemId == item.Id, Ct);
        Assert.Equal((plugin, "sha256:" + plugin, scope), (approval.PluginId, approval.BindingIdentity, approval.RouteScope));
    }

    /// <summary>
    /// A reply whose channel's route does not name where its thread was seen never reaches a provider that does not hold
    /// the thread: it is refused before a child exists, and before anybody is asked to approve it.
    /// </summary>
    [Theory]
    [InlineData("email", "th-social")]
    [InlineData("linkedin", "th-mail")]
    public async Task A_reply_to_a_thread_seen_at_another_plugin_is_refused_and_nobody_is_asked(string channel, string thread)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        await SeenByInboundReadAsync(db, campaign, "th-mail", Mailer, "sha256:" + Mailer);
        await SeenByEventsAsync(db, campaign, "th-social", Social, "sha256:" + Social);
        var item = Replying(db, campaign, channel, thread);
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(row => row.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.ThreadRouteMismatch, stored.LastError!.Code);
        Assert.Empty(await fresh.Approvals.AsNoTracking().Where(row => row.WorkItemId == item.Id).ToListAsync(Ct));
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(row => row.WorkItemId == item.Id, Ct);
        Assert.Null(attempt.StartedAt);
    }

    /// <summary>
    /// The thread's check is asked before the block: a reply to a thread seen at the social plugin, routed by its channel
    /// to the mailer while a block of the mailer's account is open, is refused for the thread rather than left waiting on
    /// the block of an account that does not hold it.
    /// </summary>
    [Fact]
    public async Task A_reply_to_a_thread_seen_elsewhere_is_refused_rather_than_held_by_the_routed_account_s_block()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        await SeenByEventsAsync(db, campaign, "th-social", Social, "sha256:" + Social);
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        opener.Status = WorkItemStatus.Failed;
        opener.FinishedAt = Noon;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, Noon, Noon.AddHours(1), binding: "sha256:" + Mailer);
        block.PluginId = Mailer;
        db.ProviderBlocks.Add(block);
        var item = Replying(db, campaign, "email", "th-social");
        await db.SaveChangesAsync(Ct);

        using var harness = ThreadHarness(campaign);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(row => row.Id == item.Id, Ct);
        Assert.Equal((WorkItemStatus.Failed, AttemptErrors.ThreadRouteMismatch), (stored.Status, stored.LastError!.Code));
        Assert.False(await fresh.WorkItemWaits.AsNoTracking().AnyAsync(row => row.WorkItemId == item.Id, Ct));
        Assert.Empty(await fresh.Approvals.AsNoTracking().Where(row => row.WorkItemId == item.Id).ToListAsync(Ct));
    }

    /// <summary>
    /// The campaign's routes: every thread operation and email sends through the mailer, by their operations; LinkedIn
    /// messages and LinkedIn replies through the social plugin, by channel routes. No route reads the campaign's threads.
    /// </summary>
    private static RouteSet ThreadRoutes()
    {
        Route mailer = new(Mailer, new JsonObject { ["seat"] = Mailer }, "sha256:" + Mailer);
        Route social = new(Social, new JsonObject { ["seat"] = Social }, "sha256:" + Social);
        return new RouteSet(
            null,
            OnAThread.ToDictionary(operation => operation, _ => mailer, StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
            {
                ["conversation.reply"] = new Dictionary<string, Route>(StringComparer.Ordinal) { ["linkedin"] = social },
                ["message.send"] = new Dictionary<string, Route>(StringComparer.Ordinal) { ["linkedin"] = social },
            });
    }

    private static Harness ThreadHarness(Campaign campaign) =>
        new(
            plugin: TestPlugins.Loaded(Mailer, OnAThread),
            routes: snapshot => Routes(snapshot, campaign.PublicId, ThreadRoutes(), null),
            others: [TestPlugins.Loaded(Social, OnAThread)]);

    /// <summary>
    /// A line of the campaign saying the thread moved, as the inbound read writes it: under the read's own work item and
    /// the attempt whose provenance recorded the plugin and the account it ran under.
    /// </summary>
    private static async Task SeenByInboundReadAsync(JasonDbContext db, Campaign campaign, string thread, string plugin, string identity)
    {
        var read = WorkItemFactory.NewProviderOp(campaign, InboundSync.Operation, Noon);
        read.Status = WorkItemStatus.Succeeded;
        read.FinishedAt = Noon;
        db.WorkItems.Add(read);
        var attempt = WorkItemFactory.NewAttempt(read, 1, AttemptStatus.Succeeded, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            InboundSync.Operation, 1, null, null, RouteScope.CampaignOperation, identity);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        new JournalWriter(new FixedClock(Noon)).Append(
            db, Actors.Dispatcher, JournalKinds.InboundReplyObserved, campaign,
            key: InboundSync.ObservationKey(plugin, thread, "2026-09-14T11:00:00.000Z"),
            updated: Said(plugin, thread, "email"),
            workItem: read,
            attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// A line of the campaign saying the thread moved, as the events store writes it: naming the stored event, read from
    /// the source of the plugin and the account.
    /// </summary>
    private static async Task SeenByEventsAsync(JasonDbContext db, Campaign campaign, string thread, string plugin, string identity)
    {
        var source = await db.EventSources.SingleOrDefaultAsync(row => row.PluginId == plugin && row.BindingIdentity == identity, Ct);
        if (source is null)
        {
            source = new EventSource { PublicId = PublicId.New("evs"), PluginId = plugin, BindingIdentity = identity, CreatedAt = Noon };
            db.EventSources.Add(source);
            await db.SaveChangesAsync(Ct);
        }

        var stored = new ProviderEvent
        {
            PublicId = PublicId.New("evt"),
            SourceId = source.Id,
            PluginId = plugin,
            Kind = "thread_moved",
            OccurredAt = Noon,
            StoredAt = Noon,
            Key = "sha256:" + PublicId.New("key"),
            Data = new JsonObject { ["thread"] = thread },
        };
        db.Events.Add(stored);
        await db.SaveChangesAsync(Ct);

        var said = Said(plugin, thread, "linkedin");
        said["event_id"] = stored.PublicId;
        said["attributed_by"] = "contact_pin";
        new JournalWriter(new FixedClock(Noon)).Append(
            db, Actors.Dispatcher, JournalKinds.InboundReplyObserved, campaign,
            key: InboundSync.ObservationKey(plugin, thread, "2026-09-14T11:30:00.000Z"),
            updated: said);
        await db.SaveChangesAsync(Ct);
    }

    private static JsonObject Said(string plugin, string thread, string channel) => new()
    {
        ["plugin"] = plugin,
        ["thread"] = thread,
        ["channel"] = channel,
        ["last_activity_at"] = "2026-09-14T11:00:00.000Z",
        ["unread"] = true,
        ["has_meeting_intent"] = null,
        ["contact_id"] = null,
    };

    /// <summary>A read, or a filing, of the thread, waiting to be claimed.</summary>
    private static WorkItem OnThread(JasonDbContext db, Campaign campaign, string operation, string thread)
    {
        var args = new JsonObject { ["thread"] = new JsonObject { ["external_id"] = thread } };
        if (operation == "conversation.get")
        {
            args["limit"] = 20;
            args["offset"] = 0;
        }
        else if (operation == "conversation.classify")
        {
            args["category"] = null;
        }

        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon);
        item.Context = new JsonObject { [WorkItemService.InputKey] = args };
        db.WorkItems.Add(item);
        return item;
    }

    /// <summary>A reply to Ada in the thread on <paramref name="channel"/>, with nothing wrong with it but that a person must approve it.</summary>
    private static WorkItem Replying(JasonDbContext db, Campaign campaign, string channel, string thread)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(channel == "email"
            ? new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true }
            : new ContactChannel { Channel = "linkedin", Value = "https://www.linkedin.com/in/ada-example", IsPrimary = true });
        db.Contacts.Add(contact);

        var item = WorkItemFactory.NewProviderOp(campaign, "conversation.reply", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = channel,
                ["thread"] = new JsonObject { ["external_id"] = thread },
                ["body"] = "Thank you, Ada.",
            },
        };
        db.WorkItems.Add(item);
        return item;
    }
}
