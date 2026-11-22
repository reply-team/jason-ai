using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A held person's touch waits at the claim: while the person's membership in the campaign is <c>paused</c>, the claim
/// takes none of their touches there, writes no status, no wait and no attempt for one, and takes the campaign's next item
/// instead; a reply to the person and a take-back of a send go on; and continuing the line makes the touch a candidate
/// again by that alone.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>
    /// The touch stays <c>created</c>, with no attempt and no line of its own, and the claim takes the reply behind it in
    /// the same scan, though the touch sits higher: a held touch is no candidate, so it does not hold its campaign's turn.
    /// </summary>
    [Fact]
    public async Task A_held_person_s_touch_is_not_taken_and_their_reply_is()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db, state: MembershipState.Paused);
        var touch = Sending(db, campaign, contact);
        touch.Priority = 90;
        var reply = Replying(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var held = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == touch.Id, Ct);
        Assert.Equal((WorkItemStatus.Created, 0, null), (held.Status, held.AttemptCount, held.LastError));
        Assert.False(await fresh.Attempts.AnyAsync(a => a.WorkItemId == touch.Id, Ct));
        Assert.False(await fresh.WorkItemWaits.AnyAsync(w => w.WorkItemId == touch.Id, Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.WorkItemId == touch.PublicId, Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == reply.Id, Ct)).Status);
    }

    /// <summary>
    /// The claim's own query, the one the eligible predicate is held to: of a held person's work, the touches are left out
    /// and a reply and a take-back are candidates.
    /// </summary>
    [Fact]
    public async Task A_held_person_s_reply_and_take_back_are_candidates_and_their_touches_are_not()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var candidates = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var operation in new[] { "message.send", "social_invitation.send", "campaign.enroll", "conversation.reply", "message.cancel" })
        {
            // A campaign each, so the one-per-campaign pick cannot hide an answer.
            var (campaign, contact) = await LineSceneAsync(db, Jason.Contracts.Ids.PublicId.New("cnt"), MembershipState.Paused);
            var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon);
            item.Contact = contact;
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(Ct);
            candidates[operation] = item.Id;
        }

        var taken = await Claimer.CandidatesAsync(db, Noon, freeSlots: 100, Ct);

        Assert.Equal(
            ["conversation.reply", "message.cancel"],
            candidates.Where(entry => taken.Contains(entry.Value)).Select(entry => entry.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// <c>campaign.continue_contacts</c> enrols the person again, and the touch that waited is a candidate again by that
    /// alone: the next claim takes it to the person's approval, as it takes any letter.
    /// </summary>
    [Fact]
    public async Task Continuing_the_line_makes_the_held_touch_a_candidate_again()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db, state: MembershipState.Paused);
        var touch = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.DoesNotContain(touch.Id, await Claimer.CandidatesAsync(db, Noon, freeSlots: 100, Ct));

        var clock = new FixedClock(new DateTimeOffset(Noon));
        var memberships = new MembershipService(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
        await memberships.ContinueAsync(new ContinueContactsRequest(campaign.PublicId, [contact.PublicId], new ActorRef(ActorType.Role, "manager"), "An answer to come back to in May."), Ct);

        Assert.Contains(touch.Id, await Claimer.CandidatesAsync(db, Noon, freeSlots: 100, Ct));
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == touch.Id, Ct)).Status);
    }

    /// <summary>A reply to the person in their thread, which a person approves before it goes.</summary>
    private static WorkItem Replying(JasonDbContext db, Campaign campaign, Contact contact)
    {
        var reply = WorkItemFactory.NewProviderOp(campaign, "conversation.reply", Noon);
        reply.Contact = contact;
        reply.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["thread"] = new JsonObject { ["external_id"] = "th-1" },
                ["body"] = "Thank you, Tuesday works.",
            },
        };
        db.WorkItems.Add(reply);
        return reply;
    }
}
