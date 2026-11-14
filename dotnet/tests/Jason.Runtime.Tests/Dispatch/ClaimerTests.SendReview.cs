using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The critic the runtime creates for a send under an automatic mode, from the create to the claim. The send waits on it, so
/// it is claimed only once the critic has ended; the critic's pass releases it under the mode, and a critic that fails, is
/// cancelled, ends without a review or doubts, or a wait whose ceiling comes first, leaves the send to the person, unless a
/// standing approval covers it where the mode only could not vouch for it.
/// </summary>
public partial class ClaimerTests
{
    [Fact]
    [Critical(Family.S2, "the automatic mode: the critic the runtime creates for a send passes it, and the send goes")]
    public async Task SendReview_TheCriticTheRuntimeCreated_PassesTheSend_AndItGoesUnderTheMode()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ReviewedSendAsync(db, reading);
        var critic = await CriticOfAsync(db, send.Id);

        var pass = await ReviewAsCriticAsync(db, critic, send, ReviewResult.Passed);
        await EndAsync(db, critic, WorkItemStatus.Succeeded);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));

        var work = Assert.Single(await ClaimSettledAsync(harness, db));

        Assert.Equal(send.Id, work.WorkItemPublicId);
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Approved, "automatic_mode", ActorType.Human, "Marta"), (approval.Status, approval.Reason, approval.DecidedByType, approval.DecidedById));
        Assert.Equal(pass, (string?)approval.Preview["send_mode"]!["review"]);

        // The pass counts because the critic is the claim's own kind of critic: one created line by system:runtime, its key
        // naming this send and its operation, and no change of its context by anybody else.
        var run = (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == critic.PublicId, Ct)).PublicId;
        var sent = await WorkItemService.LoadAsync(fresh, send.Id, Ct);
        var lines = await AutomaticRelease.ReviewLines(fresh, send.Id).ToListAsync(Ct);
        Assert.Equal([run], await AutomaticRelease.CriticsAsync(fresh, sent, lines, Ct));
        var own = await AutomaticRelease.CriticLines(fresh, critic.PublicId).ToListAsync(Ct);
        Assert.Equal((JournalKinds.WorkItemCreated, ActorType.System, "runtime"), (Assert.Single(own).Kind, own[0].ActorType, own[0].ActorId));
    }

    /// <summary>The claim cannot launch the critic it was handed — nothing starts it any more — and fails it; the send then goes to the person.</summary>
    [Fact]
    [Critical(Family.S2, "the automatic mode: a critic that fails leaves the send to the person")]
    public async Task SendReview_ACriticThatFails_ReleasesTheSendUnreviewed_AndItWaitsForThePerson()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ReviewedSendAsync(db, reading);
        var critic = await CriticOfAsync(db, send.Id);

        Assert.Empty(await ClaimSettledAsync(harness, db));
        Assert.Equal((WorkItemStatus.Failed, AttemptErrors.RoleNotLaunchable), await EndedAsync(database, critic.PublicId));
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await ParkedNotReviewedAsync(database, send.Id);
    }

    [Fact]
    public async Task SendReview_ACriticCancelledByAPerson_ReleasesTheSendUnreviewed()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ReviewedSendAsync(db, reading);
        var critic = await CriticOfAsync(db, send.Id);

        await ReviewedItems(db).CancelAsync(new WorkItemCancelRequest(critic.PublicId, Marta, "not needed"), Ct);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await ParkedNotReviewedAsync(database, send.Id);
    }

    [Fact]
    public async Task SendReview_ACriticThatEndsWithoutAReview_ReleasesTheSendUnreviewed()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ReviewedSendAsync(db, reading);
        var critic = await CriticOfAsync(db, send.Id);

        await EndAsync(db, critic, WorkItemStatus.Succeeded);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await ParkedNotReviewedAsync(database, send.Id);
    }

    /// <summary>
    /// The wait's ceiling comes before the critic has run: the send is released without a review and parks for the person,
    /// and the critic's review that comes after the park is refused.
    /// </summary>
    [Fact]
    public async Task SendReview_AtTheWaitCeiling_TheSendParksUnreviewed_AndALaterReviewIsRefused()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ReviewedSendAsync(db, reading);
        var critic = await CriticOfAsync(db, send.Id);

        var late = new FixedClock(new DateTimeOffset(Noon).AddSeconds(new DispatcherOptions().AiRole.MaxWaitSeconds + 1));
        Assert.Equal(1, await new Resumer(new JournalWriter(late), late).ResumeAsync(db, Ct));
        Assert.Equal("wait_ceiling", (string?)(await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemResumed && e.WorkItemId == send.Id, Ct)).New!["because"]);
        Assert.Empty(await ClaimSettledAsync(harness, db, except: critic));
        await ParkedNotReviewedAsync(database, send.Id);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => ReviewAsCriticAsync(db, critic, send, ReviewResult.Passed));
        Assert.Equal("not_reviewable", refused.Code);
    }

    /// <summary>A doubt is the critic's answer too: it parks the send for the person, and nothing falls through it.</summary>
    [Fact]
    public async Task SendReview_ACriticThatDoubts_ParksTheSendForThePerson()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ReviewedSendAsync(db, reading);
        var critic = await CriticOfAsync(db, send.Id);

        await ReviewAsCriticAsync(db, critic, send, ReviewResult.Doubts);
        await EndAsync(db, critic, WorkItemStatus.Succeeded);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        Assert.Equal((ApprovalStatus.Pending, SendModeReasons.Doubted), (approval.Status, approval.Reason));
    }

    /// <summary>
    /// No pass that counts is a park the mode makes only because it cannot vouch for the send, so a standing approval that
    /// covers the send still decides by its own conditions, as it did before the modes: an answer whose critic failed goes
    /// under it.
    /// </summary>
    [Fact]
    public async Task SendReview_AStandingApproval_StillReleasesASendWhoseCriticFailed()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person);
        await StandAsync(db, reading.Campaign);
        var send = await ReviewedSendAsync(
            db,
            reading,
            "conversation.reply",
            new JsonObject { [WorkItemService.InputKey] = ReplyingIn(InThread), [StandingApprovals.VerdictKey] = verdict });
        var critic = await CriticOfAsync(db, send.Id);

        Assert.Empty(await ClaimSettledAsync(harness, db));
        Assert.Equal(WorkItemStatus.Failed, (await EndedAsync(database, critic.PublicId)).Status);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        var work = Assert.Single(await ClaimSettledAsync(harness, db));

        Assert.Equal(send.Id, work.WorkItemPublicId);
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Approved, "standing_approval"), (approval.Status, approval.Reason));
    }

    /// <summary>
    /// The create reads the mode the claim reads for the contact, by the claim's own function: the mode of the contact's
    /// temperature, or either mode while the contact waits on a verdict. A warm contact under a mode that releases only cold
    /// contacts is given no critic; a contact who waits is given one under either.
    /// </summary>
    [Theory]
    [InlineData("warm", "cold", false)]
    [InlineData("waiting", "cold", true)]
    [InlineData("waiting", "warm", true)]
    public async Task SendReview_TheCreateReadsTheContactsTemperature_AsTheClaimDoes(string contact, string automatic, bool critic)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        if (contact == "warm")
        {
            await WarmAsync(db, reading);
        }
        else
        {
            await ObservedAsync(db, reading, "2026-09-14T11:30:00.000Z");
        }

        await ModeAsync(db, automatic == "cold" ? ColdMode : WarmMode, "automatic");

        var send = await ReviewedSendAsync(db, reading, reviewed: critic);

        Assert.Equal(critic ? 1 : 0, await db.WorkItems.CountAsync(w => w.Role == AutomaticRelease.CriticRole && w.CreatedByType == ActorType.System, Ct));
        Assert.Equal(critic ? WorkItemStatus.Waiting : WorkItemStatus.Created, send.Status);
    }

    /// <summary>
    /// It fails closed the other way: a contact cold when the send is made gets no critic under a mode that releases only warm
    /// contacts, and warm by the claim the send has no pass, so it parks for the person.
    /// </summary>
    [Fact]
    public async Task SendReview_AContactColdAtTheCreateAndWarmAtTheClaim_ParksUnreviewed()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, WarmMode, "automatic");
        var send = await ReviewedSendAsync(db, reading, reviewed: false);

        await WarmAsync(db, reading);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await ParkedNotReviewedAsync(database, send.Id);
    }

    /// <summary>
    /// The send the composing run creates through the verb, by the service as the runtime builds it: a critic can be launched
    /// where the send is created, so the runtime creates one under an automatic mode.
    /// </summary>
    private static async Task<WorkItemDto> ReviewedSendAsync(JasonDbContext db, Reading reading, string operation = "message.send", JsonObject? context = null, bool reviewed = true)
    {
        var send = await ReviewedItems(db).CreateAsync(
            new WorkItemCreateRequest(
                reading.Campaign.PublicId,
                WorkItemKind.ProviderOp,
                Role: null,
                Operation: operation,
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
                Reason: null),
            Ct);
        Assert.Equal(reviewed ? WorkItemStatus.Waiting : WorkItemStatus.Created, send.Status);
        return send;
    }

    private static WorkItemService ReviewedItems(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var journal = new JournalWriter(clock);
        return new WorkItemService(
            db,
            journal,
            clock,
            TestCanceller.New(clock),
            TestOptions.PluginSettings(),
            outcomes: new AttemptOutcomes(journal, clock, TestOptions.Settings()),
            roles: TestOptions.RoleSettings(new RolesOptions { DefaultEntryCommand = ["agent"] }));
    }

    /// <summary>The critic the runtime created for the send, as the send's wait names it.</summary>
    private static async Task<WorkItem> CriticOfAsync(JasonDbContext db, string sendId)
    {
        var critic = await db.WorkItems.Include(w => w.Campaign).SingleAsync(w => w.Role == AutomaticRelease.CriticRole && w.CreatedByType == ActorType.System, Ct);
        Assert.Equal(sendId, SendReview.Named(critic.Context));
        return critic;
    }

    /// <summary>The critic's own run, as the claim would have made it, records its review of the send through the verb; its line's id.</summary>
    private static async Task<string> ReviewAsCriticAsync(JasonDbContext db, WorkItem made, WorkItemDto send, ReviewResult result)
    {
        // As this context tracks it now: a settled claim clears what it tracked.
        var critic = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.Id == made.Id, Ct);
        if (critic.Attempts.Count == 0)
        {
            critic.Status = WorkItemStatus.Processing;
            critic.AttemptCount = 1;
            WorkItemFactory.NewAttempt(critic, 1, AttemptStatus.Running, Noon);
            await db.SaveChangesAsync(Ct);
        }

        var run = critic.Attempts.Single();
        var line = await new ReviewService(db, new JournalWriter(new FixedClock(Noon)), Tests.Approvals.TheClaimReadsEveryReview.Instance).ReviewAsync(
            new WorkItemReviewRequest(
                send.Id,
                send.ReviewHash,
                result,
                result == ReviewResult.Doubts ? "off_brief" : null,
                Note: null,
                new ActorRef(ActorType.Attempt, run.PublicId)),
            Ct);
        return line.Id;
    }

    /// <summary>The critic's work ends as <paramref name="status"/>, its run with it where it has one.</summary>
    private static async Task EndAsync(JasonDbContext db, WorkItem made, WorkItemStatus status)
    {
        var critic = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.Id == made.Id, Ct);
        critic.Status = status;
        critic.FinishedAt = Noon;
        foreach (var run in critic.Attempts.Where(a => a.FinishedAt is null))
        {
            run.Status = AttemptStatus.Succeeded;
            run.FinishedAt = Noon;
        }

        await db.SaveChangesAsync(Ct);
    }

    private static async Task<(WorkItemStatus Status, string? Code)> EndedAsync(TestDatabase database, string publicId)
    {
        await using var fresh = database.Open();
        var item = await fresh.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.PublicId == publicId, Ct);
        return (item.Status, item.Attempts.SingleOrDefault()?.Error?.Code);
    }

    private static async Task ParkedNotReviewedAsync(TestDatabase database, string sendId)
    {
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == sendId, Ct);
        Assert.Equal((ApprovalStatus.Pending, SendModeReasons.NotReviewed), (approval.Status, approval.Reason));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == sendId, Ct)).Status);
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == "approval_automatic_applied", Ct));
    }
}
