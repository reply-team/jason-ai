using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// One audience's people, as the people reads count and list them: every definition the campaign's counts use, over the
/// memberships a search landed under that audience and nobody else's. Audience 1 landed six of Spring's people, among them
/// one removed since; audience 2 landed two others; audience 3 landed nobody.
/// </summary>
public class CampaignPeopleAudienceTests
{
    private static readonly string[] First = ["Ada", "Ben", "Cleo", "Jon", "Oli", "Quinn"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_summary_count_of_an_audience_is_the_campaign_s_count_among_the_people_it_landed()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (world, first, _) = await AudiencesAsync(db);
        int[] spring = [world.Spring.Id];

        foreach (var filter in Enum.GetValues<CampaignSummaryFilter>())
        {
            var whole = await CampaignPeopleSummaryTests.NamesAsync(db, CampaignPeople.Matching(db, spring, filter));
            Assert.Equal(whole.Intersect(First), await CampaignPeopleSummaryTests.NamesAsync(db, CampaignPeople.Matching(db, spring, filter, first.Id)));
        }

        Assert.Equal(
            new SummaryCounts(world.Spring.Id, People: 5, Waiting: 2, Failed: 1, Working: 0, New: 1, Idle: 1, Sequence: 0, Out: 1, Removed: 1),
            Assert.Single(await CampaignPeople.SummaryCountsAsync(db, spring, first.Id, withoutArchived: false, Ct)).Value);
        Assert.Equal(
            new SummaryCounts(0, People: 5, Waiting: 2, Failed: 1, Working: 0, New: 1, Idle: 1, Sequence: 0, Out: 1, Removed: 1),
            await CampaignPeople.SummaryTotalAsync(db, spring, first.Id, withoutArchived: false, Ct));
    }

    [Fact]
    public async Task Each_activity_count_of_an_audience_is_the_campaign_s_count_among_the_people_it_landed()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (world, first, _) = await AudiencesAsync(db);
        int[] spring = [world.Spring.Id];
        var landed = First.Select(name => world.People[name].Id).ToHashSet();

        foreach (var count in Enum.GetValues<CampaignActivityFilter>())
        {
            var whole = await CampaignPeople.ByMoment(db, spring, count, CampaignWorld.Since, CampaignWorld.Until).ToListAsync(Ct);
            var theirs = await CampaignPeople.ByMoment(db, spring, count, CampaignWorld.Since, CampaignWorld.Until, first.Id).ToListAsync(Ct);
            Assert.Equal(whole.Where(person => landed.Contains(person.ContactId)).OrderBy(person => person.ContactId), theirs.OrderBy(person => person.ContactId));

            var moments = await CampaignPeople.MomentsAsync(db, spring, count, CampaignWorld.Since, CampaignWorld.Until, null, Ct);
            var theirMoments = await CampaignPeople.MomentsAsync(db, spring, count, CampaignWorld.Since, CampaignWorld.Until, first.Id, Ct);
            Assert.Equal(
                moments.Where(moment => landed.Contains(moment.ContactId)).OrderBy(moment => moment.ContactId).ThenBy(moment => moment.Ms),
                theirMoments.OrderBy(moment => moment.ContactId).ThenBy(moment => moment.Ms));
        }

        Assert.Equal(
            new ActivityCounts(world.Spring.Id, People: 5, Added: 2, WorkedOn: 2, Contacted: 0, Replied: 1, OptedOut: 0, Removed: 1),
            Assert.Single(await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, first.Id, Ct)).Value);
        Assert.Equal(
            new ActivityCounts(0, People: 5, Added: 2, WorkedOn: 2, Contacted: 0, Replied: 1, OptedOut: 0, Removed: 1),
            await CampaignPeople.ActivityTotalAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, first.Id, Ct));
    }

    /// <summary>An audience nobody landed under counts nobody, in every count, and lists nobody.</summary>
    [Fact]
    public async Task An_audience_nobody_landed_under_counts_nobody()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (world, _, empty) = await AudiencesAsync(db);
        int[] spring = [world.Spring.Id];

        Assert.Empty(await CampaignPeople.SummaryCountsAsync(db, spring, empty.Id, withoutArchived: false, Ct));
        Assert.Equal(new SummaryCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), await CampaignPeople.SummaryTotalAsync(db, spring, empty.Id, withoutArchived: false, Ct));
        Assert.Empty(await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, empty.Id, Ct));
        Assert.Equal(new ActivityCounts(0, 0, 0, 0, 0, 0, 0, 0), await CampaignPeople.ActivityTotalAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, empty.Id, Ct));
        Assert.Empty(await CampaignPeople.MomentsAsync(db, spring, CampaignActivityFilter.People, CampaignWorld.Since, CampaignWorld.Until, empty.Id, Ct));
        foreach (var filter in Enum.GetValues<CampaignSummaryFilter>())
        {
            Assert.Empty(await CampaignPeople.Matching(db, spring, filter, empty.Id).ToListAsync(Ct));
        }
    }

    /// <summary>Spring with audience 1, audience 2 landing Rae and Tess, and audience 3 landing nobody.</summary>
    private static async Task<(CampaignWorld World, Audience First, Audience Empty)> AudiencesAsync(JasonDbContext db)
    {
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var first = await world.AudienceAsync(db, 1, Ct, First);
        await world.AudienceAsync(db, 2, Ct, "Rae", "Tess");
        var empty = await world.AudienceAsync(db, 3, Ct);
        return (world, first, empty);
    }
}
