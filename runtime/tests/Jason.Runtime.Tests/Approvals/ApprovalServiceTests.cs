using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The four verbs a person decides through. What they are about is accountability: who decided, what exactly
/// they were shown, and what happened to the work as a result — and that nothing inside the runtime can stand in
/// for the person.
/// </summary>
public class ApprovalServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ActorRef Operator => new(ActorType.Human, "ada@example.test");

    /// <summary>
    /// An absent actor is an anonymous human, which is right for creating work and wrong for deciding it. A
    /// decision that names nobody is a record of nothing.
    /// </summary>
    [Fact]
    public async Task A_decision_from_nobody_in_particular_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, approval) = await ParkedWork.WriteAsync(db, Ct);

        var anonymous = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, null, null), Ct));
        var unnamed = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, new ActorRef(ActorType.Human, "  "), null), Ct));

        Assert.Equal("actor_required", anonymous.Code);
        Assert.Equal("actor_required", unnamed.Code);
        Assert.Equal(ApprovalStatus.Pending, (await db.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// Approval is a person's to give. The runtime's own actor is already refused to every caller; a role or an
    /// attempt asking to decide is asking to be the person who decides, which is the one thing INV-ROLE-002 says
    /// semantic judgment never becomes.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "planner")]
    [InlineData(ActorType.Attempt, "att_01K52JR0000000000000000001")]
    public async Task A_role_that_tries_to_approve_is_refused_the_way_a_system_actor_is(ActorType type, string id)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, approval) = await ParkedWork.WriteAsync(db, Ct);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, new ActorRef(type, id), null), Ct));

        Assert.Equal("approval_not_human", refused.Code);
        Assert.Equal(ApprovalStatus.Pending, (await db.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// Two people answering the same preview, and the second one loses cleanly: the decision that was made
    /// stands, and the work is not moved twice.
    /// </summary>
    [Fact]
    public async Task A_decision_made_twice_changes_nothing_the_second_time()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);

        await Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct);

        var again = await Assert.ThrowsAsync<ConflictException>(
            () => Service(db).RejectAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "changed my mind"), Ct));

        Assert.Equal("approval_not_pending", again.Code);
        Assert.Contains("approved", again.Message, StringComparison.Ordinal);

        await using var fresh = database.Open();
        var decided = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Approved, decided.Status);
        Assert.Equal("go ahead", decided.DecisionReason);
        Assert.Equal(WorkItemStatus.Created, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
    }

    /// <summary>
    /// Approving releases the work and changes nothing else about it: the same due date, the same priority, the
    /// same place in the queue it had before a person was asked.
    /// </summary>
    [Fact]
    public async Task An_approved_item_goes_back_into_the_queue_with_its_due_date_and_priority_untouched()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var due = ParkedWork.Noon.AddDays(1);
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct, dueAt: due, priority: 7);

        var answered = await Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, null), Ct);

        Assert.Equal(ApprovalStatus.Approved, answered.Status);
        Assert.Equal(ActorType.Human, answered.DecidedBy!.Type);
        Assert.Equal("ada@example.test", answered.DecidedBy.Id);
        Assert.NotNull(answered.DecidedAt);

        await using var fresh = database.Open();
        var released = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, released.Status);
        Assert.Equal(due, released.DueAt);
        Assert.Equal(7, released.Priority);
        Assert.Null(released.FinishedAt);
        Assert.Equal(0, released.AttemptCount);

        var kinds = await fresh.Journal.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct);
        Assert.Equal(["approval_approved", "workitem_released"], kinds);
    }

    /// <summary>
    /// A rejection is an accountable ending rather than a failure of the work: the item is failed, its last
    /// error carries the person's reason, and no attempt is invented to hold it.
    /// </summary>
    [Fact]
    public async Task A_rejected_item_ends_failed_carrying_the_decision_and_the_person_who_made_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);

        var answered = await Service(db).RejectAsync(
            new ApprovalDecisionRequest(approval.PublicId, Operator, "not this quarter"), Ct);

        Assert.Equal(ApprovalStatus.Rejected, answered.Status);
        Assert.Equal("not this quarter", answered.DecisionReason);

        await using var fresh = database.Open();
        var ended = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, ended.Status);
        Assert.Equal("approval_rejected", ended.LastError!.Code);
        Assert.Equal("not this quarter", ended.LastError.Message);
        Assert.False(ended.LastError.Retriable);
        Assert.NotNull(ended.FinishedAt);
        Assert.Empty(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));

        var kinds = await fresh.Journal.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct);
        Assert.Equal(["approval_rejected", "workitem_failed"], kinds);
    }

    /// <summary>A rejection nobody explained still says that a person made it, rather than pretending to a reason.</summary>
    [Fact]
    public async Task A_rejection_without_a_reason_still_says_a_person_ended_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);

        await Service(db).RejectAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, null), Ct);

        await using var fresh = database.Open();
        var ended = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal("approval_rejected", ended.LastError!.Code);
        Assert.Contains("gave no reason", ended.LastError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The real race, which the check above cannot answer: two people open the same preview, and each one's
    /// request has already read a pending row before the other writes. Only the write itself can decide that —
    /// each of the two rows it moves carries its own status in the WHERE — so the second decision writes nothing
    /// at all rather than half of it, and says so.
    /// </summary>
    [Fact]
    public async Task A_second_decision_that_read_the_same_row_first_loses_cleanly()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (item, approval) = await ParkedWork.WriteAsync(seed, Ct);

        // Two requests, each with its own unit of work, both holding the row as it was before either decided.
        await using var first = database.Open();
        await using var second = database.Open();
        var one = Service(first);
        var two = Service(second);
        await first.Approvals.Include(a => a.WorkItem).SingleAsync(Ct);
        await second.Approvals.Include(a => a.WorkItem).SingleAsync(Ct);

        await one.ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct);

        var late = await Assert.ThrowsAsync<ConflictException>(
            () => two.RejectAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "too late"), Ct));

        Assert.Equal("approval_not_pending", late.Code);

        await using var fresh = database.Open();
        var decided = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Approved, decided.Status);
        Assert.Equal("go ahead", decided.DecisionReason);

        // And the work moved once, the way the decision that won says: released, not failed.
        var moved = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Created, moved.Status);
        Assert.Null(moved.LastError);
    }

    /// <summary>
    /// What a person reads is what the claim wrote. Assembling the preview again at read time would answer from
    /// rows that have moved since — and the whole point of a preview is that it is about the thing being decided.
    /// </summary>
    [Fact]
    public async Task The_preview_a_person_reads_is_the_one_the_claim_wrote()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);

        // The campaign is renamed after the work was parked. The decision is still about what was parked.
        item.Campaign!.Name = "Renamed after the question was asked";
        await db.SaveChangesAsync(Ct);

        var read = await Service(db).GetAsync(new ApprovalGetRequest(approval.PublicId), Ct);

        Assert.Equal("Ada", (string?)read.Preview["contact"]!["name"]);
        Assert.Equal("ada@example.test", (string?)read.Preview["contact"]!["value"]);
        Assert.Equal("sha256:subject", read.SubjectHash);
        Assert.Equal("campaign.enroll", (string?)read.Subject["operation"]);
        Assert.Equal(item.PublicId, read.WorkItemId);
    }

    /// <summary>
    /// The question a person opens the list with is "what am I holding up?", so pending is the answer they get
    /// without asking for it — and the decisions already made are still there to be asked for.
    /// </summary>
    [Fact]
    public async Task The_list_answers_what_is_waiting_unless_it_is_asked_for_something_else()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, waiting) = await ParkedWork.WriteAsync(db, Ct);
        var (_, answered) = await ParkedWork.WriteAsync(db, Ct);
        await Service(db).RejectAsync(new ApprovalDecisionRequest(answered.PublicId, Operator, "no"), Ct);

        var pending = await Service(db).ListAsync(new ApprovalListRequest(null, null, null, null, null), Ct);
        var rejected = await Service(db).ListAsync(new ApprovalListRequest(ApprovalStatus.Rejected, null, null, null, null), Ct);

        Assert.Equal(waiting.PublicId, Assert.Single(pending.Items).Id);
        Assert.Equal("approval_required", Assert.Single(pending.Items).Reason);
        Assert.Equal(answered.PublicId, Assert.Single(rejected.Items).Id);
        Assert.Equal(ActorType.Human, Assert.Single(rejected.Items).DecidedBy!.Type);
    }

    /// <summary>A decision nobody can name is not found, and an id that is missing is a bad request rather than one.</summary>
    [Fact]
    public async Task An_approval_nobody_can_name_is_not_found()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var missing = await Assert.ThrowsAsync<NotFoundException>(
            () => Service(db).GetAsync(new ApprovalGetRequest("apr_01K52JR0000000000000000001"), Ct));
        var unnamed = await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).GetAsync(new ApprovalGetRequest(null), Ct));

        Assert.Equal("approval_not_found", missing.Code);
        Assert.Equal("approval_id", Assert.Single(unnamed.Details!).Field);
    }

    private static ApprovalService Service(JasonDbContext db)
    {
        var clock = new FixedClock(ParkedWork.Noon);
        return new ApprovalService(db, new JournalWriter(clock), clock);
    }
}
