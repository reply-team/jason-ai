using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Which earlier letter a message follows, through the real claim, its real reads and a real database, on a stand-in
/// provider whose manifest lists follows for email: the line's root the default follows, a letter named and the five checks
/// that refuse one, the record the claim keeps and the chronicle line it says once, a repeat that keeps what its first claim
/// decided, and the person asked again when the letter changes.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>
    /// The runtime's default follows the line's root: the composed input the person approves names the letter, its operation,
    /// the provider's reference and why, and the item records the same less the reference, said once in the chronicle as the
    /// dispatcher. Approved and claimed again, the decision is the same, so nothing more is recorded or said.
    /// </summary>
    [Fact]
    public async Task The_default_follows_the_line_s_root_and_the_claim_records_why_once()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var root = Wrote(db, campaign, contact, hoursAgo: 5);
        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        var followed = $$"""{"work_item":"{{root.PublicId}}","operation":"message.send","reference":"email-5","because":"line_root"}""";
        var recorded = $$"""{"work_item":"{{root.PublicId}}","operation":"message.send","because":"line_root"}""";
        await using (var parked = database.Open())
        {
            var approval = await parked.Approvals.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(followed, approval.Subject["input"]![FollowsDecision.Key]!.ToJsonString());
            var item = await parked.WorkItems.AsNoTracking().SingleAsync(w => w.Id == send.Id, Ct);
            Assert.Equal(recorded, item.Context[FollowsDecision.Key]!.ToJsonString());
            var line = await parked.Journal.AsNoTracking()
                .SingleAsync(e => e.WorkItemId == send.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct);
            Assert.Equal((ActorType.System, "dispatcher", FollowsDecision.Key), (line.ActorType, line.ActorId, line.Key));
            Assert.Null(line.Old);
            Assert.Equal(recorded, line.New!.ToJsonString());
        }

        await Decide(db, ApprovalStatus.Approved);
        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(followed, work.Plan!.Input[FollowsDecision.Key]!.ToJsonString());
        await using var fresh = database.Open();
        Assert.Equal(1, await fresh.Journal.CountAsync(e => e.WorkItemId == send.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct));
    }

    /// <summary>
    /// Every reason the claim gives reaches the person's approval and the item's record: the line's root followed, a letter
    /// named followed, a letter of its own asked for, and each condition that makes the default a new letter — no earlier
    /// letter, a plugin that does not take follows on the channel, a root that went through another plugin or from another
    /// mailbox. Only a letter followed carries the provider's reference.
    /// </summary>
    [Theory]
    [InlineData(FollowsDecision.LineRoot)]
    [InlineData(FollowsDecision.Named)]
    [InlineData(FollowsDecision.AskedNewThread)]
    [InlineData(FollowsDecision.NoEarlierLetter)]
    [InlineData(FollowsDecision.NotOffered)]
    [InlineData(FollowsDecision.OtherAccount)]
    [InlineData(FollowsDecision.OtherMailbox)]
    public async Task Each_reason_reaches_the_approval_and_the_record(string because)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness(offered: because != FollowsDecision.NotOffered);
        var (campaign, contact) = await LineSceneAsync(db);
        var older = Wrote(db, campaign, contact, hoursAgo: 9);
        var root = because switch
        {
            FollowsDecision.NoEarlierLetter => null,
            FollowsDecision.OtherAccount => Wrote(db, campaign, contact, hoursAgo: 5, plugin: "another-provider"),
            FollowsDecision.OtherMailbox => Wrote(db, campaign, contact, hoursAgo: 5, sender: "snd-2"),
            _ => Wrote(db, campaign, contact, hoursAgo: 5),
        };
        if (root is null)
        {
            // No letter of the line can be followed: the only earlier one is a reply that follows nothing the line holds.
            older.Context = Recording(older.Context, "wi_01K9R000000000000000000009");
        }

        var send = Sending(db, campaign, contact);
        if (because is FollowsDecision.Named or FollowsDecision.AskedNewThread)
        {
            send.Context[WorkItemService.InputKey]![FollowsDecision.Key] = because == FollowsDecision.Named ? older.PublicId : FollowsDecision.NewThread;
        }

        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var composed = (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Subject["input"]![FollowsDecision.Key]!.AsObject();
        var expected = because switch
        {
            FollowsDecision.LineRoot => (root!.PublicId, "email-5"),
            FollowsDecision.Named => (older.PublicId, "email-9"),
            _ => ((string?)null, (string?)null),
        };
        Assert.Equal(
            (expected.Item1, expected.Item2, because),
            ((string?)composed["work_item"], (string?)composed["reference"], (string?)composed["because"]));
        var item = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == send.Id, Ct);
        Assert.Equal(FollowsDecision.Recorded(composed).ToJsonString(), item.Context[FollowsDecision.Key]!.ToJsonString());
    }

    /// <summary>
    /// A letter named that cannot be followed is refused at the claim, before anybody is asked to approve it, with the code
    /// that says why: no message of this campaign to this person by that id, one that kept no reference, one taken back, one
    /// that went through another plugin or account, one that went from another mailbox. The item fails, permanent and final,
    /// its attempt kept and no child made, and nothing is recorded of a letter it never follows.
    /// </summary>
    [Theory]
    [InlineData("no work item by that id", AttemptErrors.FollowsUnknown)]
    [InlineData("another campaign's letter", AttemptErrors.FollowsUnknown)]
    [InlineData("a letter to another person", AttemptErrors.FollowsUnknown)]
    [InlineData("a letter on another channel", AttemptErrors.FollowsUnknown)]
    [InlineData("a letter that failed", AttemptErrors.FollowsReferenceAbsent)]
    [InlineData("a letter taken back", AttemptErrors.FollowsTakenBack)]
    [InlineData("a letter through another plugin", AttemptErrors.FollowsRouteMismatch)]
    [InlineData("a letter from another mailbox", AttemptErrors.FollowsSenderMismatch)]
    public async Task A_letter_named_that_cannot_be_followed_is_refused_before_anybody_is_asked(string named, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var letter = named switch
        {
            "no work item by that id" => null,
            "another campaign's letter" => Wrote(db, (await LineSceneAsync(db, "cnt_01K52JR0000000000000000002")).Campaign, contact, hoursAgo: 5),
            "a letter to another person" => Wrote(db, campaign, (await LineSceneAsync(db, "cnt_01K52JR0000000000000000003")).Contact, hoursAgo: 5),
            "a letter on another channel" => Wrote(db, campaign, contact, hoursAgo: 5, channel: "linkedin"),
            "a letter that failed" => await TouchedAsync(db, campaign, contact, WorkItemStatus.Failed),
            "a letter through another plugin" => Wrote(db, campaign, contact, hoursAgo: 5, plugin: "another-provider"),
            "a letter from another mailbox" => Wrote(db, campaign, contact, hoursAgo: 5, sender: "snd-2"),
            _ => Wrote(db, campaign, contact, hoursAgo: 5),
        };
        if (named == "a letter taken back")
        {
            TookBack(db, campaign, letter!);
        }

        var send = Sending(db, campaign, contact);
        send.Context[WorkItemService.InputKey]![FollowsDecision.Key] = letter?.PublicId ?? "wi_01K9R000000000000000000009";
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == send.Id, Ct);
        Assert.Equal((code, FailureClass.Permanent, false), (attempt.Error!.Code, attempt.Error.Class, attempt.Error.Retriable));
        Assert.Null(attempt.Launch);
        var item = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == send.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.False(item.Context.ContainsKey(FollowsDecision.Key));
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.WorkItemId == send.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct));
    }

    /// <summary>
    /// The line's root is the latest letter to the person on the channel that opened a thread of its own: a letter claimed
    /// before the runtime decided this, or one recorded as a letter of its own. A reply that followed a letter is never a
    /// root however late it went, and neither is a letter to another person or on another channel, so the root moves past
    /// the first letter to the later one that opened its own thread.
    /// </summary>
    [Fact]
    public async Task The_root_moves_to_the_latest_letter_that_opened_a_thread_of_its_own()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var first = Wrote(db, campaign, contact, hoursAgo: 11);
        var reply = Wrote(db, campaign, contact, hoursAgo: 9);
        reply.Context = Recording(reply.Context, first.PublicId);
        var own = Wrote(db, campaign, contact, hoursAgo: 7);
        own.Context = Recording(own.Context, workItem: null);
        var later = Wrote(db, campaign, contact, hoursAgo: 5);
        later.Context = Recording(later.Context, own.PublicId);
        Wrote(db, campaign, (await LineSceneAsync(db, "cnt_01K52JR0000000000000000003")).Contact, hoursAgo: 3);
        Wrote(db, campaign, contact, hoursAgo: 2, channel: "linkedin");
        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var composed = (await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == send.Id, Ct)).Subject["input"]![FollowsDecision.Key]!;
        Assert.Equal((own.PublicId, "email-7", "line_root"), ((string?)composed["work_item"], (string?)composed["reference"], (string?)composed["because"]));
    }

    /// <summary>A root a succeeded take-back names never went, so the line's root is the letter before it.</summary>
    [Fact]
    public async Task A_root_taken_back_is_skipped_for_the_letter_before_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var went = Wrote(db, campaign, contact, hoursAgo: 7);
        TookBack(db, campaign, Wrote(db, campaign, contact, hoursAgo: 5));
        Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var composed = (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Subject["input"]![FollowsDecision.Key]!;
        Assert.Equal((went.PublicId, "email-7"), ((string?)composed["work_item"], (string?)composed["reference"]));
    }

    /// <summary>
    /// A send whose answer was lost is repeated with what its first claim decided: the root it followed, though a later
    /// letter opened a thread of its own since and the root itself was taken back since. The input is the one that claim
    /// composed, by its digest, so the repeat runs under the person's decision, and nothing more is recorded or said.
    /// </summary>
    [Fact]
    public async Task A_repeat_after_a_lost_answer_follows_the_letter_its_first_claim_recorded()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        var root = Wrote(db, campaign, contact, hoursAgo: 5);
        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await Decide(db, ApprovalStatus.Approved);
        var first = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var lost = await db.Attempts.SingleAsync(a => a.WorkItemId == send.Id, Ct);

        // The attempt started and its answer never came back; since then a later letter opened a thread of its own, and the
        // root was taken back.
        WorkItemTransitions.Apply(send, WorkItemStatus.Processing, Noon);
        lost.StartedAt = Noon;
        lost.Status = AttemptStatus.Failed;
        lost.FinishedAt = Noon;
        lost.Error = new AttemptErrorDto("provider_answer_lost", "No answer came back.", Retriable: true, Class: FailureClass.Ambiguous);
        WorkItemTransitions.Apply(send, WorkItemStatus.Created, Noon);
        Wrote(db, campaign, contact, hoursAgo: 1);
        TookBack(db, campaign, root);
        await db.SaveChangesAsync(Ct);

        var repeat = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(first.Plan!.Input[FollowsDecision.Key]!.ToJsonString(), repeat.Plan!.Input[FollowsDecision.Key]!.ToJsonString());
        Assert.Equal(root.PublicId, (string?)repeat.Plan.Input[FollowsDecision.Key]!["work_item"]);
        await using var fresh = database.Open();
        var second = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == send.Id && a.Number == 2, Ct);
        Assert.Equal(lost.Provenance!.InputDigest, second.Provenance!.InputDigest);
        Assert.Equal(1, await fresh.Journal.CountAsync(e => e.WorkItemId == send.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct));
    }

    /// <summary>
    /// The letter a message follows is part of what the person approves: a later letter that opened a thread of its own
    /// between the park and the claim makes the claim follow it, which is another message than the one approved, so the
    /// person is asked again, the claim records the new decision, and says so.
    /// </summary>
    [Fact]
    public async Task A_letter_that_changes_between_the_park_and_the_claim_asks_the_person_again()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        Wrote(db, campaign, contact, hoursAgo: 5);
        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var approved = await Decide(db, ApprovalStatus.Approved);
        var later = Wrote(db, campaign, contact, hoursAgo: 1);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == send.Id).ToListAsync(Ct));
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal((approved.PublicId, ApprovalStatus.Superseded), (approvals[0].PublicId, approvals[0].Status));
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.InputChanged), (approvals[1].Status, approvals[1].Reason));
        Assert.Equal(later.PublicId, (string?)approvals[1].Subject["input"]![FollowsDecision.Key]!["work_item"]);
        var item = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == send.Id, Ct);
        Assert.Equal(later.PublicId, (string?)item.Context[FollowsDecision.Key]!["work_item"]);
        Assert.Equal(2, await fresh.Journal.CountAsync(e => e.WorkItemId == send.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct));
    }

    /// <summary>
    /// An approval given or parked by a runtime that composed no decision about the letter was given about a letter of its
    /// own, which is what that runtime sent. It still covers the message where the claim now makes it a letter of its own,
    /// so the message runs under it; where the claim now makes it a reply in a thread the person never saw, the person is
    /// asked again.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_approval_written_before_the_letter_was_decided_covers_a_letter_of_its_own_and_not_a_reply(bool rooted)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = LetterHarness();
        var (campaign, contact) = await LineSceneAsync(db);
        if (rooted)
        {
            Wrote(db, campaign, contact, hoursAgo: 5);
        }

        var send = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var approved = await Decide(db, ApprovalStatus.Approved);

        // The subject and the item as a runtime that composed no decision about the letter wrote them.
        var older = (JsonObject)approved.Subject.DeepClone();
        Assert.True(((JsonObject)older["input"]!).Remove(FollowsDecision.Key));
        approved.Subject = older;
        approved.SubjectHash = CanonicalJson.Hash(older);
        var context = send.Context.DeepClone().AsObject();
        context.Remove(FollowsDecision.Key);
        send.Context = context;
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        await using var fresh = database.Open();
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        if (!rooted)
        {
            var work = Assert.Single(claimed);
            Assert.Equal(FollowsDecision.NoEarlierLetter, (string?)work.Plan!.Input[FollowsDecision.Key]!["because"]);
            Assert.Equal((approved.PublicId, ApprovalStatus.Approved), (Assert.Single(approvals).PublicId, approvals[0].Status));
            var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == send.Id, Ct);
            Assert.Equal(approved.PublicId, attempt.Provenance!.ApprovalId);
            return;
        }

        Assert.Empty(claimed);
        Assert.Equal((approved.PublicId, ApprovalStatus.Superseded), (approvals[0].PublicId, approvals[0].Status));
        Assert.Equal((ApprovalStatus.Pending, ApprovalGate.InputChanged), (approvals[1].Status, approvals[1].Reason));
        Assert.Equal(FollowsDecision.LineRoot, (string?)approvals[1].Subject["input"]![FollowsDecision.Key]!["because"]);
    }

    /// <summary>
    /// The claim's harness with a provider that sends and takes back, settles a send by the work item's key, and lists follows
    /// for email where <paramref name="offered"/>; the ceiling of touches at its most, so no count of these lines refuses.
    /// </summary>
    private Harness LetterHarness(bool offered = true)
    {
        var plugin = TestPlugins.Loaded(LineProvider, ["message.send", "message.cancel"]);
        plugin = plugin with
        {
            Manifest = plugin.Manifest with
            {
                KeyedOperations = ["message.send"],
                Arguments = offered
                    ? new ManifestArguments(new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal)
                    {
                        ["message.send"] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [FollowsDecision.Key] = ["email"] },
                    })
                    : ManifestArguments.None,
            },
        };
        var harness = new Harness(plugin: plugin, route: new Route(LineProvider, null, null));
        harness.Lines.CurrentValue = new LinesOptions { MaxTouches = LinesOptions.MaximumTouches };
        return harness;
    }

    /// <summary>
    /// A letter of this campaign to <paramref name="contact"/> on <paramref name="channel"/> that went
    /// <paramref name="hoursAgo"/> hours before noon, from the provider's account <paramref name="sender"/>, under
    /// <paramref name="plugin"/> with no binding, with the provider's reference <c>email-</c> and the hours; claimed before the
    /// runtime decided which letter a message follows, so it records none.
    /// </summary>
    private static WorkItem Wrote(
        JasonDbContext db,
        Campaign campaign,
        Contact contact,
        int hoursAgo,
        string channel = "email",
        string plugin = LineProvider,
        string sender = "snd-1")
    {
        var at = Noon.AddHours(-hoursAgo);
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", at);
        item.Contact = contact;
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = at;
        item.AttemptCount = 1;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = channel,
                ["sender"] = new JsonObject { ["external_id"] = sender, ["address"] = "sales@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello again." },
            },
        };
        item.Result = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["sender_external_id"] = sender, ["status"] = "sent" }) };
        db.WorkItems.Add(item);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, at);
        attempt.FinishedAt = at;
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            "message.send", 1, null, null, RouteScope.GlobalDefault, null,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = $"email-{hoursAgo}" });
        db.Attempts.Add(attempt);
        return item;
    }

    /// <summary><paramref name="context"/> with the claim's record that the letter followed <paramref name="workItem"/>, or, null, none.</summary>
    private static JsonObject Recording(JsonObject context, string? workItem)
    {
        var recorded = context.DeepClone().AsObject();
        recorded[FollowsDecision.Key] = workItem is null
            ? new JsonObject { ["work_item"] = null, ["operation"] = null, ["because"] = FollowsDecision.AskedNewThread }
            : new JsonObject { ["work_item"] = workItem, ["operation"] = "message.send", ["because"] = FollowsDecision.Named };
        return recorded;
    }

    /// <summary>A take-back of <paramref name="letter"/> that succeeded: the provider held it and said it never went.</summary>
    private static void TookBack(JasonDbContext db, Campaign campaign, WorkItem letter)
    {
        var cancel = WorkItemFactory.NewProviderOp(campaign, "message.cancel", Noon.AddMinutes(-30));
        cancel.Status = WorkItemStatus.Succeeded;
        cancel.FinishedAt = Noon.AddMinutes(-30);
        cancel.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = letter.PublicId } };
        db.WorkItems.Add(cancel);
    }
}
