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

        var counted = filter switch
        {
            CampaignSummaryFilter.Waiting => row.Waiting,
            CampaignSummaryFilter.Failed => row.Failed,
            CampaignSummaryFilter.Working => row.Working,
            CampaignSummaryFilter.New => row.New,
            CampaignSummaryFilter.Idle => row.Idle,
            CampaignSummaryFilter.Sequence => row.Sequence,
            CampaignSummaryFilter.Out => row.Out,
            CampaignSummaryFilter.Removed => row.Removed,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "A count with no field in the row."),
        };
        Assert.Equal(counted, listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(ActivityFilters))]
    public async Task Every_activity_count_equals_its_list(CampaignActivityFilter filter)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var row = Assert.Single((await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { CampaignIds = new[] { world.Spring.PublicId }, Since, Until }, Ct)).Campaigns);
        var listed = await WalkAsync(api, new { CampaignId = world.Spring.PublicId, Activity = filter, Since, Until });

        var counted = filter switch
        {
            CampaignActivityFilter.People => row.People,
            CampaignActivityFilter.Added => row.Added,
            CampaignActivityFilter.WorkedOn => row.WorkedOn,
            CampaignActivityFilter.Contacted => row.Contacted,
            CampaignActivityFilter.Replied => row.Replied,
            CampaignActivityFilter.OptedOut => row.OptedOut,
            CampaignActivityFilter.Removed => row.Removed,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "A count with no field in the row."),
        };
        Assert.Equal(counted, listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

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
