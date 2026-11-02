using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A standing approval at the claim. Where nothing has been decided about a drafted reply yet, the claim asks whether a
/// person's standing word for the campaign and the verdict's category covers it: covered, it is released in the same
/// claim under a row the person who set the word decided; uncovered, it parks as it always did, and the line a person
/// reads says why the standing word did not let it go.
/// </summary>
public partial class ClaimerTests
{
    private const string Provider = "stand-in-provider";
    private const string InThread = "501";
    private static readonly ActorRef Operator = new(ActorType.Human, "operator");
    private static readonly ActorRef ResponderByName = new(ActorType.Role, "responder");

    /// <summary>
    /// The draft the run that read the reply made for it, under the verdict that run recorded, goes in the claim that
    /// finds it: its row is approved, decided by the person who set the standing approval and asked of nobody, and the
    /// release is one line that names the standing approval, the verdict and the category. No question is put, so
    /// nothing waits to be notified.
    /// </summary>
    [Fact]
    public async Task Standing_CoveredReply_ReleasedInSameClaim_RowApprovedBySetter_LineApplied()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        var standing = await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await ClaimSettledAsync(harness, db));

        Assert.Equal((item.PublicId, "conversation.reply"), (work.WorkItemPublicId, work.Plan!.Contract.Id));
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
        Assert.Equal((ActorType.Human, "operator", (string?)null), (approval.DecidedByType, approval.DecidedById, approval.DecisionReason));
        Assert.Equal((Noon, (DateTime?)Noon), (approval.RequestedAt, approval.DecidedAt));
        Assert.Equal("Thursday at 10 works.", (string?)approval.Subject["input"]!["args"]!["body"]);
        Assert.False(approval.Preview.ContainsKey("standing_approval"));
        Assert.Equal(approval.PublicId, (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct)).Provenance!.ApprovalId);

        var applied = Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalStandingApplied).ToListAsync(Ct));
        Assert.Equal((ActorType.System, "dispatcher"), (applied.ActorType, applied.ActorId));
        Assert.Equal((standing.LineId, reading.Campaign.Id, item.PublicId), (applied.Key, applied.CampaignId, applied.WorkItemId));
        Assert.Equal(
            ["category", "id", "operation", "standing", "subject_hash", "verdict"],
            applied.New!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            (approval.PublicId, "conversation.reply", approval.SubjectHash, standing.LineId, verdict, "interested"),
            ((string?)applied.New!["id"], (string?)applied.New!["operation"], (string?)applied.New!["subject_hash"], (string?)applied.New!["standing"], (string?)applied.New!["verdict"], (string?)applied.New!["category"]));

        // What the notify step counts is a pending row; there is none, and no question was ever put.
        Assert.Empty(await fresh.Approvals.AsNoTracking().Where(a => a.Status == ApprovalStatus.Pending).ToListAsync(Ct));
        Assert.Empty(await fresh.Journal.AsNoTracking()
            .Where(e => e.Kind == JournalKinds.ApprovalRequested || e.Kind == JournalKinds.WorkItemAwaitingApproval)
            .ToListAsync(Ct));
    }

    /// <summary>
    /// Every condition, in the order the claim asks: the first that fails parks the draft for a person, as any reply is
    /// parked, and the preview the person reads names the verdict, its category and why the standing approval did not
    /// release it. The row is pending, so the notify step counts it; nothing is released and nothing is applied.
    /// </summary>
    [Theory]
    [InlineData(StandingApprovals.Why.NotCoverable)]
    [InlineData(StandingApprovals.Why.VerdictUnknown)]
    [InlineData(StandingApprovals.Why.VerdictSuperseded)]
    [InlineData(StandingApprovals.Why.ReactionNotDraft)]
    [InlineData(StandingApprovals.Why.VerdictNotAuthentic)]
    [InlineData(StandingApprovals.Why.NotTheVerdictsWork)]
    [InlineData(StandingApprovals.Why.Edited)]
    [InlineData(StandingApprovals.Why.PluginDiffers)]
    [InlineData(StandingApprovals.Why.ThreadDiffers)]
    [InlineData(StandingApprovals.Why.NoStandingApproval)]
    [InlineData(StandingApprovals.Why.OperationNotCovered)]
    [InlineData(StandingApprovals.Why.Expired)]
    [InlineData(StandingApprovals.Why.CeilingReached)]
    public async Task Standing_Uncovered_ParksAndIsNotified_PreviewSaysWhy(string why)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var plugin = why == StandingApprovals.Why.PluginDiffers ? "another-provider" : Provider;
        var thread = why == StandingApprovals.Why.ThreadDiffers ? "502" : InThread;
        var reading = await ReadingAsync(db, Inbound(plugin, thread));
        var verdict = await VerdictAsync(
            db,
            reading,
            why == StandingApprovals.Why.VerdictNotAuthentic ? ResponderByName : RunOf(reading),
            reaction: why == StandingApprovals.Why.ReactionNotDraft ? ReplyReaction.Escalate : ReplyReaction.Draft,
            plugin: plugin,
            thread: thread);
        if (why == StandingApprovals.Why.VerdictSuperseded)
        {
            await VerdictAsync(db, reading, RunOf(reading), category: "not_now");
        }

        var standing = why switch
        {
            StandingApprovals.Why.NoStandingApproval => null,
            StandingApprovals.Why.Expired => await StandAsync(db, reading.Campaign, expiresAt: new DateTimeOffset(Noon.AddMinutes(-1))),
            StandingApprovals.Why.CeilingReached => await StandAsync(db, reading.Campaign, maxReleases: 1),
            _ => await StandAsync(db, reading.Campaign),
        };
        if (why == StandingApprovals.Why.CeilingReached)
        {
            new JournalWriter(new FixedClock(new DateTimeOffset(Noon))).Append(
                db, Actors.Dispatcher, JournalKinds.ApprovalStandingApplied, reading.Campaign, key: standing!.LineId, updated: new JsonObject { ["category"] = "interested" });
        }

        var cited = why == StandingApprovals.Why.VerdictUnknown ? PublicId.New("jrn") : verdict;
        var item = why switch
        {
            StandingApprovals.Why.NotCoverable => Enrolment(db, reading.Campaign),
            StandingApprovals.Why.NotTheVerdictsWork => Drafted(db, reading, cited, by: Operator),
            StandingApprovals.Why.OperationNotCovered => Drafted(db, reading, cited, operation: "message.send"),
            _ => Drafted(db, reading, cited),
        };
        if (why == StandingApprovals.Why.NotCoverable)
        {
            item.Context[StandingApprovals.VerdictKey] = cited;
            item.CreatedByType = ActorType.Attempt;
            item.CreatedById = reading.Run.PublicId;
        }

        if (why == StandingApprovals.Why.Edited)
        {
            new JournalWriter(new FixedClock(new DateTimeOffset(Noon))).Append(
                db, RunOf(reading), JournalKinds.WorkItemContextUpdated, reading.Campaign, workItem: item, updated: new JsonObject { ["set"] = new JsonArray("input") });
        }

        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == item.Id).ToListAsync(Ct));
        var waiting = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.ApprovalRequired), (waiting.Status, waiting.Reason));
        var category = why is StandingApprovals.Why.NotCoverable or StandingApprovals.Why.VerdictUnknown ? null : "interested";
        Assert.Equal(
            new JsonObject { ["verdict"] = cited, ["category"] = category, ["why"] = why }.ToJsonString(),
            waiting.Preview["standing_approval"]!.ToJsonString());
        Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalRequested && e.WorkItemId == item.PublicId).ToListAsync(Ct));
        Assert.Equal(
            why == StandingApprovals.Why.CeilingReached ? 1 : 0,
            await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
    }

    /// <summary>
    /// A category is the verdict's word exactly: a standing approval for <c>interested</c> covers neither a refinement of
    /// it nor the same word in another case, and nothing is folded to make them meet.
    /// </summary>
    [Theory]
    [InlineData("very_interested")]
    [InlineData("Interested")]
    public async Task Standing_BaseDoesNotCoverRefinement(string said)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading), category: said);
        await StandAsync(db, reading.Campaign, category: "interested");
        Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var waiting = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((said, StandingApprovals.Why.NoStandingApproval), ((string?)waiting.Preview["standing_approval"]!["category"], (string?)waiting.Preview["standing_approval"]!["why"]));
    }

    /// <summary>A reply that names no verdict is not asked about: it parks exactly as it did before standing approvals.</summary>
    [Fact]
    public async Task Standing_NoVerdictInContext_ParksAsToday()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await VerdictAsync(db, reading, RunOf(reading));
        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict: "unused");
        item.Context.Remove(StandingApprovals.VerdictKey);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var waiting = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.ApprovalRequired), (waiting.Status, waiting.Reason));
        Assert.False(waiting.Preview.ContainsKey("standing_approval"));
    }

    /// <summary>
    /// Whose verdict can release a draft: a run of the campaign's work that the runtime gave the reply's plugin and thread,
    /// or a person who gave their name. A run of work about another thread, or of work the runtime gave no reply, a role
    /// named outside any run and a call that named nobody are all left for a person.
    /// </summary>
    [Theory]
    [InlineData("a run of the work that read the reply", null)]
    [InlineData("a run of work about another thread", StandingApprovals.Why.VerdictNotAuthentic)]
    [InlineData("a run of work the runtime gave no reply", StandingApprovals.Why.VerdictNotAuthentic)]
    [InlineData("a role named outside a run", StandingApprovals.Why.VerdictNotAuthentic)]
    [InlineData("a named person", null)]
    [InlineData("nobody named", StandingApprovals.Why.VerdictNotAuthentic)]
    [Critical(Family.S2, "a standing approval: only an authentic verdict releases a draft")]
    public async Task Standing_Authenticity_OneRowPerActor(string who, string? parks)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db, who switch
        {
            "a run of work about another thread" => Inbound(Provider, "777"),
            "a run of work the runtime gave no reply" => null,
            _ => Inbound(Provider, InThread),
        });
        var by = who switch
        {
            "a role named outside a run" => ResponderByName,
            "a named person" => Operator,
            "nobody named" => null,
            _ => RunOf(reading),
        };
        var verdict = await VerdictAsync(db, reading, by);
        await StandAsync(db, reading.Campaign);
        Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await AssertReleasedOrParkedAsync(database, claimed, parks);
    }

    /// <summary>
    /// The draft is the verdict author's. A run's verdict releases only what a run of that same work drafted, so another
    /// role's run cannot point its own draft at the responder's verdict and ride the standing approval; a person's verdict
    /// vouches for the category, and releases whoever drafted it. A person may cite a run's verdict on a draft of their
    /// own — the reference is true, so it is accepted — and that draft waits for a person.
    /// </summary>
    [Theory]
    [InlineData("the manager's run drafts under the responder's verdict", StandingApprovals.Why.NotTheVerdictsWork)]
    [InlineData("a person drafts under a person's verdict", null)]
    [InlineData("a person drafts under a run's verdict", StandingApprovals.Why.NotTheVerdictsWork)]
    [Critical(Family.S2, "a standing approval: a run's verdict releases only that work's own draft")]
    public async Task Standing_Authorship_TheDraftIsTheVerdictAuthors(string how, string? parks)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, how == "a person drafts under a person's verdict" ? Operator : RunOf(reading));
        await StandAsync(db, reading.Campaign);
        if (how == "the manager's run drafts under the responder's verdict")
        {
            var manager = WorkItemFactory.NewAiRole(reading.Campaign, role: "manager", now: Noon);
            manager.Status = WorkItemStatus.Processing;
            manager.AttemptCount = 1;
            var run = WorkItemFactory.NewAttempt(manager, 1, AttemptStatus.Running, Noon);
            db.WorkItems.Add(manager);
            Drafted(db, reading, verdict, by: new ActorRef(ActorType.Attempt, run.PublicId));
            await db.SaveChangesAsync(Ct);
        }
        else
        {
            // Through the verb a person uses, so the reference is checked as it is for everybody: it is accepted.
            var clock = new FixedClock(new DateTimeOffset(Noon));
            await new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings()).CreateAsync(
                new WorkItemCreateRequest(
                    reading.Campaign.PublicId,
                    WorkItemKind.ProviderOp,
                    null,
                    "conversation.reply",
                    reading.Contact.PublicId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new JsonObject { [WorkItemService.InputKey] = ReplyingIn(InThread), [StandingApprovals.VerdictKey] = verdict },
                    null,
                    Operator,
                    null),
                Ct);
        }

        var claimed = await ClaimSettledAsync(harness, db);

        await AssertReleasedOrParkedAsync(database, claimed, parks);
    }

    /// <summary>
    /// A person's answer, carried by a verdict a role recorded. Where the responder asked which category a reply is and a
    /// person answered, the manager's run records the answer as the verdict that stands; carrying the decision, the line is
    /// that person's word, so the draft a later responder writes under it goes under a standing approval, whoever wrote
    /// it. The same line without the decision is the manager's own, which no standing approval trusts.
    /// </summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, StandingApprovals.Why.VerdictNotAuthentic)]
    [Critical(Family.S2, "a standing approval: a verdict carrying a person's answer is that person's word")]
    public async Task Standing_VerdictCarryingAPersonsAnswer_ReleasesTheRespondersDraft(bool carriesTheAnswer, string? parks)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var guess = await VerdictAsync(db, reading, RunOf(reading), category: "not_now", reaction: ReplyReaction.Escalate);
        var question = new Decision
        {
            PublicId = PublicId.New("dec"),
            CampaignId = reading.Campaign.Id,
            WorkItemId = reading.Responder.Id,
            AttemptId = reading.Run.Id,
            Question = "Which kind of reply is this?",
            Options = new JsonArray(
                new JsonObject { ["label"] = "interested", ["detail"] = null },
                new JsonObject { ["label"] = "not_now", ["detail"] = null }),
            Status = DecisionStatus.Answered,
            RaisedAt = Noon,
            Answer = "They asked for times.",
            ChosenOption = "interested",
            AnsweredAt = Noon,
            AnsweredByType = Operator.Type,
            AnsweredById = Operator.Id,
        };
        var manager = WorkItemFactory.NewAiRole(reading.Campaign, role: "manager", now: Noon);
        manager.Status = WorkItemStatus.Processing;
        manager.AttemptCount = 1;
        var review = WorkItemFactory.NewAttempt(manager, 1, AttemptStatus.Running, Noon);
        var responder = WorkItemFactory.NewAiRole(reading.Campaign, role: "responder", now: Noon);
        responder.Status = WorkItemStatus.Processing;
        responder.AttemptCount = 1;
        responder.Context = new JsonObject { ["inbound_reply"] = Inbound(Provider, InThread) };
        var again = WorkItemFactory.NewAttempt(responder, 1, AttemptStatus.Running, Noon);
        db.Decisions.Add(question);
        db.WorkItems.AddRange(manager, responder);
        await db.SaveChangesAsync(Ct);

        var answer = await VerdictAsync(
            db, reading, new ActorRef(ActorType.Attempt, review.PublicId), decision: carriesTheAnswer ? question.PublicId : null);
        await StandAsync(db, reading.Campaign);
        Drafted(db, reading, answer, by: new ActorRef(ActorType.Attempt, again.PublicId));
        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        Assert.NotEqual(guess, answer);
        await AssertReleasedOrParkedAsync(database, claimed, parks);
    }

    /// <summary>
    /// A standing approval decides once. Released work that fails and is tried again meets its own approved row and runs,
    /// although the standing approval was taken back since — as a person's own approval would; a draft whose text changed
    /// since is another draft, parked for a person, and the standing approval is not asked again.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standing_UnsetAfterRelease_RetryStillRuns_InputChanged_ParksForPerson(bool changed)
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await VerdictAsync(db, reading, RunOf(reading));
        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);
        Assert.Single(await ClaimSettledAsync(harness, db));

        var first = await db.Attempts.SingleAsync(a => a.WorkItemId == item.Id, Ct);
        first.Status = AttemptStatus.Failed;
        first.FinishedAt = Noon;
        first.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
        item.Status = WorkItemStatus.Created;
        if (changed)
        {
            item.Context = new JsonObject { [WorkItemService.InputKey] = ReplyingIn(InThread, "Thursday at 11 works."), [StandingApprovals.VerdictKey] = verdict };
        }

        await db.SaveChangesAsync(Ct);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        await new StandingApprovalService(db, new JournalWriter(clock), clock)
            .UnsetAsync(new ApprovalUnsetStandingRequest(reading.Campaign.PublicId, "interested", Operator), Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(1, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
        if (changed)
        {
            Assert.Empty(claimed);
            Assert.Equal([ApprovalStatus.Superseded, ApprovalStatus.Pending], approvals.Select(a => a.Status));
            Assert.Equal("input_changed", approvals[1].Reason);
            Assert.False(approvals[1].Preview.ContainsKey("standing_approval"));
        }
        else
        {
            Assert.Equal(item.PublicId, Assert.Single(claimed).WorkItemPublicId);
            var approval = Assert.Single(approvals);
            Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
        }
    }

    /// <summary>
    /// A ceiling counts the releases since the line that set it: once reached, the next draft waits for a person; the same
    /// standing approval set again is a new line, so the count starts again from it — and the draft already waiting stays
    /// with the person it was put to.
    /// </summary>
    [Fact]
    public async Task Standing_CeilingReached_Parks_ReSetRestartsCount()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await StandAsync(db, reading.Campaign, maxReleases: 1);
        var first = Drafted(db, reading, await VerdictAsync(db, reading, RunOf(reading), message: "msg-1"));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(first.PublicId, Assert.Single(await ClaimSettledAsync(harness, db)).WorkItemPublicId);

        var second = Drafted(db, reading, await VerdictAsync(db, reading, RunOf(reading), message: "msg-2"));
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        var again = await StandAsync(db, reading.Campaign, maxReleases: 1);
        Assert.Equal((0, true), (again.Released, again.InForce));
        var third = Drafted(db, reading, await VerdictAsync(db, reading, RunOf(reading), message: "msg-3"));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(third.PublicId, Assert.Single(await ClaimSettledAsync(harness, db)).WorkItemPublicId);

        await using var fresh = database.Open();
        Assert.Equal(2, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.StandingApprovalSet, Ct));
        var parked = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.Status == ApprovalStatus.Pending, Ct);
        Assert.Equal((second.Id, StandingApprovals.Why.CeilingReached), (parked.WorkItemId, (string?)parked.Preview["standing_approval"]!["why"]));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == second.Id, Ct)).Status);
    }

    /// <summary>
    /// The do-not-contact register refuses before any approval is asked, so a covered draft to a person it holds is refused,
    /// never released; and a draft the standing approval released is still held by an open block of its account, its
    /// decision kept for the moment the block clears.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standing_SuppressedContact_RefusedBeforeApproval_ProviderBlock_HoldsReleasedItem(bool blocked)
    {
        var plugin = blocked ? BlockRows.Plugin : Provider;
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(plugin, ["conversation.reply"]),
            route: new Route(plugin, null, null),
            register: blocked ? null : new Harness.EverybodyIsSuppressed());
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db, Inbound(plugin, InThread));
        var verdict = await VerdictAsync(db, reading, RunOf(reading), plugin: plugin);
        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        var block = blocked ? OpenBlock(db, reading.Campaign, nextTrialAt: Noon.AddHours(1)) : null;
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        if (blocked)
        {
            Assert.Equal((WorkItemStatus.Waiting, 0), (row.Status, row.AttemptCount));
            Assert.Equal(block!.Id, (await fresh.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == item.Id, Ct)).ProviderBlockId);
            var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
            Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
            Assert.Equal(1, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
        }
        else
        {
            Assert.Equal(WorkItemStatus.Failed, row.Status);
            Assert.Equal("suppressed", (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct)).Error!.Code);
            Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
            Assert.Equal(0, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
        }
    }

    /// <summary>
    /// A draft parked before the standing approval was set was put to a person, and stays with them: a later standing
    /// approval never answers a question already asked, even where something lets the draft back into the queue.
    /// </summary>
    [Fact]
    public async Task Standing_ParkedBeforeSet_StaysParked()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var item = Drafted(db, reading, await VerdictAsync(db, reading, RunOf(reading)));
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        await StandAsync(db, reading.Campaign);
        item.Status = WorkItemStatus.Created;
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var waiting = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Pending, waiting.Status);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        Assert.Equal(0, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
    }

    /// <summary>
    /// A draft put to a person stays theirs even when the question is gone: it expired unanswered, which cancelled its row,
    /// a standing approval was set, and a new deadline returned it to the queue. It comes to the claim with no live row,
    /// and it is parked for a person again rather than released, because somebody was already asked about it.
    /// </summary>
    [Fact]
    public async Task Standing_ParkedThenExpiredThenReopened_StaysWithThePerson()
    {
        using var harness = StandingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var item = Drafted(db, reading, await VerdictAsync(db, reading, RunOf(reading)));
        item.DueAt = Noon.AddHours(1);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ClaimSettledAsync(harness, db));

        var later = new FixedClock(new DateTimeOffset(Noon.AddHours(2)));
        Assert.Equal(1, await new Expirer(new JournalWriter(later), later).ExpireAsync(db, Ct));
        await StandAsync(db, reading.Campaign);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        await new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings()).UpdateAsync(
            new WorkItemUpdateRequest(item.PublicId, null, null, default, Optional<DateTimeOffset?>.Of(new DateTimeOffset(Noon.AddHours(3))), default, default, default, default, default, default, Operator, null),
            Ct);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.Equal(
            [ApprovalStatus.Cancelled, ApprovalStatus.Pending],
            await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).Select(a => a.Status).ToListAsync(Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        Assert.Equal(0, await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
    }

    /// <summary>The draft runs in this claim under a standing approval, or it parks for a person saying why.</summary>
    private static async Task AssertReleasedOrParkedAsync(TestDatabase database, IReadOnlyList<ClaimedWork> claimed, string? parks)
    {
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        var applied = await fresh.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct);
        if (parks is null)
        {
            Assert.Single(claimed);
            Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval, 1), (approval.Status, approval.Reason, applied));
        }
        else
        {
            Assert.Empty(claimed);
            Assert.Equal((ApprovalStatus.Pending, parks, 0), (approval.Status, (string?)approval.Preview["standing_approval"]!["why"], applied));
        }
    }

    /// <summary>
    /// A claim made once what was in flight has ended: the claim takes nothing from a campaign with work in progress, and
    /// by the time a draft is claimed the run that read the reply has ended, as has whatever an earlier claim released.
    /// </summary>
    private static async Task<IReadOnlyList<ClaimedWork>> ClaimSettledAsync(Harness harness, JasonDbContext db)
    {
        var inFlight = await db.WorkItems
            .Include(w => w.Attempts)
            .Where(w => w.Status == WorkItemStatus.Scheduled || w.Status == WorkItemStatus.Processing)
            .ToListAsync(Ct);
        foreach (var item in inFlight)
        {
            item.Status = WorkItemStatus.Succeeded;
            item.FinishedAt = Noon;
            foreach (var attempt in item.Attempts.Where(a => a.FinishedAt is null))
            {
                attempt.Status = AttemptStatus.Succeeded;
                attempt.FinishedAt = Noon;
            }
        }

        await db.SaveChangesAsync(Ct);
        return await harness.Claimer.ClaimAsync(db, 4, Ct);
    }

    private static Harness StandingHarness() => new(
        plugin: TestPlugins.Loaded(Provider, ["conversation.reply", "message.send", "campaign.enroll"]),
        route: new Route(Provider, null, null));

    private static JsonObject Inbound(string plugin, string thread) => new() { ["plugin"] = plugin, ["thread"] = thread };

    /// <summary>
    /// A campaign, the person who replied, and the run reading the reply: a responder's work the runtime gave the reply's
    /// plugin and thread under <c>inbound_reply</c> — or, given null, nothing at all — in progress under a running attempt.
    /// </summary>
    private static async Task<Reading> ReadingAsync(JasonDbContext db, JsonObject? inboundReply)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        var responder = WorkItemFactory.NewAiRole(campaign, role: "responder", now: Noon);
        responder.Status = WorkItemStatus.Processing;
        responder.AttemptCount = 1;
        responder.Context = inboundReply is null ? [] : new JsonObject { ["inbound_reply"] = inboundReply };
        var run = WorkItemFactory.NewAttempt(responder, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = MembershipState.Enrolled, AddedAt = Noon, UpdatedAt = Noon });
        db.WorkItems.Add(responder);
        await db.SaveChangesAsync(Ct);
        return new Reading(campaign, contact, responder, run);
    }

    private static Task<Reading> ReadingAsync(JasonDbContext db) => ReadingAsync(db, Inbound(Provider, InThread));

    private static ActorRef RunOf(Reading reading) => new(ActorType.Attempt, reading.Run.PublicId);

    /// <summary>A verdict on the reply, recorded through the verb a role or a person uses; its line's id.</summary>
    private static async Task<string> VerdictAsync(
        JasonDbContext db,
        Reading reading,
        ActorRef? by,
        string category = "interested",
        ReplyReaction reaction = ReplyReaction.Draft,
        string plugin = Provider,
        string thread = InThread,
        string message = "msg-1",
        string? decision = null)
    {
        var line = await new ReplyService(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon)))).RecordAsync(
            new ReplyRecordRequest(
                reading.Campaign.PublicId,
                plugin,
                thread,
                message,
                new DateTimeOffset(Noon.AddMinutes(-30)),
                category,
                0.9,
                reaction,
                ContactId: reading.Contact.PublicId,
                Actor: by,
                DecisionId: decision),
            Ct);
        return line.Id;
    }

    /// <summary>A person's standing approval for the campaign, set an hour before the claim.</summary>
    private static Task<StandingApprovalDto> StandAsync(
        JasonDbContext db,
        Campaign campaign,
        string category = "interested",
        IReadOnlyList<string>? operations = null,
        DateTimeOffset? expiresAt = null,
        int? maxReleases = null)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon.AddHours(-1)));
        return new StandingApprovalService(db, new JournalWriter(clock), clock).SetAsync(
            new ApprovalSetStandingRequest(campaign.PublicId, category, operations, expiresAt, maxReleases, Operator),
            Ct);
    }

    /// <summary>The draft answering the verdict, made by the run that read the reply unless somebody else is named.</summary>
    private static WorkItem Drafted(JasonDbContext db, Reading reading, string verdict, ActorRef? by = null, string operation = "conversation.reply")
    {
        var item = WorkItemFactory.NewProviderOp(reading.Campaign, operation, Noon);
        item.Contact = reading.Contact;
        var author = by ?? RunOf(reading);
        item.CreatedByType = author.Type;
        item.CreatedById = author.Id;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = operation == "message.send"
                ? new JsonObject
                {
                    ["channel"] = "email",
                    ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                    ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = "Thursday at 10 works." },
                }
                : ReplyingIn(InThread),
            [StandingApprovals.VerdictKey] = verdict,
        };
        db.WorkItems.Add(item);
        return item;
    }

    private static JsonObject ReplyingIn(string thread, string body = "Thursday at 10 works.") => new()
    {
        ["channel"] = "email",
        ["thread"] = new JsonObject { ["external_id"] = thread },
        ["body"] = body,
    };

    private sealed record Reading(Campaign Campaign, Contact Contact, WorkItem Responder, Attempt Run);
}
