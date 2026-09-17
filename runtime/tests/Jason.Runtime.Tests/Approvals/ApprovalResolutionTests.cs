using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// What happens to a decision when the work it was about goes away. A pending row about work nobody can run is
/// the one thing that would make <c>approval.list</c> untrue, so every way work ends resolves the decision it
/// was holding — and the ways it ends are cancellation and the clock.
/// </summary>
public class ApprovalResolutionTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A decision nobody made in time is not one anybody should still be offered. Parked work is unclaimed work,
    /// so its due date takes it like any other, and the approval goes in the same transaction.
    /// </summary>
    [Fact]
    public async Task Work_parked_past_its_due_date_expires_and_takes_its_pending_approval_with_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db, dueAt: Noon.AddMinutes(-1));

        var expired = await new Expirer(new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon)).ExpireAsync(db, Ct);

        Assert.Equal(1, expired);
        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.Expired, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        var resolved = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.Id == approval.Id, Ct);
        Assert.Equal(ApprovalStatus.Cancelled, resolved.Status);
        Assert.Equal(Noon, resolved.DecidedAt);
        Assert.Equal(ActorType.System, resolved.DecidedByType);

        var kinds = await fresh.Journal.AsNoTracking()
            .Where(e => e.WorkItemId == item.PublicId)
            .OrderBy(e => e.Id)
            .Select(e => e.Kind)
            .ToListAsync(Ct);
        Assert.Equal(["approval_cancelled", "workitem_expired"], kinds);
    }

    /// <summary>Work whose due date has not passed keeps waiting: the clock gives up on nothing early.</summary>
    [Fact]
    public async Task Work_parked_before_its_due_date_keeps_waiting()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await ParkedAsync(db, dueAt: Noon.AddHours(1));

        Assert.Equal(0, await new Expirer(new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon)).ExpireAsync(db, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal(ApprovalStatus.Pending, (await fresh.Approvals.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// Cancelling parked work is the other way a decision stops mattering, and it says who ended it: the person
    /// who cancelled the work, with their reason, rather than the runtime.
    /// </summary>
    [Fact]
    public async Task Cancelling_parked_work_leaves_no_pending_approval_behind()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approval) = await ParkedAsync(db, dueAt: null);
        var clock = new FixedClock(Noon);
        var canceller = new WorkItemCanceller(
            new JournalWriter(clock),
            clock,
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings()),
            new RunningAttemptRegistry());

        canceller.Cancel(db, item, new ActorRef(ActorType.Human, "operator"), "not this quarter", approval);
        await db.SaveChangesAsync(Ct);

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.Cancelled, (await fresh.WorkItems.AsNoTracking().SingleAsync(Ct)).Status);
        var resolved = await fresh.Approvals.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ApprovalStatus.Cancelled, resolved.Status);
        Assert.Equal("operator", resolved.DecidedById);
        Assert.Equal("not this quarter", resolved.DecisionReason);
    }

    /// <summary>
    /// The gate knows two words: <c>auto</c> runs, and anything else asks a person. A contract that one day
    /// published a third — an approval to be given for every attempt, say — would be read as "ask once" and
    /// under-enforced, so the catalog itself is held to what this build understands.
    /// </summary>
    [Fact]
    public void Every_published_contract_asks_for_an_approval_this_build_understands() =>
        Assert.All(
            OperationCatalog.All,
            contract => Assert.Contains(contract.Approval.Value, (string[])["auto", "confirm_once"], StringComparer.Ordinal));

    /// <summary>One parked item and the decision it is waiting for, written the way the claim writes them.</summary>
    private static async Task<(WorkItem Item, Approval Approval)> ParkedAsync(JasonDbContext db, DateTime? dueAt)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        item.Status = WorkItemStatus.AwaitingApproval;
        item.DueAt = dueAt;
        db.WorkItems.Add(item);

        var approval = new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = "campaign.enroll",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "campaign.enroll" },
            SubjectHash = "sha256:subject",
            Preview = new JsonObject { ["intent"] = "enrol" },
            PluginId = "stand-in-provider",
            PluginSnapshotId = "snp_one",
            RoutingSnapshotId = "rts_one",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
        };

        db.Approvals.Add(approval);
        await db.SaveChangesAsync(Ct);
        return (item, approval);
    }
}
