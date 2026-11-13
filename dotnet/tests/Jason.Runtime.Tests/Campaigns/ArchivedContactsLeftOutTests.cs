using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// <c>exclude_archived_contacts</c> on the reads beside <c>campaign.summary</c>: <c>campaign.list_contacts</c>,
/// <c>campaign.activity</c> and <c>campaign.activity_by_period</c>. True leaves out every membership of a person whose own
/// record is archived; absent, null or false answers exactly what the read answered before the option existed. In the
/// world below Rae replied in Spring this week and Hal was worked on in Spring and in Autumn; both are archived, and their
/// memberships stay.
/// </summary>
public class ArchivedContactsLeftOutTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Instant(DateTime moment) => new(moment, TimeSpan.Zero);

    private static async Task<CampaignWorld> ArchivedAsync(RuntimeApiFixture api)
    {
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await CampaignWorld.WorkedOnAsync(api, world.People["Hal"], Ct, (world.Spring, 13), (world.Autumn, 13));
        foreach (var name in new[] { "Rae", "Hal" })
        {
            await api.PostOkAsync<ContactDto>(Operations.ContactArchive, new { contact_id = world.People[name].PublicId }, Ct);
        }

        return world;
    }

    [Fact]
    public async Task The_activity_leaves_out_archived_contacts_when_asked_and_counts_them_when_not()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await ArchivedAsync(api);
        string[] both = [world.Spring.PublicId, world.Autumn.PublicId];

        var counted = await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { CampaignIds = both, Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until) }, Ct);
        var left = await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity,
            new { CampaignIds = both, Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until), ExcludeArchivedContacts = true },
            Ct);

        // Absent: the answer of a world where nobody is archived, Rae replied and Hal worked on in both campaigns.
        Assert.Equal(
            [
                new CampaignActivityRow(world.Spring.PublicId, 15, 2, 10, 4, 2, 2, Removed: 1),
                new CampaignActivityRow(world.Autumn.PublicId, 1, 0, 1, 0, 0, 0, Removed: 0),
            ],
            counted.Campaigns);
        Assert.Equal(new CampaignActivityTotal(15, 2, 10, 4, 2, 2, Removed: 1), counted.Total);
        // Left out: Spring loses Rae's reply and Hal's work, and Autumn, whose only happening was Hal's, keeps its row of zeros.
        Assert.Equal(
            [
                new CampaignActivityRow(world.Spring.PublicId, 13, 2, 9, 4, 1, 2, Removed: 1),
                new CampaignActivityRow(world.Autumn.PublicId, 0, 0, 0, 0, 0, 0, Removed: 0),
            ],
            left.Campaigns);
        Assert.Equal(new CampaignActivityTotal(13, 2, 9, 4, 1, 2, Removed: 1), left.Total);
    }

    [Fact]
    public async Task The_activity_by_period_leaves_out_archived_contacts_when_asked_and_counts_them_when_not()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await ArchivedAsync(api);
        object Body(bool? without) => without is null
            ? new { Count = "replied", Period = "day", Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until), Zone = "UTC" }
            : new { Count = "replied", Period = "day", Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until), Zone = "UTC", ExcludeArchivedContacts = without };

        var counted = await api.PostOkAsync<CampaignActivityByPeriodResponse>(Operations.CampaignActivityByPeriod, Body(null), Ct);
        var left = await api.PostOkAsync<CampaignActivityByPeriodResponse>(Operations.CampaignActivityByPeriod, Body(true), Ct);

        // From the 9th to the 16th: Rae replied on the 12th and Oli on the 14th; with Rae archived and left out, only Oli.
        Assert.Equal([0, 0, 0, 1, 0, 1, 0, 0], counted.Buckets.Select(bucket => bucket.Value));
        Assert.Equal([0, 0, 0, 0, 0, 1, 0, 0], left.Buckets.Select(bucket => bucket.Value));
        Assert.Equal(counted.Buckets.Select(bucket => (bucket.Start, bucket.End)), left.Buckets.Select(bucket => (bucket.Start, bucket.End)));
        Assert.NotNull(world);
    }

    [Fact]
    public async Task The_list_leaves_out_archived_contacts_when_asked_and_lists_them_when_not()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await ArchivedAsync(api);
        var spring = world.Spring.PublicId;
        string[] archived = [world.People["Rae"].PublicId, world.People["Hal"].PublicId];

        async Task<string[]> ListedAsync(object body) =>
            [.. (await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, body, Ct)).Items.Select(item => item.Contact.Id)];

        var everyone = await ListedAsync(new { campaign_id = spring, limit = 100 });
        var members = await ListedAsync(new { campaign_id = spring, limit = 100, exclude_archived_contacts = true });
        Assert.Equal(22, everyone.Length);
        Assert.Subset(everyone.ToHashSet(), archived.ToHashSet());
        Assert.Equal(everyone.Except(archived).ToArray(), members);

        // Under a count: Rae is Out now and Replied this week; left out of both lists, kept in both without the option.
        Assert.Contains(archived[0], await ListedAsync(new { campaign_id = spring, summary = "out", limit = 100 }));
        Assert.DoesNotContain(archived[0], await ListedAsync(new { campaign_id = spring, summary = "out", limit = 100, exclude_archived_contacts = true }));
        var period = new { since = Instant(CampaignWorld.Since), until = Instant(CampaignWorld.Until) };
        Assert.Equal(
            [world.People["Oli"].PublicId, archived[0]],
            (await ListedAsync(new { campaign_id = spring, activity = "replied", period.since, period.until, limit = 100 })).Order(StringComparer.Ordinal));
        Assert.Equal(
            [world.People["Oli"].PublicId],
            await ListedAsync(new { campaign_id = spring, activity = "replied", period.since, period.until, limit = 100, exclude_archived_contacts = true }));
    }

    /// <summary>With the option on both, a count and the list it opens agree, count by count, now and in the week.</summary>
    [Fact]
    public async Task With_the_option_each_count_is_the_number_its_list_lists()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await ArchivedAsync(api);
        var spring = world.Spring.PublicId;
        var since = Instant(CampaignWorld.Since);
        var until = Instant(CampaignWorld.Until);

        var now = Assert.Single((await api.PostOkAsync<CampaignSummaryResponse>(
            Operations.CampaignSummary, new { campaign_ids = new[] { spring }, exclude_archived_contacts = true }, Ct)).Campaigns);
        var week = Assert.Single((await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { campaign_ids = new[] { spring }, since, until, exclude_archived_contacts = true }, Ct)).Campaigns);

        async Task<int> ListedAsync(object body) =>
            (await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, body, Ct)).Items.Count;

        Assert.Equal(now.People, await ListedAsync(new { campaign_id = spring, limit = 100, exclude_archived_contacts = true }));
        var nowCounts = new Dictionary<CampaignSummaryFilter, int>
        {
            [CampaignSummaryFilter.Waiting] = now.Waiting,
            [CampaignSummaryFilter.Failed] = now.Failed,
            [CampaignSummaryFilter.Working] = now.Working,
            [CampaignSummaryFilter.New] = now.New,
            [CampaignSummaryFilter.Idle] = now.Idle,
            [CampaignSummaryFilter.Sequence] = now.Sequence,
            [CampaignSummaryFilter.Out] = now.Out,
            [CampaignSummaryFilter.Removed] = now.Removed,
        };
        foreach (var (filter, number) in nowCounts)
        {
            var name = JsonSerializer.Serialize(filter, JasonJson.Options).Trim('"');
            Assert.True(
                number == await ListedAsync(new { campaign_id = spring, summary = name, limit = 100, exclude_archived_contacts = true }),
                $"summary {name}: the count says {number}");
        }

        var weekCounts = new Dictionary<CampaignActivityFilter, int>
        {
            [CampaignActivityFilter.People] = week.People,
            [CampaignActivityFilter.Added] = week.Added,
            [CampaignActivityFilter.WorkedOn] = week.WorkedOn,
            [CampaignActivityFilter.Contacted] = week.Contacted,
            [CampaignActivityFilter.Replied] = week.Replied,
            [CampaignActivityFilter.OptedOut] = week.OptedOut,
            [CampaignActivityFilter.Removed] = week.Removed,
        };
        foreach (var (filter, number) in weekCounts)
        {
            var name = JsonSerializer.Serialize(filter, JasonJson.Options).Trim('"');
            Assert.True(
                number == await ListedAsync(new { campaign_id = spring, activity = name, since, until, limit = 100, exclude_archived_contacts = true }),
                $"activity {name}: the count says {number}");
        }
    }

    /// <summary>False and null read as the option absent, on each of the three reads.</summary>
    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    public async Task False_or_null_answers_as_the_option_absent(string value)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await ArchivedAsync(api);
        const string Window = "\"since\":\"2026-10-09T00:00:00Z\",\"until\":\"2026-10-16T09:00:00Z\"";
        var spring = world.Spring.PublicId;

        (string Operation, string Body)[] reads =
        [
            (Operations.CampaignListContacts, $"{{\"campaign_id\":\"{spring}\",\"limit\":100"),
            (Operations.CampaignListContacts, $"{{\"campaign_id\":\"{spring}\",\"activity\":\"replied\",{Window},\"limit\":100"),
            (Operations.CampaignActivity, $"{{{Window}"),
            (Operations.CampaignActivityByPeriod, $"{{\"count\":\"replied\",\"period\":\"day\",\"zone\":\"UTC\",{Window}"),
        ];
        foreach (var (operation, body) in reads)
        {
            var absent = await PostAsync(api, operation, body + "}");
            var given = await PostAsync(api, operation, body + $",\"exclude_archived_contacts\":{value}}}");
            Assert.Equal(absent, given);
        }
    }

    /// <summary>The option is a boolean on each read; anything else is refused before anything is read.</summary>
    [Theory]
    [InlineData(Operations.CampaignListContacts, "\"yes\"")]
    [InlineData(Operations.CampaignListContacts, "1")]
    [InlineData(Operations.CampaignActivity, "\"true\"")]
    [InlineData(Operations.CampaignActivityByPeriod, "{}")]
    public async Task An_option_that_is_not_a_boolean_is_refused(string operation, string value)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

        var error = await api.PostErrorAsync(operation, JsonNode.Parse($"{{\"exclude_archived_contacts\":{value}}}"), HttpStatusCode.BadRequest, Ct);

        Assert.Equal("invalid_request", error.Code);
    }

    /// <summary>
    /// One campaign's people with the option are the people <c>contact.list</c> lists for that campaign without
    /// <c>include_archived</c>: its members, removed ones and archived ones left out.
    /// </summary>
    [Fact]
    public async Task With_the_option_one_campaign_s_people_are_the_people_contact_list_lists_for_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await ArchivedAsync(api);
        var spring = world.Spring.PublicId;

        var members = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { campaign_id = spring, limit = 100, exclude_archived_contacts = true }, Ct);
        var contacts = await api.PostOkAsync<Page<ContactDto>>(Operations.ContactList, new { campaign_id = spring, limit = 100 }, Ct);

        Assert.Equal(
            contacts.Items.Select(contact => contact.Id).Order(StringComparer.Ordinal),
            members.Items.Select(item => item.Contact.Id).Order(StringComparer.Ordinal));
        Assert.Equal(20, members.Items.Count);
    }

    private static async Task<string> PostAsync(RuntimeApiFixture api, string operation, string body) =>
        (await api.PostOkAsync<JsonNode>(operation, JsonNode.Parse(body), Ct)).ToJsonString();
}
