using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignPeopleActivityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task People_is_everyone_anything_happened_to_in_the_period()
    {
        Assert.Equal(
            ["Ada", "Ben", "Cleo", "Ivy", "Kit", "Lou", "Oli", "Pia", "Quinn", "Rae", "Tess", "Uma", "Vic", "Wes"],
            await TheWeekAsync(CampaignActivityFilter.People));
    }

    [Fact]
    public async Task Added_counts_a_first_add_and_a_re_add_in_the_period()
    {
        Assert.Equal(["Ada", "Quinn"], await TheWeekAsync(CampaignActivityFilter.Added));
    }

    [Fact]
    public async Task Worked_on_counts_finished_work_and_never_a_rejection()
    {
        Assert.Equal(
            ["Ben", "Cleo", "Ivy", "Lou", "Pia", "Tess", "Uma", "Vic", "Wes"],
            await TheWeekAsync(CampaignActivityFilter.WorkedOn));
    }

    [Fact]
    public async Task Contacted_counts_exactly_the_four_succeeded_act_operations()
    {
        Assert.Equal(["Ivy", "Tess", "Uma", "Vic"], await TheWeekAsync(CampaignActivityFilter.Contacted));
    }

    [Fact]
    public async Task Replied_counts_the_first_reply_only()
    {
        Assert.DoesNotContain("Jon", await TheWeekAsync(CampaignActivityFilter.Replied));
        Assert.Equal(["Jon"], await HappenedAsync(CampaignActivityFilter.Replied, Utc(2026, 10, 5), Utc(2026, 10, 6)));
    }

    [Fact]
    public async Task Replied_dates_a_reply_by_the_provider_moment()
    {
        Assert.Equal(["Oli", "Rae"], await TheWeekAsync(CampaignActivityFilter.Replied));
        Assert.Equal(["Sam"], await HappenedAsync(CampaignActivityFilter.Replied, Utc(2026, 3, 10), Utc(2026, 3, 11)));
    }

    [Fact]
    public async Task Opted_out_comes_from_the_journal_even_after_the_entry_is_lifted()
    {
        Assert.Equal(["Kit", "Uma"], await TheWeekAsync(CampaignActivityFilter.OptedOut));
    }

    [Fact]
    public async Task Removed_counts_a_person_taken_out_in_the_period_and_nobody_taken_out_before_it()
    {
        Assert.Equal(["Oli"], await TheWeekAsync(CampaignActivityFilter.Removed));
        Assert.Equal(["Quinn"], await HappenedAsync(CampaignActivityFilter.Removed, Utc(2026, 10, 1), Utc(2026, 10, 3)));
    }

    [Fact]
    public async Task The_period_includes_since_and_excludes_until()
    {
        var raeReplied = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
        var oliReplied = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Rae"], await HappenedAsync(CampaignActivityFilter.Replied, raeReplied, oliReplied));

        var thirteenth = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
        var fourteenth = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Ben", "Pia", "Uma", "Wes"], await HappenedAsync(CampaignActivityFilter.WorkedOn, thirteenth, fourteenth));

        var adaAdded = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Ada"], await HappenedAsync(CampaignActivityFilter.Added, adaAdded, fourteenth));
    }

    [Fact]
    public async Task A_person_removed_after_the_fact_still_counts_in_the_period()
    {
        Assert.Contains("Oli", await TheWeekAsync(CampaignActivityFilter.Replied));
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static Task<string[]> TheWeekAsync(CampaignActivityFilter filter) =>
        HappenedAsync(filter, CampaignWorld.Since, CampaignWorld.Until);

    private static async Task<string[]> HappenedAsync(CampaignActivityFilter filter, DateTime since, DateTime until)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        return await CampaignPeopleSummaryTests.NamesAsync(db, CampaignPeople.Happened(db, [world.Spring.Id], filter, since, until));
    }
}
