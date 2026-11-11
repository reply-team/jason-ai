using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignNumbersEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_summary_answers_every_count_for_each_campaign_asked_and_for_them_all()
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
        // Ada waits in Spring and is new in Autumn; Hal is new in Spring and idle in Autumn: each is one person in two counts.
        Assert.Equal(new CampaignSummaryTotal(22, 3, 3, 2, 3, 7, 2, 6, Removed: 1), summary.Total);
    }

    /// <summary>
    /// Asked to leave out archived contacts, the summary counts nobody whose own record is archived, in any count; every
    /// campaign asked keeps its row, with zeros where nobody is left. Without the option they are counted as before.
    /// </summary>
    [Fact]
    public async Task The_summary_leaves_out_archived_contacts_when_asked_and_keeps_every_campaign_s_row()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        // Ada waits in Spring and is new in Autumn; archiving her cancels the enrolment that waits, so she is idle in
        // Spring. Hal is new in Spring and idle in Autumn. With both archived, nobody of Autumn is left.
        foreach (var name in new[] { "Ada", "Hal" })
        {
            await api.PostOkAsync<ContactDto>(Operations.ContactArchive, new { contact_id = world.People[name].PublicId }, Ct);
        }

        string[] both = [world.Spring.PublicId, world.Autumn.PublicId];
        var counted = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { CampaignIds = both }, Ct);
        var left = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { CampaignIds = both, ExcludeArchivedContacts = true }, Ct);

        Assert.Equal(
            [
                new CampaignSummaryRow(world.Spring.PublicId, 22, 2, 3, 2, 2, 7, 2, 6, Removed: 1, SequenceLinked: true),
                new CampaignSummaryRow(world.Autumn.PublicId, 2, 0, 0, 0, 1, 1, 0, 0, Removed: 0, SequenceLinked: false),
            ],
            counted.Campaigns);
        Assert.Equal(
            [
                new CampaignSummaryRow(world.Spring.PublicId, 20, 2, 3, 2, 1, 6, 2, 6, Removed: 1, SequenceLinked: true),
                new CampaignSummaryRow(world.Autumn.PublicId, 0, 0, 0, 0, 0, 0, 0, 0, Removed: 0, SequenceLinked: false),
            ],
            left.Campaigns);
        Assert.Equal(new CampaignSummaryTotal(20, 2, 3, 2, 1, 6, 2, 6, Removed: 1), left.Total);
    }

    /// <summary>False and null read as the option absent: everyone is counted, archived people too.</summary>
    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    public async Task Exclude_archived_contacts_false_or_null_counts_as_today(string value)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await api.PostOkAsync<ContactDto>(Operations.ContactArchive, new { contact_id = world.People["Quinn"].PublicId }, Ct);

        var absent = await api.PostOkAsync<CampaignSummaryResponse>(Operations.CampaignSummary, new { }, Ct);
        var given = await api.PostOkAsync<CampaignSummaryResponse>(
            Operations.CampaignSummary, JsonNode.Parse($"{{\"exclude_archived_contacts\":{value}}}"), Ct);

        Assert.Equal(absent.Campaigns, given.Campaigns);
        Assert.Equal(absent.Total, given.Total);
        Assert.Equal(3, given.Total.New);
    }

    /// <summary>The option is a boolean; anything else is refused before anything is read.</summary>
    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task Exclude_archived_contacts_that_is_not_a_boolean_is_refused(string value)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

        var error = await api.PostErrorAsync(
            Operations.CampaignSummary, JsonNode.Parse($"{{\"exclude_archived_contacts\":{value}}}"), HttpStatusCode.BadRequest, Ct);

        Assert.Equal("invalid_request", error.Code);
    }

    [Fact]
    public async Task The_activity_answers_the_period_for_each_campaign_asked_and_for_them_all()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await CampaignWorld.WorkedOnAsync(api, world.People["Hal"], Ct, (world.Spring, 13), (world.Autumn, 13));

        var activity = await api.PostOkAsync<CampaignActivityResponse>(
            Operations.CampaignActivity,
            new { CampaignIds = new[] { world.Spring.PublicId, world.Autumn.PublicId }, Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until) },
            Ct);

        Assert.Equal(
            [
                new CampaignActivityRow(world.Spring.PublicId, 15, 2, 10, 4, 2, 2, Removed: 1),
                new CampaignActivityRow(world.Autumn.PublicId, 1, 0, 1, 0, 0, 0, Removed: 0),
            ],
            activity.Campaigns);
        // Hal, worked on in both campaigns this week, is in both rows and once in the total.
        Assert.Equal(new CampaignActivityTotal(15, 2, 10, 4, 2, 2, Removed: 1), activity.Total);
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
