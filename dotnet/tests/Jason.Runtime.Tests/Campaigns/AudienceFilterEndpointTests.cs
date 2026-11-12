using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// The people reads of one audience: <c>audience</c>, the number of one of the campaign's audiences, keeps
/// <c>campaign.summary</c>, <c>campaign.activity</c>, <c>campaign.activity_by_period</c> and <c>campaign.list_contacts</c> to
/// the memberships a search landed under it, every count keeping its definition. Spring's audience 1 landed Ada, Ben, Cleo,
/// Jon, Oli (removed since) and Quinn; audience 2 Rae and Tess; audience 3 nobody.
/// </summary>
public class AudienceFilterEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_summary_of_an_audience_counts_its_people_alone()
    {
        await using var api = await StartAsync();
        var world = await WorldAsync(api);
        var spring = world.Spring.PublicId;

        var summary = await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "audience", new { campaign_ids = new[] { spring }, audience = 1 }, Ct);

        Assert.Equal([new CampaignSummaryRow(spring, 5, 2, 1, 0, 1, 1, 0, 1, Removed: 1, SequenceLinked: true)], summary.Campaigns);
        Assert.Equal(new CampaignSummaryTotal(5, 2, 1, 0, 1, 1, 0, 1, Removed: 1), summary.Total);
    }

    /// <summary>
    /// What happened to an audience's people in the week, and when: Oli, removed since, replied on the 14th; Rae, who replied
    /// on the 12th, is audience 2's.
    /// </summary>
    [Fact]
    public async Task The_activity_of_an_audience_counts_what_happened_to_its_people_alone()
    {
        await using var api = await StartAsync();
        var world = await WorldAsync(api);
        var spring = world.Spring.PublicId;

        var activity = await api.RecordAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, "audience", new { campaign_ids = new[] { spring }, audience = 1, since = Week.Since, until = Week.Until }, Ct);
        var byDay = await api.RecordAsync<CampaignActivityByPeriodResponse>(
            Operations.CampaignActivityByPeriod,
            "audience",
            new { campaign_ids = new[] { spring }, audience = 1, count = "replied", period = "day", since = Week.Since, until = Week.Until },
            Ct);

        Assert.Equal([new CampaignActivityRow(spring, 5, 2, 2, 0, 1, 0, Removed: 1)], activity.Campaigns);
        Assert.Equal(new CampaignActivityTotal(5, 2, 2, 0, 1, 0, Removed: 1), activity.Total);
        Assert.Equal(
            [("2026-10-14", 1)],
            byDay.Buckets.Where(bucket => bucket.Value > 0).Select(bucket => (bucket.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), bucket.Value)));
    }

    /// <summary>
    /// The people of an audience, each with the audience they came from; and its people one count counts, by the count's own
    /// definition: waiting now, and replied in the week, removed or not.
    /// </summary>
    [Fact]
    public async Task The_people_list_of_an_audience_lists_its_people_alone()
    {
        await using var api = await StartAsync();
        var world = await WorldAsync(api);
        var spring = world.Spring.PublicId;

        var members = await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "audience", new { campaign_id = spring, audience = 1 }, Ct);
        var waiting = await api.RecordAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, "audience-summary-waiting", new { campaign_id = spring, audience = 1, summary = "waiting" }, Ct);
        var replied = await api.RecordAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts,
            "audience-activity-replied",
            new { campaign_ids = new[] { spring }, audience = 1, activity = "replied", since = Week.Since, until = Week.Until },
            Ct);

        Assert.Equal(["Ada", "Ben", "Cleo", "Jon", "Quinn"], Names(members));
        Assert.All(members.Items, member => Assert.Equal(1, member.Source!.Audience.Number));
        Assert.Equal(["Ada", "Ben"], Names(waiting));
        Assert.Equal(["Oli"], Names(replied));
    }

    [Fact]
    public async Task An_audience_nobody_landed_under_answers_zeros()
    {
        await using var api = await StartAsync();
        var world = await WorldAsync(api);
        var spring = world.Spring.PublicId;

        var summary = await api.RecordAsync<CampaignSummaryResponse>(
            Operations.CampaignSummary, "audience-nobody-landed", new { campaign_ids = new[] { spring }, audience = 3 }, Ct);
        var activity = await api.RecordAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, "audience-nobody-landed", new { campaign_ids = new[] { spring }, audience = 3, since = Week.Since, until = Week.Until }, Ct);
        var members = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { campaign_id = spring, audience = 3 }, Ct);

        Assert.Equal([new CampaignSummaryRow(spring, 0, 0, 0, 0, 0, 0, 0, 0, Removed: 0, SequenceLinked: true)], summary.Campaigns);
        Assert.Equal(new CampaignSummaryTotal(0, 0, 0, 0, 0, 0, 0, 0, Removed: 0), summary.Total);
        Assert.Equal([new CampaignActivityRow(spring, 0, 0, 0, 0, 0, 0, Removed: 0)], activity.Campaigns);
        Assert.Empty(members.Items);
    }

    /// <summary>A number the campaign holds no audience under is not found, whichever campaign holds one under it.</summary>
    [Fact]
    public async Task An_audience_the_campaign_does_not_hold_is_not_found()
    {
        await using var api = await StartAsync();
        var world = await WorldAsync(api);

        var unknown = await api.RecordErrorAsync(
            Operations.CampaignSummary, "audience-unknown", new { campaign_ids = new[] { world.Spring.PublicId }, audience = 4 }, HttpStatusCode.NotFound, Ct);
        var another = await api.RecordErrorAsync(
            Operations.CampaignListContacts, "audience-unknown", new { campaign_id = world.Autumn.PublicId, audience = 1 }, HttpStatusCode.NotFound, Ct);
        var byPeriod = await api.PostErrorAsync(
            Operations.CampaignActivityByPeriod,
            new { campaign_ids = new[] { world.Spring.PublicId }, audience = 4, count = "people", period = "day", since = Week.Since, until = Week.Until },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal(("audience_unknown", "audience_unknown", "audience_unknown"), (unknown.Code, another.Code, byPeriod.Code));
        Assert.Contains("holds no audience under 4", unknown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An audience is one campaign's, so the read that names one names that campaign alone; and a number is 1 or more.
    /// </summary>
    [Fact]
    public async Task An_audience_is_read_in_the_one_campaign_named()
    {
        await using var api = await StartAsync();
        var world = await WorldAsync(api);
        var both = new[] { world.Spring.PublicId, world.Autumn.PublicId };

        var two = await api.RecordErrorAsync(
            Operations.CampaignSummary, "audience-two-campaigns", new { campaign_ids = both, audience = 1 }, HttpStatusCode.BadRequest, Ct);
        var none = await api.PostErrorAsync(Operations.CampaignActivity, new { audience = 1, since = Week.Since, until = Week.Until }, HttpStatusCode.BadRequest, Ct);
        var listed = await api.PostErrorAsync(Operations.CampaignListContacts, new { campaign_ids = both, audience = 1 }, HttpStatusCode.BadRequest, Ct);
        var zero = await api.PostErrorAsync(Operations.CampaignSummary, new { campaign_ids = new[] { world.Spring.PublicId }, audience = 0 }, HttpStatusCode.BadRequest, Ct);

        Assert.All([two, none, listed, zero], error => Assert.Equal("validation_failed", error.Code));
        Assert.All([two, none, listed], error => Assert.Equal(("audience", "invalid"), (Assert.Single(error.Details!).Field, error.Details![0].Code)));
        Assert.Equal(("audience", "invalid"), (Assert.Single(zero.Details!).Field, zero.Details![0].Code));
        Assert.Contains("one campaign", two.Details![0].Message, StringComparison.Ordinal);
    }

    private static class Week
    {
        public const string Since = "2026-10-09T00:00:00.000Z";

        public const string Until = "2026-10-16T09:00:00.000Z";
    }

    private static Task<RuntimeApiFixture> StartAsync() => RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

    /// <summary>The world at ids that sort the same on every machine, and Spring's three audiences.</summary>
    private static async Task<CampaignWorld> WorldAsync(RuntimeApiFixture api)
    {
        var n = 300;
        string Id(string prefix) => $"{prefix}_01K7QN{++n:D20}";

        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var world = await CampaignWorld.WriteAsync(db, Ct, Id);
        await world.AudienceAsync(db, 1, Ct, "Ada", "Ben", "Cleo", "Jon", "Oli", "Quinn");
        await world.AudienceAsync(db, 2, Ct, "Rae", "Tess");
        await world.AudienceAsync(db, 3, Ct);
        return world;
    }

    private static string[] Names(Page<MembershipItemDto> page) =>
        [.. page.Items.Select(member => member.Contact.FirstName!).Order(StringComparer.Ordinal)];
}
