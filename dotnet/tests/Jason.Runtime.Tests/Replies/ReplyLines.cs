using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// The lines a reply leaves in a campaign, written as their writers write them: a campaign and a contact to hold them, a
/// move of a thread as the inbound read says it, and a verdict recorded through <see cref="ReplyService"/>. Shared by the
/// tests that read warmth and the claim's tests that read it beside the register and the opt-out lock.
/// </summary>
internal static class ReplyLines
{
    internal static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    internal static readonly ActorRef Ada = new(ActorType.Human, "ada");

    internal static readonly JournalWriter Writer = new(new FixedClock(new DateTimeOffset(Noon)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal sealed record Seed(Campaign Campaign, Contact Contact);

    internal static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var campaign = await CampaignAsync(db, "Spring");
        var contact = new Contact { PublicId = "cnt_01K52JR0000000000000000001", FirstName = "Marta", LastName = "Example", CreatedAt = Noon, UpdatedAt = Noon };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact);
    }

    internal static async Task<Campaign> CampaignAsync(JasonDbContext db, string name)
    {
        var campaign = WorkItemFactory.NewCampaign(name, now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign;
    }

    /// <summary>
    /// A move of a thread as the inbound read says it, or, with a rule named, as the events store says it; naming the
    /// contact both in its body and in its column, as both writers do, or nobody.
    /// </summary>
    internal static async Task ObservedAsync(
        JasonDbContext db, Campaign campaign, Contact? contact, string thread, string moment, string? attributedBy = null, string channel = "email")
    {
        var line = new JsonObject
        {
            ["plugin"] = "mailbox",
            ["thread"] = thread,
            ["channel"] = channel,
            ["last_activity_at"] = moment,
            ["unread"] = true,
            ["has_meeting_intent"] = null,
            ["contact_id"] = contact?.PublicId,
        };
        if (attributedBy is not null)
        {
            line["event_id"] = "evt_01K52JR0000000000000000002";
            line["attributed_by"] = attributedBy;
        }

        Writer.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.InboundReplyObserved,
            campaign,
            key: InboundSync.ObservationKey("mailbox", thread, moment),
            updated: line,
            contactId: contact?.Id);
        await db.SaveChangesAsync(Ct);
    }

    internal static ReplyRecordRequest Request(Campaign campaign, Contact? contact, string thread, string message, string at, ReplyNature? nature) => new(
        campaign.PublicId,
        "mailbox",
        thread,
        message,
        DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture),
        "interested",
        0.9,
        ReplyReaction.Draft,
        ContactId: contact?.PublicId,
        Actor: Ada,
        Nature: nature);

    internal static Task<JournalEntryDto> VerdictAsync(
        JasonDbContext db, Campaign campaign, Contact? contact, string thread, string message, string at, ReplyNature? nature) =>
        new ReplyService(db, Writer).RecordAsync(Request(campaign, contact, thread, message, at, nature), Ct);
}
