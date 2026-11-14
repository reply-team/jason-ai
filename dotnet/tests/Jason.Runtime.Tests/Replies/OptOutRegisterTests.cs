using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// A verdict whose nature is <c>opt_out</c> puts every channel value the contact has on the do-not-contact register, in the
/// same save as the verdict, by the runtime's reserved actor and with the verdict as the reason; nobody is asked. A value
/// already there gets no second entry, and nothing here ever lifts one.
/// </summary>
public class OptOutRegisterTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_opt_out_puts_every_value_of_the_contact_on_the_register_by_the_runtime_with_the_verdict_as_reason()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"), ("linkedin", "https://www.linkedin.com/in/marta-example"), ("phone", "+15555550100"));

        var verdict = await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut), Ct);

        await using var fresh = database.Open();
        var entries = await fresh.Suppressions.AsNoTracking().OrderBy(s => s.Channel).ToListAsync(Ct);
        Assert.Equal(
            ["email marta@example.test", "linkedin " + Stored("linkedin", "https://www.linkedin.com/in/marta-example"), "phone +15555550100"],
            entries.Select(s => $"{s.Channel} {s.Value}"));
        Assert.All(entries, entry => Assert.Contains(verdict.Id, entry.Reason, StringComparison.Ordinal));

        var lines = await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.SuppressionAdded).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line =>
        {
            Assert.Equal((Actors.OptOut.Type, Actors.OptOut.Id), (line.ActorType, line.ActorId));
            Assert.Contains(verdict.Id, line.Reason, StringComparison.Ordinal);
            Assert.Null(line.CampaignId);
        });
        Assert.True(lines.All(line => line.Id > fresh.Journal.Single(e => e.PublicId == verdict.Id).Id));
        Assert.Equal(ActorType.System, Actors.OptOut.Type);
    }

    [Fact]
    public async Task A_value_already_on_the_register_gets_no_second_entry_and_the_others_are_added()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"), ("phone", "+15555550100"));
        var clock = new FixedClock(new DateTimeOffset(Noon));
        await new SuppressionService(db, new JournalWriter(clock), clock).AddAsync(new SuppressionAddRequest("email", "marta@example.test", Ada, "said stop by phone"), Ct);

        await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut), Ct);

        await using var fresh = database.Open();
        var email = await fresh.Suppressions.AsNoTracking().SingleAsync(s => s.Channel == "email", Ct);
        Assert.Equal("said stop by phone", email.Reason);
        Assert.True(await fresh.Suppressions.AnyAsync(s => s.Channel == "phone" && s.Value == "+15555550100", Ct));
        var lines = await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.SuppressionAdded).ToListAsync(Ct);
        Assert.Equal(["human email", "system phone"], lines.Select(line => $"{line.ActorType.ToString().ToLowerInvariant()} {line.Key}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_contact_with_no_values_has_the_verdict_recorded_and_nothing_registered()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001");

        var verdict = await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut), Ct);

        await using var fresh = database.Open();
        Assert.True(await fresh.Journal.AnyAsync(e => e.PublicId == verdict.Id, Ct));
        Assert.Empty(await fresh.Suppressions.AsNoTracking().ToListAsync(Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct));
    }

    [Theory]
    [InlineData(ReplyNature.Person)]
    [InlineData(ReplyNature.Automatic)]
    [InlineData(ReplyNature.Bounce)]
    [InlineData(null)]
    public async Task Any_other_nature_registers_nothing(ReplyNature? nature)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"));

        await Service(db).RecordAsync(Request(campaign, contact, nature), Ct);

        await using var fresh = database.Open();
        Assert.Empty(await fresh.Suppressions.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A guess below any threshold is still a verdict that says opt_out, and stops the person the same way: the runtime
    /// reads the nature, never the confidence.
    /// </summary>
    [Fact]
    public async Task A_low_confidence_guess_of_an_opt_out_stops_the_person_too()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"));

        await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut) with { Confidence = 0.2, Reaction = ReplyReaction.Escalate }, Ct);

        await using var fresh = database.Open();
        Assert.Equal("marta@example.test", (await fresh.Suppressions.AsNoTracking().SingleAsync(Ct)).Value);
    }

    [Fact]
    public async Task A_second_opt_out_registers_only_the_values_gained_since()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"));
        await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut), Ct);
        contact.Channels.Add(new ContactChannel { Channel = "phone", Value = "+15555550100" });
        await db.SaveChangesAsync(Ct);

        await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut) with { MessageId = "msg-78" }, Ct);

        await using var fresh = database.Open();
        Assert.Equal(["email", "phone"], (await fresh.Suppressions.AsNoTracking().Select(s => s.Channel).ToListAsync(Ct)).Order(StringComparer.Ordinal));
        Assert.Equal(2, await fresh.Journal.CountAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct));
    }

    /// <summary>
    /// A later verdict on the same reply that says the reply was a person's answer after all, by a person, leaves the
    /// register as it is: the runtime never lifts an entry it made.
    /// </summary>
    [Fact]
    public async Task A_later_verdict_by_a_person_leaves_the_entries_in_place()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var contact = await ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"));
        await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.OptOut), Ct);

        await Service(db).RecordAsync(Request(campaign, contact, ReplyNature.Person), Ct);

        await using var fresh = database.Open();
        Assert.Single(await fresh.Suppressions.AsNoTracking().ToListAsync(Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == JournalKinds.SuppressionRemoved, Ct));
    }

    /// <summary>
    /// The journal's contact column of a verdict is the contact its body names, whatever person the run that records it is
    /// about: a responder's run about one person that records an opt-out naming another stops the one named, and its line
    /// is that person's. The run's own person is left as they were.
    /// </summary>
    [Fact]
    public async Task An_opt_out_recorded_in_a_run_about_one_person_naming_another_stops_the_one_named()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await CampaignAsync(db);
        var aboutA = await ContactAsync(db, "cnt_01K52JR000000000000000000A", ("email", "anna@example.test"));
        var namedB = await ContactAsync(db, "cnt_01K52JR000000000000000000B", ("email", "boris@example.test"), ("phone", "+15555550111"));
        var (_, attempt) = await RunAsync(db, campaign, aboutA);

        var verdict = await Service(db).RecordAsync(
            Request(campaign, namedB, ReplyNature.OptOut) with { Actor = new ActorRef(ActorType.Attempt, attempt.PublicId) },
            Ct);

        await using var fresh = database.Open();
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.PublicId == verdict.Id, Ct);
        Assert.Equal(namedB.Id, line.ContactId);
        Assert.Equal(namedB.PublicId, (string?)line.New!["contact_id"]);
        Assert.Equal(
            ["email boris@example.test", "phone +15555550111"],
            (await fresh.Suppressions.AsNoTracking().ToListAsync(Ct)).Select(s => $"{s.Channel} {s.Value}").Order(StringComparer.Ordinal));
    }

    internal static ReplyService Service(JasonDbContext db) => new(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon))));

    internal static ReplyRecordRequest Request(Campaign campaign, Contact? contact, ReplyNature? nature) => new(
        campaign.PublicId,
        "mailbox",
        "thr-1",
        "msg-77",
        new DateTimeOffset(2026, 9, 14, 11, 30, 0, TimeSpan.Zero),
        nature == ReplyNature.OptOut ? "unsubscribe" : "interested",
        0.9,
        ReplyReaction.None,
        ContactId: contact?.PublicId,
        Actor: Ada,
        Nature: nature);

    internal static async Task<Campaign> CampaignAsync(JasonDbContext db, string name = "Spring")
    {
        var campaign = WorkItemFactory.NewCampaign(name, now: Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign;
    }

    /// <summary>A contact with the values given, each stored as the channel's rule keys it, the first one primary.</summary>
    internal static async Task<Contact> ContactAsync(JasonDbContext db, string publicId, params (string Channel, string Value)[] channels)
    {
        var contact = new Contact { PublicId = publicId, FirstName = "Marta", LastName = "Example", CreatedAt = Noon, UpdatedAt = Noon };
        foreach (var (channel, value) in channels)
        {
            contact.Channels.Add(new ContactChannel { Channel = channel, Value = Stored(channel, value), IsPrimary = !contact.Channels.Any(c => c.Channel == channel) });
        }

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        return contact;
    }

    /// <summary>A responder's run in the campaign about <paramref name="contact"/>, started now.</summary>
    internal static async Task<(WorkItem Item, Attempt Attempt)> RunAsync(JasonDbContext db, Campaign campaign, Contact contact)
    {
        var item = WorkItemFactory.NewAiRole(campaign, role: "responder", now: Noon);
        item.Contact = contact;
        item.Status = WorkItemStatus.Processing;
        item.AttemptCount = 1;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item, attempt);
    }

    internal static string Stored(string channel, string value)
    {
        var errors = new ValidationErrors();
        return ChannelValidation.Value(channel, value, "value", errors) ?? throw new InvalidOperationException($"{channel} {value} is no value.");
    }
}
