using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Approvals;
using Jason.Runtime.Tests.Harness;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

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
        var (_, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");

        var entry = await new ReplyService(db, Writer).RecordAsync(
            Request(seed.Campaign, contact: null, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person)
                with { Actor = new ActorRef(ActorType.Attempt, attempt.PublicId) },
            Ct);

        var stored = db.Journal.Single(row => row.PublicId == entry.Id);
        Assert.Equal(seed.Contact.Id, stored.ContactId);
        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A verdict whose journal column holds one person and whose body names another warms neither: warm needs the body to
    /// name this very contact, and the line to be among the contact's own.
    /// </summary>
    [Fact]
    public async Task A_verdict_whose_column_names_one_contact_and_whose_body_names_another_warms_nobody()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var other = new Contact { PublicId = "cnt_01K52JR0000000000000000008", FirstName = "Olga", LastName = "Example", CreatedAt = Noon, UpdatedAt = Noon };
        db.Contacts.Add(other);
        await db.SaveChangesAsync(Ct);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");

        Writer.Append(
            db,
            Ada,
            JournalKinds.ReplyClassified,
            seed.Campaign,
            key: InboundSync.VerdictKey("mailbox", "thr-1", "msg-1"),
            updated: new JsonObject
            {
                ["plugin"] = "mailbox",
                ["thread"] = "thr-1",
                ["message_id"] = "msg-1",
                ["message_at"] = "2026-09-14T11:30:00.000Z",
                ["contact_id"] = other.PublicId,
                ["category"] = "interested",
                ["confidence"] = 0.9,
                ["reaction"] = "draft",
                ["goal_id"] = null,
                ["goal_kind"] = null,
                ["decision_id"] = null,
                ["answered_by"] = null,
                ["nature"] = "person",
            },
            contactId: seed.Contact.Id);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, other.Id, Ct));
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
    /// creates for that line, with a read of its own made after it, finds the verdict standing and records nothing; when
    /// it has succeeded the contact is warm again. Until then they wait.
    /// </summary>
    [Fact]
    public async Task Our_own_move_waits_until_a_responder_created_after_it_succeeds_with_a_verdict_standing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        var moved = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        var running = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, moved);
        Assert.Equal(WarmthState.AwaitingJudgement, running);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The line lands while a responder created for an earlier move of the thread is scheduled; the move is patched onto
    /// it, and it starts after the line and succeeds with a verdict standing. It was created before the line, so its read
    /// may have run before the message that moved the thread: the line is not judged by it, and the contact still waits.
    /// </summary>
    [Fact]
    public async Task A_line_patched_onto_a_scheduled_responder_that_starts_after_it_and_succeeds_leaves_the_contact_waiting()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        var scheduled = await CreatedResponderAsync(db, seed, seed.Contact, "thr-1");

        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        var attempt = await StartAsync(db, scheduled);
        await FinishAsync(db, scheduled, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A move patched onto a responder that already existed stays awaiting when that responder succeeds; the next
    /// responder the runtime creates on the thread, after a later move, read the whole thread, so its success clears the
    /// later line and the patched one at once.
    /// </summary>
    [Fact]
    public async Task A_responder_created_after_a_later_line_of_the_thread_clears_the_earlier_patched_line_too()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var (first, firstAttempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        await FinishAsync(db, first, firstAttempt, succeeded: true);
        var patched = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:10:00.000Z");
        var (next, nextAttempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        await FinishAsync(db, next, nextAttempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, patched);
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

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, which == "of another thread" ? "thr-9" : "thr-1", byPerson: which == "created by a person");
        await FinishAsync(db, item, attempt, succeeded: which != "failed");

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A responder the runtime created without a read of the thread (no route named the plugin that saw it, or the read
    /// refused the thread's identifier) has seen nothing of the thread, so its success judges no line: the contact waits.
    /// </summary>
    [Fact]
    public async Task A_responder_created_without_a_read_of_the_thread_judges_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1", withRead: false);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A responder whose read failed, was cancelled or expired was released without a transcript of the thread: it saw
    /// nothing, so its success judges no line, and the contact waits.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Expired)]
    public async Task A_responder_whose_read_did_not_succeed_judges_nothing(WorkItemStatus ended)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1", readEnds: ended);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The wait from creation is released at its ceiling too, with the read still open, so the responder may run before
    /// the read succeeded. One that started before the read's success saw no transcript: though the read succeeds later
    /// and the responder succeeds, the line is not judged by it, and the contact waits.
    /// </summary>
    [Fact]
    public async Task A_responder_that_started_while_its_read_was_still_open_judges_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1", readEnds: null);
        await EndReadAsync(db, await ReadOfAsync(db, item), WorkItemStatus.Succeeded);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The read a responder names must be a real <c>conversation.get</c> of the thread the system created in the campaign:
    /// an id no item holds, a read of another thread, a read of the thread at another plugin, a read a person created, a
    /// read in another campaign, a provider operation other than <c>conversation.get</c>, or an item of another kind whose
    /// operation column holds the read's name judges nothing.
    /// </summary>
    [Theory]
    [InlineData("no such item")]
    [InlineData("of another thread")]
    [InlineData("at another plugin")]
    [InlineData("created by a person")]
    [InlineData("in another campaign")]
    [InlineData("of another operation")]
    [InlineData("of another kind")]
    public async Task A_responder_naming_a_read_that_is_not_the_systems_read_of_the_thread_judges_nothing(string which)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var item = await CreatedResponderAsync(
            db,
            seed,
            seed.Contact,
            "thr-1",
            readEnds: which == "at another plugin" ? null : WorkItemStatus.Succeeded,
            readThread: which == "of another thread" ? "thr-9" : null,
            readId: which == "no such item" ? "wi_01K52JR0000000000000000077" : null);
        if (which == "at another plugin")
        {
            await EndReadAsync(db, await ReadOfAsync(db, item), WorkItemStatus.Succeeded, plugin: "other-mailbox");
        }

        if (which == "created by a person")
        {
            var read = await ReadOfAsync(db, item);
            read.CreatedByType = ActorType.Human;
            await db.SaveChangesAsync(Ct);
        }

        if (which == "in another campaign")
        {
            var read = await ReadOfAsync(db, item);
            read.Campaign = await CampaignAsync(db, "Autumn");
            await db.SaveChangesAsync(Ct);
        }

        if (which == "of another operation")
        {
            var read = await ReadOfAsync(db, item);
            read.Operation = InboundSync.Operation;
            await db.SaveChangesAsync(Ct);
        }

        if (which == "of another kind")
        {
            var read = await ReadOfAsync(db, item);
            read.Kind = WorkItemKind.AiRole;
            read.Role = Responder.Role;
            await db.SaveChangesAsync(Ct);
        }

        var attempt = await StartAsync(db, item);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A responder created after the line whose pointer names an older read of the same thread, one that succeeded before
    /// the line was written, judges nothing: that read cannot have seen the message that moved the thread.
    /// </summary>
    [Fact]
    public async Task A_responder_naming_an_older_read_of_the_thread_made_before_the_line_judges_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var older = await ThreadReadAsync(db, seed, seed.Contact, "thr-1");
        await EndReadAsync(db, older, WorkItemStatus.Succeeded);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1", readId: older.PublicId);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A read whose first attempt ran at the line's plugin and failed transiently, and whose second, claimed after a route
    /// reload, succeeded at another plugin: an attempt at the plugin that saw the thread ran, but none succeeded there, so
    /// the responder that waited on the read judges nothing, and the contact waits.
    /// </summary>
    [Fact]
    public async Task A_responder_whose_read_succeeded_only_at_another_plugin_after_failing_at_the_lines_judges_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var item = await CreatedResponderAsync(db, seed, seed.Contact, "thr-1", readEnds: null);
        var read = await ReadOfAsync(db, item);
        await RunReadAsync(db, read, "mailbox", "snp_01K5B7Q2WE5X3M9T0YH4C6RDNC", succeeded: false);
        await RunReadAsync(db, read, "other-mailbox", "snp_01K5B7Q2WE5X3M9T0YH4C6RDNE", succeeded: true);
        var attempt = await StartAsync(db, item);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal([1, 2], read.Attempts.OrderBy(a => a.Number).Select(a => a.Number));
        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// <c>workitem.update</c> may patch any key of an open item's context, the read the responder names, its plugin and
    /// its thread included, and the read's own input. A responder or a read whose context anyone but the system patched
    /// judges nothing, whatever the patch said, and the contact waits.
    /// </summary>
    [Theory]
    [InlineData("responder", "human")]
    [InlineData("responder", "role")]
    [InlineData("read", "human")]
    [InlineData("read", "role")]
    public async Task A_responder_or_a_read_whose_context_someone_else_patched_judges_nothing(string patched, string by)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        var actor = by == "human" ? Ada : new ActorRef(ActorType.Role, "manager");

        var item = await CreatedResponderAsync(db, seed, seed.Contact, "thr-1");
        var target = patched == "responder" ? item : await ReadOfAsync(db, item);
        var key = patched == "responder" ? Responder.InboundReplyKey : "input";
        await ContextPatchedAsync(db, target, actor, key, target.Context[key]!.DeepClone());
        var attempt = await StartAsync(db, item);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The dispatcher's own patch of a further move onto the responder is the system's, and disqualifies nothing: the
    /// responder created after the line with its read still clears it once both have succeeded in order.
    /// </summary>
    [Fact]
    public async Task A_responder_the_system_patched_with_a_further_move_still_judges_the_line()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        var item = await CreatedResponderAsync(db, seed, seed.Contact, "thr-1");
        var recorded = item.Context[Responder.InboundReplyKey]!.DeepClone().AsObject();
        recorded["moves"] = new JsonArray(new JsonObject { ["line"] = "jrn_01K52JR0000000000000000009", ["at"] = "2026-09-14T12:05:00.000Z" });
        recorded["move_count"] = 2;
        await ContextPatchedAsync(db, item, Actors.Dispatcher, Responder.InboundReplyKey, recorded);
        var attempt = await StartAsync(db, item);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
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
        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingNature, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// A move patched onto a responder that is already open: the responder's arm does not judge it, because the responder
    /// was not created after the line. What clears the line is a verdict with a
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
        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        await FinishAsync(db, item, attempt, succeeded: true);
        var patched = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-2", "2026-09-14T12:05:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.AwaitingJudgement, patched);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The open responder records its verdict about the earlier message after the patched move was written: the verdict
    /// is about a message before the move and the responder was created before it, so nothing judges the move and
    /// the contact stays awaiting. A journal id above the line's judges nothing.
    /// </summary>
    [Fact]
    public async Task A_running_responders_verdict_about_the_earlier_message_recorded_after_the_patch_does_not_judge_the_move()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A verdict about an old message re-recorded after a newer move does not judge the move.</summary>
    [Fact]
    public async Task A_verdict_about_an_old_message_recorded_again_after_a_newer_move_does_not_judge_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>A verdict about the earlier message, recorded before the line of the later move, judges nothing of it.</summary>
    [Fact]
    public async Task A_verdict_about_an_earlier_message_recorded_before_the_later_line_does_not_judge_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// An own line that names nobody, on a thread where a standing verdict names the contact, makes that contact wait: the
    /// thread bounds it, so it reaches no one the thread's verdicts do not name.
    /// </summary>
    [Fact]
    public async Task An_unnamed_own_line_on_a_thread_a_verdict_names_the_contact_on_makes_them_wait()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        var warm = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        await ObservedAsync(db, seed.Campaign, contact: null, "thr-1", "2026-09-14T12:05:00.000Z");

        Assert.Equal(WarmthState.Warm, warm);
        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    [Fact]
    public async Task An_unnamed_own_line_on_another_thread_does_not_touch_a_warm_contact()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        await ObservedAsync(db, seed.Campaign, contact: null, "thr-2", "2026-09-14T12:05:00.000Z");

        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    [Fact]
    public async Task An_unnamed_own_line_does_not_touch_a_contact_no_verdict_on_its_thread_names()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var stranger = new Contact { PublicId = "cnt_01K52JR0000000000000000009", FirstName = "Ivan", LastName = "Example", CreatedAt = Noon, UpdatedAt = Noon };
        db.Contacts.Add(stranger);
        await db.SaveChangesAsync(Ct);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);

        await ObservedAsync(db, seed.Campaign, contact: null, "thr-1", "2026-09-14T12:05:00.000Z");

        Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, seed.Campaign.Id, stranger.Id, Ct));
    }

    /// <summary>The unnamed line is judged as any line is: the contact's responder created after it that succeeded.</summary>
    [Fact]
    public async Task An_unnamed_own_line_judged_by_the_responder_arm_makes_the_contact_warm_again()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, contact: null, "thr-1", "2026-09-14T12:05:00.000Z");
        var waiting = await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct);

        var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1");
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, waiting);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The respond step creates the responder for a line that names nobody about nobody. Such a responder clears nothing:
    /// the unnamed move on a warm contact's thread waits for a verdict about a message at or after it, or for a later
    /// responder that names the contact, though the one about nobody was created after the line and succeeded.
    /// </summary>
    [Fact]
    public async Task An_unnamed_own_line_is_not_judged_by_a_responder_about_nobody()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, contact: null, "thr-1", "2026-09-14T12:05:00.000Z");

        var (item, attempt) = await ResponderAsync(db, seed, contact: null, "thr-1");
        await FinishAsync(db, item, attempt, succeeded: true);

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// Inbound sync writes a line for every thread of the account that moved, most naming nobody. The read of a contact's
    /// wait visits none of them on a thread the contact's verdicts do not name: forty more such lines on other threads leave
    /// the number of steps the read takes, and of steps of a full scan, exactly as they were, and no step is a full scan's,
    /// while the one unnamed line on the contact's own thread still makes them wait. The steps are counted, not timed. A seek reads the key just past its
    /// range to know the range has ended, so a line of a thread on either side of the contact's is there in both counts.
    /// </summary>
    [Fact]
    public async Task The_wait_read_visits_no_unnamed_line_of_another_thread()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, contact: null, "thr-1", "2026-09-14T12:05:00.000Z");
        await ObservedAsync(db, seed.Campaign, contact: null, "thr-0", "2026-09-14T12:05:00.000Z");
        await ObservedAsync(db, seed.Campaign, contact: null, "thr-2", "2026-09-14T12:05:00.000Z");
        var before = await QueryPlans.StepsAsync(db, Warmth.Awaiting(db, seed.Campaign.Id, seed.Contact.Id).ToQueryString(), Ct);

        for (var other = 0; other < 20; other++)
        {
            await ObservedAsync(db, seed.Campaign, contact: null, $"thr-0-{other}", "2026-09-14T12:05:00.000Z");
            await ObservedAsync(db, seed.Campaign, contact: null, $"thr-2-{other}", "2026-09-14T12:05:00.000Z");
        }

        var after = await QueryPlans.StepsAsync(db, Warmth.Awaiting(db, seed.Campaign.Id, seed.Contact.Id).ToQueryString(), Ct);

        Assert.Equal(before, after);
        Assert.Equal(0, after.FullScanSteps);
        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
    }

    /// <summary>
    /// The responder's arm visits each of the contact's items in the campaign once for the line that waits, and each
    /// responder's read and lines by their own indexes: responders on the line's thread, each with its own read and its own
    /// lines, each of which ran before its read succeeded and so judges nothing, add the same number of steps apiece,
    /// so twenty cost nineteen times what the second one cost over the first and nothing more — the cost grows linearly
    /// with the responders, never by a multiplier of the lines each carries — and no step is a full scan's. The steps are
    /// counted, not timed.
    /// </summary>
    [Fact]
    public async Task The_wait_read_pays_each_responder_on_the_thread_once()
    {
        var one = await StepsWithRespondersAsync(1);
        var two = await StepsWithRespondersAsync(2);
        var twenty = await StepsWithRespondersAsync(20);

        Assert.True(two.Steps > one.Steps);
        Assert.Equal(19 * (two.Steps - one.Steps), twenty.Steps - one.Steps);
        Assert.Equal(0, twenty.FullScanSteps);
    }

    /// <summary>
    /// The steps the wait read takes over one waiting line of the contact's thread after <paramref name="count"/>
    /// responders, each created after the line with its own read, started before its read succeeded and succeeded after it.
    /// </summary>
    private static async Task<(int Steps, int FullScanSteps)> StepsWithRespondersAsync(int count)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T12:05:00.000Z");
        for (var n = 0; n < count; n++)
        {
            var (item, attempt) = await ResponderAsync(db, seed, seed.Contact, "thr-1", readEnds: null);
            await EndReadAsync(db, await ReadOfAsync(db, item), WorkItemStatus.Succeeded);
            await FinishAsync(db, item, attempt, succeeded: true);
        }

        Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, seed.Campaign.Id, seed.Contact.Id, Ct));
        return await QueryPlans.StepsAsync(db, Warmth.Awaiting(db, seed.Campaign.Id, seed.Contact.Id).ToQueryString(), Ct);
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
    /// A responder for the thread as the respond step creates one — the system's, about the person the line names (nobody
    /// for a line that names nobody), carrying the thread in its <c>inbound_reply</c> — created now with its read, its
    /// <c>workitem_created</c> line written here, and started now, after its read ended, its <c>workitem_processing</c>
    /// line written after it.
    /// </summary>
    private static async Task<(WorkItem Item, Attempt Attempt)> ResponderAsync(
        JasonDbContext db,
        Seed seed,
        Contact? contact,
        string thread,
        bool byPerson = false,
        bool withRead = true,
        WorkItemStatus? readEnds = WorkItemStatus.Succeeded,
        string? readThread = null,
        string? readId = null)
    {
        var item = await CreatedResponderAsync(db, seed, contact, thread, byPerson, withRead, readEnds, readThread, readId);
        return (item, await StartAsync(db, item));
    }

    /// <summary>
    /// A responder created now and not yet started: scheduled, as one waiting for its slot is. As the respond step makes it,
    /// a <c>conversation.get</c> of the thread is created first, the system's, its <c>workitem_created</c> line before the
    /// responder's, and the responder names it in <c>inbound_reply.read</c>; the read then ends as
    /// <paramref name="readEnds"/> says, or stays open where that is null. <paramref name="withRead"/> false makes no read;
    /// <paramref name="readThread"/> makes the read of another thread; <paramref name="readId"/> names that id instead and
    /// makes no read.
    /// </summary>
    private static async Task<WorkItem> CreatedResponderAsync(
        JasonDbContext db,
        Seed seed,
        Contact? contact,
        string thread,
        bool byPerson = false,
        bool withRead = true,
        WorkItemStatus? readEnds = WorkItemStatus.Succeeded,
        string? readThread = null,
        string? readId = null)
    {
        var read = withRead && readId is null ? await ThreadReadAsync(db, seed, contact, readThread ?? thread) : null;
        var item = WorkItemFactory.NewAiRole(seed.Campaign, role: Responder.Role, now: Noon, configure: item =>
        {
            item.CreatedByType = byPerson ? ActorType.Human : ActorType.System;
            item.Contact = contact;
            item.Status = WorkItemStatus.Scheduled;
            item.Context = new JsonObject
            {
                ["trigger"] = JournalKinds.InboundReplyObserved,
                [Responder.InboundReplyKey] = new JsonObject
                {
                    ["plugin"] = "mailbox",
                    ["thread"] = thread,
                    ["read"] = withRead ? readId ?? read!.PublicId : null,
                },
            };
        });
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        Created(db, seed, item, contact);
        await db.SaveChangesAsync(Ct);
        if (read is not null && readEnds is { } end)
        {
            await EndReadAsync(db, read, end);
        }

        return item;
    }

    /// <summary>
    /// The read of a thread as the respond step creates one: a <c>conversation.get</c> the system created, about the
    /// person, its input naming the thread, scheduled, its <c>workitem_created</c> line written here.
    /// </summary>
    private static async Task<WorkItem> ThreadReadAsync(JasonDbContext db, Seed seed, Contact? contact, string thread)
    {
        var read = WorkItemFactory.NewProviderOp(seed.Campaign, Responder.ReadOperation, now: Noon);
        read.CreatedByType = ActorType.System;
        read.Contact = contact;
        read.Status = WorkItemStatus.Scheduled;
        read.Context = new JsonObject
        {
            ["input"] = new JsonObject
            {
                ["thread"] = new JsonObject { ["external_id"] = thread },
                ["limit"] = 50,
                ["offset"] = 0,
            },
        };
        db.WorkItems.Add(read);
        await db.SaveChangesAsync(Ct);
        Created(db, seed, read, contact);
        await db.SaveChangesAsync(Ct);
        return read;
    }

    /// <summary>The item's <c>workitem_created</c> line, as the work item service writes it, the system's.</summary>
    private static void Created(JasonDbContext db, Seed seed, WorkItem item, Contact? contact) =>
        Writer.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.WorkItemCreated,
            seed.Campaign,
            key: "kind",
            updated: new JsonObject
            {
                ["kind"] = item.Kind == WorkItemKind.AiRole ? "ai_role" : "provider_op",
                ["role"] = item.Role,
                ["operation"] = item.Operation,
                ["contact_id"] = contact?.PublicId,
                ["priority"] = item.Priority,
                ["not_before"] = null,
                ["due_at"] = null,
            },
            workItem: item);

    /// <summary>
    /// The read ends now as <paramref name="status"/> says: one that ran has an attempt whose provenance names the plugin
    /// it ran at; its transition's line is written here.
    /// </summary>
    private static async Task EndReadAsync(JasonDbContext db, WorkItem read, WorkItemStatus status, string plugin = "mailbox")
    {
        Attempt? attempt = null;
        if (status is WorkItemStatus.Succeeded or WorkItemStatus.Failed)
        {
            attempt = WorkItemFactory.NewAttempt(read, read.Attempts.Count + 1, status == WorkItemStatus.Succeeded ? AttemptStatus.Succeeded : AttemptStatus.Failed, Noon);
            attempt.FinishedAt = Noon;
            attempt.Provenance = new AttemptProvenanceDto(
                plugin, "1.0.0", "sha256:0", 1, 1, Responder.ReadOperation, 1, "snp_01K5B7Q2WE5X3M9T0YH4C6RDNC", "rts_01K5B7Q2WE5X3M9T0YH4C6RDND", RouteScope.GlobalDefault, "sha256:1");
            read.AttemptCount = attempt.Number;
            db.Attempts.Add(attempt);
        }

        var from = read.Status;
        read.Status = status;
        read.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
        var (key, old, updated) = status switch
        {
            WorkItemStatus.Succeeded => ("result_present", (JsonNode?)null, (JsonNode?)JsonValue.Create(false)),
            WorkItemStatus.Failed => (attempt!.PublicId, null, new JsonObject { ["code"] = "provider_unavailable", ["message"] = "The read failed.", ["retriable"] = false }),
            WorkItemStatus.Cancelled => ("status", JsonValue.Create("scheduled"), JsonValue.Create("cancelled")),
            _ => ("due_at", JsonValue.Create("2026-09-14T12:00:00.000Z"), null),
        };
        Writer.Append(db, Actors.Dispatcher, WorkItemTransitions.JournalKind(from, status), campaign: null, key: key, old: old, updated: updated, workItem: read, attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// One attempt of the read, claimed and run as the claim and the attempt's end write it: numbered after the read's
    /// attempts, its provenance naming the plugin and the plugin snapshot the route resolved at its claim, its
    /// <c>workitem_processing</c> line, then either its success and the read's <c>workitem_succeeded</c> line, or a
    /// transient failure that releases the read for another attempt, its <c>workitem_released</c> line keyed by the attempt.
    /// </summary>
    private static async Task RunReadAsync(JasonDbContext db, WorkItem read, string plugin, string snapshot, bool succeeded)
    {
        var attempt = WorkItemFactory.NewAttempt(read, read.Attempts.Count + 1, AttemptStatus.Running, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", 1, 1, Responder.ReadOperation, 1, snapshot, "rts_01K5B7Q2WE5X3M9T0YH4C6RDND", RouteScope.GlobalDefault, "sha256:1");
        db.Attempts.Add(attempt);
        var from = read.Status;
        read.Status = WorkItemStatus.Processing;
        await db.SaveChangesAsync(Ct);
        Writer.Append(db, Actors.Dispatcher, WorkItemTransitions.JournalKind(from, WorkItemStatus.Processing), campaign: null, key: "attempt", updated: JsonValue.Create(attempt.Number), workItem: read, attempt: attempt);
        await db.SaveChangesAsync(Ct);

        attempt.FinishedAt = Noon;
        read.AttemptCount = attempt.Number;
        if (succeeded)
        {
            attempt.Status = AttemptStatus.Succeeded;
            read.Status = WorkItemStatus.Succeeded;
            read.FinishedAt = Noon;
            await db.SaveChangesAsync(Ct);
            Writer.Append(db, Actors.Dispatcher, JournalKinds.WorkItemSucceeded, campaign: null, key: "result_present", updated: JsonValue.Create(false), workItem: read, attempt: attempt);
        }
        else
        {
            attempt.Status = AttemptStatus.Failed;
            attempt.Error = new AttemptErrorDto("provider_unavailable", "The read failed.", Retriable: true, Class: FailureClass.Transient);
            read.Status = WorkItemStatus.Created;
            await db.SaveChangesAsync(Ct);
            Writer.Append(
                db,
                Actors.Dispatcher,
                WorkItemTransitions.JournalKind(WorkItemStatus.Processing, WorkItemStatus.Created),
                campaign: null,
                key: attempt.PublicId,
                updated: new JsonObject { ["code"] = "provider_unavailable", ["retry_after"] = null },
                workItem: read,
                attempt: attempt);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The read the responder names in its <c>inbound_reply.read</c>.</summary>
    private static Task<WorkItem> ReadOfAsync(JasonDbContext db, WorkItem responder)
    {
        var id = (string)responder.Context[Responder.InboundReplyKey]!["read"]!;
        return db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.PublicId == id, Ct);
    }

    /// <summary>
    /// A patch of the item's context as the work item service journals it: one <c>workitem_context_updated</c> line by
    /// <paramref name="actor"/> under the key it changed.
    /// </summary>
    private static async Task ContextPatchedAsync(JasonDbContext db, WorkItem item, ActorRef actor, string key, JsonNode? updated)
    {
        var old = item.Context[key]?.DeepClone();
        item.Context[key] = updated?.DeepClone();
        await db.SaveChangesAsync(Ct);
        Writer.Append(db, actor, JournalKinds.WorkItemContextUpdated, campaign: null, key: key, old: old, updated: updated?.DeepClone(), reason: "a patch", workItem: item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The responder's first attempt starts now: its <c>workitem_processing</c> line is written here.</summary>
    private static async Task<Attempt> StartAsync(JasonDbContext db, WorkItem item)
    {
        item.Status = WorkItemStatus.Processing;
        item.AttemptCount = 1;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        Writer.Append(db, Actors.Dispatcher, JournalKinds.WorkItemProcessing, campaign: null, key: "attempt", updated: JsonValue.Create(1), workItem: item, attempt: attempt);
        await db.SaveChangesAsync(Ct);
        return attempt;
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
