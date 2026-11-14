using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The automatic mode at the claim. For a send nobody was ever asked about, the mode in force for its contact's temperature
/// releases it when a pass of its text by the critic or by a person stands, under a row born approved in the name of the
/// person who set the mode; anything the mode cannot vouch for parks it for the person, the reason on the row and the facts
/// on the preview. Nothing changes where no mode is in force.
/// </summary>
public partial class ClaimerTests
{
    private static readonly ActorRef Marta = new(ActorType.Human, "Marta");

    private const string ColdMode = "send_mode.cold";

    private const string WarmMode = "send_mode.warm";

    /// <summary>
    /// With no preference set both modes are every message: a send to a contact whose reply waits for a judgement, with a
    /// critic's pass standing, parks under the reason every first park has and says nothing of a mode, so warmth is never the
    /// claim's question. Nothing is released and no line of the mode is written.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "the automatic mode: with nothing set every send waits for a person")]
    public async Task SendMode_WithNothingSet_EverySendParksAsBefore()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ObservedAsync(db, reading, "2026-09-14T11:30:00.000Z");
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, "approval_required"), (approval.Status, approval.Reason));
        Assert.False(approval.Preview.ContainsKey("send_mode"));
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == "approval_automatic_applied", Ct));
    }

    /// <summary>
    /// A first message to a cold contact under the installation's automatic mode for cold contacts, the critic's pass
    /// standing for its text, goes in the claim that finds it: its row is approved with the reason <c>automatic_mode</c>, decided
    /// at the claim's moment by the person who set the mode, and one line names the release, the source, the temperature and
    /// the pass. The attempt runs under the row; nobody was asked, so no line says anybody was.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "the automatic mode: a send goes only under the critic's pass, in the setter's name")]
    public async Task SendMode_AColdSendTheCriticPassed_IsReleasedInTheSettersName()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        var pass = await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        var work = Assert.Single(await ClaimSettledAsync(harness, db));

        Assert.Equal(send.Id, work.WorkItemPublicId);
        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Approved, "automatic_mode"), (approval.Status, approval.Reason));
        Assert.Equal((ActorType.Human, "Marta"), (approval.DecidedByType, approval.DecidedById));
        Assert.Equal((Noon, (DateTime?)Noon), (approval.RequestedAt, approval.DecidedAt));
        Assert.Equal(
            new JsonObject { ["mode"] = "automatic", ["source"] = "preference", ["temperature"] = "cold", ["review"] = pass }.ToJsonString(),
            approval.Preview["send_mode"]!.ToJsonString());
        Assert.Equal(approval.PublicId, (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct)).Provenance!.ApprovalId);

        var applied = Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == "approval_automatic_applied").ToListAsync(Ct));
        Assert.Equal((ActorType.System, "dispatcher", "cold", send.Id), (applied.ActorType, applied.ActorId, applied.Key, applied.WorkItemId));
        Assert.Equal(
            new JsonObject
            {
                ["id"] = approval.PublicId,
                ["operation"] = "message.send",
                ["subject_hash"] = approval.SubjectHash,
                ["source"] = "preference",
                ["temperature"] = "cold",
                ["review"] = pass,
            }.ToJsonString(),
            applied.New!.ToJsonString());
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == JournalKinds.ApprovalRequested || e.Kind == JournalKinds.WorkItemAwaitingApproval, Ct));
    }

    /// <summary>
    /// The campaign's own mode outranks the preference, and the release goes in the name of whoever set it on the campaign:
    /// the row is decided by that person and the line says the mode came from the campaign.
    /// </summary>
    [Fact]
    public async Task SendMode_TheCampaignsOwnMode_ReleasesInItsSettersName()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "every_message");
        reading.Campaign.SendModeCold = SendMode.Automatic;
        reading.Campaign.SendModeColdSetBy = "Ines";
        await db.SaveChangesAsync(Ct);
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        Assert.Single(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("automatic_mode", ActorType.Human, "Ines"), (approval.Reason, approval.DecidedByType, approval.DecidedById));
        Assert.Equal("campaign", (string?)(await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == "approval_automatic_applied", Ct)).New!["source"]);
    }

    /// <summary>
    /// Each temperature follows its own mode. A contact whose reply a standing verdict judged a person's is warm, so the warm
    /// mode decides: automatic there releases the send, keyed warm; every message there parks it as before, whatever the cold
    /// mode says, and says nothing of a mode.
    /// </summary>
    [Theory]
    [InlineData("automatic", "every_message", true)]
    [InlineData("every_message", "automatic", false)]
    public async Task SendMode_AWarmContact_FollowsTheWarmMode(string warm, string cold, bool released)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, WarmMode, warm);
        await ModeAsync(db, ColdMode, cold);
        await WarmAsync(db, reading);
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        if (released)
        {
            Assert.Single(claimed);
            Assert.Equal("automatic_mode", approval.Reason);
            Assert.Equal("warm", (await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == "approval_automatic_applied", Ct)).Key);
        }
        else
        {
            Assert.Empty(claimed);
            Assert.Equal((ApprovalStatus.Pending, "approval_required"), (approval.Status, approval.Reason));
            Assert.False(approval.Preview.ContainsKey("send_mode"));
        }
    }

    /// <summary>
    /// Whose pass releases: the critic the runtime made for this send, or a person by name outside any run. A critic queued
    /// by hand, one the runtime made for another send, or one whose context anybody but the runtime patched is shown and
    /// releases nothing, as another role's pass; a line that names a person but carries an attempt is the run's, and a run's line that names another
    /// attempt than the one it carries is nobody's pass. None that counts parks the send <c>not_reviewed</c>, the newest pass
    /// of its text on the preview where one stands.
    /// </summary>
    [Theory]
    [InlineData("the critic the runtime made for the send", null)]
    [InlineData("the runtime's critic, then a copywriter's pass", null)]
    [InlineData("a person by name", null)]
    [InlineData("a person by name in the desktop's chat", "not_reviewed")]
    [InlineData("nobody", "not_reviewed")]
    [InlineData("a copywriter's run", "not_reviewed")]
    [InlineData("a person named on a run's line", "not_reviewed")]
    [InlineData("a critic's line naming another attempt", "not_reviewed")]
    [InlineData("a critic a person queued by hand", "not_reviewed")]
    [InlineData("the critic the runtime made for another send", "not_reviewed")]
    [InlineData("the runtime's critic, its context patched by a person", "not_reviewed")]
    [InlineData("the runtime's critic, its context patched by its own attempt", "not_reviewed")]
    [Critical(Family.S2, "the automatic mode: only the critic's or a person's pass releases")]
    public async Task SendMode_WhosePassReleases(string who, string? parks)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        var item = await db.WorkItems.Include(w => w.Campaign).SingleAsync(w => w.PublicId == send.Id, Ct);
        string? shown = who switch
        {
            "the critic the runtime made for the send" => await CriticPassAsync(db, reading, send.Id, send.ReviewHash),
            "a critic a person queued by hand" => await CriticPassAsync(db, reading, send.Id, send.ReviewHash, critic: Critic.QueuedByHand),
            "the critic the runtime made for another send" => await CriticPassAsync(db, reading, send.Id, send.ReviewHash, critic: Critic.AnotherSends),
            "the runtime's critic, its context patched by a person" => await CriticPassAsync(db, reading, send.Id, send.ReviewHash, critic: Critic.Patched),
            "the runtime's critic, its context patched by its own attempt" => await CriticPassAsync(db, reading, send.Id, send.ReviewHash, critic: Critic.PatchedByItself),
            "a person by name in the desktop's chat" => await InTheChatAsync(() => PersonPassAsync(db, send.Id, send.ReviewHash)),
            "a person by name" => (await new ReviewService(db, new JournalWriter(new FixedClock(Noon)), Tests.Approvals.TheClaimReadsEveryReview.Instance).ReviewAsync(
                new WorkItemReviewRequest(send.Id, send.ReviewHash, ReviewResult.Passed, null, null, new ActorRef(ActorType.Human, "ada")), Ct)).Id,
            "the runtime's critic, then a copywriter's pass" => await ThenAsync(await CriticPassAsync(db, reading, send.Id, send.ReviewHash), CriticPassAsync(db, reading, send.Id, send.ReviewHash, role: "copywriter")),
            "a copywriter's run" => await CriticPassAsync(db, reading, send.Id, send.ReviewHash, role: "copywriter"),
            "a person named on a run's line" => await PassLineAsync(db, item, new ActorRef(ActorType.Human, "ada"), await RunningAsync(db, reading, "critic"), "critic"),
            "a critic's line naming another attempt" => await PassLineAsync(db, item, new ActorRef(ActorType.Attempt, "att_01K52JR0000000000000000099"), await RunningAsync(db, reading, "critic"), "critic"),
            _ => null,
        };

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        if (parks is null)
        {
            Assert.Single(claimed);
            Assert.Equal((ApprovalStatus.Approved, "automatic_mode", shown), (approval.Status, approval.Reason, (string?)approval.Preview["send_mode"]!["review"]));
            return;
        }

        Assert.Empty(claimed);
        Assert.Equal((ApprovalStatus.Pending, parks), (approval.Status, approval.Reason));
        Assert.Equal(
            new JsonObject { ["mode"] = "automatic", ["source"] = "preference", ["temperature"] = "cold", ["review"] = shown }.ToJsonString(),
            approval.Preview["send_mode"]!.ToJsonString());
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == "approval_automatic_applied", Ct));
        Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalRequested && e.Reason == parks).ToListAsync(Ct));
    }

    /// <summary>
    /// An edit of the input after the critic passed it voids the pass: the send parks <c>edited_after_review</c>, the voided
    /// pass on the preview. The critic's pass of the new text releases it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendMode_AnEditAfterThePass_ParksEditedAfterReview(bool passedAgain)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        var voided = await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        db.ChangeTracker.Clear();
        var edited = await Items(db).UpdateAsync(
            new WorkItemUpdateRequest(
                send.Id,
                new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") },
                null, default, default, default, default, default, default, default, default,
                new ActorRef(ActorType.Human, "operator"),
                Reason: null),
            Ct);
        if (passedAgain)
        {
            await CriticPassAsync(db, reading, send.Id, edited.ReviewHash);
        }

        db.ChangeTracker.Clear();
        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        if (passedAgain)
        {
            Assert.Single(claimed);
            Assert.Equal("automatic_mode", approval.Reason);
        }
        else
        {
            Assert.Empty(claimed);
            Assert.Equal(("edited_after_review", voided), (approval.Reason, (string?)approval.Preview["send_mode"]!["review"]));
        }
    }

    /// <summary>
    /// A contact whose reply waits — no verdict judged it, or only one that says no nature — is neither cold nor warm, so
    /// nothing goes to them automatically: the send parks with the reason that says which, its temperature and mode unknown
    /// on the preview, the critic's pass notwithstanding.
    /// </summary>
    [Theory]
    [InlineData("no verdict", "awaiting_judgement")]
    [InlineData("a verdict with no nature", "awaiting_nature")]
    public async Task SendMode_AContactWhoseReplyWaits_ParksForThePerson(string judged, string parks)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        await ObservedAsync(db, reading, "2026-09-14T11:30:00.000Z");
        if (judged == "a verdict with no nature")
        {
            await NatureVerdictAsync(db, reading, nature: null);
        }

        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        Assert.Equal((ApprovalStatus.Pending, parks), (approval.Status, approval.Reason));
        Assert.Equal(
            new JsonObject { ["mode"] = null, ["source"] = null, ["temperature"] = null, ["review"] = null }.ToJsonString(),
            approval.Preview["send_mode"]!.ToJsonString());
    }

    /// <summary>
    /// An answer goes automatically only where the verdict it answers stands and says a person wrote the reply, and a role did
    /// not mark it for the person: an answer naming no verdict that stands, one whose verdict says no nature, an automatic
    /// reply or a bounce, and an escalated reply each park with a reason of their own. A request to stop never reaches the
    /// gate at all: the lock refuses it.
    /// </summary>
    [Theory]
    [InlineData("a verdict nobody recorded", "answers_no_verdict")]
    [InlineData("no verdict named, none on its thread", "answers_no_verdict")]
    [InlineData("a verdict that says no nature", "awaiting_nature")]
    [InlineData("an automatic reply", "reply_not_from_a_person")]
    [InlineData("a bounce", "reply_not_from_a_person")]
    [InlineData("an escalated reply", "escalated")]
    [InlineData("a person's reply", "automatic_mode")]
    [InlineData("a request to stop", "opted_out")]
    public async Task SendMode_AnAnswer_GoesOnlyToAPersonsReplyNobodyEscalated(string answers, string outcome)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        var verdict = answers switch
        {
            "a verdict nobody recorded" => "jrn_01K52JR0000000000000000077",
            "no verdict named, none on its thread" => null,
            "a verdict that says no nature" => await NatureVerdictAsync(db, reading, nature: null),
            "an automatic reply" => await NatureVerdictAsync(db, reading, ReplyNature.Automatic),
            "a bounce" => await NatureVerdictAsync(db, reading, ReplyNature.Bounce),
            "an escalated reply" => await NatureVerdictAsync(db, reading, ReplyNature.Person, ReplyReaction.Escalate),
            "a request to stop" => await NatureVerdictAsync(db, reading, ReplyNature.OptOut),
            _ => await NatureVerdictAsync(db, reading, ReplyNature.Person),
        };
        var item = Drafted(db, reading, verdict ?? "unused");
        if (verdict is null)
        {
            item.Context.Remove(StandingApprovals.VerdictKey);
        }

        await db.SaveChangesAsync(Ct);
        await CriticPassAsync(db, reading, item.PublicId, ReviewSubject.Hash(item));

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        if (outcome == "opted_out")
        {
            Assert.Equal(AttemptErrors.OptedOut, (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct)).Error!.Code);
            Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
            return;
        }

        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(outcome, approval.Reason);
        Assert.Equal(outcome == "automatic_mode" ? 1 : 0, claimed.Count);
    }

    /// <summary>
    /// An escalation is read across the conversation, not only on the reply answered. An answer to a person's reply waits
    /// for the person, <c>escalated</c>, where a verdict that stands on the same thread, about a reply at or after the one
    /// answered, says escalate; an escalation of an earlier reply holds nothing, nor does one a later verdict on the same
    /// reply replaced.
    /// </summary>
    [Theory]
    [InlineData("a later reply judged escalate", "escalated")]
    [InlineData("an earlier reply judged escalate", "automatic_mode")]
    [InlineData("a later escalation judged again as a draft", "automatic_mode")]
    public async Task SendMode_AnEscalationInTheConversation_HoldsTheAnswer(string meanwhile, string outcome)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        var answered = await NatureVerdictAsync(db, reading, ReplyNature.Person, message: "msg-1", minutesBefore: 30);
        switch (meanwhile)
        {
            case "a later reply judged escalate":
                await NatureVerdictAsync(db, reading, ReplyNature.Person, ReplyReaction.Escalate, message: "msg-2", minutesBefore: 10);
                break;
            case "an earlier reply judged escalate":
                await NatureVerdictAsync(db, reading, ReplyNature.Person, ReplyReaction.Escalate, message: "msg-0", minutesBefore: 50);
                break;
            case "a later escalation judged again as a draft":
                await NatureVerdictAsync(db, reading, ReplyNature.Person, ReplyReaction.Escalate, message: "msg-2", minutesBefore: 10);
                await NatureVerdictAsync(db, reading, ReplyNature.Person, ReplyReaction.Draft, message: "msg-2", minutesBefore: 10);
                break;
        }

        var item = Drafted(db, reading, answered);
        await db.SaveChangesAsync(Ct);
        await CriticPassAsync(db, reading, item.PublicId, ReviewSubject.Hash(item));

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(outcome, approval.Reason);
        Assert.Equal(outcome == "automatic_mode" ? 1 : 0, claimed.Count);
    }

    /// <summary>
    /// A moment that is not known holds: an earlier reply's escalation holds nothing against an answer whose moment is
    /// known, and holds it where the answered verdict carries none, since nothing tells which came first. The store refuses
    /// a verdict line without its moment, so the read is asked directly with the answered verdict's body.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SendMode_AnEscalationWhoseOrderIsNotKnown_Holds(bool momentKnown, bool holds)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var answered = await NatureVerdictAsync(db, reading, ReplyNature.Person, message: "msg-1", minutesBefore: 30);
        await NatureVerdictAsync(db, reading, ReplyNature.Person, ReplyReaction.Escalate, message: "msg-0", minutesBefore: 50);
        var body = (JsonObject)(await db.Journal.AsNoTracking().SingleAsync(e => e.PublicId == answered, Ct)).New!.DeepClone();
        if (!momentKnown)
        {
            body.Remove("message_at");
        }

        Assert.Equal(holds, await AutomaticRelease.EscalatedSinceAsync(db, reading.Campaign.Id, body, Ct));
    }

    /// <summary>
    /// A doubt parks the send for the person before anything can release it: its creator's <c>ask_person</c>, or a doubting
    /// review of its text by anybody, stands against the critic's pass and parks it <c>doubted</c>, with the doubt on the
    /// preview and no line of the mode.
    /// </summary>
    [Theory]
    [InlineData("ask_person")]
    [InlineData("a copywriter's doubt")]
    [Critical(Family.S2, "the automatic mode: a doubt parks the send before anything can release it")]
    public async Task SendMode_ADoubt_ParksDoubted_ThePassNotwithstanding(string door)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null, context: door == "ask_person" ? Asking("sensitive_topic") : null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        if (door != "ask_person")
        {
            var doubter = await RunningAsync(db, reading, "copywriter");
            await new ReviewService(db, new JournalWriter(new FixedClock(Noon)), Tests.Approvals.TheClaimReadsEveryReview.Instance).ReviewAsync(
                new WorkItemReviewRequest(send.Id, send.ReviewHash, ReviewResult.Doubts, "off_brief", null, new ActorRef(ActorType.Attempt, doubter.PublicId)), Ct);
        }

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, "doubted"), (approval.Status, approval.Reason));
        Assert.Single(approval.Preview[Doubts.PreviewKey]!.AsArray());
        Assert.Equal("cold", (string?)approval.Preview["send_mode"]!["temperature"]);
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == "approval_automatic_applied", Ct));
    }

    /// <summary>
    /// The parks that say the mode cannot vouch for a send leave a standing approval that covers it to decide by its own
    /// conditions, as it did before the modes; a doubt never does. Unreviewed, or answering while a newer reply on the thread
    /// waits to be judged, a covered draft goes under the standing approval; doubted, it parks <c>doubted</c>, and the
    /// standing approval's answer says so.
    /// </summary>
    [Theory]
    [InlineData("not reviewed", "standing_approval")]
    [InlineData("a newer reply waits to be judged", "standing_approval")]
    [InlineData("doubted", "doubted")]
    public async Task SendMode_AParkOfTheMode_FallsThroughToAStandingApproval_ButADoubtDoesNot(string why, string outcome)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person);
        if (why == "a newer reply waits to be judged")
        {
            await ObservedAsync(db, reading, "2026-09-14T11:50:00.000Z");
        }

        await StandAsync(db, reading.Campaign);
        var item = Drafted(db, reading, verdict);
        if (why == "doubted")
        {
            item.Context[Doubts.AskPersonKey] = new JsonObject { ["code"] = "sensitive_topic" };
        }

        await db.SaveChangesAsync(Ct);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(outcome, approval.Reason);
        if (outcome == "standing_approval")
        {
            // The facts the mode decided on stay on the preview of the row the standing approval wrote.
            Assert.Single(claimed);
            Assert.Equal(why == "not reviewed" ? "cold" : null, (string?)approval.Preview["send_mode"]!["temperature"]);
            Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ApprovalStandingApplied).ToListAsync(Ct));
        }
        else
        {
            Assert.Empty(claimed);
            Assert.Equal(StandingApprovals.Why.Doubted, (string?)approval.Preview["standing_approval"]!["why"]);
        }
    }

    /// <summary>
    /// A send that was ever put to a person is never released automatically: parked while the mode was every message, then
    /// the mode set to automatic, the critic's pass standing, its question expired and the item reopened — it parks for the
    /// person again; so does one a ceiling's park asked about, the next day. The same send that nobody was asked about is
    /// released.
    /// </summary>
    [Theory]
    [InlineData("parked under every message, expired and reopened", false)]
    [InlineData("parked at a ceiling, the next day", false)]
    [InlineData("never asked", true)]
    public async Task SendMode_ASendEverPutToAPerson_IsNeverReleasedAutomatically(string path, bool released)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var send = await ComposeAsync(db, reading, waitOn: null);
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == send.Id, Ct);
        if (path.StartsWith("parked under", StringComparison.Ordinal))
        {
            Assert.Empty(await ClaimSettledAsync(harness, db));
            await db.Approvals.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ApprovalStatus.Cancelled), Ct);
            await db.WorkItems.Where(w => w.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkItemStatus.Created), Ct);
        }
        else if (path.StartsWith("parked at", StringComparison.Ordinal))
        {
            db.Approvals.Add(new Approval
            {
                PublicId = "apr_01K52JR0000000000000000011",
                WorkItemId = item.Id,
                CampaignId = item.CampaignId,
                Operation = "message.send",
                OperationVersion = 1,
                Subject = [],
                SubjectHash = "sha256:" + new string('b', 64),
                Preview = [],
                PluginId = Provider,
                RouteScope = RouteScope.GlobalOperation,
                PluginSnapshotId = "pls_01K52JR0000000000000000001",
                RoutingSnapshotId = "rts_01K52JR0000000000000000001",
                Status = ApprovalStatus.Cancelled,
                Reason = "daily_ceiling",
                RequestedAt = Noon.AddDays(-1),
                DecidedAt = Noon.AddDays(-1),
            });
            await db.SaveChangesAsync(Ct);
        }

        db.ChangeTracker.Clear();
        await ModeAsync(db, ColdMode, "automatic");
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        db.ChangeTracker.Clear();

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var newest = await fresh.Approvals.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync(Ct);
        Assert.Equal(released ? 1 : 0, claimed.Count);
        Assert.Equal(released ? "automatic_mode" : "approval_required", newest.Reason);
        Assert.Equal(released ? 1 : 0, await fresh.Journal.CountAsync(e => e.Kind == "approval_automatic_applied", Ct));
    }

    /// <summary>
    /// A mode releases only in a named person's name: a preference whose row a role set, or one that names nobody, is no
    /// automatic mode, and the send parks as before.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "manager")]
    [InlineData(ActorType.Human, "")]
    [InlineData(ActorType.Human, null)]
    public async Task SendMode_AModeNoNamedPersonSet_ReleasesNothing(ActorType type, string? id)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic", new ActorRef(type, id));
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((ApprovalStatus.Pending, "approval_required"), (approval.Status, approval.Reason));
    }

    /// <summary>
    /// The automatic release decides once. A retry after a failure that is retried runs under its row with no second row and
    /// no second line; a send whose text changed since parks <c>input_changed</c> for the person; and a reply that landed
    /// between the failure and the retry parks the retry for the person, the row superseded, since the release read the
    /// contact's warmth before the reply was there.
    /// </summary>
    [Theory]
    [InlineData("nothing changed", null)]
    [InlineData("the text changed", "input_changed")]
    [InlineData("a reply landed", "awaiting_judgement")]
    public async Task SendMode_ARetryUnderTheAutomaticRow(string meanwhile, string? parks)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        Assert.Single(await ClaimSettledAsync(harness, db));
        await FailRetriablyAsync(db, send.Id);
        switch (meanwhile)
        {
            case "the text changed":
                var item = await db.WorkItems.SingleAsync(w => w.PublicId == send.Id, Ct);
                item.Context = new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") };
                await db.SaveChangesAsync(Ct);
                break;
            case "a reply landed":
                await ObservedAsync(db, reading, "2026-09-14T11:55:00.000Z");
                break;
        }

        db.ChangeTracker.Clear();
        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(1, await fresh.Journal.CountAsync(e => e.Kind == "approval_automatic_applied", Ct));
        if (parks is null)
        {
            Assert.Single(claimed);
            var row = Assert.Single(approvals);
            Assert.Equal((ApprovalStatus.Approved, "automatic_mode"), (row.Status, row.Reason));
            Assert.Equal(row.PublicId, (await fresh.Attempts.AsNoTracking().Where(a => a.WorkItem!.PublicId == send.Id).OrderBy(a => a.Number).LastAsync(Ct)).Provenance!.ApprovalId);
            return;
        }

        Assert.Empty(claimed);
        Assert.Equal([ApprovalStatus.Superseded, ApprovalStatus.Pending], approvals.Select(a => a.Status));
        Assert.Equal(("automatic_mode", parks), (approvals[0].Reason, approvals[1].Reason));
    }

    /// <summary>
    /// Only the automatic mode's row reads warmth again at a retry. A retry under a person's own approval, or under a
    /// standing approval's row, goes under that decision even where a reply of the contact's landed since and waits for a
    /// verdict, with the automatic mode in force all the while.
    /// </summary>
    [Theory]
    [InlineData("a person's approval")]
    [InlineData("a standing approval")]
    public async Task SendMode_ARetryUnderAnotherRow_ReadsNoWarmthAgain(string under)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        string sendId;
        if (under == "a person's approval")
        {
            sendId = (await ComposeAsync(db, reading, waitOn: null)).Id;
            Assert.Empty(await ClaimSettledAsync(harness, db));
            var asked = await db.Approvals.AsNoTracking().SingleAsync(Ct);
            var clock = new FixedClock(Noon);
            await new ApprovalService(db, new JournalWriter(clock), clock).ApproveAsync(new ApprovalDecisionRequest(asked.PublicId, Operator, Reason: null), Ct);
            db.ChangeTracker.Clear();
            db.Attach(reading.Campaign);
        }
        else
        {
            var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person, by: Operator);
            await StandAsync(db, reading.Campaign);
            sendId = (await Items(db).CreateAsync(
                new WorkItemCreateRequest(
                    reading.Campaign.PublicId, WorkItemKind.ProviderOp, null, "conversation.reply", reading.Contact.PublicId,
                    null, null, null, null, null, null, null,
                    new JsonObject { [WorkItemService.InputKey] = ReplyingIn(InThread), [StandingApprovals.VerdictKey] = verdict },
                    null, Operator, null),
                Ct)).Id;
        }

        Assert.Single(await ClaimSettledAsync(harness, db));
        var decided = await db.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(under == "a person's approval" ? "approval_required" : "standing_approval", decided.Reason);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        await FailRetriablyAsync(db, sendId);
        await ObservedAsync(db, reading, "2026-09-14T11:55:00.000Z");
        db.ChangeTracker.Clear();

        Assert.Single(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var row = Assert.Single(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.Equal((ApprovalStatus.Approved, decided.Reason), (row.Status, row.Reason));
    }

    /// <summary>
    /// A review is refused exactly where the claim would not read it. Released automatically and then failed for a reason
    /// that is retried, the send's retry goes under its automatic row and reads no review, so a doubt sent then is refused
    /// <c>not_reviewable</c>, naming the row, and nothing is written.
    /// </summary>
    [Fact]
    public async Task SendMode_AReviewUnderAnAutomaticRow_IsRefused()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        var pass = await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        Assert.Single(await ClaimSettledAsync(harness, db));
        await FailRetriablyAsync(db, send.Id);
        db.ChangeTracker.Clear();
        db.Attach(reading.Campaign);
        var approval = await db.Approvals.AsNoTracking().SingleAsync(Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => ReviewAsync(db, reading, send.Id, send.ReviewHash, ReviewResult.Doubts, "off_brief", ReviewSentence, harness.Claimer));

        Assert.Equal("not_reviewable", refused.Code);
        Assert.Contains($"approval '{approval.PublicId}' stands for the send as it would now go", refused.Message, StringComparison.Ordinal);
        await using var fresh = database.Open();
        Assert.Equal(pass, (await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemReviewed, Ct)).PublicId);
    }

    /// <summary>
    /// A refusal comes before the mode: a send to a person the register holds is refused <c>suppressed</c>, never released
    /// and never parked; and a send the mode released is still held by an open block of its account, its row kept.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendMode_ARefusalComesFirst_AndABlockHoldsARelease(bool blocked)
    {
        var plugin = blocked ? BlockRows.Plugin : Provider;
        using var harness = new Harness(
            plugin: TestPlugins.Loaded(plugin, ["message.send", "conversation.reply", "social_invitation.send"]),
            route: new Route(plugin, null, null),
            register: blocked ? null : new Harness.EverybodyIsSuppressed());
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        if (blocked)
        {
            OpenBlock(db, reading.Campaign, nextTrialAt: Noon.AddHours(1));
            await db.SaveChangesAsync(Ct);
        }

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var row = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == send.Id, Ct);
        if (blocked)
        {
            Assert.Equal((WorkItemStatus.Waiting, 0), (row.Status, row.AttemptCount));
            var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
            Assert.Equal((ApprovalStatus.Approved, "automatic_mode"), (approval.Status, approval.Reason));
        }
        else
        {
            Assert.Equal(WorkItemStatus.Failed, row.Status);
            Assert.Equal("suppressed", (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == row.Id, Ct)).Error!.Code);
            Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        }

        Assert.Equal(blocked ? 1 : 0, await fresh.Journal.CountAsync(e => e.Kind == "approval_automatic_applied", Ct));
    }

    /// <summary>
    /// A send written in the desktop's chat waits for the person in every mode, ahead of any standing approval. Under the
    /// automatic mode, its text passed by the critic, it parks <c>sent_from_chat</c>; under every message it parks under the
    /// reason every first park has, and a standing approval that would have released it says <c>sent_from_chat</c>; under the
    /// automatic mode with such a standing approval it parks <c>sent_from_chat</c> too. The same send written outside the
    /// chat goes, automatically or under the standing approval.
    /// </summary>
    [Theory]
    [InlineData("automatic", false, true, "sent_from_chat")]
    [InlineData("every_message", true, true, "approval_required")]
    [InlineData("automatic", true, true, "sent_from_chat")]
    [InlineData("automatic", false, false, "automatic_mode")]
    [InlineData("every_message", true, false, "standing_approval")]
    [Critical(Family.S2, "a send written in the chat always waits for the person")]
    public async Task SendMode_ASendWrittenInTheChat_AlwaysWaitsForThePerson(string mode, bool standing, bool chat, string reason)
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, mode);
        var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person, by: Operator);
        if (standing)
        {
            await StandAsync(db, reading.Campaign);
        }

        WorkItemDto draft;
        using (chat ? ConfinedScope.Open() : null)
        {
            draft = await Items(db).CreateAsync(
                new WorkItemCreateRequest(
                    reading.Campaign.PublicId, WorkItemKind.ProviderOp, null, "conversation.reply", reading.Contact.PublicId,
                    null, null, null, null, null, null, null,
                    new JsonObject { [WorkItemService.InputKey] = ReplyingIn(InThread), [StandingApprovals.VerdictKey] = verdict },
                    null, Operator, null),
                Ct);
        }

        await CriticPassAsync(db, reading, draft.Id, draft.ReviewHash);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(reason, approval.Reason);
        Assert.Equal(reason is "automatic_mode" or "standing_approval" ? 1 : 0, claimed.Count);
        if (chat && standing)
        {
            Assert.Equal(StandingApprovals.Why.SentFromChat, (string?)approval.Preview["standing_approval"]!["why"]);
        }

        if (chat)
        {
            Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == "approval_automatic_applied" || e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
        }
    }

    /// <summary>
    /// The gate never meets a send that names no contact: each send acts on a contact, so the pre-flight refuses one that
    /// names none, <c>contact_required</c>, before the approval is asked, a chat send's under a standing approval that
    /// would cover its verdict included. The step reads the chat's mark before it asks for the contact all the same.
    /// </summary>
    [Fact]
    public async Task SendMode_ASendNamingNoContact_IsRefusedBeforeTheGate()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person, by: Operator);
        await StandAsync(db, reading.Campaign);
        WorkItemDto draft;
        using (ConfinedScope.Open())
        {
            draft = await Items(db).CreateAsync(
                new WorkItemCreateRequest(
                    reading.Campaign.PublicId, WorkItemKind.ProviderOp, null, "conversation.reply", null,
                    null, null, null, null, null, null, null,
                    new JsonObject { [WorkItemService.InputKey] = ReplyingIn(InThread), [StandingApprovals.VerdictKey] = verdict },
                    null, Operator, null),
                Ct);
        }

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var item = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == draft.Id, Ct);
        Assert.Equal((WorkItemStatus.Failed, "contact_required"), (item.Status, item.LastError!.Code));
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// An edit of a send's input in the chat makes it the chat's: a send written outside the chat, its text then edited
    /// there and passed again by the critic, parks <c>sent_from_chat</c> under the automatic mode.
    /// </summary>
    [Fact]
    public async Task SendMode_AnEditInTheChat_MakesTheSendTheChats()
    {
        using var harness = ModeHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        var send = await ComposeAsync(db, reading, waitOn: null);
        db.ChangeTracker.Clear();
        WorkItemDto edited;
        using (ConfinedScope.Open())
        {
            edited = await Items(db).UpdateAsync(
                new WorkItemUpdateRequest(
                    send.Id,
                    new JsonObject { [WorkItemService.InputKey] = Message("Friday at 10 works.") },
                    null, default, default, default, default, default, default, default, default,
                    new ActorRef(ActorType.Human, "operator"),
                    Reason: null),
                Ct);
        }

        await CriticPassAsync(db, reading, send.Id, edited.ReviewHash);
        db.ChangeTracker.Clear();

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        Assert.Equal("sent_from_chat", (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Reason);
    }

    private static Harness ModeHarness() => new(
        plugin: TestPlugins.Loaded(Provider, ["conversation.reply", "message.send", "social_invitation.send"]),
        route: new Route(Provider, null, null));

    /// <summary>The installation's preference for one temperature, set by a person unless another is named.</summary>
    private static async Task ModeAsync(JasonDbContext db, string key, string mode, ActorRef? by = null)
    {
        var setter = by ?? Marta;
        db.Preferences.Add(new Preference
        {
            Key = key,
            Value = new JsonObject { ["mode"] = mode },
            ValueHash = "sha256:" + new string('a', 64),
            UpdatedByType = setter.Type,
            UpdatedById = setter.Id,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A move of the reading's thread as the campaign's own inbound read says it, naming the contact.</summary>
    private static async Task ObservedAsync(JasonDbContext db, Reading reading, string moment)
    {
        new JournalWriter(new FixedClock(new DateTimeOffset(Noon))).Append(
            db,
            Actors.Dispatcher,
            JournalKinds.InboundReplyObserved,
            reading.Campaign,
            key: InboundSync.ObservationKey(Provider, InThread, moment),
            updated: new JsonObject
            {
                ["plugin"] = Provider,
                ["thread"] = InThread,
                ["channel"] = "email",
                ["last_activity_at"] = moment,
                ["unread"] = true,
                ["has_meeting_intent"] = null,
                ["contact_id"] = reading.Contact.PublicId,
            },
            contactId: reading.Contact.Id);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A verdict of the reading's run on the reply, saying the nature given; its line's id.</summary>
    private static async Task<string> NatureVerdictAsync(
        JasonDbContext db, Reading reading, ReplyNature? nature, ReplyReaction reaction = ReplyReaction.Draft, ActorRef? by = null,
        string message = "msg-1", int minutesBefore = 30)
    {
        var line = await new ReplyService(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon)))).RecordAsync(
            new ReplyRecordRequest(
                reading.Campaign.PublicId,
                Provider,
                InThread,
                message,
                new DateTimeOffset(Noon.AddMinutes(-minutesBefore)),
                "interested",
                0.9,
                reaction,
                ContactId: reading.Contact.PublicId,
                Actor: by ?? RunOf(reading),
                Nature: nature),
            Ct);
        return line.Id;
    }

    /// <summary>A contact warm in the campaign: the campaign's read saw their reply, and a verdict judged it a person's.</summary>
    private static async Task WarmAsync(JasonDbContext db, Reading reading)
    {
        await ObservedAsync(db, reading, "2026-09-14T11:30:00.000Z");
        await NatureVerdictAsync(db, reading, ReplyNature.Person);
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, reading.Campaign.Id, reading.Contact.Id, Ct));
    }

    /// <summary>A running attempt of a role's work in the reading's campaign.</summary>
    private static async Task<Attempt> RunningAsync(JasonDbContext db, Reading reading, string role)
    {
        // The campaign as this context tracks it, attached where an earlier clear left it tracked by nobody.
        var campaign = db.Campaigns.Local.FirstOrDefault(c => c.Id == reading.Campaign.Id);
        if (campaign is null)
        {
            db.Attach(reading.Campaign);
            campaign = reading.Campaign;
        }

        var work = WorkItemFactory.NewAiRole(campaign, role: role, now: Noon);
        work.Status = WorkItemStatus.Processing;
        var run = WorkItemFactory.NewAttempt(work, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(work);
        await db.SaveChangesAsync(Ct);
        return run;
    }

    /// <summary>Which critic reviews a send: the one the runtime made for it, or one of the three that release nothing.</summary>
    internal enum Critic
    {
        TheRuntimes,
        QueuedByHand,
        AnotherSends,
        Patched,
        PatchedByItself,
    }

    /// <summary>
    /// A running attempt of a critic's work, made as the runtime will make it for a send: created by the runtime's own actor,
    /// its created line saying so, and its context's <c>send_review</c> naming the send; or queued by a person, naming another
    /// send, or with its context patched by a person afterwards.
    /// </summary>
    private static async Task<Attempt> CriticRunAsync(JasonDbContext db, Reading reading, string sendId, Critic critic)
    {
        var campaign = db.Campaigns.Local.FirstOrDefault(c => c.Id == reading.Campaign.Id);
        if (campaign is null)
        {
            db.Attach(reading.Campaign);
            campaign = reading.Campaign;
        }

        var operation = await db.WorkItems.AsNoTracking().Where(w => w.PublicId == sendId).Select(w => w.Operation).SingleAsync(Ct);
        var creator = critic == Critic.QueuedByHand ? Operator : Actors.Runtime;
        var work = WorkItemFactory.NewAiRole(campaign, role: AutomaticRelease.CriticRole, now: Noon);
        work.CreatedByType = creator.Type;
        work.CreatedById = creator.Id;
        work.Context = new JsonObject
        {
            [SendReview.Key] = new JsonObject
            {
                [SendReview.WorkItemIdMember] = critic == Critic.AnotherSends ? "wi_01K52JR0000000000000000055" : sendId,
                [SendReview.OperationMember] = operation,
            },
        };
        work.Status = WorkItemStatus.Processing;
        var run = WorkItemFactory.NewAttempt(work, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(work);
        var journal = new JournalWriter(new FixedClock(Noon));
        journal.Append(
            db,
            creator,
            JournalKinds.WorkItemCreated,
            campaign,
            key: "kind",
            updated: new JsonObject
            {
                ["kind"] = "ai_role",
                ["role"] = AutomaticRelease.CriticRole,
                ["operation"] = null,
                ["contact_id"] = null,
                ["priority"] = work.Priority,
                ["not_before"] = null,
                ["due_at"] = null,
            },
            workItem: work);
        if (critic == Critic.Patched)
        {
            journal.Append(db, Operator, JournalKinds.WorkItemContextUpdated, campaign, key: "brief", old: null, updated: "Look at the tone.", workItem: work);
        }
        else if (critic == Critic.PatchedByItself)
        {
            journal.Append(db, Actors.ForAttempt(run), JournalKinds.WorkItemContextUpdated, campaign, key: "brief", old: null, updated: "Look at the tone.", workItem: work, attempt: run);
        }

        await db.SaveChangesAsync(Ct);
        return run;
    }

    /// <summary>A named person's pass of the send's text, through the verb; its line's id.</summary>
    private static async Task<string> PersonPassAsync(JasonDbContext db, string sendId, string? hash) =>
        (await new ReviewService(db, new JournalWriter(new FixedClock(Noon)), Tests.Approvals.TheClaimReadsEveryReview.Instance).ReviewAsync(
            new WorkItemReviewRequest(sendId, hash, ReviewResult.Passed, null, null, new ActorRef(ActorType.Human, "ada")), Ct)).Id;

    /// <summary>What <paramref name="act"/> answers, done under the desktop's chat marker, as the door opens it.</summary>
    private static async Task<T> InTheChatAsync<T>(Func<Task<T>> act)
    {
        using var confined = ConfinedScope.Open();
        return await act();
    }

    /// <summary>The first line's id, once the review that follows it is written too.</summary>
    private static async Task<string> ThenAsync(string first, Task<string> then)
    {
        await then;
        return first;
    }

    /// <summary>A pass of the send's text by a running attempt of the critic's work, through the verb; its line's id.</summary>
    private static async Task<string> CriticPassAsync(
        JasonDbContext db, Reading reading, string sendId, string? hash, string role = "critic", Critic critic = Critic.TheRuntimes)
    {
        var run = role == AutomaticRelease.CriticRole ? await CriticRunAsync(db, reading, sendId, critic) : await RunningAsync(db, reading, role);
        var line = await new ReviewService(db, new JournalWriter(new FixedClock(Noon)), Tests.Approvals.TheClaimReadsEveryReview.Instance).ReviewAsync(
            new WorkItemReviewRequest(sendId, hash, ReviewResult.Passed, null, null, new ActorRef(ActorType.Attempt, run.PublicId)),
            Ct);
        return line.Id;
    }

    /// <summary>A passing review line written as no door writes one: by <paramref name="actor"/>, carrying <paramref name="run"/>.</summary>
    private static async Task<string> PassLineAsync(JasonDbContext db, WorkItem item, ActorRef actor, Attempt run, string role)
    {
        var line = new JournalWriter(new FixedClock(Noon)).Append(
            db,
            actor,
            JournalKinds.WorkItemReviewed,
            item.Campaign,
            key: ReviewSubject.Hash(item),
            updated: new JsonObject { ["result"] = "passed", ["code"] = null, ["operation"] = item.Operation, ["role"] = role },
            workItem: item,
            attempt: run);
        await db.SaveChangesAsync(Ct);
        return line.PublicId;
    }

    /// <summary>The send's running attempt fails for a reason that is retried, and the send is queued again.</summary>
    private static async Task FailRetriablyAsync(JasonDbContext db, string sendId)
    {
        var item = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.PublicId == sendId, Ct);
        var run = item.Attempts.Single(a => a.FinishedAt is null);
        run.Status = AttemptStatus.Failed;
        run.FinishedAt = Noon;
        run.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
        item.Status = WorkItemStatus.Created;
        await db.SaveChangesAsync(Ct);
    }
}
