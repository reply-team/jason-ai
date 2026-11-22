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

    /// <summary>
    /// New is a person nobody has started on: no work about them in this campaign that a person, a role, an attempt or a
    /// plugin created. Work the runtime made about them by itself, the read of a thread they replied on, is nobody
    /// starting on them; and the reserve's count is this number, read on its own.
    /// </summary>
    [Theory]
    [InlineData(ActorType.System, true)]
    [InlineData(ActorType.Human, false)]
    [InlineData(ActorType.Role, false)]
    [InlineData(ActorType.Attempt, false)]
    [InlineData(ActorType.Plugin, false)]
    public async Task New_is_a_person_nobody_has_started_on(ActorType madeBy, bool stillNew)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        Assert.Equal(["Hal", "Quinn"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], CampaignSummaryFilter.New)));

        var read = WorkItemFactory.NewProviderOp(world.Spring, "conversation.get", CampaignWorld.Now);
        read.Contact = world.People["Quinn"];
        read.CreatedByType = madeBy;
        read.Status = WorkItemStatus.Succeeded;
        read.FinishedAt = CampaignWorld.Now;
        db.WorkItems.Add(read);
        await db.SaveChangesAsync(Ct);

        string[] expected = stillNew ? ["Hal", "Quinn"] : ["Hal"];
        Assert.Equal(expected, await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], CampaignSummaryFilter.New)));
        Assert.Equal(expected.Length, await CampaignPeople.UntouchedAsync(db, world.Spring.Id, Ct));
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
    public async Task Paused_is_every_member_whose_line_is_held()
    {
        Assert.Equal(["Rae"], await CountedAsync(CampaignSummaryFilter.Paused));
    }

    [Fact]
    public async Task Finished_is_every_member_whose_line_is_ended()
    {
        Assert.Equal(["Jon"], await CountedAsync(CampaignSummaryFilter.Finished));
    }

    /// <summary>
    /// A person whose line is ended is nobody still to start on and nobody left idle: <c>new</c> and <c>idle</c> leave the
    /// finished out, so the reserve, which is <c>new</c>, no longer counts them. A held line is not ended, so a paused
    /// person stays where they stood. Each state is counted, and <c>people</c> and <c>out</c> are as they were.
    /// </summary>
    [Fact]
    public async Task New_and_idle_leave_the_finished_out_and_keep_the_paused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var before = (await CampaignPeople.SummaryCountsAsync(db, [world.Spring.Id], null, withoutArchived: false, Ct))[world.Spring.Id];
        await StateAsync(db, world, MembershipState.Finished, "Quinn", "Tess");
        await StateAsync(db, world, MembershipState.Paused, "Hal", "Uma");

        Assert.Equal(["Hal"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], CampaignSummaryFilter.New)));
        Assert.Equal(["Ben", "Finn", "Uma", "Vic", "Wes"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], CampaignSummaryFilter.Idle)));
        Assert.Equal(["Jon", "Quinn", "Tess"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], CampaignSummaryFilter.Finished)));
        Assert.Equal(["Hal", "Rae", "Uma"], await NamesAsync(db, CampaignPeople.Matching(db, [world.Spring.Id], CampaignSummaryFilter.Paused)));
        Assert.Equal(1, await CampaignPeople.UntouchedAsync(db, world.Spring.Id, Ct));

        var after = (await CampaignPeople.SummaryCountsAsync(db, [world.Spring.Id], null, withoutArchived: false, Ct))[world.Spring.Id];
        Assert.Equal(before with { New = 1, Idle = 5, Paused = 3, Finished = 3 }, after);
        var total = await CampaignPeople.SummaryTotalAsync(db, [world.Spring.Id], null, withoutArchived: false, Ct);
        Assert.Equal(
            (after.People, after.Out, after.New, after.Idle, after.Paused, after.Finished),
            (total.People, total.Out, total.New, total.Idle, total.Paused, total.Finished));

        // Where each of them stands says the same, one bit per count.
        var standing = await CampaignPeople.StandingAsync(db, [world.Spring.Id], [.. new[] { "Hal", "Tess", "Jon" }.Select(name => world.People[name].Id)], Ct);
        Assert.Equal([CampaignSummaryFilter.New, CampaignSummaryFilter.Paused], standing[(world.People["Hal"].Id, world.Spring.Id)]);
        Assert.Equal([CampaignSummaryFilter.Finished], standing[(world.People["Tess"].Id, world.Spring.Id)]);
        Assert.Equal([CampaignSummaryFilter.Out, CampaignSummaryFilter.Finished], standing[(world.People["Jon"].Id, world.Spring.Id)]);
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

    /// <summary>The Spring memberships of these people put in <paramref name="state"/>, as the verbs that move a line leave them.</summary>
    private static async Task StateAsync(JasonDbContext db, CampaignWorld world, MembershipState state, params string[] names)
    {
        var people = names.Select(name => world.People[name].Id).ToList();
        await db.CampaignContacts
            .Where(membership => membership.CampaignId == world.Spring.Id && people.Contains(membership.ContactId))
            .ExecuteUpdateAsync(set => set.SetProperty(membership => membership.State, state), Ct);
    }

    internal static async Task<string[]> NamesAsync(JasonDbContext db, IQueryable<CampaignContact> memberships)
    {
        var names = await memberships
            .Join(db.Contacts, membership => membership.ContactId, contact => contact.Id, (_, contact) => contact.FirstName!)
            .ToListAsync(Ct);
        return [.. names.Order(StringComparer.Ordinal)];
    }
}
