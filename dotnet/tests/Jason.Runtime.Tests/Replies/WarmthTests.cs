using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Harness;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// Whether a contact is cold or warm in a campaign, or waits on a verdict, read from the campaign's own observed lines,
/// its standing verdicts and the responders the runtime created: warm only by a standing <c>person</c> verdict that names
/// the contact on a thread the campaign's own read saw; awaiting while a reply of theirs is unjudged; never by a clock.
/// </summary>
public class WarmthTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JournalWriter Writer = new(new FixedClock(new DateTimeOffset(Noon)));

    [Fact]
    public async Task A_contact_with_nothing_said_is_cold()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    [Fact]
    public async Task An_own_reply_nobody_judged_waits_and_a_person_verdict_on_it_makes_the_contact_warm()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");

        var waiting = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.AwaitingJudgement, waiting);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>An automatic reply, a bounce and an opt-out are judged, so nothing waits, and none is the contact answering.</summary>
    [Theory]
    [InlineData(ReplyNature.Automatic)]
    [InlineData(ReplyNature.Bounce)]
    [InlineData(ReplyNature.OptOut)]
    public async Task A_reply_that_is_not_a_persons_answer_is_judged_and_leaves_the_contact_cold(ReplyNature nature)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", nature);

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A verdict that says no nature — an old skill's, or any line before the runtime took one — judges the reply without
    /// saying what it is: the contact is never cold for it, and waits on the nature rather than on a judgement.
    /// </summary>
    [Fact]
    public async Task A_verdict_without_a_nature_leaves_the_contact_awaiting_its_nature_never_cold()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", nature: null);

        Assert.Equal(WarmthState.AwaitingNature, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A reply nobody judged outranks one judged without a nature: that a reply waits to be judged is the true reason.</summary>
    [Fact]
    public async Task A_reply_waiting_for_a_judgement_outranks_one_waiting_for_its_nature()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", nature: null);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-2", "2026-09-14T11:40:00.000Z");

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>Warmth is read from the verdict that stands: a person's answer corrected to an automatic reply warms nobody.</summary>
    [Fact]
    public async Task A_person_verdict_corrected_to_automatic_warms_nobody()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Automatic);

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A line that reached the campaign only through the contact's pin parks nobody, and a verdict on its thread warms
    /// nobody: the campaign holds the thread only through a person it may share.
    /// </summary>
    [Fact]
    public async Task A_thread_heard_of_only_through_the_contacts_pin_neither_waits_nor_warms()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z", attributedBy: "contact_pin");

        var heard = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.Cold, heard);
        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A provider event said by the campaign's own pin is the campaign's own read, as the inbound read's line is.</summary>
    [Fact]
    public async Task A_line_said_by_the_campaigns_own_pin_waits_and_warms_as_the_inbound_reads_does()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z", attributedBy: "campaign_pin");

        var waiting = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.AwaitingJudgement, waiting);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// An own line that names nobody parks nobody, since nothing ties it to a person; a person verdict that names the
    /// contact on that thread still warms them, for the reply reached the campaign by its own read.
    /// </summary>
    [Fact]
    public async Task An_own_line_naming_nobody_parks_nobody_and_a_verdict_naming_the_contact_on_it_warms()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, contact: null, "thr-1", "2026-09-14T11:30:00.000Z");

        var unnamed = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.Cold, unnamed);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A reply in another campaign never makes the contact warm in this one, nor makes them wait here.</summary>
    [Fact]
    public async Task A_reply_in_another_campaign_leaves_this_one_cold()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var other = await CampaignAsync(db, "Autumn");
        await ObservedAsync(db, other, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, other, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, other, seed.Contact, "thr-2", "2026-09-14T11:40:00.000Z");

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, other.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A warm contact's new reply waits before anything else is sent, since it may ask to stop; judged, they are warm
    /// again. Warmth does not decay: nothing in between reads a moment.
    /// </summary>
    [Fact]
    public async Task A_warm_contacts_new_reply_waits_and_its_verdict_makes_them_warm_again()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        var warm = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2027-09-14T11:30:00.000Z");
        var waiting = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-2", "2027-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.Warm, warm);
        Assert.Equal(WarmthState.AwaitingJudgement, waiting);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A verdict recorded before the read said the move judges it when it is about a message at or after the move.</summary>
    [Fact]
    public async Task A_verdict_recorded_before_its_line_judges_it_when_it_is_about_the_move_or_later()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var judged = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:31:00.000Z");

        Assert.Equal(WarmthState.Warm, judged);
        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A verdict whose body names nobody warms nobody, though the journal's contact column holds the responder's person:
    /// warm needs the verdict to name the contact itself. It still judges the reply, so nothing waits.
    /// </summary>
    [Fact]
    public async Task A_verdict_naming_nobody_warms_nobody_whatever_the_contact_column_holds()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var (_, attempt) = await ResponderAsync(db, seed, "thr-1");

        var entry = await new ReplyService(db, Writer).RecordAsync(
            Request(seed.Campaign, contact: null, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person)
                with { Actor = new ActorRef(ActorType.Attempt, attempt.PublicId) },
            Ct);

        var stored = db.Journal.Single(row => row.PublicId == entry.Id);
        Assert.Equal(seed.Contact.Id, stored.ContactId);
        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A verdict on a thread the campaign's own read never saw warms nobody, a person's verdict by hand included.</summary>
    [Fact]
    public async Task A_verdict_with_no_own_line_for_its_thread_warms_nobody()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>Channels are one bucket: a reply on a LinkedIn thread makes the contact warm for the campaign, not for a channel.</summary>
    [Fact]
    public async Task A_reply_on_one_channel_warms_the_contact_on_every_channel()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z", channel: "linkedin");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A provider event's <c>reply_detected</c> line is not a reply that waits: no responder ever wakes on it.</summary>
    [Fact]
    public async Task A_provider_events_reply_detected_line_does_not_make_the_contact_wait()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        Writer.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.ProviderEventObserved,
            seed.Campaign,
            key: "sha256:" + new string('e', 64),
            updated: new JsonObject
            {
                ["kind"] = "reply_detected",
                ["plugin"] = "mailbox",
                ["event_id"] = "evt_01K52JR0000000000000000001",
                ["occurred_at"] = "2026-09-14T11:30:00.000Z",
                ["channel"] = "email",
                ["contact_id"] = seed.Contact.PublicId,
                ["attributed_by"] = "campaign_pin",
                ["thread"] = "thr-1",
            },
            contact: seed.Contact);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// Our own send into a replied thread moves it, and the read says the move as a reply. The responder the runtime
    /// creates for that line starts after it, finds the verdict standing and records nothing; when it has succeeded the
    /// contact is warm again. Until then they wait.
    /// </summary>
    [Fact]
    public async Task Our_own_move_waits_until_a_responder_started_after_it_succeeds_with_a_verdict_standing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        var moved = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        var (item, attempt) = await ResponderAsync(db, seed, "thr-1");
        var running = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, moved);
        Assert.Equal(WarmthState.AwaitingJudgement, running);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The responder's arm is exact and fails closed: a responder that failed, one a person created, or one of another
    /// thread judges nothing, and the contact keeps waiting.
    /// </summary>
    [Theory]
    [InlineData("failed")]
    [InlineData("created by a person")]
    [InlineData("of another thread")]
    public async Task A_responder_that_did_not_succeed_on_the_thread_as_the_runtimes_own_judges_nothing(string which)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var (item, attempt) = await ResponderAsync(db, seed, which == "of another thread" ? "thr-9" : "thr-1", byPerson: which == "created by a person");
        await FinishAsync(db, item, attempt, succeeded: which != "failed");

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The responder's arm needs a verdict with a nature to stand on the thread: where the one standing says none, the
    /// contact waits on the nature.
    /// </summary>
    [Fact]
    public async Task Our_own_move_with_only_a_verdict_without_a_nature_standing_awaits_the_nature()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", nature: null);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        var (item, attempt) = await ResponderAsync(db, seed, "thr-1");
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingNature, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A move patched onto a responder that is already open, whose attempt started before the line: the responder's arm
    /// does not judge it, because its attempt did not start after the line. What clears the line is a verdict with a
    /// nature recorded on the thread after it — the open responder's own verdict when it records after the patch, or any
    /// later one, a person's by hand included. Where the responder recorded its verdict before the patch and nothing
    /// records after, nothing clears it: the contact stays awaiting and a send to them parks for the person.
    /// </summary>
    [Fact]
    public async Task A_move_patched_onto_a_running_responder_after_its_verdict_stays_awaiting_until_a_later_verdict()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var (item, attempt) = await ResponderAsync(db, seed, "thr-1");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        await FinishAsync(db, item, attempt, succeeded: true);
        var patched = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-2", "2026-09-14T12:05:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.AwaitingJudgement, patched);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>The same patched move is judged by the open responder's own verdict when it records after the patch.</summary>
    [Fact]
    public async Task A_move_patched_onto_a_running_responder_is_judged_by_its_verdict_recorded_after_the_patch()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var (item, attempt) = await ResponderAsync(db, seed, "thr-1");
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    private sealed record Seed(Campaign Campaign, Contact Contact);

    private static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var campaign = await CampaignAsync(db, "Spring");
        var contact = new Contact { PublicId = "cnt_01K52JR0000000000000000001", FirstName = "Marta", LastName = "Example", CreatedAt = Noon, UpdatedAt = Noon };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact);
    }

    private static async Task<Campaign> CampaignAsync(JasonDbContext db, string name)
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
    private static async Task ObservedAsync(
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

    private static ReplyRecordRequest Request(Campaign campaign, Contact? contact, string thread, string message, string at, ReplyNature? nature) => new(
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

    private static Task<JournalEntryDto> VerdictAsync(
        JasonDbContext db, Campaign campaign, Contact? contact, string thread, string message, string at, ReplyNature? nature) =>
        new ReplyService(db, Writer).RecordAsync(Request(campaign, contact, thread, message, at, nature), Ct);

    /// <summary>
    /// A responder for the thread as the respond step creates one — the system's, about the contact, carrying the thread in
    /// its <c>inbound_reply</c> — whose attempt starts now: its <c>workitem_processing</c> line is written here.
    /// </summary>
    private static async Task<(WorkItem Item, Attempt Attempt)> ResponderAsync(JasonDbContext db, Seed seed, string thread, bool byPerson = false)
    {
        var item = WorkItemFactory.NewAiRole(seed.Campaign, role: Responder.Role, now: Noon, configure: item =>
        {
            item.CreatedByType = byPerson ? ActorType.Human : ActorType.System;
            item.Contact = seed.Contact;
            item.Status = WorkItemStatus.Processing;
            item.AttemptCount = 1;
            item.Context = new JsonObject
            {
                ["trigger"] = JournalKinds.InboundReplyObserved,
                [Responder.InboundReplyKey] = new JsonObject { ["plugin"] = "mailbox", ["thread"] = thread },
            };
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        Writer.Append(db, Actors.Dispatcher, JournalKinds.WorkItemProcessing, campaign: null, key: "attempt", updated: JsonValue.Create(1), workItem: item, attempt: attempt);
        await db.SaveChangesAsync(Ct);
        return (item, attempt);
    }

    private static async Task FinishAsync(JasonDbContext db, WorkItem item, Attempt attempt, bool succeeded)
    {
        item.Status = succeeded ? WorkItemStatus.Succeeded : WorkItemStatus.Failed;
        item.FinishedAt = Noon;
        attempt.Status = succeeded ? AttemptStatus.Succeeded : AttemptStatus.Failed;
        attempt.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
    }
}
