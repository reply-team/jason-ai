using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The park moves a send's doubts onto the approval's preview, where the person deciding reads who doubted and why, and
/// clears both sentences from the row in the same save: the creator's <c>ask_person</c> and the review that stands when it
/// doubts. No line of the park carries a sentence.
/// </summary>
public partial class ClaimerTests
{
    private const string AskSentence = "SENTINEL_PARK_ASK_51c0: they asked whether we work with agencies.";

    private const string ReviewSentence = "SENTINEL_PARK_REVIEW_7e3d: the subject promises a discount nobody offered.";

    [Fact]
    public async Task Doubts_TheCreatorsAskPerson_IsOnThePreview_WithItsSentence_AndTheRowIsCleared()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null, context: Asking("sensitive_topic"), note: AskSentence);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        var entry = Assert.Single(approval.Preview[Doubts.PreviewKey]!.AsArray())!.AsObject();
        Assert.Equal(
            new JsonObject
            {
                ["source"] = "ask_person",
                ["by"] = new JsonObject { ["type"] = "attempt", ["id"] = reading.Run.PublicId },
                ["role"] = "responder",
                ["code"] = "sensitive_topic",
                ["note"] = AskSentence,
                ["line"] = null,
            }.ToJsonString(),
            entry.ToJsonString());
        await AssertClearedAndUnsaidAsync(fresh, send.Id);
    }

    [Fact]
    public async Task Doubts_ADoubtingReview_IsOnThePreview_WithItsReviewerItsLineAndItsSentence()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        var (line, run) = await ReviewAsync(db, reading, send, ReviewResult.Doubts, "off_brief", ReviewSentence);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        var entry = Assert.Single(approval.Preview[Doubts.PreviewKey]!.AsArray())!.AsObject();
        Assert.Equal(
            new JsonObject
            {
                ["source"] = "review",
                ["by"] = new JsonObject { ["type"] = "attempt", ["id"] = run },
                ["role"] = "reviewer",
                ["code"] = "off_brief",
                ["note"] = ReviewSentence,
                ["line"] = line,
            }.ToJsonString(),
            entry.ToJsonString());
        await AssertClearedAndUnsaidAsync(fresh, send.Id);
    }

    [Fact]
    public async Task Doubts_BothDoors_AreTwoEntries_TheCreatorsFirst()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null, context: Asking("new_kind_of_person"), note: AskSentence);
        await ReviewAsync(db, reading, send, ReviewResult.Doubts, "wrong_recipient", note: null);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var doubts = (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Preview[Doubts.PreviewKey]!.AsArray();
        Assert.Equal(["ask_person", "review"], doubts.Select(entry => (string?)entry!["source"]));
        Assert.Equal([AskSentence, null], doubts.Select(entry => (string?)entry!["note"]));
        await AssertClearedAndUnsaidAsync(fresh, send.Id);
    }

    /// <summary>A pass is no doubt: the preview carries no doubts, and nothing is left on the row.</summary>
    [Fact]
    public async Task Doubts_APassingReview_PutsNoDoubtOnThePreview()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        await ReviewAsync(db, reading, send, ReviewResult.Passed, code: null, note: null);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.False((await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Preview.ContainsKey(Doubts.PreviewKey));
        await AssertClearedAndUnsaidAsync(fresh, send.Id);
    }

    /// <summary>
    /// A sentence on the row that no standing doubt stands behind — a review's, left by a review that lost a race with an
    /// edit — never reaches the preview, and is cleared all the same.
    /// </summary>
    [Fact]
    public async Task Doubts_ASentenceNoStandingDoubtStandsBehind_IsClearedAndNeverShown()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        await db.WorkItems.Where(w => w.PublicId == send.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReviewNote, ReviewSentence), Ct);
        db.ChangeTracker.Clear();

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.False((await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Preview.ContainsKey(Doubts.PreviewKey));
        await AssertClearedAndUnsaidAsync(fresh, send.Id);
        Assert.DoesNotContain("SENTINEL_PARK_REVIEW_7e3d", (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Preview.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Parked again after an edit of its input: the creator's doubt and its sentence are carried over from the first
    /// approval, since <c>ask_person</c> is never taken back; the review was about the text the edit replaced, so it is not.
    /// </summary>
    [Fact]
    public async Task Doubts_AReparkAfterAnEdit_CarriesTheCreatorsSentence_AndDropsTheReview()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null, context: Asking("unsupported_claim"), note: AskSentence);
        await ReviewAsync(db, reading, send, ReviewResult.Doubts, "off_brief", ReviewSentence);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        db.ChangeTracker.Clear();
        await Items(db).UpdateAsync(
            new WorkItemUpdateRequest(
                send.Id,
                new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") },
                null, default, default, default, default, default, default, default, default,
                new ActorRef(ActorType.Human, "operator"),
                Reason: null),
            Ct);
        db.ChangeTracker.Clear();
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal([ApprovalStatus.Superseded, ApprovalStatus.Pending], approvals.Select(a => a.Status));
        var entry = Assert.Single(approvals[1].Preview[Doubts.PreviewKey]!.AsArray())!;
        Assert.Equal(("ask_person", "unsupported_claim", AskSentence), ((string?)entry["source"], (string?)entry["code"], (string?)entry["note"]));
        await AssertClearedAndUnsaidAsync(fresh, send.Id);
    }

    /// <summary>
    /// A doubt parks for the person before a standing approval can release the send: a draft the standing approval would
    /// cover, carrying its creator's <c>ask_person</c> or a doubting review of its text, parks with its doubts on the preview
    /// and the standing approval's answer <c>doubted</c>; no row is born approved and nothing is applied.
    /// </summary>
    [Theory]
    [InlineData("ask_person")]
    [InlineData("review")]
    public async Task Doubts_ADoubtedSendUnderACoveringStandingApproval_ParksForThePerson(string door)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        if (door == "ask_person")
        {
            item.Context[Doubts.AskPersonKey] = new JsonObject { ["code"] = "sensitive_topic" };
            item.AskPersonNote = AskSentence;
        }

        await db.SaveChangesAsync(Ct);
        if (door == "review")
        {
            await ReviewAsync(db, reading, item.PublicId, ReviewSubject.Hash(item), ReviewResult.Doubts, "off_brief", ReviewSentence);
        }

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.ApprovalRequired), (approval.Status, approval.Reason));
        var entry = Assert.Single(approval.Preview[Doubts.PreviewKey]!.AsArray())!;
        Assert.Equal((door, door == "review" ? ReviewSentence : AskSentence), ((string?)entry["source"], (string?)entry["note"]));
        Assert.Equal(
            new JsonObject { ["verdict"] = verdict, ["category"] = "interested", ["why"] = StandingApprovals.Why.Doubted }.ToJsonString(),
            approval.Preview["standing_approval"]!.ToJsonString());
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalStandingApplied).ToListAsync(Ct));
        await AssertClearedAndUnsaidAsync(fresh, item.PublicId);
    }

    /// <summary>A passing review is no doubt: the standing approval releases the send in the same claim, as it does unreviewed.</summary>
    [Fact]
    public async Task Doubts_APassedSendUnderACoveringStandingApproval_IsReleased()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);
        await ReviewAsync(db, reading, item.PublicId, ReviewSubject.Hash(item), ReviewResult.Passed, code: null, note: null);

        var work = Assert.Single(await ClaimSettledAsync(harness, db));

        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
        Assert.False(approval.Preview.ContainsKey(Doubts.PreviewKey));
        Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalStandingApplied).ToListAsync(Ct));
    }

    /// <summary>
    /// A review is refused exactly where the claim would not read it. Released under a standing approval and then failed for
    /// a reason that is retried, the send is queued again and its retry runs under its approved row; approved by a person, it
    /// goes under their decision. Neither reads a review, so a doubt recorded then would be accepted and never heard: it is
    /// refused <c>not_reviewable</c>, naming the approval by its id and no sentence, and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("a standing release, then a transient failure")]
    [InlineData("a person's approval")]
    public async Task Doubts_AReviewTheClaimWouldNotRead_IsRefused_AndWritesNothing(string path)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        var standing = path.StartsWith("a standing", StringComparison.Ordinal);
        if (standing)
        {
            await StandAsync(db, reading.Campaign);
        }

        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);
        var hash = ReviewSubject.Hash(item);
        if (standing)
        {
            Assert.Single(await ClaimSettledAsync(harness, db));
            var run = await db.Attempts.SingleAsync(a => a.WorkItemId == item.Id, Ct);
            run.Status = AttemptStatus.Failed;
            run.FinishedAt = Noon;
            run.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
            item.Status = WorkItemStatus.Created;
            await db.SaveChangesAsync(Ct);
        }
        else
        {
            Assert.Empty(await ClaimSettledAsync(harness, db));
            var asked = await db.Approvals.AsNoTracking().SingleAsync(Ct);
            var clock = new FixedClock(Noon);
            await new ApprovalService(db, new JournalWriter(clock), clock).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, Reason: null), Ct);
        }

        db.ChangeTracker.Clear();
        db.Attach(reading.Campaign);
        Assert.Equal(WorkItemStatus.Created, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        var approval = await db.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Approved, approval.Status);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => ReviewAsync(db, reading, item.PublicId, hash, ReviewResult.Doubts, "off_brief", ReviewSentence, harness.Claimer));

        Assert.Equal("not_reviewable", refused.Code);
        Assert.Contains($"approval '{approval.PublicId}' approved it as it now reads, and a retry goes under that approval", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_PARK_", refused.Message, StringComparison.Ordinal);
        await using var fresh = database.Open();
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemReviewed).ToListAsync(Ct));
        Assert.Null((await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).ReviewNote);
    }

    /// <summary>
    /// Everywhere else the claim hears a review, so it is accepted, and the next claim parks the send with that doubt on the
    /// preview: after an attempt that failed before any decision, after an expiry and a reopen, after a park and an edit by
    /// another, and after an approval, a retry that failed and an edit, whose text the approval no longer stands for.
    /// </summary>
    [Theory]
    [InlineData("an attempt that failed before any decision")]
    [InlineData("an expiry and a reopen")]
    [InlineData("a park and an edit")]
    [InlineData("an approval, a retry that failed and an edit")]
    public async Task Doubts_AReviewTheClaimWillRead_IsAccepted_AndTheNextClaimParksWithIt(string path)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == send.Id, Ct);
        switch (path)
        {
            case "an attempt that failed before any decision":
                var early = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon);
                early.FinishedAt = Noon;
                early.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
                item.AttemptCount = 1;
                await db.SaveChangesAsync(Ct);
                break;
            case "an expiry and a reopen":
                Assert.Empty(await ClaimSettledAsync(harness, db));
                await db.Approvals.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ApprovalStatus.Cancelled), Ct);
                await db.WorkItems.Where(w => w.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Created), Ct);
                break;
            case "a park and an edit":
                Assert.Empty(await ClaimSettledAsync(harness, db));
                await EditAsync();
                break;
            default:
                Assert.Empty(await ClaimSettledAsync(harness, db));
                var asked = await db.Approvals.AsNoTracking().SingleAsync(Ct);
                var clock = new FixedClock(Noon);
                await new ApprovalService(db, new JournalWriter(clock), clock).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, Reason: null), Ct);
                Assert.Single(await ClaimSettledAsync(harness, db));
                await db.Attempts.Where(a => a.WorkItemId == item.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Failed).SetProperty(a => a.FinishedAt, Noon), Ct);
                await db.WorkItems.Where(w => w.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Created), Ct);
                await EditAsync();
                break;
        }

        db.ChangeTracker.Clear();
        db.Attach(reading.Campaign);
        var now = await WorkItemService.LoadAsync(db, send.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, now.Status);

        var (line, _) = await ReviewAsync(db, reading, send.Id, ReviewSubject.Hash(now), ReviewResult.Doubts, "off_brief", ReviewSentence, harness.Claimer);

        db.ChangeTracker.Clear();
        Assert.Empty(await ClaimSettledAsync(harness, db));
        await using var fresh = database.Open();
        var parked = await fresh.Approvals.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync(Ct);
        Assert.Equal(ApprovalStatus.Pending, parked.Status);
        var doubt = Assert.Single(parked.Preview[Doubts.PreviewKey]!.AsArray())!;
        Assert.Equal(("review", line, ReviewSentence), ((string?)doubt["source"], (string?)doubt["line"], (string?)doubt["note"]));

        async Task EditAsync()
        {
            db.ChangeTracker.Clear();
            await Items(db).UpdateAsync(
                new WorkItemUpdateRequest(
                    send.Id,
                    new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") },
                    null, default, default, default, default, default, default, default, default,
                    new ActorRef(ActorType.Human, "operator"),
                    Reason: null),
                Ct);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Where the claim cannot be judged it fails closed. With the send's plugin unavailable the pre-flight refuses it, so no
    /// subject can be composed: a live approved row may still stand for the text once the plugin is back, and the review is
    /// refused naming that row; with no approved row the claim will decide anew, and the review is accepted. Back, and with
    /// the text edited since the approval, the row no longer stands for it, and the review is accepted.
    /// </summary>
    [Theory]
    [InlineData("unavailable, approved", false)]
    [InlineData("unavailable, never approved", true)]
    [InlineData("back, the text edited since the approval", true)]
    public async Task Doubts_AReviewTheClaimCannotJudgeNow_IsRefusedOnlyUnderALiveApproval(string path, bool accepted)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        if (path != "unavailable, never approved")
        {
            Assert.Empty(await ClaimSettledAsync(harness, db));
            var asked = await db.Approvals.AsNoTracking().SingleAsync(Ct);
            var clock = new FixedClock(Noon);
            await new ApprovalService(db, new JournalWriter(clock), clock).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, Reason: null), Ct);
        }

        harness.Load(PluginStatus.Unavailable);
        if (path == "back, the text edited since the approval")
        {
            harness.Load(PluginStatus.Valid);
            db.ChangeTracker.Clear();
            await Items(db).UpdateAsync(
                new WorkItemUpdateRequest(
                    send.Id,
                    new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") },
                    null, default, default, default, default, default, default, default, default,
                    new ActorRef(ActorType.Human, "operator"),
                    Reason: null),
                Ct);
        }

        db.ChangeTracker.Clear();
        db.Attach(reading.Campaign);
        var now = await WorkItemService.LoadAsync(db, send.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, now.Status);
        Task<(string Line, string Run)> Review() => ReviewAsync(db, reading, send.Id, ReviewSubject.Hash(now), ReviewResult.Doubts, "off_brief", ReviewSentence, harness.Claimer);

        if (accepted)
        {
            await Review();
            return;
        }

        var refused = await Assert.ThrowsAsync<ConflictException>(Review);
        var approval = await db.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("not_reviewable", refused.Code);
        Assert.Contains(
            $"approval '{approval.PublicId}' cannot be checked against the send's text right now; review it again once the send can be claimed",
            refused.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_PARK_", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A claim that comes and goes between a review's read and its save (a standing approval releases the send, its run fails
    /// for a reason that is retried, and the send is queued again) leaves the status as the review read it, so the guard on
    /// the status alone would let the doubt land after the decision it can no longer stop. The claim's question is asked
    /// again inside the save, which meets the approval that claim wrote and is refused <c>not_reviewable</c>; nothing is
    /// written. A person reviews here, since a reviewing run would keep the campaign busy and no claim would come between.
    /// </summary>
    [Fact]
    public async Task Doubts_AReviewWhoseSendWasReleasedAndRetriedDuringItsSave_IsRefused()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);
        var hash = ReviewSubject.Hash(item);
        await db.WorkItems.Where(w => w.Status == WorkItemStatus.Processing).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Succeeded).SetProperty(w => w.FinishedAt, Noon), Ct);
        await db.Attempts.Where(a => a.FinishedAt == null).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Succeeded).SetProperty(a => a.FinishedAt, Noon), Ct);

        var meanwhile = new Meanwhile(async () =>
        {
            await using var other = database.Open();
            Assert.Single(await harness.Claimer.ClaimAsync(other, 4, Ct));
            await other.Attempts.Where(a => a.WorkItemId == item.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AttemptStatus.Failed).SetProperty(a => a.FinishedAt, Noon), Ct);
            await other.WorkItems.Where(w => w.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Created), Ct);
        });
        await using var racing = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(meanwhile).Options);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => new ReviewService(racing, new JournalWriter(new FixedClock(Noon)), harness.Claimer).ReviewAsync(
            new WorkItemReviewRequest(item.PublicId, hash, ReviewResult.Doubts, "off_brief", ReviewSentence, new ActorRef(ActorType.Human, "ada")),
            Ct));

        Assert.True(meanwhile.Fired);
        Assert.Equal("not_reviewable", refused.Code);
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
        Assert.Contains($"approval '{approval.PublicId}' approved it as it now reads", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemReviewed).ToListAsync(Ct));
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal((WorkItemStatus.Created, (string?)null), (row.Status, row.ReviewNote));
    }

    /// <summary>
    /// The claim's question inside the save is asked of the database as it now stands, never of the entities the review loaded
    /// first. The send is approved while the person is named Ada, and the approval stops standing for it when the name becomes
    /// Anna, since the person's name is composed into what the send carries. A person reviews and doubts, and between the
    /// review's read and its save the name is set back to Ada: the approval stands for the send again, and the claim would run
    /// it under that approval without reading a review. The question asked inside the save meets the name as it is, and the
    /// review is refused <c>not_reviewable</c>, naming the approval; nothing is written, and the claim runs the send under it.
    /// </summary>
    [Fact]
    public async Task Doubts_AReviewWhoseSendAnApprovalStandsForAgainDuringItsSave_IsRefused()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        Assert.Empty(await ClaimSettledAsync(harness, db));
        var asked = await db.Approvals.AsNoTracking().SingleAsync(Ct);
        var clock = new FixedClock(Noon);
        await new ApprovalService(db, new JournalWriter(clock), clock).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, Reason: null), Ct);
        await db.Contacts.Where(c => c.Id == reading.Contact.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.FirstName, "Anna"), Ct);
        db.ChangeTracker.Clear();
        var now = await WorkItemService.LoadAsync(db, send.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, now.Status);

        var meanwhile = new Meanwhile(async () =>
        {
            await using var other = database.Open();
            await other.Contacts.Where(c => c.Id == reading.Contact.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.FirstName, "Ada"), Ct);
        });
        await using var racing = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(meanwhile).Options);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => new ReviewService(racing, new JournalWriter(new FixedClock(Noon)), harness.Claimer).ReviewAsync(
            new WorkItemReviewRequest(send.Id, ReviewSubject.Hash(now), ReviewResult.Doubts, "off_brief", ReviewSentence, new ActorRef(ActorType.Human, "ada")),
            Ct));

        Assert.True(meanwhile.Fired);
        Assert.Equal("not_reviewable", refused.Code);
        Assert.Contains($"approval '{asked.PublicId}' approved it as it now reads", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_PARK_", refused.Message, StringComparison.Ordinal);
        await using var fresh = database.Open();
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemReviewed).ToListAsync(Ct));
        Assert.Null((await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == now.Id, Ct)).ReviewNote);
        db.ChangeTracker.Clear();
        Assert.Single(await ClaimSettledAsync(harness, db));
    }

    /// <summary>
    /// The claim decides against the route and plugin snapshots it reads once it holds the database, so a review that held it
    /// first was asked the claim's question against the same routes or older ones. The send is approved under one provider;
    /// as the claim is about to begin, the route is set to another, under which the approval no longer stands, and a person's
    /// doubt is accepted. The claim then decides under the new route: it does not run the send under the old approval, which
    /// would leave the doubt unread, but parks it with the doubt on the preview.
    /// </summary>
    [Fact]
    public async Task Doubts_ARouteSetAsTheClaimBegins_IsTheRouteTheClaimDecidesBy_SoAReviewAcceptedThenIsRead()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(Provider, ["conversation.reply", "message.send", "campaign.enroll"]),
            route: new Route(Provider, null, null),
            others: [TestPlugins.Loaded(Social, ["message.send"])]);
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        Assert.Empty(await ClaimSettledAsync(harness, db));
        var asked = await db.Approvals.AsNoTracking().SingleAsync(Ct);
        var clock = new FixedClock(Noon);
        await new ApprovalService(db, new JournalWriter(clock), clock).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, Reason: null), Ct);
        db.ChangeTracker.Clear();
        var now = await WorkItemService.LoadAsync(db, send.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, now.Status);

        var meanwhile = new BeforeBegin(async () =>
        {
            harness.Reroute(new Route(Social, null, null));
            await using var other = database.Open();
            await new ReviewService(other, new JournalWriter(new FixedClock(Noon)), harness.Claimer).ReviewAsync(
                new WorkItemReviewRequest(send.Id, ReviewSubject.Hash(now), ReviewResult.Doubts, "off_brief", ReviewSentence, new ActorRef(ActorType.Human, "ada")),
                Ct);
        });
        await using var claiming = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(meanwhile).Options);

        var claimed = await harness.Claimer.ClaimAsync(claiming, 4, Ct);

        Assert.True(meanwhile.Fired);
        Assert.Empty(claimed);
        await using var fresh = database.Open();
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemReviewed, Ct);
        var parked = await fresh.Approvals.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, Social), (parked.Status, parked.PluginId));
        var doubt = Assert.Single(parked.Preview[Doubts.PreviewKey]!.AsArray())!;
        Assert.Equal(("review", line.PublicId, ReviewSentence), ((string?)doubt["source"], (string?)doubt["line"], (string?)doubt["note"]));
    }

    /// <summary>Another writer as a context is about to begin its first transaction: the action runs first, on contexts of its own.</summary>
    private sealed class BeforeBegin(Func<Task> act) : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        private int _fired;

        public bool Fired => _fired == 1;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
            System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionStartingEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await act().ConfigureAwait(false);
            }

            return result;
        }
    }

    /// <summary>
    /// Another writer between a review's read and its save: the first time the context is about to begin a transaction or to
    /// save with the review's line added, the action runs on a context of its own and commits, before this save takes the
    /// database.
    /// </summary>
    private sealed class Meanwhile(Func<Task> act) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor, Microsoft.EntityFrameworkCore.Diagnostics.IDbTransactionInterceptor
    {
        private int _fired;

        public bool Fired => _fired == 1;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            await FireAsync(eventData.Context!).ConfigureAwait(false);
            return result;
        }

        public async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
            System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionStartingEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            await FireAsync(eventData.Context!).ConfigureAwait(false);
            return result;
        }

        private async Task FireAsync(DbContext context)
        {
            if (_fired == 0
                && context.ChangeTracker.Entries<JournalEntry>().Any(e => e.State == EntityState.Added)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await act().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Both columns are empty after the park, and no line — the park's own or any other — says either sentence.</summary>
    private static async Task AssertClearedAndUnsaidAsync(JasonDbContext fresh, string sendId)
    {
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == sendId, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, row.Status);
        Assert.Null(row.AskPersonNote);
        Assert.Null(row.ReviewNote);
        var lines = await fresh.Journal.AsNoTracking().ToListAsync(Ct);
        Assert.Contains(lines, line => line.Kind == JournalKinds.ApprovalRequested);
        Assert.All(lines, line =>
        {
            var said = JsonSerializer.Serialize(JournalWriter.ToDto(line, null), JasonJson.Options);
            Assert.DoesNotContain("SENTINEL_PARK_", said, StringComparison.Ordinal);
        });
        Assert.All(await fresh.Attempts.AsNoTracking().ToListAsync(Ct), attempt =>
            Assert.DoesNotContain("SENTINEL_PARK_", JsonSerializer.Serialize(attempt.ContextSnapshot, JasonJson.Options), StringComparison.Ordinal));
    }

    private static JsonObject Asking(string code) => new()
    {
        [WorkItemService.InputKey] = Message(),
        [Doubts.AskPersonKey] = new JsonObject { ["code"] = code },
    };

    /// <summary>A review of the send by a running attempt of a reviewer's work in the campaign; the line's id and the attempt's.</summary>
    private static Task<(string Line, string Run)> ReviewAsync(JasonDbContext db, Reading reading, WorkItemDto send, ReviewResult result, string? code, string? note) =>
        ReviewAsync(db, reading, send.Id, send.ReviewHash, result, code, note);

    private static async Task<(string Line, string Run)> ReviewAsync(JasonDbContext db, Reading reading, string sendId, string? hash, ReviewResult result, string? code, string? note, IClaimReading? claim = null)
    {
        var review = WorkItemFactory.NewAiRole(reading.Campaign, role: "reviewer", now: Noon);
        review.Status = WorkItemStatus.Processing;
        var run = WorkItemFactory.NewAttempt(review, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(review);
        await db.SaveChangesAsync(Ct);
        var line = await new ReviewService(db, new JournalWriter(new FixedClock(Noon)), claim ?? Tests.Approvals.TheClaimReadsEveryReview.Instance).ReviewAsync(
            new WorkItemReviewRequest(sendId, hash, result, code, note, new ActorRef(ActorType.Attempt, run.PublicId)),
            Ct);
        return (line.Id, run.PublicId);
    }
}
