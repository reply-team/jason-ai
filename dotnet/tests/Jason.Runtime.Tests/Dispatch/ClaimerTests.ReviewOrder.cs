using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The order between a send and its review is the runtime's: the send is created waiting on the review's work, so it is
/// never claimed before that work has ended. The wait only orders: once the review's work ends, in any final state, the
/// send is ordinary queued work and the claim decides about it as ever, which today is to park it for the person.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>
    /// Created waiting: the send is not claimable while the review's work is still to run, and the resume step leaves it
    /// waiting. When that work ends — passed, failed or cancelled alike — the send goes back to the queue and the claim
    /// parks it for the person, under the reason every first park has.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Expired)]
    public async Task ReviewOrder_SendCreatedWaitingOnItsReview_IsClaimedOnlyOnceTheReviewEnds_InAnyFinalState(WorkItemStatus ends)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var review = await ReviewWorkAsync(db, reading);

        var send = await ComposeAsync(db, reading, waitOn: [review.PublicId]);

        Assert.Equal(WorkItemStatus.Waiting, send.Status);
        Assert.False(send.Eligible);
        var wait = Assert.Single(send.Wait!.On);
        Assert.Equal(review.PublicId, wait.WorkItemId);
        Assert.Null(send.Wait.AttemptId);

        Assert.Equal(0, await Resumer().ResumeAsync(db, Ct));
        Assert.Empty(await ClaimSettledAsync(harness, db, except: review));
        Assert.Equal(WorkItemStatus.Waiting, await StatusAsync(database, send.Id));

        await db.WorkItems.Where(w => w.Id == review.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, ends), Ct);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Equal(WorkItemStatus.Created, await StatusAsync(database, send.Id));

        Assert.Empty(await ClaimSettledAsync(harness, db, except: review));
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.ApprovalRequired), (approval.Status, approval.Reason));
        Assert.Equal(WorkItemStatus.AwaitingApproval, await StatusAsync(database, send.Id));
    }

    /// <summary>A send created with no wait at all is claimed without a review, and parks for the person as every send does.</summary>
    [Fact]
    public async Task ReviewOrder_SendCreatedWithNoWait_IsClaimedUnreviewed_AndParks()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);

        var send = await ComposeAsync(db, reading, waitOn: null);
        Assert.Equal(WorkItemStatus.Created, send.Status);

        Assert.Empty(await ClaimSettledAsync(harness, db));
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.ApprovalRequired), (approval.Status, approval.Reason));
    }

    /// <summary>
    /// The wait from creation is said once, as the item is written: one <c>workitem_waiting</c> line by the creator, naming
    /// the review's work and no attempt, beside the item's <c>workitem_created</c>.
    /// </summary>
    [Fact]
    public async Task ReviewOrder_TheWaitIsOneLineByTheCreator_NamingTheReviewsWork()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var review = await ReviewWorkAsync(db, reading);

        var send = await ComposeAsync(db, reading, waitOn: [review.PublicId]);

        var lines = await db.Journal.AsNoTracking().Where(e => e.WorkItemId == send.Id).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal([JournalKinds.WorkItemCreated, JournalKinds.WorkItemWaiting], lines.Select(e => e.Kind));
        var waiting = lines[1];
        Assert.Equal((ActorType.Attempt, reading.Run.PublicId), (waiting.ActorType, waiting.ActorId));
        Assert.Null(waiting.Key);
        Assert.Equal(review.PublicId, (string?)waiting.New!["on"]![0]);
        Assert.Equal("all", (string?)waiting.New!["mode"]);
    }

    /// <summary>The review's work: a role's item of the same campaign, made by the composing run, still to run.</summary>
    private static async Task<WorkItem> ReviewWorkAsync(JasonDbContext db, Reading reading)
    {
        var review = WorkItemFactory.NewAiRole(reading.Campaign, role: "reviewer", now: Noon);
        review.CreatedByType = ActorType.Attempt;
        review.CreatedById = reading.Run.PublicId;
        db.WorkItems.Add(review);
        await db.SaveChangesAsync(Ct);
        return review;
    }

    /// <summary>The send the composing run creates through the verb, waiting on the items named.</summary>
    private static Task<WorkItemDto> ComposeAsync(JasonDbContext db, Reading reading, IReadOnlyList<string>? waitOn, JsonObject? context = null, string? note = null) =>
        Items(db).CreateAsync(
            new WorkItemCreateRequest(
                reading.Campaign.PublicId,
                WorkItemKind.ProviderOp,
                Role: null,
                Operation: "message.send",
                reading.Contact.PublicId,
                ExecutionProfile: null,
                Priority: null,
                NotBefore: null,
                DueAt: null,
                TimeoutSeconds: null,
                HeartbeatSeconds: null,
                MaxAttempts: null,
                context ?? new JsonObject { [WorkItemService.InputKey] = Message() },
                ResultFormat: null,
                new ActorRef(ActorType.Attempt, reading.Run.PublicId),
                Reason: null,
                AskPersonNote: note,
                WaitOn: waitOn),
            Ct);

    private static JsonObject Message(string body = "Thursday at 10 works.") => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = body },
    };

    private static WorkItemService Items(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var journal = new JournalWriter(clock);
        return new WorkItemService(
            db,
            journal,
            clock,
            TestCanceller.New(clock),
            TestOptions.PluginSettings(),
            outcomes: new AttemptOutcomes(journal, clock, TestOptions.Settings()));
    }

    private static Resumer Resumer()
    {
        var clock = new FixedClock(Noon);
        return new Resumer(new JournalWriter(clock), clock);
    }

    private static async Task<WorkItemStatus> StatusAsync(TestDatabase database, string publicId)
    {
        await using var fresh = database.Open();
        return (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == publicId, Ct)).Status;
    }

    /// <summary>
    /// A settled claim that leaves one item alone: the review's work stays as the test set it, so only the send is the claim's
    /// to decide about.
    /// </summary>
    private static async Task<IReadOnlyList<ClaimedWork>> ClaimSettledAsync(Harness harness, JasonDbContext db, WorkItem except)
    {
        var held = await db.WorkItems.AsNoTracking().Where(w => w.Id == except.Id).Select(w => w.Status).SingleAsync(Ct);
        if (held == WorkItemStatus.Created)
        {
            // Out of the claim's reach for this read: a later scan is where the review's own run would be made.
            await db.WorkItems.Where(w => w.Id == except.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.NotBefore, Noon.AddDays(1)), Ct);
        }

        db.ChangeTracker.Clear();
        return await ClaimSettledAsync(harness, db);
    }
}
