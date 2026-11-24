using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A thread moves when Jason writes into it too, and the inbound read says every move as a reply line. A line whose moment
/// is not later than the latest moment Jason's own letter went out to the person in the campaign — a touch's
/// <c>accepted_for</c> where its result names one, otherwise the end of its succeeded attempt, or a
/// <c>conversation.reply</c> into that same thread — is Jason's own move: it holds nobody and takes nothing back. Only a
/// moment that has come counts, and a letter whose cancel succeeded never went.
/// </summary>
public partial class HeldSendsCancelTests
{
    /// <summary>
    /// The line continued, Jason's second letter goes now and moves the thread, and a third waits at the provider: the
    /// line the move makes holds nobody and takes the third back from nobody, whether the letter's moment is the provider's
    /// own or the end of the attempt that sent it.
    /// </summary>
    [Theory]
    [InlineData("the provider named the moment it went")]
    [InlineData("the attempt that sent it ended after the thread moved")]
    public async Task Jasons_own_letter_moving_the_thread_holds_nobody_and_takes_nothing_back(string how)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var went = Noon.AddMinutes(-10);
        if (how == "the provider named the moment it went")
        {
            await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, acceptedFor: Moment(went));
        }
        else
        {
            await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, wentAt: went);
        }

        await SentAsync(harness, campaign, person, Later);
        await ReplyLineAsync(harness, campaign, person, "th-1", how == "the provider named the moment it went" ? went : went.AddSeconds(-30));

        await harness.RespondAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(MembershipState.Enrolled, await StateOfAsync(db, campaign, person));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
        Assert.False(await db.WorkItems.AnyAsync(w => w.Operation == HeldSends.Cancel, Ct));
    }

    /// <summary>
    /// A line later than every letter that went holds the person and takes back the one still waiting; so does a line
    /// whose moment only a letter still to come, a letter taken back, a letter to the person in another campaign or a
    /// letter to somebody else here would reach.
    /// </summary>
    [Theory]
    [InlineData("the person wrote after Jason's letter went")]
    [InlineData("only a letter still to come is later")]
    [InlineData("the later letter was taken back")]
    [InlineData("the later letter was in another campaign")]
    [InlineData("the later letter went to somebody else")]
    public async Task A_reply_later_than_every_letter_that_went_holds_the_person(string what)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var (other, _) = await SceneAsync(harness, contact: person);
        var (_, someoneElse) = await SceneAsync(harness, campaign: campaign);
        var went = Noon.AddMinutes(-10);
        var moved = what == "the person wrote after Jason's letter went" ? went.AddMinutes(5) : went.AddMinutes(-1);
        switch (what)
        {
            case "the person wrote after Jason's letter went":
                await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, wentAt: went);
                break;
            case "only a letter still to come is later":
                await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, acceptedFor: Moment(Noon.AddHours(4)));
                break;
            case "the later letter was taken back":
                var taken = await SentAsync(harness, campaign, person, sendAfter: null, cancelable: true, acceptedFor: Moment(went));
                await CancelAsync(harness, campaign, taken, WorkItemStatus.Succeeded);
                break;
            case "the later letter was in another campaign":
                await SentAsync(harness, other, person, sendAfter: null, cancelable: false, wentAt: went);
                break;
            case "the later letter went to somebody else":
                await SentAsync(harness, campaign, someoneElse, sendAfter: null, cancelable: false, wentAt: went);
                break;
        }

        var waiting = await SentAsync(harness, campaign, person, Later);
        await ReplyLineAsync(harness, campaign, person, "th-1", moved);

        await harness.RespondAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(MembershipState.Paused, await StateOfAsync(db, campaign, person));
        Assert.Equal(1, await db.Journal.CountAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
        var cancel = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Operation == HeldSends.Cancel && w.Status == WorkItemStatus.Created, Ct);
        Assert.Equal(waiting, (string?)cancel.Context["input"]!["send"]);
    }

    /// <summary>
    /// A letter that a succeeded read of the campaign says the provider failed or cancelled never went, so it moved no thread:
    /// a line at its moment is not Jason's own move, holds the person and takes back the letter still waiting. A read that
    /// says the letter went, that the provider still holds it, or that the provider lost track of it, leaves the letter
    /// Jason's own move, and the line holds nobody.
    /// </summary>
    [Theory]
    [InlineData("failed", MembershipState.Paused)]
    [InlineData("cancelled", MembershipState.Paused)]
    [InlineData("unknown", MembershipState.Enrolled)]
    [InlineData("queued", MembershipState.Enrolled)]
    [InlineData("sent", MembershipState.Enrolled)]
    public async Task A_letter_a_read_says_never_went_is_no_move_of_Jasons(string state, MembershipState expected)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var went = Noon.AddMinutes(-10);
        var letter = await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, acceptedFor: Moment(went));
        await StatusReadAsync(harness, campaign, letter, state, cancelable: false);
        var waiting = await SentAsync(harness, campaign, person, Later);
        await ReplyLineAsync(harness, campaign, person, "th-1", went.AddSeconds(-30));

        await harness.RespondAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(expected, await StateOfAsync(db, campaign, person));
        Assert.Equal(expected == MembershipState.Paused ? 1 : 0, await db.Journal.CountAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
        string?[] takenBack = expected == MembershipState.Paused ? [waiting] : [];
        var cancels = await db.WorkItems.AsNoTracking().Where(w => w.Operation == HeldSends.Cancel).ToListAsync(Ct);
        Assert.Equal(takenBack, cancels.Select(w => (string?)w.Context["input"]!["send"]));
    }

    /// <summary>
    /// Right after a continue the responder answers in the thread, and the thread moves: the line that move makes holds
    /// nobody. Its answer in another thread is no reason to leave this thread's line alone.
    /// </summary>
    [Theory]
    [InlineData("th-1", MembershipState.Enrolled)]
    [InlineData("th-2", MembershipState.Paused)]
    public async Task The_responders_own_answer_in_the_thread_holds_nobody(string answeredIn, MembershipState expected)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var answered = Noon.AddMinutes(-2);
        await AnsweredAsync(harness, campaign, person, answeredIn, answered);
        await ReplyLineAsync(harness, campaign, person, "th-1", answered.AddSeconds(-10));

        await harness.RespondAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(expected, await StateOfAsync(db, campaign, person));
        Assert.Equal(expected == MembershipState.Paused ? 1 : 0, await db.Journal.CountAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
    }

    /// <summary>
    /// The skip is for a line that does not know who moved the thread. A line the events store wrote from an event that names
    /// the other side always holds, though its moment is not later than Jason's letter: where the provider said who wrote,
    /// the moments do not overrule it. One written from an event that names no side may be Jason's own move, and is skipped
    /// as the inbound read's line is.
    /// </summary>
    [Theory]
    // Critical: a reply the provider says the other side wrote, skipped by its moments, lets the next touch go to a person who answered.
    [InlineData("other_side", MembershipState.Paused, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.N2)])]
    [InlineData(null, MembershipState.Enrolled)]
    public async Task An_events_line_that_names_the_other_side_holds_whatever_the_moments(string? movedBy, MembershipState expected)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var went = Noon.AddMinutes(-10);
        await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, acceptedFor: Moment(went));
        await EventReplyLineAsync(harness, campaign, person, "th-1", went.AddSeconds(-30), movedBy);

        await harness.RespondAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(expected, await StateOfAsync(db, campaign, person));
        Assert.Equal(expected == MembershipState.Paused ? 1 : 0, await db.Journal.CountAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
    }

    /// <summary>
    /// A reply line as the events store writes it from a <c>thread_moved</c> event found by the contact's pin: the stored
    /// event names <paramref name="movedBy"/>, or no side, and the line names the event and the rule that found the campaign.
    /// </summary>
    private static async Task EventReplyLineAsync(DispatchHarness harness, int campaign, int person, string thread, DateTime moved, string? movedBy)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        var publicId = await db.Contacts.Where(c => c.Id == person).Select(c => c.PublicId).SingleAsync(Ct);
        var moment = InboundSync.Instant(new DateTimeOffset(moved));
        var source = new EventSource { PublicId = PublicId.New("evs"), PluginId = Mailer, BindingIdentity = EventSource.NoBinding, CreatedAt = Noon };
        db.EventSources.Add(source);
        await db.SaveChangesAsync(Ct);
        var stored = new ProviderEvent
        {
            PublicId = PublicId.New("evt"),
            SourceId = source.Id,
            PluginId = Mailer,
            Kind = "thread_moved",
            OccurredAt = moved,
            StoredAt = Noon,
            Key = "sha256:" + PublicId.New("key"),
            ContactId = person,
            Data = new JsonObject
            {
                ["kind"] = "thread_moved",
                ["id"] = "ev-1",
                ["occurred_at"] = moment,
                ["thread"] = thread,
                ["contact"] = "c-1",
                ["campaign"] = "p-1",
                ["moved_by"] = movedBy,
                ["channel"] = "email",
                ["unread"] = true,
                ["has_meeting_intent"] = null,
            },
        };
        db.Events.Add(stored);
        await db.SaveChangesAsync(Ct);

        new JournalWriter(harness.Clock).Append(
            db,
            Actors.Dispatcher,
            JournalKinds.InboundReplyObserved,
            owner,
            key: InboundSync.ObservationKey(Mailer, thread, moment),
            updated: new JsonObject
            {
                ["plugin"] = Mailer,
                ["thread"] = thread,
                ["channel"] = "email",
                ["last_activity_at"] = moment,
                ["unread"] = true,
                ["has_meeting_intent"] = null,
                ["contact_id"] = publicId,
                ["event_id"] = stored.PublicId,
                ["attributed_by"] = "contact_pin",
            },
            contactId: person);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A <c>conversation.reply</c> of the campaign to the person, into <paramref name="thread"/>, whose attempt succeeded at <paramref name="at"/>.</summary>
    private static async Task AnsweredAsync(DispatchHarness harness, int campaign, int person, string thread, DateTime at)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, "conversation.reply", at.AddMinutes(-1));
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["thread"] = new JsonObject { ["external_id"] = thread },
                ["body"] = "Thank you, Tuesday works.",
            },
        };
        item.ContactId = person;
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = at;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, at.AddMinutes(-1));
        attempt.FinishedAt = at;
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    private static Task<MembershipState> StateOfAsync(JasonDbContext db, int campaign, int person) =>
        db.CampaignContacts.AsNoTracking().Where(m => m.CampaignId == campaign && m.ContactId == person).Select(m => m.State).SingleAsync(Ct);
}
