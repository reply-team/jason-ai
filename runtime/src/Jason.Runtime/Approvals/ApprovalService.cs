using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The four verbs a person decides through. Nothing inside the runtime calls them: the dispatcher parks work and
/// reads decisions, and the only way one is made is a request that arrives here naming the person who made it.
/// </summary>
public sealed class ApprovalService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public const int MaxReasonLength = 2000;

    /// <summary>
    /// What is waiting, newest question last. Pending by default, because the question a person opens this with
    /// is "what am I holding up?" — the other statuses are the history of decisions already made.
    /// </summary>
    public async Task<Page<ApprovalSummaryDto>> ListAsync(ApprovalListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Approvals.AsNoTracking().Include(a => a.WorkItem).ThenInclude(w => w!.Campaign).AsQueryable();
        query = query.Where(a => a.Status == (request.Status ?? ApprovalStatus.Pending));

        if (request.CampaignId is not null)
        {
            var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
            query = query.Where(a => a.CampaignId == campaign.Id);
        }

        if (request.WorkItemId is not null)
        {
            var item = await db.WorkItems.AsNoTracking()
                .FirstOrDefaultAsync(w => w.PublicId == request.WorkItemId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw DomainErrors.WorkItemNotFound(request.WorkItemId);
            query = query.Where(a => a.WorkItemId == item.Id);
        }

        if (after is not null)
        {
            query = query.Where(a => string.Compare(a.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(a => a.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, a => a.PublicId, Summary);
    }

    public async Task<ApprovalDto> GetAsync(ApprovalGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var approval = await LoadAsync(request.ApprovalId, tracking: false, cancellationToken).ConfigureAwait(false);
        return Detail(approval);
    }

    /// <summary>The work is let back into the queue, where the claim will check that it is still what was approved.</summary>
    public Task<ApprovalDto> ApproveAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(request, ApprovalStatus.Approved, cancellationToken);

    /// <summary>The work ends, carrying the decision and the person who made it as its last error.</summary>
    public Task<ApprovalDto> RejectAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(request, ApprovalStatus.Rejected, cancellationToken);

    private async Task<ApprovalDto> DecideAsync(ApprovalDecisionRequest request, ApprovalStatus decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);
        var reason = Reason(request.Reason);
        var approval = await LoadAsync(request.ApprovalId, tracking: true, cancellationToken).ConfigureAwait(false);
        var item = approval.WorkItem!;

        if (approval.Status != ApprovalStatus.Pending)
        {
            throw DomainErrors.ApprovalNotPending(approval.PublicId, approval.Status);
        }

        if (item.Status != WorkItemStatus.AwaitingApproval)
        {
            // The row is pending and the work has moved on without it — which the claim and the canceller do not
            // leave behind, so this is the answer to a race rather than to a state anybody should meet.
            throw DomainErrors.ApprovalNotPending(approval.PublicId, approval.Status);
        }

        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;

        ApprovalGate.Resolve(db, journal, item, approval, decision, actor, reason, now);
        if (decision == ApprovalStatus.Approved)
        {
            Release(item, actor, reason, now);
        }
        else
        {
            Reject(item, actor, reason, now);
        }

        try
        {
            // Two rows, one transaction, and each of the two UPDATEs carries its own status in its WHERE, so a
            // second approver — or a cancellation that arrived first — makes this write nothing at all rather
            // than half of it. In every flow that exists today the work item's guard is the one that fires,
            // because nothing moves a decision without moving the work it is about; the approval's own guard is
            // what keeps that true if something ever does.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row in front of us is the one this request was in the middle of writing, so it says what this
            // caller wanted rather than what happened. What an operator needs to be told is the decision that
            // stands, which is only readable by asking the database again.
            db.ChangeTracker.Clear();
            throw DomainErrors.ApprovalNotPending(approval.PublicId, await StandsAsync(approval.PublicId, cancellationToken).ConfigureAwait(false));
        }

        return Detail(approval);
    }

    /// <summary>
    /// The decision as it stands now, read afresh. A row somebody deleted is impossible — nothing deletes an
    /// approval — so the only way this finds nothing is a database that lost it, and <c>pending</c> is then the
    /// honest answer: whatever refused this write, it was not a decision that had already been made.
    /// </summary>
    private async Task<ApprovalStatus> StandsAsync(string publicId, CancellationToken cancellationToken) =>
        await db.Approvals.AsNoTracking()
            .Where(a => a.PublicId == publicId)
            .Select(a => (ApprovalStatus?)a.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? ApprovalStatus.Pending;

    /// <summary>Back into the queue with everything else about it untouched: its due date, its priority, its place.</summary>
    private void Release(WorkItem item, ActorRef actor, string? reason, DateTime now)
    {
        var previous = item.Status;
        WorkItemTransitions.Apply(item, WorkItemStatus.Created, now);
        journal.Append(
            db,
            actor,
            WorkItemTransitions.JournalKind(previous, WorkItemStatus.Created),
            campaign: null,
            key: "status",
            old: WorkItemJson.Status(previous),
            updated: WorkItemJson.Status(WorkItemStatus.Created),
            reason: reason,
            workItem: item);
    }

    /// <summary>
    /// A rejection is an accountable ending, not a failure of the work: the item is failed with the decision as
    /// its last error, which is what a manager reading the item sees, and no attempt is invented to carry it.
    /// </summary>
    private void Reject(WorkItem item, ActorRef actor, string? reason, DateTime now)
    {
        var previous = item.Status;
        WorkItemTransitions.Apply(item, WorkItemStatus.Failed, now);
        item.LastError = new AttemptErrorDto(
            "approval_rejected",
            reason ?? "A person rejected this work and gave no reason.",
            Retriable: false,
            Class: FailureClass.Permanent);

        journal.Append(
            db,
            actor,
            WorkItemTransitions.JournalKind(previous, WorkItemStatus.Failed),
            campaign: null,
            key: "status",
            old: WorkItemJson.Status(previous),
            updated: WorkItemJson.Status(WorkItemStatus.Failed),
            reason: reason,
            workItem: item);
    }

    /// <summary>
    /// Who decided. An absent actor is an anonymous human, which is right for creating work and wrong for
    /// deciding it: a recorded decision names a person. What this cannot do is tell a person from a process
    /// holding that person's own command line — that is the operator's trust to give, and the guarantee here is
    /// that nothing inside the runtime can give it.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.ApprovalNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.ActorRequired() : actor;
    }

    private static string? Reason(string? reason)
    {
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
        {
            throw new ValidationException([new ErrorDetail("reason", "too_long", $"reason must be at most {MaxReasonLength} characters.")]);
        }

        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private async Task<Approval> LoadAsync(string? id, bool tracking, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw DomainErrors.Required("approval_id");
        }

        var query = db.Approvals.Include(a => a.WorkItem).ThenInclude(w => w!.Campaign).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(a => a.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.ApprovalNotFound(id);
    }

    private static ApprovalDto Detail(Approval approval) =>
        ApprovalMapper.ToDto(approval, approval.WorkItem!.PublicId, approval.WorkItem.Campaign!.PublicId);

    private static ApprovalSummaryDto Summary(Approval approval) =>
        ApprovalMapper.ToSummary(approval, approval.WorkItem!.PublicId, approval.WorkItem.Campaign!.PublicId);
}
