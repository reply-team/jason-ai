using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Contacts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// Where a person stands in each of their campaigns, read from the same definitions the Summary counts are: each
/// membership's <c>standing</c> names exactly the counts that list the person under that campaign, and
/// <c>contact.list</c>'s <c>summary</c> lists exactly the people a count counts across the campaigns not archived.
/// </summary>
public class ContactStandingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The number a count's total says and the people <c>contact.list</c> lists under it can differ: an archived person
    /// keeps their memberships, so the total counts them, while the list hides archived people until they are asked for.
    /// With them asked for, the two agree.
    /// </summary>
    [Fact]
    public async Task The_total_counts_an_archived_member_whom_the_list_shows_only_when_archived_people_are_asked_for()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        var hal = world.People["Hal"].PublicId;
        await api.PostOkAsync<ContactDto>(Operations.ContactArchive, new { contact_id = hal, reason = "Left the company." }, Ct);

        var total = (await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { }, Ct)).Total.New;
        var shown = await WalkAsync(api, new JsonObject { ["summary"] = "new" });
        var asked = await WalkAsync(api, new JsonObject { ["summary"] = "new", ["include_archived"] = true });

        Assert.DoesNotContain(hal, shown);
        Assert.Contains(hal, asked);
        Assert.Equal(total, asked.Count);
        Assert.Equal(total - 1, shown.Count);
    }

    /// <summary>
    /// For every membership of every person, in every campaign archived or not, and for every count: the membership's
    /// standing holds the count exactly when <c>campaign.list_contacts</c> under that count lists the person under that
    /// campaign. The three answers that carry a person's campaigns say the same standing.
    /// </summary>
    [Fact]
    public async Task Each_membership_stands_in_exactly_the_counts_that_list_the_person_under_its_campaign()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        string[] every = [world.Spring.PublicId, world.Autumn.PublicId, world.Winter.PublicId];

        var listed = new HashSet<(string Contact, string Campaign, CampaignSummaryFilter Count)>();
        var carried = new HashSet<(string, string, CampaignSummaryFilter)>();
        foreach (var count in Enum.GetValues<CampaignSummaryFilter>())
        {
            var before = listed.Count;
            foreach (var item in await MembersAsync(api, new { campaign_ids = every, summary = count }))
            {
                carried.UnionWith(Standing(item.Contact));
                foreach (var counted in item.Counted!)
                {
                    listed.Add((item.Contact.Id, counted.CampaignId, count));
                }
            }

            Assert.True(listed.Count > before, $"The world has nobody under {count}, so the test would prove nothing about it.");
        }

        var got = new HashSet<(string, string, CampaignSummaryFilter)>();
        foreach (var person in world.People.Values)
        {
            got.UnionWith(Standing(await api.PostOkAsync<ContactDto>(Operations.ContactGet, new { contact_id = person.PublicId }, Ct)));
        }

        Assert.Equal(Sorted(listed), Sorted(got));

        var page = await api.PostOkAsync<Page<ContactDto>>(Operations.ContactList, new { order = "activity", limit = 1000 }, Ct);
        Assert.Equal(Sorted(listed), Sorted([.. page.Items.SelectMany(Standing)]));
        Assert.Equal(Sorted(listed), Sorted(carried));
    }

    /// <summary>A removed membership stands in removed and in nothing else, whatever its work and lines say.</summary>
    [Fact]
    public async Task A_removed_membership_stands_in_removed_alone()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var oli = await api.PostOkAsync<ContactDto>(Operations.ContactGet, new { contact_id = world.People["Oli"].PublicId }, Ct);

        var membership = Assert.Single(oli.Campaigns!);
        Assert.Equal(MembershipState.Excluded, membership.State);
        Assert.Equal([CampaignSummaryFilter.Removed], membership.Standing);
    }

    /// <summary>
    /// <c>contact.list</c> under a count lists the people <c>campaign.list_contacts</c> lists under it across every campaign
    /// not archived, in the order of activity; <c>campaign_id</c> keeps the people it counts in that campaign, removed ones
    /// too under removed; <c>query</c> narrows as it does without a count.
    /// </summary>
    [Fact]
    public async Task The_list_under_a_count_lists_the_people_the_count_counts_in_the_order_asked()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        var byActivity = await WalkAsync(api, new JsonObject { ["order"] = "activity" });

        foreach (var count in Enum.GetValues<CampaignSummaryFilter>())
        {
            var counted = (await MembersAsync(api, new { summary = count })).Select(item => item.Contact.Id).ToHashSet();
            var listed = await WalkAsync(api, new JsonObject { ["summary"] = Snake(count), ["order"] = "activity" });
            Assert.Equal([.. byActivity.Where(counted.Contains)], listed);

            var inSpring = (await MembersAsync(api, new { campaign_id = world.Spring.PublicId, summary = count })).Select(item => item.Contact.Id).Order(StringComparer.Ordinal);
            var listedInSpring = await WalkAsync(api, new JsonObject { ["summary"] = Snake(count), ["campaign_id"] = world.Spring.PublicId });
            Assert.Equal(inSpring, listedInSpring);
        }

        var found = await WalkAsync(api, new JsonObject { ["query"] = "a" });
        var waiting = await WalkAsync(api, new JsonObject { ["summary"] = "waiting" });
        var both = await WalkAsync(api, new JsonObject { ["summary"] = "waiting", ["query"] = "a" });
        Assert.NotEmpty(both);
        Assert.Equal([.. found.Intersect(waiting)], both);

        var removed = await WalkAsync(api, new JsonObject { ["summary"] = "removed", ["campaign_id"] = world.Spring.PublicId });
        Assert.Equal([world.People["Oli"].PublicId], removed);
    }

    /// <summary>A count lists people by where they stand; a channel or a value lists them by an address. One request asks one.</summary>
    [Theory]
    [InlineData("{\"summary\":\"waiting\",\"channel\":\"email\"}")]
    [InlineData("{\"summary\":\"out\",\"channel\":\"email\",\"value\":\"ada@spring.example\"}")]
    [InlineData("{\"summary\":\"new\",\"value\":\"ada@spring.example\"}")]
    public async Task A_count_with_a_channel_or_a_value_is_refused(string body)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.ContactList, JsonNode.Parse(body), HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Contains("summary", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A count is one of the Summary's eight words; any other is refused before anything is read.</summary>
    [Theory]
    [InlineData("\"stage\"")]
    [InlineData("3")]
    public async Task A_count_the_summary_does_not_have_is_refused(string count)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.ContactList, JsonNode.Parse($"{{\"summary\":{count}}}"), HttpStatusCode.BadRequest, Ct);

        Assert.Equal("invalid_request", error.Code);
        Assert.False(error.Retryable);
    }

    /// <summary>
    /// A page of everyone costs the database the same number of commands as a page of two, with a count and without: where
    /// each person stands is read once for the page, never once per person or per campaign.
    /// </summary>
    [Fact]
    public async Task A_page_of_everyone_costs_as_many_commands_as_a_page_of_two()
    {
        using var database = new TestDatabase();
        await using (var seed = database.Open())
        {
            await CampaignWorld.WriteAsync(seed, Ct);
        }

        var clock = new FixedClock(CampaignWorld.Now);
        async Task<(int Commands, int People)> PageAsync(int limit, CampaignSummaryFilter? summary)
        {
            var recorder = new Recorder();
            var builder = new DbContextOptionsBuilder<JasonDbContext>();
            JasonDbContext.Configure(builder, database.File, JournalLines.Strict).AddInterceptors(recorder);
            await using var db = new JasonDbContext(builder.Options);
            var service = new ContactService(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
            var page = await service.ListAsync(new ContactListRequest(null, null, null, limit, null, Order: ContactListOrder.Activity, Summary: summary), Ct);
            Assert.All(page.Items, contact => Assert.NotNull(contact.Campaigns));
            return (recorder.Commands, page.Items.Count);
        }

        foreach (var summary in new CampaignSummaryFilter?[] { null, CampaignSummaryFilter.Out })
        {
            var two = await PageAsync(2, summary);
            var all = await PageAsync(200, summary);
            Assert.Equal(2, two.People);
            Assert.True(all.People > 2, $"Only {all.People} people under {summary}: the page of everyone would prove nothing.");
            Assert.Equal(two.Commands, all.Commands);
        }
    }

    private static string Snake(CampaignSummaryFilter count) => JsonSerializer.SerializeToNode(count, JasonJson.Options)!.GetValue<string>();

    private static IEnumerable<(string, string, CampaignSummaryFilter)> Standing(ContactDto contact) =>
        contact.Campaigns!.SelectMany(campaign => campaign.Standing.Select(count => (contact.Id, campaign.CampaignId, count)));

    private static List<(string, string, CampaignSummaryFilter)> Sorted(IEnumerable<(string, string, CampaignSummaryFilter)> set) =>
        [.. set.Order()];

    /// <summary>Every page of <c>contact.list</c> under this request, two people a page, the ids in the order listed.</summary>
    private static async Task<List<string>> WalkAsync(RuntimeApiFixture api, JsonObject request)
    {
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var body = request.DeepClone().AsObject();
            body["limit"] = 2;
            body["cursor"] = cursor;
            var page = await api.PostOkAsync<Page<ContactDto>>(Operations.ContactList, body, Ct);
            ids.AddRange(page.Items.Select(contact => contact.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return ids;
    }

    /// <summary>Counts the commands a context sends.</summary>
    private sealed class Recorder : DbCommandInterceptor
    {
        public int Commands { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Every page of <c>campaign.list_contacts</c> under this request.</summary>
    private static async Task<List<MembershipItemDto>> MembersAsync(RuntimeApiFixture api, object request)
    {
        var items = new List<MembershipItemDto>();
        string? cursor = null;
        do
        {
            var body = JsonSerializer.SerializeToNode(request, JasonJson.Options)!.AsObject();
            body["limit"] = 50;
            body["cursor"] = cursor;
            var page = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, body, Ct);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return items;
    }
}
