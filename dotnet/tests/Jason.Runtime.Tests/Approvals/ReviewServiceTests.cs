using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// <c>workitem.review</c>: a review of a send before it goes, recorded as the reserved line <c>workitem_reviewed</c> under
/// the send's review hash, by a running attempt of the campaign's role work other than the work that made the send, or by
/// a named person who did not make it; and <see cref="Reviews.StandingAsync"/>, the read the claim makes of them.
/// </summary>
public class ReviewServiceTests
{
    private const string Sentence = "SENTINEL_REVIEW_9b2a: the price quoted is last year's.";

    private static readonly DateTime Noon = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pass_is_one_reserved_line_under_the_review_hash_naming_the_send_and_the_reviewing_attempt()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).ReviewAsync(Pass(seed), Ct);

        Assert.Equal(JournalKinds.WorkItemReviewed, entry.Kind);
        Assert.Equal((seed.Campaign.PublicId, seed.Send.PublicId, seed.ReviewRun.PublicId), (entry.CampaignId, entry.WorkItemId, entry.AttemptId));
        Assert.Equal(seed.Contact.PublicId, entry.ContactId);
        Assert.Equal(new ActorRef(ActorType.Attempt, seed.ReviewRun.PublicId), entry.Actor);
        Assert.Equal(ReviewSubject.Hash(seed.Send), entry.Key);
        Assert.Null(entry.Old);
        Assert.Null(entry.Reason);
        Assert.Equal("""{"result":"passed","code":null,"operation":"message.send","role":"reviewer"}""", entry.New!.ToJsonString());

        var standing = await Reviews.StandingAsync(db, seed.Send, Ct);
        Assert.Equal(new ReviewStanding(ReviewResult.Passed, null, entry.Id, entry.Actor, "reviewer"), standing);
    }

    [Fact]
    public async Task A_doubt_carries_its_code_on_the_line_and_its_sentence_on_the_row_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).ReviewAsync(Doubt(seed, "sensitive_topic") with { Note = $" {Sentence} " }, Ct);

        Assert.Equal("""{"result":"doubts","code":"sensitive_topic","operation":"message.send","role":"reviewer"}""", entry.New!.ToJsonString());
        Assert.DoesNotContain("SENTINEL_REVIEW_9b2a", JsonSerializer.Serialize(entry, JasonJson.Options), StringComparison.Ordinal);
        Assert.All(await db.Journal.AsNoTracking().ToListAsync(Ct), line =>
            Assert.DoesNotContain("SENTINEL_REVIEW_9b2a", JsonSerializer.Serialize(JournalWriter.ToDto(line, null), JasonJson.Options), StringComparison.Ordinal));
        db.ChangeTracker.Clear();
        Assert.Equal(Sentence, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == seed.Send.Id, Ct)).ReviewNote);
        Assert.Equal((ReviewResult.Doubts, "sensitive_topic"), await StandsAsync(db, seed));
    }

    /// <summary>A doubt stands against any later pass of the same text: only the person's decision on the approval answers it.</summary>
    [Fact]
    public async Task A_doubt_stands_against_a_later_pass_of_the_same_text()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        await Service(db).ReviewAsync(Doubt(seed, "off_brief") with { Note = Sentence }, Ct);
        await Service(db).ReviewAsync(Pass(seed), Ct);

        Assert.Equal((ReviewResult.Doubts, "off_brief"), await StandsAsync(db, seed));
        db.ChangeTracker.Clear();
        Assert.Equal(Sentence, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == seed.Send.Id, Ct)).ReviewNote);
    }

    /// <summary>
    /// The review is bound to the text it read: an update that names the input changes the hash, so the review no longer
    /// stands, its sentence is cleared, and a review made with the hash read before the edit is refused as stale.
    /// </summary>
    [Fact]
    public async Task An_update_that_names_the_input_changes_the_hash_and_voids_the_review()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var before = ReviewSubject.Hash(seed.Send);
        await Service(db).ReviewAsync(Doubt(seed, "unsupported_claim") with { Note = Sentence }, Ct);

        await Items(db).UpdateAsync(Patch(seed.Send.PublicId, new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") }), Ct);

        db.ChangeTracker.Clear();
        var send = await WorkItemService.LoadAsync(db, seed.Send.PublicId, Ct);
        Assert.NotEqual(before, ReviewSubject.Hash(send));
        Assert.Null(await Reviews.StandingAsync(db, send, Ct));
        Assert.Null(send.ReviewNote);
        var stale = await Assert.ThrowsAsync<ConflictException>(() => Service(db).ReviewAsync(Pass(seed) with { ReviewHash = before }, Ct));
        Assert.Equal("review_stale", stale.Code);
    }

    /// <summary>An update that leaves the input alone leaves the review standing, its sentence with it.</summary>
    [Fact]
    public async Task An_update_that_leaves_the_input_alone_leaves_the_review_standing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await Service(db).ReviewAsync(Doubt(seed, "other") with { Note = Sentence }, Ct);

        await Items(db).UpdateAsync(Patch(seed.Send.PublicId, new JsonObject { ["planner_note"] = "kept for the manager" }), Ct);

        db.ChangeTracker.Clear();
        var send = await WorkItemService.LoadAsync(db, seed.Send.PublicId, Ct);
        Assert.Equal(ReviewResult.Doubts, (await Reviews.StandingAsync(db, send, Ct))!.Result);
        Assert.Equal(Sentence, send.ReviewNote);
    }

    [Fact]
    public async Task No_review_recorded_is_no_standing_review()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        Assert.Null(await Reviews.StandingAsync(db, seed.Send, Ct));
    }

    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "a reason", "reason", "not_allowed" },
        { "no hash", "review_hash", "required" },
        { "no result", "result", "required" },
        { "a doubt without its code", "code", "required" },
        { "a code outside the list", "code", "invalid" },
        { "a pass with a code", "code", "not_allowed" },
        { "a pass with a note", "note", "not_allowed" },
        { "a note too long", "note", "too_long" },
        { "a note of two lines", "note", "invalid" },
        { "no actor", "actor", "required" },
        { "a role by name", "actor.type", "not_allowed" },
        { "a person who gave no name", "actor.id", "required" },
        { "an attempt of another campaign", "actor.id", "invalid" },
        { "an attempt of provider work", "actor.id", "invalid" },
        { "an attempt that has finished", "actor.id", "invalid" },
        { "an attempt nobody made", "actor.id", "unknown" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_review_the_verb_may_not_take_is_refused_at_its_field_and_writes_nothing(string why, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var request = why switch
        {
            "a reason" => Pass(seed) with { Reason = "looks fine" },
            "no hash" => Pass(seed) with { ReviewHash = " " },
            "no result" => Pass(seed) with { Result = null },
            "a doubt without its code" => Doubt(seed, null!),
            "a code outside the list" => Doubt(seed, "looks_off"),
            "a pass with a code" => Pass(seed) with { Code = "other" },
            "a pass with a note" => Pass(seed) with { Note = Sentence },
            "a note too long" => Doubt(seed, "other") with { Note = new string('a', DoubtCodes.MaxNoteLength + 1) },
            "a note of two lines" => Doubt(seed, "other") with { Note = "one line\nand another" },
            "no actor" => Pass(seed) with { Actor = null },
            "a role by name" => Pass(seed) with { Actor = new ActorRef(ActorType.Role, "reviewer") },
            "a person who gave no name" => Pass(seed) with { Actor = new ActorRef(ActorType.Human) },
            "an attempt of another campaign" => Pass(seed) with { Actor = Run(await RunningAsync(db, WorkItemFactory.NewAiRole(Add(db, WorkItemFactory.NewCampaign("Another", now: Noon)), "reviewer", Noon))) },
            "an attempt of provider work" => Pass(seed) with { Actor = Run(await RunningAsync(db, WorkItemFactory.NewProviderOp(seed.Campaign, "conversation.get", Noon))) },
            "an attempt that has finished" => Pass(seed) with { Actor = Run(await RunningAsync(db, WorkItemFactory.NewAiRole(seed.Campaign, "reviewer", Noon), AttemptStatus.Succeeded)) },
            "an attempt nobody made" => Pass(seed) with { Actor = new ActorRef(ActorType.Attempt, "att_nobody") },
            _ => throw new ArgumentOutOfRangeException(nameof(why), why, null),
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).ReviewAsync(request, Ct));

        Assert.Contains(refused.Details!, detail => detail.Field == field && detail.Code == code);
        Assert.All(refused.Details!, detail => Assert.DoesNotContain("SENTINEL_REVIEW_9b2a", detail.Message, StringComparison.Ordinal));
        await NothingWrittenAsync(database, seed);
    }

    /// <summary>
    /// Nobody reviews their own send: not the attempt that created it, not another attempt of the same work — a retry of the
    /// composer is the same author — and not the person who created it, by their name in any case. A person's own send goes
    /// to them as an approval, their own explicit yes.
    /// </summary>
    [Theory]
    [InlineData("the creating attempt")]
    [InlineData("another attempt of the creating work")]
    [InlineData("the person who created it")]
    [InlineData("the person who created it, in another case")]
    public async Task The_creator_of_a_send_may_not_review_it(string who)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db, createdBy: who.StartsWith("the person", StringComparison.Ordinal) ? Ada : null);
        var actor = who switch
        {
            "the creating attempt" => Run(seed.ComposeRun),
            "another attempt of the creating work" => Run(await RetryAsync(db, seed)),
            "the person who created it" => Ada,
            _ => new ActorRef(ActorType.Human, " ADA "),
        };

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Service(db).ReviewAsync(Pass(seed) with { Actor = actor }, Ct));

        Assert.Equal("review_by_creator", refused.Code);
        await NothingWrittenAsync(database, seed);
    }

    /// <summary>A person by name, from outside a run, reviews a send somebody else made, a run or another person.</summary>
    [Theory]
    [InlineData("a run")]
    [InlineData("another person")]
    public async Task A_named_person_reviews_a_send_somebody_else_made(string madeBy)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db, createdBy: madeBy == "a run" ? null : new ActorRef(ActorType.Human, "grace"));

        var entry = await Service(db).ReviewAsync(Doubt(seed, "wrong_recipient") with { Actor = Ada }, Ct);

        Assert.Equal(Ada, entry.Actor);
        Assert.Null(entry.AttemptId);
        Assert.Null((string?)entry.New!["role"]);
        Assert.Equal((ReviewResult.Doubts, "wrong_recipient"), await StandsAsync(db, seed));
    }

    public static TheoryData<string> NotReviewable() => ["role work", "a read", "a send awaiting approval", "a send that went", "a send that expired"];

    [Theory]
    [MemberData(nameof(NotReviewable))]
    public async Task Work_that_is_no_send_still_to_go_is_not_reviewable(string what)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var target = what switch
        {
            "role work" => Add(db, WorkItemFactory.NewAiRole(seed.Campaign, "researcher", Noon)),
            "a read" => Add(db, WorkItemFactory.NewProviderOp(seed.Campaign, "conversation.get", Noon)),
            _ => seed.Send,
        };
        target.Status = what switch
        {
            "a send awaiting approval" => WorkItemStatus.AwaitingApproval,
            "a send that went" => WorkItemStatus.Succeeded,
            "a send that expired" => WorkItemStatus.Expired,
            _ => target.Status,
        };
        await db.SaveChangesAsync(Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(db).ReviewAsync(Pass(seed) with { WorkItemId = target.PublicId, ReviewHash = ReviewSubject.Hash(target) }, Ct));

        Assert.Equal("not_reviewable", refused.Code);
    }

    /// <summary>
    /// A review is made before the send is first decided about. Queued again after a run that ended, or after a question
    /// put to a person that was then withdrawn, the send is refused <c>not_reviewable</c>, the refusal naming the run or the
    /// approval by its id, and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("a send that ran")]
    [InlineData("a send that was put to a person")]
    public async Task A_send_queued_again_after_it_was_decided_about_or_run_is_not_reviewable(string what)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        string named;
        if (what == "a send that ran")
        {
            var run = WorkItemFactory.NewAttempt(seed.Send, 1, AttemptStatus.Failed, Noon);
            run.FinishedAt = Noon;
            run.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
            seed.Send.AttemptCount = 1;
            named = $"it already ran as attempt '{run.PublicId}'";
        }
        else
        {
            var asked = new Approval
            {
                PublicId = PublicId.New(ApprovalGate.IdPrefix),
                WorkItemId = seed.Send.Id,
                CampaignId = seed.Send.CampaignId,
                Operation = "message.send",
                OperationVersion = 1,
                Subject = new JsonObject { ["operation"] = "message.send", ["work_item"] = seed.Send.PublicId },
                SubjectHash = "sha256:subject",
                Preview = new JsonObject(),
                PluginId = "stand-in-provider",
                PluginSnapshotId = "snp_one",
                RoutingSnapshotId = "rts_one",
                Status = ApprovalStatus.Cancelled,
                Reason = ApprovalGate.ApprovalRequired,
                RequestedAt = Noon,
            };
            db.Approvals.Add(asked);
            named = $"it was put to a person as approval '{asked.PublicId}'";
        }

        await db.SaveChangesAsync(Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(db).ReviewAsync(Doubt(seed, "off_brief") with { Note = Sentence }, Ct));

        Assert.Equal("not_reviewable", refused.Code);
        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentence, refused.Message, StringComparison.Ordinal);
        await using var fresh = database.Open();
        Assert.Equal(0, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemReviewed, Ct));
        Assert.Null((await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == seed.Send.Id, Ct)).ReviewNote);
    }

    [Fact]
    public async Task A_send_waiting_on_its_review_is_reviewable()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        seed.Send.Status = WorkItemStatus.Waiting;
        await db.SaveChangesAsync(Ct);

        await Service(db).ReviewAsync(Pass(seed), Ct);

        Assert.Equal(ReviewResult.Passed, (await Reviews.StandingAsync(db, seed.Send, Ct))!.Result);
    }

    /// <summary>
    /// Inside a run a review speaks as its run: the request's header names the attempt it comes from, and a review that
    /// names anybody else — a person, another attempt — is refused, so a run cannot pass its own text under another name.
    /// </summary>
    [Theory]
    [InlineData("a person", false)]
    [InlineData("another attempt", false)]
    [InlineData("the run itself", true)]
    public async Task Inside_a_run_a_review_speaks_as_its_run(string named, bool accepted)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var other = await RunningAsync(db, WorkItemFactory.NewAiRole(seed.Campaign, "reviewer", Noon));
        var actor = named switch
        {
            "a person" => Ada,
            "another attempt" => Run(other),
            _ => Run(seed.ReviewRun),
        };
        var service = new ReviewService(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon))), new RequestRun { AttemptId = seed.ReviewRun.PublicId });

        if (accepted)
        {
            Assert.Equal(seed.ReviewRun.PublicId, (await service.ReviewAsync(Pass(seed) with { Actor = actor }, Ct)).AttemptId);
            return;
        }

        var refused = await Assert.ThrowsAsync<ValidationException>(() => service.ReviewAsync(Pass(seed) with { Actor = actor }, Ct));
        Assert.Equal("actor", Assert.Single(refused.Details!).Field);
        await NothingWrittenAsync(database, seed);
    }

    /// <summary>
    /// A review that read the send still to go and saves after the claim parked it is refused <c>not_reviewable</c> and
    /// writes nothing, whatever it would have changed on the row: a pass, a doubt without a sentence, the same sentence again.
    /// The park arrives between the review's read and its save by construction, on a connection of its own.
    /// </summary>
    [Theory]
    [InlineData("a pass")]
    [InlineData("a doubt without a sentence")]
    [InlineData("the same sentence again")]
    public async Task A_review_that_loses_the_race_with_the_park_is_refused_not_reviewable(string review)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        if (review == "the same sentence again")
        {
            await Service(db).ReviewAsync(Doubt(seed, "off_brief") with { Note = Sentence }, Ct);
        }

        var before = await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemReviewed, Ct);
        var park = new CompetingInsert(
            database.File,
            context => context.ChangeTracker.Entries<JournalEntry>().Any(e => e.State == EntityState.Added)
                ? $"UPDATE work_items SET status = 'awaiting_approval' WHERE id = {seed.Send.Id};"
                : null);
        await using var racing = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(park).Options);
        var request = review switch
        {
            "a pass" => Pass(seed),
            "a doubt without a sentence" => Doubt(seed, "off_brief"),
            _ => Doubt(seed, "off_brief") with { Note = Sentence },
        };

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(racing).ReviewAsync(request, Ct));

        Assert.True(park.Fired);
        Assert.Equal("not_reviewable", refused.Code);
        await using var fresh = database.Open();
        Assert.Equal(before, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemReviewed, Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == seed.Send.Id, Ct)).Status);
    }

    /// <summary>The read is bounded by the send's own lines: answered from the index of the journal by work item.</summary>
    [Fact]
    public async Task The_read_of_a_send_s_reviews_uses_the_index_of_the_journal_by_work_item()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(db, Reviews.Lines(db, "wi_01K52JR0000000000000000007", "sha256:00").ToQueryString(), Ct);

        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX ix_journal_work_item_id_public_id (work_item_id=?)", StringComparison.Ordinal)),
            $"The plan is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal));
    }

    private static async Task<(ReviewResult, string?)> StandsAsync(JasonDbContext db, Seed seed)
    {
        var standing = await Reviews.StandingAsync(db, seed.Send, Ct);
        return (standing!.Result, standing.Code);
    }

    private static async Task NothingWrittenAsync(TestDatabase database, Seed seed)
    {
        await using var fresh = database.Open();
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemReviewed).ToListAsync(Ct));
        Assert.Null((await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == seed.Send.Id, Ct)).ReviewNote);
    }

    private static WorkItemReviewRequest Pass(Seed seed) =>
        new(seed.Send.PublicId, ReviewSubject.Hash(seed.Send), ReviewResult.Passed, Code: null, Note: null, Run(seed.ReviewRun));

    private static WorkItemReviewRequest Doubt(Seed seed, string code) =>
        new(seed.Send.PublicId, ReviewSubject.Hash(seed.Send), ReviewResult.Doubts, code, Note: null, Run(seed.ReviewRun));

    private static ActorRef Run(Attempt attempt) => new(ActorType.Attempt, attempt.PublicId);

    private static WorkItemUpdateRequest Patch(string id, JsonObject set) =>
        new(id, set, null, default, default, default, default, default, default, default, default, Ada, Reason: null);

    private static ReviewService Service(JasonDbContext db) => new(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon))));

    private static WorkItemService Items(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static JsonObject Message(string body = "Thursday at 10 works.") => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = body },
    };

    private static T Add<T>(JasonDbContext db, T entity)
        where T : class
    {
        db.Add(entity);
        return entity;
    }

    private static async Task<Attempt> RunningAsync(JasonDbContext db, WorkItem work, AttemptStatus status = AttemptStatus.Running)
    {
        work.Status = status == AttemptStatus.Running ? WorkItemStatus.Processing : WorkItemStatus.Succeeded;
        var attempt = WorkItemFactory.NewAttempt(work, work.Attempts.Count + 1, status, Noon);
        db.Add(work);
        await db.SaveChangesAsync(Ct);
        return attempt;
    }

    /// <summary>A second attempt of the composer's own work, running: the same author by another run.</summary>
    private static async Task<Attempt> RetryAsync(JasonDbContext db, Seed seed)
    {
        seed.ComposeRun.Status = AttemptStatus.Failed;
        var retry = WorkItemFactory.NewAttempt(seed.Compose, 2, AttemptStatus.Running, Noon);
        await db.SaveChangesAsync(Ct);
        return retry;
    }

    /// <summary>
    /// A campaign and its person; the composer's work, which made the send, and the reviewer's, both running; and the send,
    /// made by the composer's attempt unless a person is named.
    /// </summary>
    private static async Task<Seed> SeedAsync(JasonDbContext db, ActorRef? createdBy = null)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = MembershipState.Enrolled, AddedAt = Noon, UpdatedAt = Noon });

        var compose = WorkItemFactory.NewAiRole(campaign, "composer", Noon);
        var composeRun = await RunningAsync(db, compose);
        var review = WorkItemFactory.NewAiRole(campaign, "reviewer", Noon);
        var reviewRun = await RunningAsync(db, review);

        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        send.Contact = contact;
        var author = createdBy ?? Run(composeRun);
        send.CreatedByType = author.Type;
        send.CreatedById = author.Id;
        send.Context = new JsonObject { [WorkItemService.InputKey] = Message() };
        db.WorkItems.Add(send);
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact, compose, composeRun, reviewRun, send);
    }

    private sealed record Seed(Campaign Campaign, Contact Contact, WorkItem Compose, Attempt ComposeRun, Attempt ReviewRun, WorkItem Send);
}
