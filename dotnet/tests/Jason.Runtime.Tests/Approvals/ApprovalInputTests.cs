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
/// A person's yes on work whose arguments no longer hold to the operation's document. The document can refuse what it
/// once accepted — a reply drafted with markup before its text was held to plain text — and the work then waits as it
/// was parked. The claim refuses such work once it is approved, before any child exists; the yes is refused first, at the
/// person's own act, so the person who approves learns it while the card is still in front of them, and nothing moves.
/// </summary>
public class ApprovalInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ActorRef Operator => new(ActorType.Human, "ada@example.test");

    private const string Marked = "Hi there,<br><br>Thursday at ten works for me.";

    [Fact]
    [Critical(Family.S2, "a yes on broken input changes nothing")]
    public async Task A_yes_on_work_whose_arguments_the_document_now_refuses_is_refused_and_changes_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedReplyAsync(db, Marked);

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "go ahead"), Ct));

        // The API's own shape: the validation code, final, and the document's own reason in the detail it names.
        Assert.Equal(("validation_failed", false), (refused.Code, refused.Retryable));
        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("context.input/body", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("never a tag and never an entity", detail.Message, StringComparison.Ordinal);

        // Nothing moved: the question still waits, undecided; the work is still parked on it; no line was written.
        await using var fresh = database.Open();
        var still = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((approval.PublicId, ApprovalStatus.Pending), (still.PublicId, still.Status));
        Assert.Null(still.DecidedAt);
        Assert.Null(still.DecidedById);
        Assert.Null(still.DecisionReason);
        var parked = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, parked.Status);
        Assert.Equal(Marked, (string?)parked.Context[WorkItemService.InputKey]!["body"]);
        Assert.Empty(await fresh.Journal.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// One rule, read in three places: the yes refuses exactly what an edit of the item to the same arguments is refused
    /// for, field, code and words, because both ask the one function <c>workitem.create</c> asks.
    /// </summary>
    [Fact]
    public async Task The_yes_is_refused_for_exactly_what_an_edit_to_the_same_arguments_is_refused_for()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedReplyAsync(db, Marked);

        var yes = await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, null), Ct));
        var edit = await Assert.ThrowsAsync<ValidationException>(
            () => Editor(db).UpdateAsync(
                new WorkItemUpdateRequest(item.PublicId, null, null, default, default, default, default, default, default, default, default, null, null)
                {
                    Set = new JsonObject { [WorkItemService.InputKey] = Replying(Marked) },
                    Actor = Operator,
                },
                Ct));

        Assert.Equal((edit.Code, edit.Message), (yes.Code, yes.Message));
        Assert.Equal(edit.Details!.Select(d => (d.Field, d.Code, d.Message)), yes.Details!.Select(d => (d.Field, d.Code, d.Message)));
    }

    /// <summary>A no is never refused for the draft it turns down: the work ends as any rejected work does.</summary>
    [Fact]
    public async Task A_no_on_work_whose_arguments_the_document_now_refuses_goes_through()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedReplyAsync(db, Marked);

        var answered = await Service(db).RejectAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, "not like this"), Ct);

        Assert.Equal(ApprovalStatus.Rejected, answered.Status);
        await using var fresh = database.Open();
        var ended = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal((WorkItemStatus.Failed, "approval_rejected"), (ended.Status, ended.LastError!.Code));
        Assert.Equal(
            [JournalKinds.ApprovalRejected, JournalKinds.WorkItemFailed],
            await fresh.Journal.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct));
    }

    /// <summary>The same work with plain text in it: the yes reads the arguments, finds nothing wrong, and releases it.</summary>
    [Fact]
    public async Task A_yes_on_work_whose_arguments_hold_to_the_document_releases_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedReplyAsync(db, "Hi there,\n\nThursday at ten works for me.");

        var answered = await Service(db).ApproveAsync(new ApprovalDecisionRequest(approval.PublicId, Operator, null), Ct);

        Assert.Equal(ApprovalStatus.Approved, answered.Status);
        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.Created, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
    }

    /// <summary>
    /// A reply parked with <paramref name="body"/> as its text, as the claim parked it under the document of its day: the
    /// item holds the arguments and the question was asked about them.
    /// </summary>
    private static async Task<(WorkItem Item, Approval Approval)> ParkedReplyAsync(JasonDbContext db, string body)
    {
        var (item, approval) = await ParkedWork.WriteAsync(db, Ct);
        item.Operation = "conversation.reply";
        item.Context = new JsonObject { [WorkItemService.InputKey] = Replying(body) };
        approval.Operation = "conversation.reply";
        await db.SaveChangesAsync(Ct);
        return (item, approval);
    }

    private static JsonObject Replying(string body) => new()
    {
        ["channel"] = "email",
        ["thread"] = new JsonObject { ["external_id"] = "t_501" },
        ["body"] = body,
    };

    private static ApprovalService Service(JasonDbContext db)
    {
        var clock = new FixedClock(ParkedWork.Noon);
        return new ApprovalService(db, new JournalWriter(clock), clock);
    }

    private static WorkItemService Editor(JasonDbContext db)
    {
        var clock = new FixedClock(ParkedWork.Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }
}
