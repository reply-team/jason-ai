using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
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

    private static async Task<(string Line, string Run)> ReviewAsync(JasonDbContext db, Reading reading, string sendId, string? hash, ReviewResult result, string? code, string? note)
    {
        var review = WorkItemFactory.NewAiRole(reading.Campaign, role: "reviewer", now: Noon);
        review.Status = WorkItemStatus.Processing;
        var run = WorkItemFactory.NewAttempt(review, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(review);
        await db.SaveChangesAsync(Ct);
        var line = await new ReviewService(db, new JournalWriter(new FixedClock(Noon))).ReviewAsync(
            new WorkItemReviewRequest(sendId, hash, result, code, note, new ActorRef(ActorType.Attempt, run.PublicId)),
            Ct);
        return (line.Id, run.PublicId);
    }
}
