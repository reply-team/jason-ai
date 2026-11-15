using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>Every number of the Campaigns screen opens exactly the people it counts, however the list is paged.</summary>
public class CountEqualsListTests
{
    private static readonly DateTimeOffset Since = new(CampaignWorld.Since, TimeSpan.Zero);

    private static readonly DateTimeOffset Until = new(CampaignWorld.Until, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<CampaignSummaryFilter> SummaryFilters => [.. Enum.GetValues<CampaignSummaryFilter>()];

    public static TheoryData<CampaignActivityFilter> ActivityFilters => [.. Enum.GetValues<CampaignActivityFilter>()];

    [Theory]
    [MemberData(nameof(SummaryFilters))]
    public async Task Every_summary_count_equals_its_list(CampaignSummaryFilter filter)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var row = Assert.Single((await api.PostOkAsync<CampaignSummaryResponse>(
            Operations.CampaignSummary, new { CampaignIds = new[] { world.Spring.PublicId } }, Ct)).Campaigns);
        var listed = await WalkAsync(api, new { CampaignId = world.Spring.PublicId, Summary = filter });
        Assert.Equal(Of(filter, new CampaignSummaryTotal(row.People, row.Waiting, row.Failed, row.Working, row.New, row.Idle, row.Sequence, row.Out, row.Removed)), listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());

        // Across both campaigns, where Ada and Hal are each in two counts: the total is the people listed, each once.
        string[] both = [world.Spring.PublicId, world.Autumn.PublicId];
        var total = (await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { CampaignIds = both }, Ct)).Total;
        var across = await WalkAsync(api, new { CampaignIds = both, Summary = filter });
        Assert.Equal(Of(filter, total), across.Count);
        Assert.Equal(across.Count, across.Distinct().Count());
    }

    /// <summary>
    /// Leaving archived contacts out, every Summary count, and <c>people</c>, equals the people <c>contact.list</c> returns
    /// for it without <c>include_archived</c>: in one campaign, and across every campaign the summary counts when none is
    /// named. Archived people stand in several counts: waiting and idle (Ben, whose question archiving leaves pending),
    /// idle (Ada, whose waiting enrolment archiving cancelled, and Wes), new and idle in two campaigns (Hal), failed
    /// (Cleo), sequence (Ivy), out (Jon) and removed (Oli).
    /// </summary>
    [Theory]
    [MemberData(nameof(CountsWithPeople))]
    public async Task Without_archived_contacts_every_summary_count_equals_the_contact_list(string count)
    {
        CampaignSummaryFilter? filter = count == "people" ? null : Enum.Parse<CampaignSummaryFilter>(count);
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        foreach (var name in Archived)
        {
            await api.PostOkAsync<ContactDto>(Operations.ContactArchive, new { contact_id = world.People[name].PublicId }, Ct);
        }

        var spring = world.Spring.PublicId;
        var one = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { CampaignIds = new[] { spring }, ExcludeArchivedContacts = true }, Ct);
        var row = Assert.Single(one.Campaigns);
        var listed = await ContactsAsync(api, filter is { } f ? new { CampaignId = spring, Summary = f } : (object)new { CampaignId = spring });
        Assert.Equal(Of(filter, new CampaignSummaryTotal(row.People, row.Waiting, row.Failed, row.Working, row.New, row.Idle, row.Sequence, row.Out, row.Removed)), listed.Count);

        // Across every campaign the summary counts, a person once; people is everyone listed in one of those campaigns.
        var every = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { ExcludeArchivedContacts = true }, Ct);
        var across = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string?> campaigns = filter is null ? [.. every.Campaigns.Select(each => each.CampaignId)] : [null];
        foreach (var campaign in campaigns)
        {
            across.UnionWith(await ContactsAsync(api, filter is { } g ? new { Summary = g } : (object)new { CampaignId = campaign }));
        }

        Assert.Equal(Of(filter, every.Total), across.Count);

        // The seed reaches: the archived people stood in this count before they were left out, except in Working, which
        // only open work decides, and archiving cancels a person's open work. A question waiting on them stays pending.
        var everyone = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { }, Ct);
        if (filter is not CampaignSummaryFilter.Working)
        {
            Assert.True(Of(filter, everyone.Total) > Of(filter, every.Total), $"No archived person stands in {filter?.ToString() ?? "people"}.");
        }
    }

    private static readonly string[] Archived = ["Ada", "Ben", "Cleo", "Hal", "Ivy", "Jon", "Oli", "Wes"];

    public static TheoryData<string> CountsWithPeople => ["people", .. Enum.GetNames<CampaignSummaryFilter>()];

    private static int Of(CampaignSummaryFilter? filter, CampaignSummaryTotal counts) => filter is { } count ? Of(count, counts) : counts.People;

    private static async Task<List<string>> ContactsAsync(RuntimeApiFixture api, object filter)
    {
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var body = System.Text.Json.JsonSerializer.SerializeToNode(filter, Jason.Contracts.Json.JasonJson.Options)!.AsObject();
            body["limit"] = 2;
            body["cursor"] = cursor;
            var page = await api.PostOkAsync<Page<ContactDto>>(Operations.ContactList, body, Ct);
            ids.AddRange(page.Items.Select(contact => contact.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(ids.Count, ids.Distinct().Count());
        return ids;
    }

    private static int Of(CampaignSummaryFilter filter, CampaignSummaryTotal counts) => filter switch
    {
        CampaignSummaryFilter.Waiting => counts.Waiting,
        CampaignSummaryFilter.Failed => counts.Failed,
        CampaignSummaryFilter.Working => counts.Working,
        CampaignSummaryFilter.New => counts.New,
        CampaignSummaryFilter.Idle => counts.Idle,
        CampaignSummaryFilter.Sequence => counts.Sequence,
        CampaignSummaryFilter.Out => counts.Out,
        CampaignSummaryFilter.Removed => counts.Removed,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "A count with no field in the row."),
    };

    [Theory]
    [MemberData(nameof(ActivityFilters))]
    public async Task Every_activity_count_equals_its_list(CampaignActivityFilter filter)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var row = Assert.Single((await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { CampaignIds = new[] { world.Spring.PublicId }, Since, Until }, Ct)).Campaigns);
        var listed = await WalkAsync(api, new { CampaignId = world.Spring.PublicId, Activity = filter, Since, Until });
        Assert.Equal(Of(filter, new CampaignActivityTotal(row.People, row.Added, row.WorkedOn, row.Contacted, row.Replied, row.Bounced, row.OptedOut, row.Removed)), listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());

        // Across both campaigns, Hal worked on in each: the total is the people listed, each once.
        await CampaignWorld.WorkedOnAsync(api, world.People["Hal"], Ct, (world.Spring, 13), (world.Autumn, 14));
        string[] both = [world.Spring.PublicId, world.Autumn.PublicId];
        var total = (await api.PostOkAsync<CampaignActivityResponse>(Operations.CampaignActivity, new { CampaignIds = both, Since, Until }, Ct)).Total;
        var across = await WalkAsync(api, new { CampaignIds = both, Activity = filter, Since, Until });
        Assert.Equal(Of(filter, total), across.Count);
        Assert.Equal(across.Count, across.Distinct().Count());
    }

    /// <summary>
    /// A hard bounce's stop is the runtime's entry on the register (<c>system:bounce</c>): a dead address, not a person's
    /// wish. It is no opt-out in the count, in the list the count opens, in a person's why and in the period series; a
    /// person's own <c>suppression add</c> still is. Ivy's email is stopped for a bounce on the 13th, after her work of the
    /// 12th, and Tess's by a person on the 14th, after her send of the 12th.
    /// </summary>
    [Fact]
    public async Task A_hard_bounce_s_stop_is_no_opt_out_and_a_person_s_own_entry_still_is()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        string[] spring = [world.Spring.PublicId];
        var before = Assert.Single((await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { CampaignIds = spring, Since, Until }, Ct)).Campaigns).OptedOut;
        var seriesBefore = await OptedOutByDayAsync(api, spring);

        var ivy = world.People["Ivy"];
        var tess = world.People["Tess"];
        await CampaignWorld.StoppedAsync(api, ivy, new ActorRef(ActorType.System, "bounce"), new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc), Ct);
        await CampaignWorld.StoppedAsync(api, tess, new ActorRef(ActorType.Human, "you@example.com"), new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc), Ct);

        var row = Assert.Single((await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { CampaignIds = spring, Since, Until }, Ct)).Campaigns);
        Assert.Equal(before + 1, row.OptedOut);
        var listed = await WalkAsync(api, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.OptedOut, Since, Until });
        Assert.Equal(row.OptedOut, listed.Count);
        Assert.Contains(tess.PublicId, listed);
        Assert.DoesNotContain(ivy.PublicId, listed);

        var people = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.People, Since, Until, Limit = 100 }, Ct);
        Assert.NotEqual("opted_out", Assert.Single(people.Items, item => item.Contact.Id == ivy.PublicId).Why?.Text);
        Assert.Equal("opted_out", Assert.Single(people.Items, item => item.Contact.Id == tess.PublicId).Why?.Text);

        var seriesAfter = await OptedOutByDayAsync(api, spring);
        Assert.Equal(seriesBefore.Keys, seriesAfter.Keys);
        foreach (var (day, value) in seriesAfter)
        {
            Assert.Equal(seriesBefore[day] + (day.Day == 14 ? 1 : 0), value);
        }
    }

    private static async Task<Dictionary<DateTimeOffset, int>> OptedOutByDayAsync(RuntimeApiFixture api, string[] campaigns) =>
        (await api.PostOkAsync<CampaignActivityByPeriodResponse>(
            Operations.CampaignActivityByPeriod,
            new { CampaignIds = campaigns, Count = "opted_out", Period = "day", Since, Until },
            Ct)).Buckets.ToDictionary(bucket => bucket.Start, bucket => bucket.Value);

    private static int Of(CampaignActivityFilter filter, CampaignActivityTotal counts) => filter switch
    {
        CampaignActivityFilter.People => counts.People,
        CampaignActivityFilter.Added => counts.Added,
        CampaignActivityFilter.WorkedOn => counts.WorkedOn,
        CampaignActivityFilter.Contacted => counts.Contacted,
        CampaignActivityFilter.Replied => counts.Replied,
        CampaignActivityFilter.Bounced => counts.Bounced,
        CampaignActivityFilter.OptedOut => counts.OptedOut,
        CampaignActivityFilter.Removed => counts.Removed,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "A count with no field in the row."),
    };

    private static async Task<List<string>> WalkAsync(RuntimeApiFixture api, object filter)
    {
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var body = System.Text.Json.JsonSerializer.SerializeToNode(filter, Jason.Contracts.Json.JasonJson.Options)!.AsObject();
            body["limit"] = 2;
            body["cursor"] = cursor;
            var page = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, body, Ct);
            Assert.True(page.Items.Count <= 2);
            ids.AddRange(page.Items.Select(item => item.Contact.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return ids;
    }
}
