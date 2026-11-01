using System.Data.Common;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// An event the campaign rule finds no campaign for reaches the person it names instead: every campaign that is not
/// archived and holds, as a local member, the one contact pinned with the event's contact identifier at the event's own
/// plugin — rule <c>contact_pin</c>. Never beside a campaign the campaign rule found, and never by a pin of another
/// plugin, a divergence, an identifier two contacts share, or a membership made after the event was stored.
/// </summary>
public class ContactPinRuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_event_naming_no_campaign_reaches_every_campaign_holding_the_person()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var first = await store.CampaignAsync();
        var second = await store.CampaignAsync();
        var stranger = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, first);
        await store.MemberAsync(marta, second);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var attributions = await AttributionsAsync(store);
        Assert.True(
            attributions.SequenceEqual([(first.Id, EventAttributionRules.ContactPin), (second.Id, EventAttributionRules.ContactPin)]),
            $"The person's campaigns {first.Id} and {second.Id} were not reached by the person alone: {Said(attributions)}.");
        Assert.Single(await store.LinesAsync(first, JournalKinds.ProviderEventObserved));
        Assert.Single(await store.LinesAsync(second, JournalKinds.ProviderEventObserved));
        Assert.Empty(await store.LinesAsync(stranger, JournalKinds.ProviderEventObserved));
    }

    /// <summary>A sequence's event is the campaign's that runs the sequence: the person's other campaigns hear nothing of it.</summary>
    [Fact]
    public async Task An_event_its_campaign_pin_reaches_tells_the_person_s_other_campaigns_nothing()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var pinned = await store.CampaignAsync(pins: (Reader, "p_1"));
        var other = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, pinned);
        await store.MemberAsync(marta, other);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1")));

        var attributions = await AttributionsAsync(store);
        Assert.True(
            attributions.SequenceEqual([(pinned.Id, EventAttributionRules.CampaignPin)]),
            $"Only campaign {pinned.Id} by its pin was expected: {Said(attributions)}.");
        Assert.Empty(await store.LinesAsync(other, JournalKinds.ProviderEventObserved));
    }

    /// <summary>A provider campaign no live campaign holds, or two hold, finds no campaign by its pin, and the person's own are reached.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    public async Task An_event_naming_a_campaign_nobody_or_two_hold_falls_to_the_person_s_campaigns(int live, int archived)
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var holders = new List<Campaign>();
        for (var index = 0; index < live; index++)
        {
            holders.Add(await store.CampaignAsync(pins: (Reader, "p_1")));
        }

        for (var index = 0; index < archived; index++)
        {
            await store.CampaignAsync(CampaignStatus.Archived, pins: (Reader, "p_1"));
        }

        var own = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, own);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1")));

        var attributions = await AttributionsAsync(store);
        Assert.True(
            attributions.SequenceEqual([(own.Id, EventAttributionRules.ContactPin)]),
            $"With {live} live and {archived} archived holders, only the person's campaign {own.Id} was expected: {Said(attributions)}.");
        foreach (var holder in holders)
        {
            Assert.Empty(await store.LinesAsync(holder, JournalKinds.ProviderEventObserved));
        }
    }

    [Fact]
    public async Task An_archived_campaign_holding_the_person_is_not_reached()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var archived = await store.CampaignAsync(CampaignStatus.Archived);
        var live = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, archived);
        await store.MemberAsync(marta, live);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var attributions = await AttributionsAsync(store);
        Assert.True(
            attributions.SequenceEqual([(live.Id, EventAttributionRules.ContactPin)]),
            $"The archived campaign {archived.Id} was reached, or the live one {live.Id} was not: {Said(attributions)}.");
    }

    /// <summary>A local member is any membership not excluded: a paused or finished one is still the person's campaign.</summary>
    [Fact]
    public async Task An_excluded_member_is_not_reached_and_a_paused_or_finished_one_is()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var excluded = await store.CampaignAsync();
        var paused = await store.CampaignAsync();
        var finished = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, excluded, MembershipState.Excluded);
        await store.MemberAsync(marta, paused, MembershipState.Paused);
        await store.MemberAsync(marta, finished, MembershipState.Finished);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var attributions = await AttributionsAsync(store);
        Assert.True(
            attributions.SequenceEqual([(paused.Id, EventAttributionRules.ContactPin), (finished.Id, EventAttributionRules.ContactPin)]),
            $"Expected the paused {paused.Id} and finished {finished.Id} memberships and not the excluded {excluded.Id}: {Said(attributions)}.");
    }

    /// <summary>An archived contact is over for every campaign; the event still names them, as it names anyone pinned with the identifier.</summary>
    [Fact]
    public async Task An_archived_contact_reaches_no_campaign_and_is_still_named()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, campaign);
        await using (var db = store.Open())
        {
            await db.Contacts.ExecuteUpdateAsync(set => set.SetProperty(contact => contact.ArchivedAt, Noon.AddHours(-1)), Ct);
        }

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var attributions = await AttributionsAsync(store);
        Assert.True(attributions.Count == 0, $"An archived contact reached a campaign: {Said(attributions)}.");
        await using var check = store.Open();
        var stored = await check.Events.SingleAsync(Ct);
        Assert.True(stored.ContactId == marta.Id, $"The event names contact {stored.ContactId}, not the archived {marta.Id}.");
    }

    [Fact]
    public async Task A_person_pinned_only_at_another_plugin_reaches_nobody()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001", plugin: "other-provider");
        await store.MemberAsync(marta, campaign);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var attributions = await AttributionsAsync(store);
        Assert.True(attributions.Count == 0, $"A pin at another plugin reached a campaign: {Said(attributions)}.");
    }

    /// <summary>A value recorded beside a contact's pin as a divergence is a disagreement, not a pin, and finds nobody.</summary>
    [Fact]
    public async Task A_value_kept_only_as_a_divergence_beside_a_contact_pin_reaches_nobody()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, campaign);
        await using (var db = store.Open())
        {
            await db.ExternalIds.Where(pin => pin.Kind == "contact").ExecuteUpdateAsync(set => set.SetProperty(pin => pin.DivergedValue, "c_5"), Ct);
        }

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null, contact: "c_5")));

        var attributions = await AttributionsAsync(store);
        Assert.True(attributions.Count == 0, $"A divergence reached a campaign: {Said(attributions)}.");
    }

    /// <summary>An identifier two contacts are pinned with names nobody, so it reaches neither one's campaigns: the event's person and its campaigns cannot disagree.</summary>
    [Fact]
    public async Task A_value_two_contacts_are_pinned_with_reaches_no_campaign()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var first = await store.CampaignAsync();
        var second = await store.CampaignAsync();
        await store.MemberAsync(await store.ContactAsync("c_1001"), first);
        await store.MemberAsync(await store.ContactAsync("c_1001"), second);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var attributions = await AttributionsAsync(store);
        Assert.True(attributions.Count == 0, $"An identifier two contacts share reached a campaign: {Said(attributions)}.");
        await using var db = store.Open();
        var named = (await db.Events.SingleAsync(Ct)).ContactId;
        Assert.True(named is null, $"The event names contact {named} by an identifier two contacts share.");
    }

    /// <summary>
    /// Which campaigns an event concerns is decided once, when it is stored: a person who joins a campaign afterwards is
    /// not told it, even by a span read again, which stores nothing it already holds.
    /// </summary>
    [Fact]
    public async Task A_member_added_after_the_event_was_stored_is_never_told()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        var page = Page("c-1", Bounced(campaign: null));
        await store.StoreAsync(source, null, page);

        await store.MemberAsync(marta, campaign, addedAt: Noon);
        var backfill = await store.BackfillAsync(source);
        var again = await store.StoreAsync(source, EventFence.OfBackfill(backfill.Id, null), page);

        Assert.True(again.Duplicates == 1 && again.Stored == 0, $"The span read again stored {again.Stored} and found {again.Duplicates} held.");
        var attributions = await AttributionsAsync(store);
        Assert.True(attributions.Count == 0, $"A member added after the event was stored was told it: {Said(attributions)}.");
        Assert.Empty(await store.LinesAsync(campaign, JournalKinds.ProviderEventObserved));
    }

    /// <summary>
    /// The person's campaigns are read through the index by contact: the members step searches the memberships by
    /// <c>contact_id</c> for each person the page names and each campaign and contact by its key, and walks no table. The plan SQLite makes for the very
    /// command the store sends, bound to the values it was sent with, on the migrated database.
    /// </summary>
    [Fact]
    public async Task The_person_s_campaigns_are_read_through_the_index_by_contact()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync();
        await store.MemberAsync(await store.ContactAsync("c_1001"), campaign);
        await store.MemberAsync(await store.ContactAsync("c_1002"), campaign);
        var recorder = new Recorder();

        await using (var db = store.Open(recorder))
        {
            var page = Page("c-1", Bounced(campaign: null, contact: "c_1001"), Bounced(campaign: null, contact: "c_1002"));
            await store.Store.StoreAsync(db, source, null, page, "evr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", Ct);
        }

        var members = Assert.Single(recorder.Commands, command => command.Text.Contains("FROM \"campaign_contacts\"", StringComparison.Ordinal));
        var plan = PlanOf(store.File, members);
        var shown = $"{Environment.NewLine}{string.Join(Environment.NewLine, plan)}{Environment.NewLine}{members.Text}";
        Assert.True(
            plan.Any(step => step.StartsWith("SEARCH ", StringComparison.Ordinal) && step.Contains("USING INDEX ix_campaign_contacts_contact_id (contact_id=?)", StringComparison.Ordinal)),
            $"The memberships are not searched by contact:{shown}");
        Assert.True(!plan.Any(step => step.StartsWith("SCAN ", StringComparison.Ordinal)), $"The members step walks a table:{shown}");
    }

    /// <summary>
    /// The runtime asks both rules, in their order, as the dispatcher module composes them: an event naming the provider
    /// campaign one campaign holds reaches that campaign alone, and an event naming none reaches every campaign the person
    /// is a member of. The store here is the one the module registers, not one a test built.
    /// </summary>
    [Fact]
    public async Task The_runtime_s_store_finds_campaigns_by_both_rules_in_their_order()
    {
        using var harness = new DispatchHarness(Noon);
        Campaign pinned;
        Campaign other;
        EventSource source;
        await using (var db = harness.Open())
        {
            pinned = WorkItemFactory.NewCampaign(now: Noon.AddDays(-2));
            other = WorkItemFactory.NewCampaign(now: Noon.AddDays(-2));
            pinned.EventsFrom = other.EventsFrom = Noon.AddDays(-1);
            var marta = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Marta", CreatedAt = Noon, UpdatedAt = Noon };
            source = new EventSource { PublicId = PublicId.New("evs"), PluginId = Reader, BindingIdentity = EventSource.NoBinding, CreatedAt = Noon };
            db.Campaigns.AddRange(pinned, other);
            db.Contacts.Add(marta);
            db.EventSources.Add(source);
            db.ExternalIds.Add(Pin("campaign", "p_1", campaign: pinned));
            db.ExternalIds.Add(Pin("contact", "c_1001", contact: marta));
            db.CampaignContacts.Add(new CampaignContact { Campaign = pinned, Contact = marta, AddedAt = Noon.AddDays(-1), UpdatedAt = Noon.AddDays(-1) });
            db.CampaignContacts.Add(new CampaignContact { Campaign = other, Contact = marta, AddedAt = Noon.AddDays(-1), UpdatedAt = Noon.AddDays(-1) });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = harness.Scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var page = Page("c-1", Bounced(campaign: "p_1", at: Noon.AddHours(-3)), Bounced(campaign: null, at: Noon.AddHours(-2)));
            await scope.ServiceProvider.GetRequiredService<EventStore>().StoreAsync(db, source, null, page, "evr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", Ct);
        }

        await using var check = harness.Open();
        var rows = await check.EventAttributions.AsNoTracking()
            .OrderBy(row => row.EventId)
            .ThenBy(row => row.CampaignId)
            .Select(row => new { row.CampaignId, row.Rule })
            .ToListAsync(Ct);
        List<(int Campaign, string Rule)> found = [.. rows.Select(row => (row.CampaignId, row.Rule))];
        List<(int Campaign, string Rule)> expected =
        [
            (pinned.Id, EventAttributionRules.CampaignPin),
            (pinned.Id, EventAttributionRules.ContactPin),
            (other.Id, EventAttributionRules.ContactPin),
        ];
        Assert.True(found.SequenceEqual(expected), $"The runtime's store found {Said(found)}; expected {Said(expected)}.");
    }

    private static ExternalId Pin(string kind, string value, Campaign? campaign = null, Contact? contact = null) => new()
    {
        Campaign = campaign,
        Contact = contact,
        PluginId = Reader,
        Kind = kind,
        Value = value,
        RecordedAt = Noon.AddDays(-2),
        RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
    };

    private static async Task<List<(int Campaign, string Rule)>> AttributionsAsync(EventStoreFixture store)
    {
        await using var db = store.Open();
        var rows = await db.EventAttributions.AsNoTracking().OrderBy(row => row.CampaignId).Select(row => new { row.CampaignId, row.Rule }).ToListAsync(Ct);
        return [.. rows.Select(row => (row.CampaignId, row.Rule))];
    }

    private static string Said(List<(int Campaign, string Rule)> attributions) =>
        attributions.Count == 0 ? "none" : string.Join(", ", attributions.Select(row => $"{row.Campaign} by {row.Rule}"));

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

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command) =>
            Commands.Add(new Command(command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value ?? DBNull.Value))]));
    }
}
