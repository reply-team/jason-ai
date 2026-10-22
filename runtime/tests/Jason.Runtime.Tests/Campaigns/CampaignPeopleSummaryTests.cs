using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignPeopleSummaryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Waiting_counts_a_pending_approval_and_a_question_that_references_their_work()
    {
        Assert.Equal(["Ada", "Ben", "Pia"], await CountedAsync(CampaignSummaryFilter.Waiting));
    }

    [Fact]
    public async Task Failed_counts_the_latest_finished_failure_and_never_a_rejection_or_a_cancel()
    {
        Assert.Equal(["Cleo", "Eve", "Pia"], await CountedAsync(CampaignSummaryFilter.Failed));
    }

    [Fact]
    public async Task Working_leaves_awaiting_approval_to_Waiting()
    {
        Assert.Equal(["Dan", "Gus"], await CountedAsync(CampaignSummaryFilter.Working));
    }

    [Fact]
    public async Task New_is_a_person_with_no_work_at_all()
    {
        Assert.Equal(["Hal", "Quinn"], await CountedAsync(CampaignSummaryFilter.New));
    }

    [Fact]
    public async Task Idle_had_work_nothing_open_not_failed_never_enrolled()
    {
        Assert.Equal(["Ben", "Finn", "Tess", "Uma", "Vic", "Wes"], await CountedAsync(CampaignSummaryFilter.Idle));
    }

    [Fact]
    public async Task Sequence_is_a_succeeded_enroll_not_out()
    {
        Assert.Equal(["Ivy", "Ned"], await CountedAsync(CampaignSummaryFilter.Sequence));
    }

    [Fact]
    public async Task Out_is_a_reply_a_suppression_a_stop_or_a_hard_bounce()
    {
        Assert.Equal(["Jon", "Kit", "Lou", "Max", "Rae", "Sam"], await CountedAsync(CampaignSummaryFilter.Out));
    }

    [Fact]
    public async Task Removed_is_everyone_taken_out_of_the_campaign()
    {
        Assert.Equal(["Oli"], await CountedAsync(CampaignSummaryFilter.Removed));
    }

    [Fact]
    public async Task A_removed_person_counts_in_nothing_else_on_the_Summary_side()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var members = db.CampaignContacts.Where(membership => membership.CampaignId == world.Spring.Id && membership.State != MembershipState.Excluded);

        var people = await NamesAsync(db, members);
        Assert.Equal(22, people.Length);
        Assert.DoesNotContain("Oli", people);
        foreach (var filter in Enum.GetValues<CampaignSummaryFilter>().Where(filter => filter != CampaignSummaryFilter.Removed))
        {
            Assert.DoesNotContain("Oli", await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], filter)));
        }
    }

    [Fact]
    public async Task A_person_is_counted_by_the_work_of_the_campaign_asked_about()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);

        Assert.Equal(["Ada"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Autumn.Id], CampaignSummaryFilter.New)));
        Assert.Equal(["Hal"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Autumn.Id], CampaignSummaryFilter.Idle)));
    }

    private static async Task<string[]> CountedAsync(CampaignSummaryFilter filter)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        return await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], filter));
    }

    internal static async Task<string[]> NamesAsync(JasonDbContext db, IQueryable<CampaignContact> memberships)
    {
        var names = await memberships
            .Join(db.Contacts, membership => membership.ContactId, contact => contact.Id, (_, contact) => contact.FirstName!)
            .ToListAsync(Ct);
        return [.. names.Order(StringComparer.Ordinal)];
    }
}
