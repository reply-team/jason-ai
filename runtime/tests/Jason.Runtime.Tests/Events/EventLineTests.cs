using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// What a stored event says in the campaigns it concerns. A thread the other side moved is said as the inbound read says
/// a reply — the same fields, under the same key, so a move both reads found is said once; a thread the account or the
/// provider moved is no reply and says nothing; the other kinds are said as a provider event, with their kind. What
/// happened before a campaign went live is history and is said by no line.
/// </summary>
public class EventLineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_move_by_the_other_side_or_by_nobody_known_says_inbound_reply_observed_under_inbound_sync_s_key()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var marta = await store.ContactAsync("c_1001");
        var at = Noon.AddHours(-3);

        await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", at: at), Moved(thread: "t_502", by: null, at: at)));

        var lines = await store.LinesAsync(campaign, JournalKinds.InboundReplyObserved);
        Assert.Equal(2, lines.Count);
        await using var db = store.Open();
        var events = await db.Events.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
        var moment = Stamp(at);
        Assert.Equal(InboundSync.ObservationKey(Reader, "t_501", moment), lines[0].Key);
        Assert.Equal(InboundSync.ObservationKey(Reader, "t_502", moment), lines[1].Key);
        Assert.Equal(ActorType.System, lines[0].ActorType);
        Assert.Equal("dispatcher", lines[0].ActorId);
        Assert.Null(lines[0].WorkItemId);
        Assert.Null(lines[0].AttemptId);
        Assert.Equal(marta.Id, lines[0].ContactId);
        Assert.Equal(
            new JsonObject
            {
                ["plugin"] = Reader,
                ["thread"] = "t_501",
                ["channel"] = "email",
                ["last_activity_at"] = moment,
                ["unread"] = true,
                ["has_meeting_intent"] = null,
                ["contact_id"] = marta.PublicId,
                ["event_id"] = events[0].PublicId,
            }.ToJsonString(),
            lines[0].New!.ToJsonString());
    }

    /// <summary>A reply the account sent into a thread, or a category set on one, moves it; neither is a reply, and nothing is said.</summary>
    [Fact]
    public async Task A_move_by_the_account_or_by_the_provider_is_kept_and_says_nothing()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        await store.CampaignAsync(pins: (Reader, "p_1"));

        var result = await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", by: "account"), Moved(thread: "t_502", by: "provider")));

        Assert.Equal(2, result.Stored);
        Assert.Equal(0, result.Said);
        Assert.Empty(await store.AllLinesAsync());
        await using var db = store.Open();
        Assert.Equal(2, await db.EventAttributions.CountAsync(Ct));
    }

    /// <summary>
    /// Two moves of one thread at one moment by two movers — the provider setting a category as the reply arrives — are two
    /// events, and the other side's is said, whichever the page lists first. Who moved a thread is part of what makes a
    /// move the one it is.
    /// </summary>
    [Fact]
    public async Task A_reply_at_the_moment_the_provider_moved_the_thread_is_its_own_event_and_is_said()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var at = Noon.AddHours(-3);

        var result = await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", by: "provider", at: at), Moved(thread: "t_501", by: "other_side", at: at)));

        Assert.Equal(new EventStoreResult(2, 0, 1, false), result);
        var line = Assert.Single(await store.LinesAsync(campaign, JournalKinds.InboundReplyObserved));
        await using var db = store.Open();
        var reply = await db.Events.AsNoTracking().OrderBy(e => e.Id).Skip(1).SingleAsync(Ct);
        Assert.Equal(reply.PublicId, (string?)line.New!["event_id"]);
    }

    /// <summary>The same across two reads: the account's own move stored by one, the other side's at the same moment by the next.</summary>
    [Fact]
    public async Task A_reply_at_the_moment_the_account_moved_the_thread_is_said_by_the_next_read()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var at = Noon.AddHours(-3);

        var first = await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", by: "account", at: at)));
        var second = await store.StoreAsync(source, "c-1", Page("c-2", Moved(thread: "t_501", by: "other_side", at: at)));

        Assert.Equal(new EventStoreResult(1, 0, 0, false), first);
        Assert.Equal(new EventStoreResult(1, 0, 1, false), second);
        Assert.Single(await store.LinesAsync(campaign, JournalKinds.InboundReplyObserved));
        await using var db = store.Open();
        Assert.Equal(2, await db.Events.CountAsync(Ct));
    }

    /// <summary>A move the inbound read already said in the campaign is the same move, said once whichever read found it first.</summary>
    [Fact]
    public async Task A_move_the_inbound_read_already_said_is_not_said_again()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var at = Noon.AddHours(-3);
        await using (var db = store.Open())
        {
            db.Journal.Add(new JournalEntry
            {
                PublicId = PublicId.New("jrn"),
                Ts = Noon.AddHours(-2),
                ActorType = ActorType.System,
                ActorId = "dispatcher",
                Kind = JournalKinds.InboundReplyObserved,
                CampaignId = campaign.Id,
                Key = InboundSync.ObservationKey(Reader, "t_501", Stamp(at)),
            });
            await db.SaveChangesAsync(Ct);
        }

        var result = await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", at: at)));

        Assert.Equal(1, result.Stored);
        Assert.Equal(0, result.Said);
        Assert.Single(await store.LinesAsync(campaign, JournalKinds.InboundReplyObserved));
    }

    [Fact]
    public async Task A_detected_reply_an_accepted_invitation_and_a_bounce_say_provider_event_observed_with_their_kind()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var marta = await store.ContactAsync("c_1001");

        await store.StoreAsync(source, null, Page("c-1", Detected(), Accepted(), Bounced(contact: "c_nobody")));

        var lines = await store.LinesAsync(campaign, JournalKinds.ProviderEventObserved);
        Assert.Equal(marta.Id, lines[0].ContactId);
        Assert.Null(lines[2].ContactId);
        Assert.Equal(["reply_detected", "invitation_accepted", "bounce"], lines.Select(line => (string?)line.New!["kind"]));
        await using var db = store.Open();
        var events = await db.Events.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(events.Select(e => e.Key), lines.Select(line => line.Key));
        Assert.Equal(
            new JsonObject
            {
                ["kind"] = "bounce",
                ["plugin"] = Reader,
                ["event_id"] = events[2].PublicId,
                ["occurred_at"] = Stamp(Noon.AddHours(-3)),
                ["channel"] = null,
                ["contact_id"] = null,
                ["message"] = "m_88",
                ["bounce"] = "hard",
            }.ToJsonString(),
            lines[2].New!.ToJsonString());
        Assert.Equal("li_9", (string?)lines[1].New!["account"]);
        Assert.Equal("t_501", (string?)lines[0].New!["thread"]);
    }

    /// <summary>
    /// What happened before a campaign went live is history: the event is kept and found to concern the campaign, and no
    /// line says it. A campaign that has never been live says nothing either.
    /// </summary>
    [Fact]
    public async Task An_event_before_the_campaign_went_live_is_kept_and_said_by_no_line()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var live = await store.CampaignAsync(eventsFrom: Noon, pins: (Reader, "p_1"));
        var draft = await store.CampaignAsync(status: CampaignStatus.Draft, live: false, pins: (Reader, "p_2"));

        var result = await store.StoreAsync(
            source,
            null,
            Page("c-1", Bounced(campaign: "p_1", at: Noon.AddMilliseconds(-1)), Bounced(campaign: "p_1", contact: "c_2", at: Noon), Bounced(campaign: "p_2")));

        Assert.Equal(3, result.Stored);
        Assert.Equal(1, result.Said);
        var said = Assert.Single(await store.LinesAsync(live, JournalKinds.ProviderEventObserved));
        Assert.Equal(Stamp(Noon), (string?)said.New!["occurred_at"]);
        Assert.Empty(await store.LinesAsync(draft, JournalKinds.ProviderEventObserved));
        await using var db = store.Open();
        Assert.Equal(3, await db.EventAttributions.CountAsync(Ct));
    }

    /// <summary>
    /// An event may concern several campaigns — a rule a later version adds — and then each of them is told once, in its
    /// own chronicle, under the same key: the store and the line index were built for one event and many campaigns.
    /// </summary>
    [Fact]
    public async Task Two_attributions_write_two_rows_and_two_lines()
    {
        var both = new Both();
        using var store = new EventStoreFixture(both);
        var source = await store.SourceAsync();
        var first = await store.CampaignAsync();
        var second = await store.CampaignAsync();
        both.Campaigns = [first.Id, second.Id];

        var result = await store.StoreAsync(source, null, Page("c-1", Bounced()));

        Assert.Equal(2, result.Said);
        var one = Assert.Single(await store.LinesAsync(first, JournalKinds.ProviderEventObserved));
        var two = Assert.Single(await store.LinesAsync(second, JournalKinds.ProviderEventObserved));
        Assert.Equal(one.Key, two.Key);
        await using var db = store.Open();
        Assert.Equal([first.Id, second.Id], await db.EventAttributions.OrderBy(a => a.CampaignId).Select(a => a.CampaignId).ToListAsync(Ct));
    }

    /// <summary>
    /// Two rules may find the same campaign for one event — its provider campaign pinned, and the person a member — and
    /// the campaign is told once: one attribution, by the first rule that found it, and one line. A campaign named twice
    /// is one campaign, never a page that cannot be stored.
    /// </summary>
    [Fact]
    public async Task A_campaign_two_rules_find_is_one_attribution_and_one_line()
    {
        var twice = new Twice();
        using var store = new EventStoreFixture(twice);
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync();
        twice.Campaign = campaign.Id;

        var result = await store.StoreAsync(source, null, Page("c-1", Bounced()));

        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.Said);
        Assert.Single(await store.LinesAsync(campaign, JournalKinds.ProviderEventObserved));
        await using var db = store.Open();
        var attribution = await db.EventAttributions.SingleAsync(Ct);
        Assert.Equal(EventAttributionRules.CampaignPin, attribution.Rule);
        Assert.Equal("c-1", (await db.EventSources.SingleAsync(Ct)).Cursor);
    }

    /// <summary>A line the store writes is a trigger: under the default triggers, a bounce wakes the campaign's manager.</summary>
    [Fact]
    public async Task A_provider_event_wakes_the_manager_under_the_default_triggers()
    {
        using var harness = new DispatchHarness(Noon);
        Campaign campaign;
        EventSource source;
        await using (var db = harness.Open())
        {
            campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
            campaign.EventsFrom = Noon.AddDays(-1);
            source = new EventSource { PublicId = PublicId.New("evs"), PluginId = Reader, BindingIdentity = EventSource.NoBinding, CreatedAt = Noon };
            db.Campaigns.Add(campaign);
            db.EventSources.Add(source);
            db.ExternalIds.Add(new ExternalId
            {
                Campaign = campaign,
                PluginId = Reader,
                Kind = "campaign",
                Value = "p_1",
                RecordedAt = Noon.AddDays(-1),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = harness.Open())
        {
            var store = new EventStore(new JournalWriter(harness.Resolve<TimeProvider>()), harness.Resolve<TimeProvider>(), new CampaignPinAttribution());
            Assert.Equal(1, (await store.StoreAsync(db, source, null, Page("c-1", Bounced()), "evr_x", Ct)).Said);
        }

        Assert.Equal(1, await harness.SummonAsync(Ct));
        await using var check = harness.Open();
        var checkIn = await check.WorkItems.SingleAsync(item => item.Role == ManagerCheckIn.Role, Ct);
        Assert.Contains(JournalKinds.ProviderEventObserved, checkIn.Context.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>An attribution of the test's: every event concerns one campaign, found by two rules.</summary>
    private sealed class Twice : IEventAttribution
    {
        public int Campaign { get; set; }

        public IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins) =>
            [new Attributed(Campaign, EventAttributionRules.CampaignPin), new Attributed(Campaign, "contact_pin")];
    }

    /// <summary>An attribution of the test's: every event concerns the campaigns it is told to.</summary>
    private sealed class Both : IEventAttribution
    {
        public IReadOnlyList<int> Campaigns { get; set; } = [];

        public IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins) =>
            [.. Campaigns.Select(id => new Attributed(id, EventAttributionRules.CampaignPin))];
    }
}
