using System.Data.Common;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A message the account sent, as a provider's events report it: stored once like every event, found to concern a campaign
/// by the provider campaign it belongs to and by nothing else, and said there as <c>provider_send_observed</c>, a kind of
/// its own, under the event's own key. It is no reply and no provider event: the lines a reply, a bounce or a detected
/// reply say are not written for it, and the manager is woken by it only where a person listed the kind.
/// </summary>
public class ProviderSendLineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_send_its_provider_campaign_places_says_provider_send_observed_under_the_event_s_key()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var marta = await store.ContactAsync("c_1001");
        var at = Noon.AddHours(-3);

        var result = await store.StoreAsync(source, null, Page("c-1", Sent(at: at)));

        Assert.Equal(new EventStoreResult(1, 0, 1, false), result);
        var line = Assert.Single(await store.AllLinesAsync());
        await using var db = store.Open();
        var stored = await db.Events.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(JournalKinds.ProviderSendObserved, line.Kind);
        Assert.Equal(campaign.Id, line.CampaignId);
        Assert.Equal(stored.Key, line.Key);
        Assert.Equal(EventKeys.Store(Reader, ReadEvent.Read(Sent(at: at))!), line.Key);
        Assert.Equal(ActorType.System, line.ActorType);
        Assert.Equal("dispatcher", line.ActorId);
        Assert.Null(line.WorkItemId);
        Assert.Null(line.AttemptId);
        Assert.Equal(marta.Id, line.ContactId);
        Assert.Equal(
            new JsonObject
            {
                ["plugin"] = Reader,
                ["event_id"] = stored.PublicId,
                ["occurred_at"] = Stamp(at),
                ["channel"] = "email",
                ["contact_id"] = marta.PublicId,
                ["attributed_by"] = EventAttributionRules.CampaignPin,
                ["thread"] = "t_501",
                ["message"] = "m_89",
            }.ToJsonString(),
            line.New!.ToJsonString());
        var attribution = await db.EventAttributions.SingleAsync(Ct);
        Assert.Equal((campaign.Id, EventAttributionRules.CampaignPin), (attribution.CampaignId, attribution.Rule));
    }

    /// <summary>
    /// A page read again, and the same account read through two bindings, store the send once and say it once: the key is
    /// the plugin's, never the account's, and the campaign holds one line under it.
    /// </summary>
    [Fact]
    public async Task A_send_read_again_and_through_a_second_binding_is_stored_once_and_said_once()
    {
        using var store = new EventStoreFixture();
        var first = await store.SourceAsync(binding: "sha256:aaaa");
        var second = await store.SourceAsync(binding: "sha256:bbbb");
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));

        var once = await store.StoreAsync(first, null, Page("c-1", Sent()));
        var again = await store.StoreAsync(first, "c-1", Page("c-2", Sent()));
        var other = await store.StoreAsync(second, null, Page("d-1", Sent()));

        Assert.Equal(new EventStoreResult(1, 0, 1, false), once);
        Assert.Equal(new EventStoreResult(0, 1, 0, false), again);
        Assert.Equal(new EventStoreResult(0, 1, 0, false), other);
        Assert.Single(await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved));
        await using var db = store.Open();
        Assert.Equal(1, await db.Events.CountAsync(Ct));
    }

    /// <summary>
    /// The store looks a line up before it writes it, by campaign, kind and key: a send whose event was lost but whose line
    /// the campaign already holds is stored and not said twice, which the index on the kind also refuses.
    /// </summary>
    [Fact]
    public async Task A_send_whose_line_the_campaign_already_holds_is_not_said_again()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var key = EventKeys.Store(Reader, ReadEvent.Read(Sent())!);
        await using (var db = store.Open())
        {
            db.Journal.Add(new JournalEntry
            {
                PublicId = PublicId.New("jrn"),
                Ts = Noon.AddHours(-2),
                ActorType = ActorType.System,
                ActorId = "dispatcher",
                Kind = JournalKinds.ProviderSendObserved,
                CampaignId = campaign.Id,
                Key = key,
                New = new JsonObject
                {
                    ["plugin"] = Reader,
                    ["event_id"] = "evt_01JC0000000000000000000004",
                    ["occurred_at"] = Stamp(Noon.AddHours(-3)),
                    ["channel"] = "email",
                    ["contact_id"] = null,
                    ["attributed_by"] = EventAttributionRules.CampaignPin,
                    ["thread"] = "t_501",
                    ["message"] = "m_89",
                },
            });
            await db.SaveChangesAsync(Ct);
        }

        var result = await store.StoreAsync(source, null, Page("c-1", Sent()));

        Assert.Equal(new EventStoreResult(1, 0, 0, false), result);
        Assert.Single(await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved));
    }

    /// <summary>
    /// A send is found by its provider campaign alone. One that names none — a direct send, a person's own mail at the
    /// provider — and one whose provider campaign two campaigns hold reach nobody, though the person is a member of a
    /// campaign here: the person's rule is never asked for a send. Both are kept.
    /// </summary>
    [Fact]
    public async Task A_send_no_sole_campaign_holds_reaches_nobody_even_where_the_person_is_a_member()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var member = await store.CampaignAsync(pins: (Reader, "p_2"));
        var rival = await store.CampaignAsync(pins: (Reader, "p_2"));
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, member);

        var result = await store.StoreAsync(source, null, Page("c-1", Sent(campaign: null), Sent(campaign: "p_2", message: "m_90")));

        Assert.Equal(new EventStoreResult(2, 0, 0, false), result);
        Assert.Empty(await store.AllLinesAsync());
        await using var db = store.Open();
        Assert.Equal(0, await db.EventAttributions.CountAsync(Ct));
        Assert.Equal([marta.Id, marta.Id], await db.Events.OrderBy(e => e.Id).Select(e => e.ContactId).ToListAsync(Ct));
        Assert.Empty(await store.LinesAsync(rival, JournalKinds.ProviderSendObserved));
    }

    /// <summary>
    /// Where a send is found is decided once, by the sequence of rules, and not by each rule: the rules of this version find
    /// the person's campaign for a bounce and nobody for a send, and a rule added after them, which would find a campaign
    /// for anything, is not asked about a send either.
    /// </summary>
    [Fact]
    public void The_sequence_asks_the_campaign_rule_alone_about_a_send_whatever_rules_follow_it()
    {
        var pins = new PinFacts([], [new ContactPin(7, "cnt_01JC0000000000000000000003", "c_1001")], [new ContactMembership(7, 3, Noon)]);
        var send = ReadEvent.Read(Sent(campaign: null))!;

        Assert.Equal([3], EventAttributionSequence.OfThisVersion().Attribute(ReadEvent.Read(Bounced(campaign: null))!, pins).Select(found => found.CampaignId));
        Assert.Empty(EventAttributionSequence.OfThisVersion().Attribute(send, pins));

        var widened = new EventAttributionSequence(new CampaignPinAttribution(), new ContactPinAttribution(), new Anything(5));
        Assert.Equal([5], widened.Attribute(ReadEvent.Read(Bounced(campaign: null, contact: "c_nobody"))!, pins).Select(found => found.CampaignId));
        Assert.Empty(widened.Attribute(send, pins));
    }

    /// <summary>
    /// The realistic case the rule is there for: a sequence at the provider that no campaign here runs sends to a person who
    /// is a member of one of ours. The send is kept, concerns nobody, and that campaign hears nothing of it.
    /// </summary>
    [Fact]
    public async Task A_send_of_a_provider_campaign_none_of_ours_holds_tells_the_person_s_campaign_nothing()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var mine = await store.CampaignAsync(pins: (Reader, "p_1"));
        await store.MemberAsync(await store.ContactAsync("c_1001"), mine);

        var result = await store.StoreAsync(source, null, Page("c-1", Sent(campaign: "p_9")));

        Assert.Equal(new EventStoreResult(1, 0, 0, false), result);
        Assert.Empty(await store.AllLinesAsync());
        await using var db = store.Open();
        Assert.Equal(0, await db.EventAttributions.CountAsync(Ct));
    }

    /// <summary>Two messages on one thread, each with its own identifier, are two sends: two events, two lines.</summary>
    [Fact]
    public async Task Two_sends_on_one_thread_with_their_own_message_identifiers_are_two_lines()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var at = Noon.AddHours(-3);

        var result = await store.StoreAsync(source, null, Page("c-1", Sent(thread: "t_501", message: "m_1", at: at), Sent(thread: "t_501", message: "m_2", at: at)));

        Assert.Equal(new EventStoreResult(2, 0, 2, false), result);
        var lines = await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved);
        Assert.Equal(["m_1", "m_2"], lines.Select(line => (string?)line.New!["message"]));
        Assert.NotEqual(lines[0].Key, lines[1].Key);
    }

    /// <summary>
    /// A span asked for again reads a send the account's own reading already stored: the span counts it as a duplicate and
    /// says nothing again, and the account's cursor stays where it was.
    /// </summary>
    [Fact]
    public async Task A_span_read_again_holding_a_send_already_stored_counts_a_duplicate_and_says_nothing()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        await store.StoreAsync(source, null, Page("c-1", Sent()));
        var backfill = await store.BackfillAsync(source);

        var result = await store.StoreAsync(source, EventFence.OfBackfill(backfill.Id, null), Page("b-1", Sent()));

        Assert.Equal(new EventStoreResult(0, 1, 0, false), result);
        Assert.Single(await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved));
        await using var db = store.Open();
        Assert.Equal(1, await db.Events.CountAsync(Ct));
        var span = await db.EventBackfills.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((0, 1, "b-1"), (span.Stored, span.Duplicates, span.Cursor));
        Assert.Equal("c-1", (await db.EventSources.AsNoTracking().SingleAsync(Ct)).Cursor);
    }

    /// <summary>A rule of the test's: every event concerns the one campaign it is told, by the campaign's pin.</summary>
    private sealed class Anything(int campaign) : IEventAttribution
    {
        public IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins) => [new Attributed(campaign, EventAttributionRules.CampaignPin)];
    }

    /// <summary>A send before the campaign went live is history: kept, found to concern the campaign, and said by no line.</summary>
    [Fact]
    public async Task A_send_before_the_campaign_went_live_is_kept_and_said_by_no_line()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(eventsFrom: Noon, pins: (Reader, "p_1"));

        var result = await store.StoreAsync(
            source,
            null,
            Page("c-1", Sent(message: "m_1", at: Noon.AddMilliseconds(-1)), Sent(message: "m_2", at: Noon)));

        Assert.Equal(new EventStoreResult(2, 0, 1, false), result);
        var said = Assert.Single(await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved));
        Assert.Equal("m_2", (string?)said.New!["message"]);
        await using var db = store.Open();
        Assert.Equal(2, await db.EventAttributions.CountAsync(Ct));
    }

    /// <summary>
    /// A provider that names no identifier for the message, and no thread, still gives the moment: the line carries both as
    /// null and the moment as given, and two such sends at two moments are two sends.
    /// </summary>
    [Fact]
    public async Task A_send_with_no_message_identifier_is_said_with_null_and_the_moment_given()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var first = Noon.AddHours(-3);
        var second = Noon.AddHours(-2);

        await store.StoreAsync(source, null, Page("c-1", Sent(thread: null, message: null, at: first), Sent(thread: null, message: null, at: second)));

        var lines = await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved);
        Assert.Equal([Stamp(first), Stamp(second)], lines.Select(line => (string?)line.New!["occurred_at"]));
        Assert.All(lines, line =>
        {
            var body = (JsonObject)line.New!;
            Assert.True(body.ContainsKey("message") && body.ContainsKey("thread"), body.ToJsonString());
            Assert.Null(body["message"]);
            Assert.Null(body["thread"]);
        });
        Assert.NotEqual(lines[0].Key, lines[1].Key);
    }

    /// <summary>
    /// The person a send names is whoever is pinned with the provider's identifier, member or not: pinned to nobody, the line
    /// says <c>contact_id</c> null; pinned to a person who is in no campaign here, it says that person. The campaign is told
    /// either way, by its provider campaign.
    /// </summary>
    [Fact]
    public async Task A_send_to_a_person_pinned_to_nobody_or_to_a_non_member_is_said_with_who_the_pin_names()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var outsider = await store.ContactAsync("c_2002");

        await store.StoreAsync(source, null, Page("c-1", Sent(contact: "c_nobody", message: "m_1"), Sent(contact: "c_2002", message: "m_2")));

        var lines = await store.LinesAsync(campaign, JournalKinds.ProviderSendObserved);
        Assert.Equal([null, outsider.Id], lines.Select(line => line.ContactId));
        Assert.Equal([null, outsider.PublicId], lines.Select(line => (string?)line.New!["contact_id"]));
        await using var db = store.Open();
        Assert.Equal(0, await db.CampaignContacts.CountAsync(member => member.CampaignId == campaign.Id, Ct));
    }

    /// <summary>
    /// A send is the course the campaign planned: under the default triggers its line wakes no manager, and neither the bounce
    /// step nor any reader of provider events sees a line of theirs for it.
    /// </summary>
    [Fact]
    public async Task A_send_wakes_no_manager_under_the_default_triggers_and_says_no_provider_event()
    {
        using var harness = new DispatchHarness(Noon);
        var (campaign, source) = await SeedAsync(harness);

        await StoreThroughAsync(harness, source, Page("c-1", Sent()));

        Assert.Equal(0, await harness.SummonAsync(Ct));
        Assert.Equal(0, await harness.BounceAsync(Ct));
        await using var check = harness.Open();
        var kinds = await check.Journal.Where(entry => entry.CampaignId == campaign.Id).Select(entry => entry.Kind).ToListAsync(Ct);
        Assert.Contains(JournalKinds.ProviderSendObserved, kinds);
        Assert.DoesNotContain(JournalKinds.ProviderEventObserved, kinds);
        Assert.DoesNotContain(JournalKinds.InboundReplyObserved, kinds);
        Assert.False(await check.WorkItems.AnyAsync(item => item.Role == ManagerCheckIn.Role, Ct));
    }

    /// <summary>An installation that lists the kind beside the defaults is woken by a send, once.</summary>
    [Fact]
    public async Task A_send_wakes_the_manager_once_a_person_lists_the_kind()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { Triggers = [.. ManagerOptions.Default, JournalKinds.ProviderSendObserved] });
        var (_, source) = await SeedAsync(harness);

        await StoreThroughAsync(harness, source, Page("c-1", Sent()));

        Assert.Equal(1, await harness.SummonAsync(Ct));
        await using var check = harness.Open();
        var checkIn = await check.WorkItems.SingleAsync(item => item.Role == ManagerCheckIn.Role, Ct);
        Assert.Contains(JournalKinds.ProviderSendObserved, checkIn.Context.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Before it writes, the store asks which of the page's lines its campaigns already said. It asks each of the three
    /// kinds through that kind's own filtered unique index, never by walking the campaigns' whole chronicles: a campaign
    /// that has said many sends pays for the keys the page holds, not for its history.
    /// </summary>
    [Fact]
    public async Task The_lines_already_said_are_looked_up_through_each_kind_s_own_index()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        await store.CampaignAsync(pins: (Reader, "p_1"));
        var recorder = new Recorder();

        await using (var db = store.Open(recorder))
        {
            await store.Store.StoreAsync(db, source, null, Page("c-1", Sent(), Bounced(), Moved()), "evr_x", Ct);
        }

        var lookup = Assert.Single(recorder.Commands, command => command.Text.Contains("UNION ALL", StringComparison.Ordinal) && command.Text.Contains("FROM \"journal\"", StringComparison.Ordinal));
        var plan = PlanOf(store.File, lookup);
        Assert.Contains(plan, step => step.Contains("USING COVERING INDEX ix_journal_campaign_kind_key ", StringComparison.Ordinal));
        Assert.Contains(plan, step => step.Contains("USING COVERING INDEX ix_journal_campaign_kind_key_events ", StringComparison.Ordinal));
        Assert.Contains(plan, step => step.Contains("USING COVERING INDEX ix_journal_campaign_kind_key_provider_sends ", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, step => step.Contains("ix_journal_campaign_id_id", StringComparison.Ordinal) || step.StartsWith("SCAN", StringComparison.Ordinal));
    }

    /// <summary>What SQLite plans for a recorded command, bound to the values it was sent with; one step a line.</summary>
    private static List<string> PlanOf(string file, Command command)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(file));
        connection.Open();
        using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + command.Text;
        foreach (var (name, value) in command.Parameters)
        {
            explain.Parameters.AddWithValue(name, value);
        }

        using var reader = explain.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private sealed record Command(string Text, IReadOnlyList<(string Name, object Value)> Parameters);

    /// <summary>Every query a context sends, with its parameters, as it is sent.</summary>
    private sealed class Recorder : DbCommandInterceptor
    {
        public List<Command> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new Command(command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value ?? DBNull.Value))]));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>A live campaign holding the provider campaign <c>p_1</c> at the reader, and the account it is read from.</summary>
    private static async Task<(Campaign Campaign, EventSource Source)> SeedAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
        campaign.EventsFrom = Noon.AddDays(-1);
        var source = new EventSource { PublicId = PublicId.New("evs"), PluginId = Reader, BindingIdentity = EventSource.NoBinding, CreatedAt = Noon };
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
        return (campaign, source);
    }

    /// <summary>Stores the page through the runtime's own store over the harness's database, and holds it to one line said.</summary>
    private static async Task StoreThroughAsync(DispatchHarness harness, EventSource source, EventPage page)
    {
        await using var db = harness.Open();
        var clock = harness.Resolve<TimeProvider>();
        var store = new EventStore(new JournalWriter(clock), clock, EventAttributionSequence.OfThisVersion());
        Assert.Equal(1, (await store.StoreAsync(db, source, null, page, "evr_x", Ct)).Said);
    }
}
