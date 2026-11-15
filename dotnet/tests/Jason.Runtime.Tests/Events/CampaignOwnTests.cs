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
/// A reply found by the contact's pin is marked <c>campaign_own</c> by the first of two clauses that holds: in a campaign
/// that already holds the thread as its own, by its own read, its own pin or a mark; and, only on a thread none holds yet,
/// in the one campaign, of those not archived, that wrote to the person through the plugin that saw the thread — a message,
/// an invitation or an answer that succeeded there. Who gets a line does not change; nobody is marked where no campaign
/// holds the thread and no campaign wrote, or two did.
/// </summary>
public class CampaignOwnTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string CampaignOwn = "campaign_own";

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

    private static bool Marked(JsonObject line) => line.ContainsKey(CampaignOwn);

    /// <summary>The member's name before it said what it means, which no document, fixture, page or line may carry.</summary>
    private const string Retired = "sole_sender";

    /// <summary>
    /// The mark has one name, <c>campaign_own</c>: the journal document describes it, its fixtures and the pages that say it
    /// use it, and the line the store writes carries it; the name it had while it meant only the sole writer is nowhere.
    /// </summary>
    [Fact]
    public async Task The_mark_is_named_campaign_own_in_the_document_its_fixtures_the_pages_and_the_line()
    {
        var root = RepositoryRoot();
        var document = File.ReadAllText(Path.Combine(root, "docs", "contracts", "journal", "inbound_reply_observed.json"));
        Assert.NotNull(JsonNode.Parse(document)!["body"]!["properties"]!["new"]!["properties"]![CampaignOwn]);
        var said = new List<string> { "docs/contracts/journal/inbound_reply_observed.json" };
        said.AddRange(Directory.EnumerateFiles(Path.Combine(root, "docs", "contracts", "fixtures", "journal", "inbound_reply_observed"), "*.json")
            .Select(path => Path.GetRelativePath(root, path)));
        said.AddRange(["docs/events.md", "docs/inbound-sync.md", "docs/work-execution.md", "docs/architecture.md", "skills/runtime/roles/manager/SKILL.md"]);
        foreach (var path in said)
        {
            Assert.DoesNotContain(Retired, File.ReadAllText(Path.Combine(root, path)), StringComparison.Ordinal);
        }

        Assert.Contains(
            Directory.EnumerateFiles(Path.Combine(root, "docs", "contracts", "fixtures", "journal", "inbound_reply_observed"), "*.json"),
            path => File.ReadAllText(path).Contains($"\"{CampaignOwn}\": true", StringComparison.Ordinal));

        using var store = new EventStoreFixture();
        var (source, sender, _, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender);
        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null)));
        await using var db = store.Open();
        var written = await db.Database
            .SqlQuery<string>($"SELECT new_json AS \"Value\" FROM journal WHERE kind = 'inbound_reply_observed'")
            .ToListAsync(Ct);
        Assert.Equal(2, written.Count);
        Assert.All(written, text => Assert.DoesNotContain(Retired, text, StringComparison.Ordinal));
        Assert.Contains(written, text => text.Contains($"\"{CampaignOwn}\":true", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

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
        Assert.True(marked[CampaignOwn] is JsonValue value && value.GetValue<bool>(), $"The sender's line is {marked.ToJsonString()}.");
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
        Assert.False(line.New!.AsObject().ContainsKey(CampaignOwn), $"A bounce's line is {line.New.ToJsonString()}.");
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

        var lines = (await store.AllLinesAsync()).Select(line => (line.CampaignId, Thread: (string?)line.New!["thread"], Marked: line.New!.AsObject().ContainsKey(CampaignOwn))).ToList();
        Assert.Contains((sender.Id, "t_501", true), lines);
        Assert.Contains((other.Id, "t_501", false), lines);
        Assert.Contains((sender.Id, "t_502", false), lines);
        Assert.Contains((other.Id, "t_502", true), lines);
    }

    public enum Held
    {
        ByItsOwnRead,
        ByItsOwnPin,
        Marked,
    }

    /// <summary>
    /// A campaign that holds the thread as its own already — by its own read, by its own pin, or by a line marked before —
    /// has the person's next reply on it marked, whoever has written to the person since: another campaign that alone wrote
    /// to them, or one that wrote beside it. The other campaign's line is not marked.
    /// </summary>
    [Theory]
    [InlineData(Held.ByItsOwnRead)]
    [InlineData(Held.ByItsOwnPin)]
    [InlineData(Held.Marked)]
    public async Task A_thread_already_the_campaigns_own_stays_its_own_whoever_wrote_since(Held held)
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var owner = held == Held.ByItsOwnPin ? await store.CampaignAsync(pins: (Reader, "p_1")) : await store.CampaignAsync();
        var other = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, owner);
        await store.MemberAsync(marta, other);
        string? cursor = null;
        switch (held)
        {
            case Held.ByItsOwnRead:
                await store.ObservedByTheCampaignAsync(owner, "t_501", Noon.AddHours(-4));
                break;
            case Held.ByItsOwnPin:
                await store.StoreAsync(source, null, Page("c-0", Moved(campaign: "p_1", at: Noon.AddHours(-4))));
                cursor = "c-0";
                break;
            case Held.Marked:
                await store.SentAsync(marta, owner);
                await store.StoreAsync(source, null, Page("c-0", Moved(campaign: null, at: Noon.AddHours(-4))));
                cursor = "c-0";
                Assert.True(Marked(await LineOfAsync(store, owner)), "The first reply of the one writer was not marked.");
                break;
        }

        await store.SentAsync(marta, other);
        await store.StoreAsync(source, cursor, Page("c-1", Moved(campaign: null, at: Noon.AddHours(-2))));

        var mine = (await store.LinesAsync(owner, JournalKinds.InboundReplyObserved)).Select(line => (JsonObject)line.New!).Last();
        Assert.Equal(EventAttributionRules.ContactPin, (string?)mine["attributed_by"]);
        Assert.True(mine[CampaignOwn] is JsonValue value && value.GetValue<bool>(), $"The owner's later line is {mine.ToJsonString()}.");
        var theirs = (await store.LinesAsync(other, JournalKinds.InboundReplyObserved)).Select(line => (JsonObject)line.New!).Last();
        Assert.False(Marked(theirs), $"Another campaign was marked on a thread already the owner's: {theirs.ToJsonString()}.");
    }

    /// <summary>
    /// The first clause decides alone: where a campaign the event was found in holds the thread already, the one campaign
    /// that alone had written to the person is not asked, though it would have been marked on a thread nobody held.
    /// </summary>
    [Fact]
    public async Task An_owned_thread_is_marked_in_its_owner_even_where_another_campaign_alone_wrote()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var owner = await store.CampaignAsync(pins: (Reader, "p_1"));
        var writer = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, owner);
        await store.MemberAsync(marta, writer);
        await store.SentAsync(marta, writer);
        await store.StoreAsync(source, null, Page("c-0", Moved(campaign: "p_1", at: Noon.AddHours(-4))));

        await using (var db = store.Open())
        {
            Assert.Equal(writer.Id, (await SoleSenders.OfAsync(db, Reader, [marta.Id], Ct)).SoleBefore(marta.Id, Noon.AddHours(-2)));
        }

        await store.StoreAsync(source, "c-0", Page("c-1", Moved(thread: "t_501", campaign: null, at: Noon.AddHours(-2)), Moved(thread: "t_777", campaign: null, at: Noon.AddHours(-1))));

        var lines = (await store.AllLinesAsync())
            .Where(line => (string?)line.New!["attributed_by"] == EventAttributionRules.ContactPin)
            .Select(line => (line.CampaignId, Thread: (string?)line.New!["thread"], Marked: line.New!.AsObject().ContainsKey(CampaignOwn)))
            .ToList();
        Assert.Contains((owner.Id, "t_501", true), lines);
        Assert.Contains((writer.Id, "t_501", false), lines);

        // A thread nobody holds yet still goes to the one campaign that wrote.
        Assert.Contains((owner.Id, "t_777", false), lines);
        Assert.Contains((writer.Id, "t_777", true), lines);
    }

    /// <summary>
    /// A line the same page says first counts as one already stored: the campaign's pin finds the thread, and the person's
    /// reply on it later in the page is marked in that campaign, not in the one that alone wrote.
    /// </summary>
    [Fact]
    public async Task A_line_said_earlier_in_the_page_makes_the_thread_the_campaigns_own_for_the_rest_of_it()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var owner = await store.CampaignAsync(pins: (Reader, "p_1"));
        var writer = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, owner);
        await store.MemberAsync(marta, writer);
        await store.SentAsync(marta, writer);

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: "p_1", at: Noon.AddHours(-4)), Moved(campaign: null, at: Noon.AddHours(-2))));

        var mine = await store.LinesAsync(owner, JournalKinds.InboundReplyObserved);
        Assert.Equal(2, mine.Count);
        Assert.Equal(EventAttributionRules.CampaignPin, (string?)mine[0].New!["attributed_by"]);
        Assert.False(mine[0].New!.AsObject().ContainsKey(CampaignOwn));
        Assert.True(mine[1].New!.AsObject().ContainsKey(CampaignOwn), $"The owner's second line is {mine[1].New!.ToJsonString()}.");
        Assert.False(Marked(await LineOfAsync(store, writer)));
    }

    /// <summary>
    /// What stays open: once two campaigns have written to the person, a reply on a thread neither holds as its own is
    /// marked nowhere, though one of them held another thread of the person's.
    /// </summary>
    [Fact]
    public async Task Two_writers_and_a_thread_neither_holds_mark_nobody()
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender);
        await store.StoreAsync(source, null, Page("c-0", Moved(thread: "t_501", campaign: null, at: Noon.AddHours(-4))));
        await store.SentAsync(marta, other);

        await store.StoreAsync(source, "c-0", Page("c-1", Moved(thread: "t_777", campaign: null, at: Noon.AddHours(-2))));

        var lines = (await store.AllLinesAsync())
            .Where(line => (string?)line.New!["thread"] == "t_777")
            .ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.False(line.New!.AsObject().ContainsKey(CampaignOwn), $"A line was marked: {line.New.ToJsonString()}."));
    }

    public enum Named
    {
        ACampaignNobodyHolds,
        ACampaignOnlyAnArchivedOneHolds,
        ACampaignTwoHold,
    }

    /// <summary>
    /// A reply that names a provider campaign reached the person's campaigns only because no live campaign, or more than
    /// one, holds that campaign: it is a sequence's thread — run by hand at the provider, an archived campaign's, or one two
    /// campaigns pin — and never a direct send's. The one campaign that alone wrote to the person is not marked on it;
    /// beside it, on the same page, the person's reply that names no campaign is.
    /// </summary>
    [Theory]
    [InlineData(Named.ACampaignNobodyHolds)]
    [InlineData(Named.ACampaignOnlyAnArchivedOneHolds)]
    [InlineData(Named.ACampaignTwoHold)]
    public async Task A_reply_that_names_a_provider_campaign_is_marked_nowhere_though_one_campaign_alone_wrote(Named named)
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        switch (named)
        {
            case Named.ACampaignOnlyAnArchivedOneHolds:
                await store.CampaignAsync(status: CampaignStatus.Archived, pins: (Reader, "p_9"));
                break;
            case Named.ACampaignTwoHold:
                await store.CampaignAsync(pins: (Reader, "p_9"));
                await store.CampaignAsync(pins: (Reader, "p_9"));
                break;
        }

        await store.SentAsync(marta, sender);

        await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", campaign: "p_9"), Moved(thread: "t_777", campaign: null)));

        var lines = (await store.AllLinesAsync())
            .Where(line => line.Kind == JournalKinds.InboundReplyObserved)
            .Select(line => (line.CampaignId, Thread: (string?)line.New!["thread"], Rule: (string?)line.New!["attributed_by"], Marked: line.New!.AsObject().ContainsKey(CampaignOwn)))
            .ToList();
        Assert.Contains((sender.Id, "t_501", EventAttributionRules.ContactPin, false), lines);
        Assert.Contains((other.Id, "t_501", EventAttributionRules.ContactPin, false), lines);
        Assert.Contains((sender.Id, "t_777", EventAttributionRules.ContactPin, true), lines);
        Assert.Contains((other.Id, "t_777", EventAttributionRules.ContactPin, false), lines);
        Assert.Equal(4, lines.Count);
    }

    /// <summary>
    /// The first clause is asked whatever the event names: a thread the campaign holds by a mark stays its own when the
    /// person's next reply on it names a provider campaign nobody holds.
    /// </summary>
    [Fact]
    public async Task A_thread_already_the_campaigns_own_stays_its_own_when_the_next_reply_names_a_provider_campaign()
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender);
        await store.StoreAsync(source, null, Page("c-0", Moved(campaign: null, at: Noon.AddHours(-4))));

        await store.StoreAsync(source, "c-0", Page("c-1", Moved(campaign: "p_9", at: Noon.AddHours(-2))));

        var mine = await store.LinesAsync(sender, JournalKinds.InboundReplyObserved);
        Assert.Equal(2, mine.Count);
        Assert.All(mine, line => Assert.True(line.New!.AsObject().ContainsKey(CampaignOwn), $"A line of the owner's was not marked: {line.New.ToJsonString()}."));
        Assert.All(await store.LinesAsync(other, JournalKinds.InboundReplyObserved), line => Assert.False(Marked((JsonObject)line.New!)));
    }

    /// <summary>
    /// A writer counts only where it wrote before the reply. A second campaign that writes to the person after the reply and
    /// before the page is read takes nothing from the campaign that alone had written when the person replied; the person's
    /// later reply on the same page, after both had written, is marked nowhere.
    /// </summary>
    [Fact]
    public async Task A_second_writer_after_the_reply_and_before_the_read_takes_no_mark()
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        await store.SentAsync(marta, sender);
        await store.SentAsync(marta, other, at: Noon.AddHours(-2));

        await store.StoreAsync(source, null, Page("c-1", Moved(thread: "t_501", campaign: null, at: Noon.AddHours(-3)), Moved(thread: "t_777", campaign: null, at: Noon.AddHours(-1))));

        var lines = (await store.AllLinesAsync())
            .Select(line => (line.CampaignId, Thread: (string?)line.New!["thread"], Marked: line.New!.AsObject().ContainsKey(CampaignOwn)))
            .ToList();
        Assert.Contains((sender.Id, "t_501", true), lines);
        Assert.Contains((other.Id, "t_501", false), lines);
        Assert.Contains((sender.Id, "t_777", false), lines);
        Assert.Contains((other.Id, "t_777", false), lines);
    }

    /// <summary>
    /// A campaign whose first send finished after the reply had not written when the person replied: it is not marked on a
    /// thread it could not have started. One whose send finished at the reply's own moment had written.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task A_first_send_after_the_reply_marks_nothing(int secondsAfter, bool marked)
    {
        using var store = new EventStoreFixture();
        var (source, sender, other, marta) = await WorldAsync(store);
        var replied = Noon.AddHours(-3);
        await store.SentAsync(marta, sender, at: replied.AddSeconds(secondsAfter));

        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null, at: replied)));

        Assert.Equal(marked, Marked(await LineOfAsync(store, sender)));
        Assert.False(Marked(await LineOfAsync(store, other)));
    }

    /// <summary>
    /// A span read again is decided by the writers of its time: of two campaigns that have both written to the person by
    /// now, a backfilled move made when only the first had written is the first one's own, and one made after the second
    /// wrote too is marked nowhere.
    /// </summary>
    [Fact]
    public async Task A_backfilled_move_is_decided_by_the_writers_of_its_time()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync(cursor: "c-9");
        var sender = await store.CampaignAsync(eventsFrom: Noon.AddDays(-60));
        var other = await store.CampaignAsync(eventsFrom: Noon.AddDays(-60));
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, sender, addedAt: Noon.AddDays(-60));
        await store.MemberAsync(marta, other, addedAt: Noon.AddDays(-60));
        await store.SentAsync(marta, sender, at: Noon.AddDays(-50));
        await store.SentAsync(marta, other, at: Noon.AddDays(-40));
        var backfill = await store.BackfillAsync(source);

        await store.StoreAsync(source, EventFence.OfBackfill(backfill.Id, null), Page("b-1", Moved(thread: "t_501", campaign: null, at: Noon.AddDays(-45)), Moved(thread: "t_777", campaign: null, at: Noon.AddDays(-35))));

        var lines = (await store.AllLinesAsync())
            .Select(line => (line.CampaignId, Thread: (string?)line.New!["thread"], Marked: line.New!.AsObject().ContainsKey(CampaignOwn)))
            .ToList();
        Assert.Equal(4, lines.Count);
        Assert.Contains((sender.Id, "t_501", true), lines);
        Assert.Contains((other.Id, "t_501", false), lines);
        Assert.Contains((sender.Id, "t_777", false), lines);
        Assert.Contains((other.Id, "t_777", false), lines);
    }

    /// <summary>
    /// The read of the threads a campaign holds is answered from the observed lines by the campaign and the thread each
    /// names, through the thread index: never a scan, never a campaign's history, at one campaign and thread or many.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(40, 60)]
    public async Task The_read_of_the_threads_a_campaign_holds_searches_by_the_campaign_and_the_thread(int campaigns, int threads)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var query = CampaignOwnThreads.Lines(db, Reader, [.. Enumerable.Range(3, campaigns)], [.. Enumerable.Range(1, threads).Select(n => $"t_{n}")]);
        var plan = await QueryPlans.PlanAsync(db, query.ToQueryString(), Ct);

        var searches = plan.Where(detail => detail.StartsWith("SEARCH ", StringComparison.Ordinal)).ToList();
        Assert.True(
            searches.Count == 1 && searches[0] == "SEARCH o USING INDEX ix_journal_campaign_thread (campaign_id=? AND <expr>=?)",
            $"The plan is {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
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
