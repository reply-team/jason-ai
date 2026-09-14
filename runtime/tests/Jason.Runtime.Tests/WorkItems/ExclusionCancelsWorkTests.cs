using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// Taking somebody out of a campaign means "stop touching this person here"; these are the three ways that is
/// said and the one routine that carries it out.
/// </summary>
public class ExclusionCancelsWorkTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_a_member_cancels_that_members_work_and_nobody_elses()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = WorkItemFactory.NewCampaign();
        var ada = NewContact(db, campaign);
        var grace = NewContact(db, campaign);
        var forAda = WorkItemFactory.NewAiRole(campaign, configure: w => w.Contact = ada);
        var forGrace = WorkItemFactory.NewAiRole(campaign, configure: w => w.Contact = grace);
        var forNobody = WorkItemFactory.NewAiRole(campaign);
        db.WorkItems.AddRange(forAda, forGrace, forNobody);
        await db.SaveChangesAsync(Ct);

        var memberships = new MembershipService(db, new JournalWriter(clock), clock, NewCanceller(clock));
        await memberships.RemoveAsync(new RemoveContactsRequest(campaign.PublicId, [ada.PublicId], null, "taken out"), Ct);

        Assert.Equal(WorkItemStatus.Cancelled, await StatusOf(db, forAda, Ct));
        Assert.Equal(WorkItemStatus.Created, await StatusOf(db, forGrace, Ct));
        Assert.Equal(WorkItemStatus.Created, await StatusOf(db, forNobody, Ct));

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.WorkItemCancelled).ToListAsync(Ct));
        Assert.Equal(forAda.PublicId, entry.WorkItemId);
        Assert.Equal(campaign.Id, entry.CampaignId);
        Assert.Equal("contact removed from campaign", entry.Reason);
    }

    [Fact]
    public async Task Archiving_a_contact_cancels_its_work_in_every_campaign()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var first = WorkItemFactory.NewCampaign("First");
        var second = WorkItemFactory.NewCampaign("Second");
        var grace = NewContact(db, first);
        Enrol(db, second, grace);
        var inFirst = WorkItemFactory.NewAiRole(first, configure: w => w.Contact = grace);
        var inSecond = WorkItemFactory.NewAiRole(second, configure: w => w.Contact = grace);
        var untouched = WorkItemFactory.NewAiRole(first);
        db.WorkItems.AddRange(inFirst, inSecond, untouched);
        await db.SaveChangesAsync(Ct);

        var contacts = new ContactService(db, new JournalWriter(clock), clock, NewCanceller(clock));
        await contacts.ArchiveAsync(new ContactArchiveRequest(grace.PublicId, null, "asked to be forgotten"), Ct);

        Assert.Equal(WorkItemStatus.Cancelled, await StatusOf(db, inFirst, Ct));
        Assert.Equal(WorkItemStatus.Cancelled, await StatusOf(db, inSecond, Ct));
        Assert.Equal(WorkItemStatus.Created, await StatusOf(db, untouched, Ct));

        var entries = await db.Journal.Where(e => e.Kind == JournalKinds.WorkItemCancelled).ToListAsync(Ct);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal("contact archived", entry.Reason));
        Assert.Equal([first.Id, second.Id], entries.Select(e => e.CampaignId!.Value).Order().ToArray());
    }

    [Fact]
    public async Task Archiving_a_campaign_cancels_everything_still_open_in_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = WorkItemFactory.NewCampaign();
        var open = WorkItemFactory.NewAiRole(campaign);
        var claimed = WorkItemFactory.NewAiRole(campaign, configure: w => w.Status = WorkItemStatus.Scheduled);
        var done = WorkItemFactory.NewAiRole(campaign, configure: w =>
        {
            w.Status = WorkItemStatus.Succeeded;
            w.FinishedAt = Noon.UtcDateTime;
        });
        db.WorkItems.AddRange(open, claimed, done);
        await db.SaveChangesAsync(Ct);

        var campaigns = new CampaignService(db, new JournalWriter(clock), clock, NewCanceller(clock));
        await campaigns.ArchiveAsync(new CampaignTransitionRequest(campaign.PublicId, null, "finished"), Ct);

        Assert.Equal(WorkItemStatus.Cancelled, await StatusOf(db, open, Ct));
        Assert.Equal(WorkItemStatus.Cancelled, await StatusOf(db, claimed, Ct));
        Assert.Equal(WorkItemStatus.Succeeded, await StatusOf(db, done, Ct));

        var entries = await db.Journal.Where(e => e.Kind == JournalKinds.WorkItemCancelled).ToListAsync(Ct);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal("campaign archived", entry.Reason);
            Assert.Equal(campaign.Id, entry.CampaignId);
        });
    }

    private static WorkItemCanceller NewCanceller(TimeProvider clock) => TestCanceller.New(clock);

    private static async Task<WorkItemStatus> StatusOf(JasonDbContext db, WorkItem item, CancellationToken cancellationToken) =>
        (await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == item.PublicId, cancellationToken)).Status;

    private static Contact NewContact(JasonDbContext db, Campaign campaign)
    {
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = "Member",
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        };
        db.Contacts.Add(contact);
        Enrol(db, campaign, contact);
        return contact;
    }

    private static void Enrol(JasonDbContext db, Campaign campaign, Contact contact) =>
        db.CampaignContacts.Add(new CampaignContact
        {
            Campaign = campaign,
            Contact = contact,
            State = MembershipState.Enrolled,
            AddedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        });
}
