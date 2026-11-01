using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Reports;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// A campaign with one member and one piece of managed work, so a report has something real to name — and the
/// service that admits reports into it, on a clock that does not move.
/// </summary>
internal static class ReportedWorld
{
    public static readonly DateTime Noon = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    public static ReportService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new ReportService(db, new JournalWriter(clock), clock);
    }

    public static async Task<(Campaign Campaign, Contact Contact, WorkItem Item)> WriteAsync(JasonDbContext db, CancellationToken ct)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);

        var contact = NewContact(db);
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        db.WorkItems.Add(item);

        await db.SaveChangesAsync(ct);

        db.CampaignContacts.Add(new CampaignContact
        {
            CampaignId = campaign.Id,
            ContactId = contact.Id,
            State = MembershipState.Enrolled,
            AddedAt = Noon,
            UpdatedAt = Noon,
        });

        item.ContactId = contact.Id;
        await db.SaveChangesAsync(ct);
        return (campaign, contact, item);
    }

    /// <summary>A contact who belongs to no campaign, for the case a report reaches somebody the local campaign
    /// has never heard of.</summary>
    public static Contact NewContact(JasonDbContext db)
    {
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = "Someone",
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };

        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"{Guid.NewGuid():N}@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        return contact;
    }

    /// <summary>The smallest submission this API accepts, as a document rather than as a typed request.</summary>
    public static JsonObject Submission(ActorRef? reporter = null) => new()
    {
        ["effect"] = "email_sent",
        ["tool"] = "some-other-cli 1.2.3",
        ["summary"] = "A follow-up was sent by hand while the runtime was not involved.",
        ["actor"] = Actor(reporter ?? new ActorRef(ActorType.Human, "person-1")),
    };

    public static JsonObject Actor(ActorRef actor) => new()
    {
        ["type"] = actor.Type.ToString().ToLowerInvariant(),
        ["id"] = actor.Id,
    };
}
