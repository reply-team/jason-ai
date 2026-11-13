using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// An edit of what a parked item would do, made while a person is still being asked about it. The question was about
/// the old arguments, so the edit withdraws it in its own transaction and hands the item back to the queue; the claim,
/// which is the one place that decides whether a person is asked, asks again about what the item now says.
/// </summary>
public class ParkedEditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The person who edits the parked work: anybody who may update an item, a chat's agent included.</summary>
    private static ActorRef Editor => new(ActorType.Human, "grace@example.test");

    private static ActorRef Operator => new(ActorType.Human, "ada@example.test");

    [Fact]
    public async Task An_edit_of_a_parked_items_input_withdraws_the_question_and_hands_the_item_back_to_the_queue()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db);

        var updated = await Service(db).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922"), reason: "a better sequence"), Ct);

        Assert.Equal(WorkItemStatus.Created, updated.Status);
        Assert.Equal("seq_9922", (string?)updated.Context["input"]!["campaign"]!["external_id"]);

        await using var fresh = database.Open();

        // The question is withdrawn by the person who changed what it was about, saying why; no new one is written here.
        var withdrawn = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(approval.PublicId, withdrawn.PublicId);
        Assert.Equal(ApprovalStatus.Superseded, withdrawn.Status);
        Assert.Equal(ActorType.Human, withdrawn.DecidedByType);
        Assert.Equal("grace@example.test", withdrawn.DecidedById);
        Assert.Equal("input_changed", withdrawn.DecisionReason);
        Assert.Equal(ParkedWork.Noon, withdrawn.DecidedAt);

        var released = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, released.Status);
        Assert.Equal(0, released.AttemptCount);
        Assert.Null(released.FinishedAt);

        // The chronicle reads: the input changed, so the question about the old one ended, and the item went back.
        var lines = await fresh.Journal.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(
            [JournalKinds.WorkItemContextUpdated, JournalKinds.ApprovalSuperseded, JournalKinds.WorkItemReleased],
            lines.Select(line => line.Kind));
        Assert.All(lines, line => Assert.Equal(item.PublicId, line.WorkItemId));
        Assert.All(lines, line => Assert.Equal("grace@example.test", line.ActorId));
        Assert.Equal("input", lines[0].Key);
        Assert.Equal("a better sequence", lines[0].Reason);
        Assert.Equal("input_changed", lines[1].Reason);
        Assert.Equal(approval.PublicId, (string?)lines[1].New!["id"]);
        Assert.Equal("sha256:subject", (string?)lines[1].New!["subject_hash"]);
        Assert.Equal("awaiting_approval", (string?)lines[2].Old);
        Assert.Equal("created", (string?)lines[2].New);
    }

    /// <summary>
    /// A patch that writes the input and takes it away again ends where it began: work that carried no arguments still
    /// carries none, so the question is still about what the item says, and only the context's own lines are written.
    /// </summary>
    [Fact]
    public async Task A_patch_that_sets_and_unsets_the_input_so_it_ends_as_it_began_leaves_the_question_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);
        item.Operation = "campaign.pause";
        approval.Operation = "campaign.pause";
        await db.SaveChangesAsync(Ct);

        await Service(db).UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["input"] = new JsonObject() }, Unset = ["input"], Actor = Editor },
            Ct);

        await using var fresh = database.Open();
        var still = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((approval.PublicId, ApprovalStatus.Pending), (still.PublicId, still.Status));
        var parked = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, parked.Status);
        Assert.False(parked.Context.ContainsKey("input"));
        Assert.All(
            await fresh.Journal.AsNoTracking().Select(e => e.Kind).ToListAsync(Ct),
            kind => Assert.Equal(JournalKinds.WorkItemContextUpdated, kind));
    }

    [Theory]
    [InlineData("the same input again")]
    [InlineData("another key of the context")]
    public async Task An_edit_that_leaves_the_input_as_it_was_leaves_the_question_alone(string edit)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db);

        var set = edit == "the same input again"
            ? new JsonObject { ["input"] = Enrolment("seq_8811") }
            : new JsonObject { ["brief"] = "Mention the autumn webinar." };
        await Service(db).UpdateAsync(Patch(item.PublicId) with { Set = set, Actor = Editor }, Ct);

        await using var fresh = database.Open();
        var still = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(approval.PublicId, still.PublicId);
        Assert.Equal(ApprovalStatus.Pending, still.Status);
        Assert.Null(still.DecidedAt);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);

        // Nothing about the question is written: at most the line of the other key that moved.
        var kinds = await fresh.Journal.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct);
        Assert.Equal(edit == "the same input again" ? [] : [JournalKinds.WorkItemContextUpdated], kinds);
    }

    /// <summary>The edit arrives first: an approval that names the withdrawn question afterwards approves nothing.</summary>
    [Fact]
    public async Task An_approval_of_the_withdrawn_question_after_the_edit_is_answered_as_not_pending()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db);
        await Service(db).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922")), Ct);

        var approve = await Assert.ThrowsAsync<ConflictException>(
            () => Decider(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct));
        var reject = await Assert.ThrowsAsync<ConflictException>(
            () => Decider(db).RejectAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "no"), Ct));

        Assert.Equal("approval_not_pending", approve.Code);
        Assert.Equal("approval_not_pending", reject.Code);
        Assert.Contains("superseded", approve.Message, StringComparison.Ordinal);

        await using var fresh = database.Open();
        Assert.Equal(ApprovalStatus.Superseded, (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal(WorkItemStatus.Created, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
    }

    /// <summary>
    /// The race the other way round the check above cannot see: the approver's request already read the question as
    /// pending when the edit committed. The approval's own UPDATE carries the status it read in its WHERE, so it writes
    /// nothing and says what the row is.
    /// </summary>
    [Fact]
    public async Task An_approval_that_read_the_question_before_the_edit_committed_approves_nothing()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (item, approval) = await ParkedAsync(seed);

        await using var approving = database.Open();
        await using var editing = database.Open();
        await approving.Approvals.Include(a => a.WorkItem).SingleAsync(Ct);

        await Service(editing).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922")), Ct);

        var late = await Assert.ThrowsAsync<ConflictException>(
            () => Decider(approving).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct));
        Assert.Equal("approval_not_pending", late.Code);
        Assert.Contains("superseded", late.Message, StringComparison.Ordinal);

        await using var fresh = database.Open();
        var row = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Superseded, row.Status);
        Assert.Equal("grace@example.test", row.DecidedById);
        Assert.Equal(WorkItemStatus.Created, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        Assert.DoesNotContain(JournalKinds.ApprovalApproved, await fresh.Journal.AsNoTracking().Select(e => e.Kind).ToListAsync(Ct));
    }

    /// <summary>
    /// The approval arrives first, while the edit's request already holds the item as parked: the edit's guarded UPDATE
    /// finds the item released and writes nothing (the door answers <c>concurrent_update</c>, retryable). Sent again, it
    /// meets work that is approved and has not run, and takes that path unchanged: the claim decides about it.
    /// </summary>
    [Fact]
    public async Task An_edit_that_read_the_item_before_an_approval_committed_writes_nothing_and_sent_again_leaves_the_approval_to_the_claim()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (item, approval) = await ParkedAsync(seed);

        await using var editing = database.Open();
        await using var approving = database.Open();
        await WorkItemService.LoadAsync(editing, item.PublicId, Ct);
        await editing.Approvals.SingleAsync(Ct);

        await Decider(approving).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => Service(editing).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922")), Ct));

        await using (var between = database.Open())
        {
            Assert.Equal(ApprovalStatus.Approved, (await between.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
            var untouched = await between.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
            Assert.Equal("seq_8811", (string?)untouched.Context["input"]!["campaign"]!["external_id"]);
        }

        await using var again = database.Open();
        await Service(again).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922")), Ct);
        await AssertApprovedPathAsync(database, item, approval);
    }

    /// <summary>Work that is approved and has not run: the edit writes nothing about the approval; the claim supersedes it.</summary>
    [Fact]
    public async Task An_edit_of_approved_work_that_has_not_run_leaves_the_approval_to_the_claim()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db);
        await Decider(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct);

        await Service(db).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922")), Ct);

        await AssertApprovedPathAsync(database, item, approval);
    }

    [Fact]
    public async Task An_edit_the_operations_schema_refuses_leaves_the_question_and_the_item_where_they_were()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db);
        var broken = Enrolment("seq_9922");
        broken["collision"] = "trample";

        await Assert.ThrowsAsync<ValidationException>(() => Service(db).UpdateAsync(Edit(item.PublicId, broken), Ct));

        await using var fresh = database.Open();
        var still = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(approval.PublicId, still.PublicId);
        Assert.Equal(ApprovalStatus.Pending, still.Status);
        var parked = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, parked.Status);
        Assert.Equal("seq_8811", (string?)parked.Context["input"]!["campaign"]!["external_id"]);
        Assert.Empty(await fresh.Journal.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task An_edit_of_a_finished_item_is_refused_as_before_and_its_decision_is_untouched()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db);
        await Decider(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = ParkedWork.Noon;
        await db.SaveChangesAsync(Ct);
        var before = await db.Journal.AsNoTracking().CountAsync(Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(
            () => Service(db).UpdateAsync(Edit(item.PublicId, Enrolment("seq_9922")), Ct));

        Assert.Equal("workitem_terminal", refused.Code);
        await using var fresh = database.Open();
        Assert.Equal(ApprovalStatus.Approved, (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal(before, await fresh.Journal.AsNoTracking().CountAsync(Ct));
    }

    /// <summary>The approved-not-run path as it is: the edit lands, the approval stays approved, and nothing ends it here.</summary>
    private static async Task AssertApprovedPathAsync(TestDatabase database, WorkItem item, Approval approval)
    {
        await using var fresh = database.Open();
        var row = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(approval.PublicId, row.PublicId);
        Assert.Equal(ApprovalStatus.Approved, row.Status);
        Assert.Equal("ada@example.test", row.DecidedById);
        var edited = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, edited.Status);
        Assert.Equal("seq_9922", (string?)edited.Context["input"]!["campaign"]!["external_id"]);
        Assert.DoesNotContain(JournalKinds.ApprovalSuperseded, await fresh.Journal.AsNoTracking().Select(e => e.Kind).ToListAsync(Ct));
    }

    /// <summary>A parked enrolment and its pending question, with the arguments the question was asked about.</summary>
    private static async Task<(WorkItem Item, Approval Approval)> ParkedAsync(JasonDbContext db)
    {
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);
        item.Context = new JsonObject { ["input"] = Enrolment("seq_8811") };
        await db.SaveChangesAsync(Ct);
        return (item, approval);
    }

    private static WorkItemUpdateRequest Edit(string workItemId, JsonObject input, string? reason = null) =>
        Patch(workItemId) with { Set = new JsonObject { ["input"] = input }, Actor = Editor, Reason = reason };

    private static WorkItemUpdateRequest Patch(string workItemId) =>
        new(workItemId, null, null, default, default, default, default, default, default, default, default, null, null);

    private static WorkItemService Service(JasonDbContext db)
    {
        var clock = new FixedClock(ParkedWork.Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static ApprovalService Decider(JasonDbContext db)
    {
        var clock = new FixedClock(ParkedWork.Noon);
        return new ApprovalService(db, new JournalWriter(clock), clock);
    }

    private static JsonObject Enrolment(string providerCampaign) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = providerCampaign },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "authored_delay",
    };
}
