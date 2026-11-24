using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The line's two checks through the real claim, its real reads and a real database, with <c>Lines:MaxTouches</c> at two:
/// what the ceiling counts — a touch that succeeded, or any attempt of which ended classed ambiguous whatever came after,
/// once per item, and nothing else — and
/// that a refusal is decided before anybody is asked to approve, ends the item failed, and wakes the manager.
/// </summary>
public partial class ClaimerTests
{
    private const string LineProvider = "stand-in-provider";

    /// <summary>
    /// Two touches reached the person, so a third is refused at the claim: failed, permanent, with its attempt kept and no
    /// child, and no approval or question made for it — nobody is asked to approve a touch that could only be refused.
    /// </summary>
    [Fact]
    public async Task The_third_touch_is_refused_at_the_ceiling_before_an_approval_is_asked()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded, operation: "campaign.enroll");
        var third = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == third.Id, Ct);
        Assert.Equal((AttemptErrors.TouchCeilingReached, FailureClass.Permanent), (attempt.Error!.Code, attempt.Error.Class));
        Assert.False(attempt.Error.Retriable);
        Assert.Contains("Lines:MaxTouches allows in this campaign, 2;", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Null(attempt.Launch);
        Assert.Equal(WorkItemStatus.Failed, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == third.Id, Ct)).Status);
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await fresh.Decisions.AsNoTracking().ToListAsync(Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == JournalKinds.ApprovalRequested, Ct));
    }

    /// <summary>
    /// A touch whose last attempt lost its answer may have reached the person, so it counts; one the provider refused
    /// permanently reached nobody, so it does not, and the touch after it goes on to the person's approval.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Ambiguous, AttemptErrors.TouchCeilingReached)]
    [InlineData(FailureClass.Permanent, null)]
    [InlineData(FailureClass.Validation, null)]
    public async Task A_failed_touch_counts_only_where_its_last_attempt_may_have_reached_the_person(FailureClass failed, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Failed, failed);
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct);
        if (refused is null)
        {
            Assert.Equal(WorkItemStatus.AwaitingApproval, row.Status);
            Assert.Single(await fresh.Approvals.AsNoTracking().Where(a => a.WorkItemId == next.Id).ToListAsync(Ct));
        }
        else
        {
            Assert.Equal(refused, await RefusalAsync(fresh, next));
        }
    }

    /// <summary>
    /// Any attempt of a touch that ended ambiguous may have reached the person, whatever came after it — a transient attempt,
    /// a permanent refusal — and whether the item then failed, its attempts spent, or was cancelled: it counts. Only a touch
    /// none of whose attempts was ambiguous counts for nothing. A later attempt that found nothing was sent still leaves it
    /// counted, one too many, on the safe side of the ceiling.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Ambiguous, FailureClass.Permanent, WorkItemStatus.Failed, AttemptErrors.TouchCeilingReached)]
    [InlineData(FailureClass.Permanent, FailureClass.Ambiguous, WorkItemStatus.Failed, AttemptErrors.TouchCeilingReached)]
    [InlineData(FailureClass.Ambiguous, FailureClass.Transient, WorkItemStatus.Failed, AttemptErrors.TouchCeilingReached)]
    [InlineData(FailureClass.Ambiguous, FailureClass.Transient, WorkItemStatus.Cancelled, AttemptErrors.TouchCeilingReached)]
    [InlineData(FailureClass.Permanent, FailureClass.Transient, WorkItemStatus.Failed, null)]
    [InlineData(FailureClass.Transient, FailureClass.Permanent, WorkItemStatus.Cancelled, null)]
    public async Task A_touch_counts_where_any_of_its_attempts_ended_ambiguous(FailureClass first, FailureClass last, WorkItemStatus ended, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var twice = await TouchedAsync(db, campaign, contact, WorkItemStatus.Failed, first);
        var repeated = WorkItemFactory.NewAttempt(twice, 2, AttemptStatus.Failed, Noon.AddHours(-1));
        repeated.FinishedAt = Noon.AddMinutes(-50);
        repeated.Error = new AttemptErrorDto(last == FailureClass.Ambiguous ? "provider_answer_lost" : "recipient_rejected", "The provider answered so.", false, Class: last);
        twice.AttemptCount = 2;
        twice.LastError = repeated.Error;
        twice.Status = ended;
        db.Attempts.Add(repeated);
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct);
        Assert.Equal(refused is null ? WorkItemStatus.AwaitingApproval : WorkItemStatus.Failed, row.Status);
        if (refused is not null)
        {
            Assert.Equal(refused, await RefusalAsync(fresh, next));
        }
    }

    /// <summary>
    /// What the ceiling counts is the person's touches in this campaign that may have reached them and nothing else: another
    /// person's, the person's in another campaign, work that reaches nobody, and touches that never ran count for nothing;
    /// a touch whose only attempt was ambiguous counts whatever became of the item since, put back for a repeat or cancelled.
    /// </summary>
    [Fact]
    public async Task Only_the_person_s_own_touches_in_this_campaign_that_may_have_reached_them_are_counted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var (elsewhere, other) = await LineSceneAsync(db, "cnt_01K52JR0000000000000000002");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, elsewhere, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, other, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded, operation: "list_membership.add");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded, operation: "conversation.reply");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Expired);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.AwaitingApproval);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Cancelled);
        await AmbiguousAsync(db, await TouchedAsync(db, campaign, contact, WorkItemStatus.Created));
        await AmbiguousAsync(db, await TouchedAsync(db, campaign, contact, WorkItemStatus.Cancelled));

        // One item is one touch at most, however many of its attempts lost their answer.
        var lostTwice = await TouchedAsync(db, campaign, contact, WorkItemStatus.Failed);
        await AmbiguousAsync(db, lostTwice, number: 2);
        await AmbiguousAsync(db, lostTwice, number: 3);

        Assert.Equal(4, await LineTouches.OthersAsync(db, campaign.Id, contact.Id, workItemId: 0, Ct));
    }

    /// <summary>
    /// A send the provider held and a succeeded <c>message.cancel</c> of the campaign took back never reached the person, so
    /// it is no touch, and the next touch goes on to the person's approval. A cancel that failed, refused or ending
    /// ambiguous, a cancel still waiting, or a succeeded cancel of another campaign that names the same item takes nothing
    /// back: the letter may still go, and the send still counts.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Succeeded, true, null, null)]
    [InlineData(WorkItemStatus.Failed, true, null, AttemptErrors.TouchCeilingReached)]
    [InlineData(WorkItemStatus.Failed, true, FailureClass.Ambiguous, AttemptErrors.TouchCeilingReached)]
    [InlineData(WorkItemStatus.Failed, true, FailureClass.Permanent, AttemptErrors.TouchCeilingReached)]
    [InlineData(WorkItemStatus.AwaitingApproval, true, null, AttemptErrors.TouchCeilingReached)]
    [InlineData(WorkItemStatus.Succeeded, false, null, AttemptErrors.TouchCeilingReached)]
    public async Task A_send_a_succeeded_cancel_of_the_campaign_took_back_is_no_touch(WorkItemStatus cancel, bool sameCampaign, FailureClass? ended, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var (elsewhere, _) = await LineSceneAsync(db, "cnt_01K52JR0000000000000000002");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var held = await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var takeBack = WorkItemFactory.NewProviderOp(sameCampaign ? campaign : elsewhere, HeldSends.Cancel, Noon.AddHours(-1));
        takeBack.Status = cancel;
        takeBack.FinishedAt = cancel == WorkItemStatus.AwaitingApproval ? null : Noon.AddMinutes(-30);
        takeBack.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = held.PublicId } };
        db.WorkItems.Add(takeBack);
        if (ended is { } failure)
        {
            // The take-back's own attempt ended so: refused, or not knowing whether the provider cancelled; the letter may
            // still go, so it stays a touch.
            var attempt = WorkItemFactory.NewAttempt(takeBack, 1, AttemptStatus.Failed, Noon.AddMinutes(-40));
            attempt.FinishedAt = Noon.AddMinutes(-30);
            attempt.Error = new AttemptErrorDto(failure == FailureClass.Ambiguous ? "provider_answer_lost" : "send_not_cancelable", "The provider answered so.", false, Class: failure);
            takeBack.AttemptCount = 1;
            takeBack.LastError = attempt.Error;
            db.Attempts.Add(attempt);
        }

        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(refused is null ? 1 : 2, await LineTouches.OthersAsync(db, campaign.Id, contact.Id, next.Id, Ct));
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct);
        if (refused is null)
        {
            Assert.Equal(WorkItemStatus.AwaitingApproval, row.Status);
        }
        else
        {
            Assert.Equal(refused, await RefusalAsync(fresh, next));
        }
    }

    /// <summary>
    /// A send that a succeeded <c>message.status</c> of the campaign says the provider failed or cancelled never went, so it
    /// is no touch, whoever made the read — the dispatcher, a person or a role — and the next touch goes on to the person's
    /// approval. A read that says the send went, that the provider still holds it or that the provider lost track of it, a
    /// read that failed, a read of another person's send, and a read of another campaign that names the same item leave it
    /// counted: the letter went, may still go, or may have gone.
    /// </summary>
    [Theory]
    [InlineData("failed", WorkItemStatus.Succeeded, ActorType.System, "the send", null)]
    [InlineData("cancelled", WorkItemStatus.Succeeded, ActorType.System, "the send", null)]
    [InlineData("failed", WorkItemStatus.Succeeded, ActorType.Human, "the send", null)]
    [InlineData("cancelled", WorkItemStatus.Succeeded, ActorType.Role, "the send", null)]
    [InlineData("unknown", WorkItemStatus.Succeeded, ActorType.System, "the send", AttemptErrors.TouchCeilingReached)]
    [InlineData("queued", WorkItemStatus.Succeeded, ActorType.System, "the send", AttemptErrors.TouchCeilingReached)]
    [InlineData("sent", WorkItemStatus.Succeeded, ActorType.System, "the send", AttemptErrors.TouchCeilingReached)]
    [InlineData(null, WorkItemStatus.Failed, ActorType.System, "the send", AttemptErrors.TouchCeilingReached)]
    [InlineData("failed", WorkItemStatus.Succeeded, ActorType.System, "another person's send", AttemptErrors.TouchCeilingReached)]
    [InlineData("failed", WorkItemStatus.Succeeded, ActorType.System, "the send, from another campaign", AttemptErrors.TouchCeilingReached)]
    public async Task A_send_a_succeeded_read_says_the_provider_failed_or_cancelled_is_no_touch(
        string? state, WorkItemStatus read, ActorType madeBy, string names, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var (elsewhere, _) = await LineSceneAsync(db, "cnt_01K52JR0000000000000000002");
        var (_, other) = await LineSceneAsync(db, "cnt_01K52JR0000000000000000003");
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var held = await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var othersSend = await TouchedAsync(db, campaign, other, WorkItemStatus.Succeeded);
        ReadSaid(
            db,
            names == "the send, from another campaign" ? elsewhere : campaign,
            names == "another person's send" ? othersSend : held,
            state,
            read,
            madeBy);
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(refused is null ? 1 : 2, await LineTouches.OthersAsync(db, campaign.Id, contact.Id, next.Id, Ct));
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct);
        if (refused is null)
        {
            Assert.Equal(WorkItemStatus.AwaitingApproval, row.Status);
        }
        else
        {
            Assert.Equal(refused, await RefusalAsync(fresh, next));
        }
    }

    /// <summary>
    /// The ceiling end to end, through a whole dispatcher and reads the provider answers: a person held at the ceiling of two
    /// by a letter the provider took is refused the touch after it. A read of that letter, made by the manager and recorded as
    /// every read is, that answers the provider lost track of it leaves it counted, since it may have gone, and the next touch
    /// is refused too; a second read that answers the provider failed it takes it out of the count, and the touch after that
    /// goes on to the person's approval: a letter that never went no longer holds the person at the ceiling.
    /// </summary>
    [Fact]
    [Critical(Family.W3, "the ceiling of touches leaves out a letter a read says never went, and keeps one that may have gone")]
    public async Task A_person_at_the_ceiling_by_a_letter_a_read_says_failed_may_be_written_to_again()
    {
        var answers = new Queue<string>(["unknown", "failed"]);
        var command = new FakeCommand(
            WorkItemKind.ProviderOp,
            _ => Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(StatusAnswer(answers.Dequeue()), null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 2 };
        RoutedToTheLineProvider(harness, "message.send", HeldSends.Status);
        await using var db = harness.Open();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var letter = Wrote(db, campaign, contact, hoursAgo: 2);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptErrors.TouchCeilingReached, await TouchAsync(harness, db, campaign, contact));

        Assert.Equal("unknown", await ReadThroughAsync(harness, db, campaign, letter));
        Assert.Equal(AttemptErrors.TouchCeilingReached, await TouchAsync(harness, db, campaign, contact));

        Assert.Equal("failed", await ReadThroughAsync(harness, db, campaign, letter));
        Assert.Null(await TouchAsync(harness, db, campaign, contact));
        Assert.Empty(answers);
    }

    /// <summary>
    /// The item being claimed is never counted against itself, whatever its own attempts were: an earlier attempt of it that
    /// lost its answer is the repeat's own concern (check 23), not one more touch. That attempt is ambiguous, so counted it
    /// would bring the item to the ceiling of two with the one other touch; left out, the claim goes on to check 23.
    /// </summary>
    [Fact]
    public async Task The_item_s_own_earlier_attempt_is_not_counted_against_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var repeat = Sending(db, campaign, contact);
        repeat.AttemptCount = 1;
        var lost = WorkItemFactory.NewAttempt(repeat, 1, AttemptStatus.Failed, Noon.AddHours(-1));
        lost.Error = new AttemptErrorDto("provider_answer_lost", "The answer was lost.", true, Class: FailureClass.Ambiguous);
        db.Attempts.Add(lost);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var refusal = (await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == repeat.Id).OrderByDescending(a => a.Number).FirstAsync(Ct)).Error!;
        Assert.Equal(AttemptErrors.ProviderAnswerLost, refusal.Code);

        // The count itself: the repeat's lost attempt counts for every other item, and never for the repeat; and an item
        // left out is left out even where it ended as one that would count.
        await using var counting = database.Open();
        Assert.Equal(1, await LineTouches.OthersAsync(counting, campaign.Id, contact.Id, repeat.Id, Ct));
        Assert.Equal(2, await LineTouches.OthersAsync(counting, campaign.Id, contact.Id, workItemId: 0, Ct));
        var ended = await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        Assert.Equal(2, await LineTouches.OthersAsync(counting, campaign.Id, contact.Id, ended.Id, Ct));
        Assert.Equal(3, await LineTouches.OthersAsync(counting, campaign.Id, contact.Id, workItemId: 0, Ct));
    }

    /// <summary>
    /// The boundary of the ceiling: four touches went out, and a fifth's first attempt lost its answer. Whether the fifth was
    /// put back to be tried again later, or tried again transiently and then failed with its attempts spent, or was
    /// cancelled after that, it may have reached the person, so a sixth meets the ceiling of five and is refused.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public async Task A_touch_whose_first_attempt_lost_its_answer_counts_against_the_next_one_at_the_boundary(WorkItemStatus fifth)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 5 };
        var (campaign, contact) = await LineSceneAsync(db);
        for (var sent = 0; sent < 4; sent++)
        {
            await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        }

        var lost = await TouchedAsync(db, campaign, contact, WorkItemStatus.Created);
        lost.RetryAfter = Noon.AddHours(1);
        await AmbiguousAsync(db, lost);
        if (fifth != WorkItemStatus.Created)
        {
            var again = WorkItemFactory.NewAttempt(lost, 2, AttemptStatus.Failed, Noon.AddMinutes(-30));
            again.FinishedAt = Noon.AddMinutes(-20);
            again.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", true, Class: FailureClass.Transient);
            db.Attempts.Add(again);
            lost.AttemptCount = 2;
            lost.LastError = again.Error;
            lost.Status = fifth;
            lost.FinishedAt = Noon.AddMinutes(-20);
        }

        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(AttemptErrors.TouchCeilingReached, await RefusalAsync(fresh, next));
        Assert.Equal(fifth, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == lost.Id, Ct)).Status);
    }

    /// <summary>
    /// A person whose line in this campaign was ended is refused a touch at the claim, and a reply in their thread is not:
    /// the reply carries on their conversation, and only the person reopens the line.
    /// </summary>
    [Fact]
    public async Task A_finished_person_is_refused_a_touch_and_not_a_reply()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db, state: MembershipState.Finished);
        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using (var fresh = database.Open())
        {
            Assert.Equal(AttemptErrors.LineFinished, await RefusalAsync(fresh, send));
            var error = (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == send.Id, Ct)).Error!;
            Assert.Equal("The line with this person in this campaign was ended; only the person reopens it.", error.Message);
            Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        }

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
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var after = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await after.WorkItems.AsNoTracking().SingleAsync(w => w.Id == reply.Id, Ct)).Status);
    }

    /// <summary>
    /// The line ended through <c>campaign.finish_contacts</c>, and read back: the people list shows the person finished, the
    /// journal holds the line that ended it with its reason, the touch the person still had open was cancelled by it, a touch
    /// made afterwards is refused at the claim, and a reply in the person's thread still goes to the person's approval.
    /// </summary>
    [Fact]
    public async Task A_line_ended_by_the_manager_reads_back_finished_and_the_claim_refuses_its_touches_but_not_a_reply()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var open = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var memberships = new MembershipService(db, new JournalWriter(clock), clock, TestCanceller.New(clock));

        await memberships.FinishAsync(new FinishContactsRequest(campaign.PublicId, [contact.PublicId], new ActorRef(ActorType.Role, "manager"), "Four touches and no answer."), Ct);

        var listed = await memberships.ListAsync(new ListContactsRequest(campaign.PublicId, MembershipState.Finished, null, null), Ct);
        Assert.Equal(contact.PublicId, Assert.Single(listed.Items).Contact.Id);
        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.ContactsFinished, Ct);
        Assert.Equal(("Four touches and no answer.", ActorType.Role, "manager"), (line.Reason, line.ActorType, line.ActorId));
        Assert.Equal(WorkItemStatus.Cancelled, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == open.Id, Ct)).Status);

        var send = Sending(db, campaign, contact);
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
        await db.SaveChangesAsync(Ct);

        // One item a campaign a scan: the touch is refused in the first, the reply parked in the second.
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(AttemptErrors.LineFinished, await RefusalAsync(fresh, send));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == reply.Id, Ct)).Status);
    }

    /// <summary>The ceiling is read once a scan: one raised between two scans lets the next touch through to the person's approval.</summary>
    [Fact]
    public async Task A_ceiling_raised_between_scans_applies_from_the_next_scan()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LineHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
        var refused = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 3 };
        var next = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(AttemptErrors.TouchCeilingReached, await RefusalAsync(fresh, refused));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == next.Id, Ct)).Status);
    }

    /// <summary>
    /// No notice of its own: the refusal ends the item failed and writes <c>workitem_failed</c> as the dispatcher, about the
    /// touch and not about a check-in, so the summon's filter of a check-in's own lines keeps it, and the next summon wakes
    /// the manager with it, who ends the line.
    /// </summary>
    [Theory]
    [InlineData(MembershipState.Finished, AttemptErrors.LineFinished)]
    [InlineData(MembershipState.Enrolled, AttemptErrors.TouchCeilingReached)]
    public async Task A_line_s_refusal_wakes_the_manager_through_workitem_failed(MembershipState state, string code)
    {
        using var harness = new DispatchHarness(Noon);
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 2 };
        RoutedToTheLineProvider(harness);
        string touch;
        await using (var db = harness.Open())
        {
            var (campaign, contact) = await LineSceneAsync(db, state: state);
            await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
            await TouchedAsync(db, campaign, contact, WorkItemStatus.Succeeded);
            touch = Sending(db, campaign, contact).PublicId;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Empty(await harness.ClaimAsync(Ct));

        var failed = Assert.Single(await harness.ReadJournalAsync(touch, Ct), e => e.Kind == JournalKinds.WorkItemFailed);
        Assert.Equal((Actors.Dispatcher.Type, Actors.Dispatcher.Id), (failed.ActorType, failed.ActorId));
        Assert.Equal(code, (string?)failed.New!["code"]);

        Assert.Equal(1, await harness.SummonAsync(Ct));
        await using var read = harness.Open();
        var checkIn = await read.WorkItems.AsNoTracking().SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
        Assert.Equal(JournalKinds.WorkItemFailed, (string?)checkIn.Context["trigger"]);
        Assert.Equal(touch, (string?)checkIn.Context["cause"]!["work_item_id"]);
    }

    /// <summary>The claim's harness with a provider that touches and replies, and a ceiling of two.</summary>
    private Harness LineHarness()
    {
        var harness = new Harness(
            plugin: TestPlugins.Loaded(LineProvider, ["message.send", "campaign.enroll", "conversation.reply"]),
            route: new Route(LineProvider, null, null));
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = 2 };
        return harness;
    }

    /// <summary>
    /// The same provider loaded and routed by default in a whole dispatcher's composition, performing
    /// <paramref name="operations"/>, or <c>message.send</c> alone where none are named.
    /// </summary>
    private static void RoutedToTheLineProvider(DispatchHarness harness, params string[] operations)
    {
        var plugins = harness.Resolve<PluginRegistry>();
        plugins.Replace(
            new PluginSnapshot(
                PublicId.New(PluginProtocol.SnapshotIdPrefix),
                Noon,
                SnapshotSource.Startup,
                [TestPlugins.Loaded(LineProvider, operations.Length > 0 ? operations : ["message.send"])]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Snapshot.Id,
            new RouteSet(new Route(LineProvider, null, null), RouteSet.Empty.Operations),
            RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
    }

    /// <summary>An active campaign and a person who is its member in <paramref name="state"/>, reachable by email.</summary>
    private static async Task<(Campaign Campaign, Contact Contact)> LineSceneAsync(
        JasonDbContext db,
        string contactId = "cnt_01K52JR0000000000000000001",
        MembershipState state = MembershipState.Enrolled)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-7));
        var contact = new Contact { PublicId = contactId, FirstName = "Marta", CreatedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-7) };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"{contactId[^4..].ToLowerInvariant()}@example.test", IsPrimary = true });
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = state, AddedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-1) });
        await db.SaveChangesAsync(Ct);
        return (campaign, contact);
    }

    /// <summary>An attempt of <paramref name="item"/>, number <paramref name="number"/>, that lost its answer: it may have reached the person.</summary>
    private static async Task AmbiguousAsync(JasonDbContext db, WorkItem item, int number = 1)
    {
        var attempt = WorkItemFactory.NewAttempt(item, number, AttemptStatus.Failed, Noon.AddHours(-2));
        attempt.FinishedAt = Noon.AddMinutes(-90);
        attempt.Error = new AttemptErrorDto("provider_answer_lost", "The answer was lost.", true, Class: FailureClass.Ambiguous);
        item.AttemptCount = number;
        item.LastError = attempt.Error with { Trace = null };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Earlier work of <paramref name="operation"/> about the person, ended or left <paramref name="status"/>, with one attempt;
    /// a failed one ended with <paramref name="failed"/>.
    /// </summary>
    private static async Task<WorkItem> TouchedAsync(
        JasonDbContext db,
        Campaign campaign,
        Contact contact,
        WorkItemStatus status,
        FailureClass failed = FailureClass.Permanent,
        string operation = "message.send")
    {
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon.AddHours(-3));
        item.Contact = contact;
        item.Status = status;
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["channel"] = "email" } };
        db.WorkItems.Add(item);
        if (status is WorkItemStatus.Succeeded or WorkItemStatus.Failed)
        {
            item.FinishedAt = Noon.AddHours(-2);
            item.AttemptCount = 1;
            var attempt = WorkItemFactory.NewAttempt(item, 1, status == WorkItemStatus.Succeeded ? AttemptStatus.Succeeded : AttemptStatus.Failed, Noon.AddHours(-3));
            attempt.FinishedAt = Noon.AddHours(-2);
            if (status == WorkItemStatus.Failed)
            {
                attempt.Error = new AttemptErrorDto(
                    failed == FailureClass.Ambiguous ? "provider_answer_lost" : "recipient_rejected",
                    "The provider answered so.",
                    Retriable: false,
                    Class: failed);
                item.LastError = attempt.Error;
            }

            db.Attempts.Add(attempt);
        }
        else if (status is WorkItemStatus.Expired or WorkItemStatus.Cancelled)
        {
            item.FinishedAt = Noon.AddHours(-2);
        }

        await db.SaveChangesAsync(Ct);
        return item;
    }

    /// <summary>
    /// A <c>message.status</c> of <paramref name="campaign"/> naming <paramref name="send"/>, made by <paramref name="madeBy"/>,
    /// that ended <paramref name="status"/>: succeeded, answering <paramref name="state"/>, or failed, the provider answering
    /// that it holds no such send.
    /// </summary>
    private static WorkItem ReadSaid(
        JasonDbContext db,
        Campaign campaign,
        WorkItem send,
        string? state,
        WorkItemStatus status = WorkItemStatus.Succeeded,
        ActorType madeBy = ActorType.System)
    {
        var read = WorkItemFactory.NewProviderOp(campaign, HeldSends.Status, Noon.AddMinutes(-40));
        read.CreatedByType = madeBy;
        read.CreatedById = madeBy switch
        {
            ActorType.Human => "ada",
            ActorType.Role => "manager",
            _ => Actors.Dispatcher.Id,
        };
        read.Status = status;
        read.FinishedAt = Noon.AddMinutes(-30);
        read.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send.PublicId } };
        if (status == WorkItemStatus.Succeeded)
        {
            read.Result = ReadAnswer(state!);
        }
        else
        {
            read.LastError = new AttemptErrorDto("send_not_found", "The provider holds no such send.", Retriable: false, Class: FailureClass.Permanent);
        }

        db.WorkItems.Add(read);
        return read;
    }

    /// <summary>
    /// One more touch to the person, made and claimed through the whole dispatcher: the code the claim refused it with, or null
    /// where it went on to the person's approval.
    /// </summary>
    private static async Task<string?> TouchAsync(DispatchHarness harness, JasonDbContext db, Campaign campaign, Contact contact)
    {
        var touch = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.ClaimAsync(Ct));

        await using var fresh = harness.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == touch.Id, Ct);
        return row.Status == WorkItemStatus.AwaitingApproval ? null : await RefusalAsync(fresh, touch);
    }

    /// <summary>
    /// A read of <paramref name="letter"/> made by the campaign's manager, claimed and run through the whole dispatcher with
    /// the provider answering what the test queued: the state the read recorded.
    /// </summary>
    private static async Task<string?> ReadThroughAsync(DispatchHarness harness, JasonDbContext db, Campaign campaign, WorkItem letter)
    {
        var read = WorkItemFactory.NewProviderOp(campaign, HeldSends.Status, Noon);
        read.CreatedByType = ActorType.Role;
        read.CreatedById = "manager";
        read.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = letter.PublicId } };
        db.WorkItems.Add(read);
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.ClaimAsync(Ct));
        Assert.Equal(read.PublicId, work.WorkItemPublicId);
        await ProviderBlockRaceTests.RunAsync(harness, work);

        await using var fresh = harness.Open();
        var ended = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == read.Id, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, ended.Status);
        return (string?)ended.Result!["state"];
    }

    /// <summary>The stand-in provider's answer to a read of a send, in one of the operation's five words.</summary>
    private static PluginInvocationResult StatusAnswer(string state) => new(
        new InvocationOutcome.Succeeded(ReadAnswer(state), null, new OutcomeDiagnostics(12, 1, 0, 1)),
        new InvocationProvenance(
            LineProvider,
            "1.0.0",
            "sha256:0",
            PluginProtocol.CurrentVersion,
            PluginProtocol.OperationContractVersion,
            PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC",
            "att_01K5B7Q2WE5X3M9T0YH4C6RDND",
            PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNE"),
        Launch: null);

    /// <summary>What a read of a send answers, in one of the operation's five words; the letter's text is not carried.</summary>
    private static JsonObject ReadAnswer(string state) => new()
    {
        ["state"] = state,
        ["channel"] = "email",
        ["cancelable"] = state == "queued",
        ["accepted_for"] = null,
        ["sent_at"] = state == "sent" ? "2026-09-14T10:05:00Z" : null,
        ["message_id"] = state == "sent" ? "msg-2001" : null,
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = state == "failed" ? "The receiving server refused the message." : null,
        ["failure_code"] = state == "failed" ? "relay_refused" : null,
        ["cancelled_at"] = state == "cancelled" ? "2026-09-14T10:05:00Z" : null,
    };
}
