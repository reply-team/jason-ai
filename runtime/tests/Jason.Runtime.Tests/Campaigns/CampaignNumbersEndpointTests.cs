using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignNumbersEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_summary_answers_every_count_for_each_campaign_asked()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var summary = await api.PostOkAsync<CampaignSummaryResponse>(
            Operations.CampaignSummary, new { CampaignIds = new[] { world.Spring.PublicId, world.Autumn.PublicId } }, Ct);

        Assert.Equal(
            [
                new CampaignSummaryRow(world.Spring.PublicId, 22, 3, 3, 2, 2, 6, 2, 6, Removed: 1, SequenceLinked: true),
                new CampaignSummaryRow(world.Autumn.PublicId, 2, 0, 0, 0, 1, 1, 0, 0, Removed: 0, SequenceLinked: false),
            ],
            summary.Campaigns);
    }

    [Fact]
    public async Task The_activity_answers_the_period()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var activity = await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity,
            new { CampaignIds = new[] { world.Spring.PublicId, world.Autumn.PublicId }, Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until) },
            Ct);

        Assert.Equal(
            [
                new CampaignActivityRow(world.Spring.PublicId, 14, 2, 9, 4, 2, 2, Removed: 1),
                new CampaignActivityRow(world.Autumn.PublicId, 0, 0, 0, 0, 0, 0, Removed: 0),
            ],
            activity.Campaigns);
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

        var error = await api.PostErrorAsync(
            Operations.CampaignActivity,
            new { Since = Instant(CampaignWorld.Until), Until = Instant(CampaignWorld.Since) },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("until", "invalid", "until must not be before since."), (detail.Field, detail.Code, detail.Message));
    }

    [Fact]
    public async Task An_activity_without_its_period_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

        var error = await api.PostErrorAsync(Operations.CampaignActivity, new { }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal([("since", "required"), ("until", "required")], error.Details!.Select(detail => (detail.Field, detail.Code)));
    }

    [Fact]
    public async Task An_unknown_campaign_is_not_found()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var error = await api.PostErrorAsync(
            Operations.CampaignSummary, new { CampaignIds = new[] { world.Spring.PublicId, "cmp_01JASONNOTHERE" } }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("campaign_not_found", error.Code);
    }

    [Fact]
    public async Task No_campaign_ids_means_every_campaign_the_list_shows()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var listed = await api.PostOkAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, new { }, Ct);
        var summary = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { }, Ct);
        var activity = await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity, new { Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until) }, Ct);

        Assert.Equal(2, listed.Items.Count);
        Assert.DoesNotContain(world.Winter.PublicId, summary.Campaigns.Select(row => row.CampaignId));
        Assert.Equal(listed.Items.Select(campaign => campaign.Id), summary.Campaigns.Select(row => row.CampaignId));
        Assert.Equal(listed.Items.Select(campaign => campaign.Id), activity.Campaigns.Select(row => row.CampaignId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task An_empty_list_of_ids_or_one_past_a_hundred_is_refused(int count)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var ids = Enumerable.Range(1, count).Select(n => $"cmp_01K7QN{n:D20}").ToArray();

        var error = await api.PostErrorAsync(Operations.CampaignSummary, new { CampaignIds = ids }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("campaign_ids", "invalid"), (detail.Field, detail.Code));
    }

    private static DateTimeOffset Instant(DateTime moment) => new(moment, TimeSpan.Zero);
}
