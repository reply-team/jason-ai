using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A reply found by the contact's pin is marked <c>sole_sender</c> in the one campaign, of those not archived, that wrote
/// to the person through the plugin that saw the thread — a message, an invitation or an answer that succeeded there —
/// and in no other campaign. Who gets a line does not change; nobody is marked where no campaign wrote or two did.
/// </summary>
public class SoleSenderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SoleSender = "sole_sender";

    /// <summary>The two campaigns that hold the person, the person, and the account the page is read from.</summary>
    private static async Task<(EventSource Source, Campaign Sender, Campaign Other, Contact Marta)> WorldAsync(EventStoreFixture store)
    {
        var source = await store.SourceAsync();
        var sender = await store.CampaignAsync();
        var other = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, sender);
        await store.MemberAsync(marta, other);
        return (source, sender, other, marta);
    }

    private static async Task<JsonObject> LineOfAsync(EventStoreFixture store, Campaign campaign) =>
        (JsonObject)Assert.Single(await store.LinesAsync(campaign, JournalKinds.InboundReplyObserved)).New!;

    private static bool Marked(JsonObject line) => line.ContainsKey(SoleSender);

    [Theory]
    [InlineData("message.send")]
    [InlineData("social_invitation.send")]
    [InlineData("conversation.reply")]
    public async Task The_one_campaign_that_wrote_to_the_person_has_its_contact_pin_line_marked_and_no_other_has(string operation)
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender, operation);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        var marked = await LineOfAsync(store, sender);
        Assert.True(marked[SoleSender] is JsonValue value && value.GetValue<bool>(), $"The sender's line is {marked.ToJsonString()}.");
        Assert.Equal(EventAttributionRules.ContactPin, (string?)marked["attributed_by"]);
        var unmarked = await LineOfAsync(store, other);
        Assert.False(Marked(unmarked), $"The campaign that wrote nothing was marked: {unmarked.ToJsonString()}.");
    }

    [Fact]
    public async Task Two_campaigns_that_wrote_to_the_person_mark_nobody()
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender);
        await store.SentAsync(marta, other, "social_invitation.send");

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.False(Marked(await LineOfAsync(store, sender)));
        Assert.False(Marked(await LineOfAsync(store, other)));
    }

    [Fact]
    public async Task No_campaign_that_wrote_marks_nobody()
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, _) = await WorldAsync(store);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.False(Marked(await LineOfAsync(store, sender)));
        Assert.False(Marked(await LineOfAsync(store, other)));
    }

    /// <summary>An archived campaign's send is no writer: the one live campaign that wrote is marked.</summary>
    [Fact]
    public async Task An_archived_writer_does_not_count()
    {
        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        var archived = await store.CampaignAsync(status: CampaignStatus.Archived);
        await store.MemberAsync(marta, archived);
        await store.SentAsync(marta, archived);
        await store.SentAsync(marta, sender);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.True(Marked(await LineOfAsync(store, sender)), "The one live writer was not marked beside an archived one.");
        Assert.Empty(await store.LinesAsync(archived, JournalKinds.InboundReplyObserved));
    }

    /// <summary>A send that ended ambiguous may have gone and did not succeed: it writes nothing here, and nobody is marked.</summary>
    [Fact]
    public async Task A_send_that_ended_ambiguous_does_not_count()
    {
        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender, status: WorkItemStatus.Failed, attemptStatus: AttemptStatus.Failed, failure: FailureClass.Ambiguous);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.False(Marked(await LineOfAsync(store, sender)));
    }

    /// <summary>A send the provider took for later is a send that succeeded: it counts from when it was taken.</summary>
    [Fact]
    public async Task A_send_the_provider_accepted_for_later_counts()
    {
        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        var accepted = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["status"] = "accepted",
                ["accepted_for"] = Stamp(Noon.AddDays(1)),
                ["reference"] = "snd_77",
                ["cancelable"] = true,
            }),
        };
        await store.SentAsync(marta, sender, result: accepted);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.True(Marked(await LineOfAsync(store, sender)), "An accepted send did not count as written.");
    }

    /// <summary>An enrolment's reach is conditional: it writes to nobody by itself, and marks nobody.</summary>
    [Fact]
    public async Task A_conditional_act_does_not_count()
    {
        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender, "campaign.enroll");

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.False(Marked(await LineOfAsync(store, sender)));
    }

    /// <summary>A send through another plugin is not a write through the plugin that saw the thread.</summary>
    [Fact]
    public async Task A_send_through_another_plugin_does_not_count()
    {
        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender, plugin: "other-mail");

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.False(Marked(await LineOfAsync(store, sender)));
    }

    /// <summary>
    /// A writer that has since excluded the person gets no line and marks nobody, and still counts against a second writer:
    /// the campaign that holds the person and wrote too is not the only one that wrote.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_writer_that_excluded_the_person_marks_nobody_and_counts_against_another(bool otherWrote)
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var excluding = await store.CampaignAsync();
        var holding = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, excluding, MembershipState.Excluded);
        await store.MemberAsync(marta, holding);
        await store.SentAsync(marta, excluding);
        if (otherWrote)
        {
            await store.SentAsync(marta, holding);
        }

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));

        Assert.Empty(await store.LinesAsync(excluding, JournalKinds.InboundReplyObserved));
        Assert.False(Marked(await LineOfAsync(store, holding)));
    }

    /// <summary>A line found by the campaign's own pin is the campaign's already, and is never marked.</summary>
    [Fact]
    public async Task A_line_found_by_the_campaign_pin_is_never_marked()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, campaign);
        await store.SentAsync(marta, campaign);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: "p_1")));

        var line = await LineOfAsync(store, campaign);
        Assert.Equal(EventAttributionRules.CampaignPin, (string?)line["attributed_by"]);
        Assert.False(Marked(line));
    }

    /// <summary>Only a reply carries the mark: a provider event of another kind, found by the person, carries none.</summary>
    [Fact]
    public async Task A_provider_event_line_carries_no_mark()
    {
        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender);

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));

        var line = Assert.Single(await store.LinesAsync(sender, JournalKinds.ProviderEventObserved));
        Assert.False(line.New!.AsObject().ContainsKey(SoleSender), $"A bounce's line is {line.New.ToJsonString()}.");
    }

    /// <summary>The mark is per person: two people on one page, each with their own one writer, are each marked where they should be.</summary>
    [Fact]
    public async Task Each_person_on_a_page_is_marked_by_their_own_writer()
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        var ola = await store.ContactAsync("c_1002");
        await store.MemberAsync(ola, sender);
        await store.MemberAsync(ola, other);
        await store.SentAsync(marta, sender);
        await store.SentAsync(ola, other);

        await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", campaign: null), Moved(thread: "t_502", campaign: null, contact: "c_1002")));

        var lines = (await store.AllLinesAsync()).Select(line => (line.CampaignId, Thread: (string?)line.New!["thread"], Marked: line.New!.AsObject().ContainsKey(SoleSender))).ToList();
        Assert.Contains((sender.Id, "t_501", true), lines);
        Assert.Contains((other.Id, "t_501", false), lines);
        Assert.Contains((sender.Id, "t_502", false), lines);
        Assert.Contains((other.Id, "t_502", true), lines);
    }

    /// <summary>
    /// The read of who wrote is answered from the person's own items through the contact index, each one's attempts through
    /// the item's attempts, its campaign by its key: never a scan, never a campaign's history.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task The_read_of_who_wrote_searches_every_table_by_the_columns_that_bound_it(int people)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["w"] = "INDEX ix_work_items_contact_id (contact_id=?)",
            ["a"] = "INDEX ix_attempts_work_item_id_number (work_item_id=?)",
            ["c"] = "INTEGER PRIMARY KEY (rowid=?)",
        };

        var plan = await QueryPlans.PlanAsync(db, SoleSenders.Writers(db, Reader, [.. Enumerable.Range(7, people)]).ToQueryString(), Ct);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detail in plan.Where(detail => detail.StartsWith("SEARCH ", StringComparison.Ordinal)))
        {
            var alias = detail.Split(' ')[1];
            Assert.True(expected.TryGetValue(alias, out var bound), $"The plan searches {alias}, which it names no bound for: {string.Join(" | ", plan)}");
            Assert.True(
                detail.EndsWith(" USING " + bound, StringComparison.Ordinal) || detail.EndsWith(" USING COVERING " + bound, StringComparison.Ordinal),
                $"The plan does not search {alias} by {bound}: {detail} | {string.Join(" | ", plan)}");
            seen.Add(alias);
        }

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
    }
}
