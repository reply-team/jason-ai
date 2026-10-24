using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The one way work stops on purpose. A caller cancelling a single item, an archived campaign and a contact
/// nobody may touch any more all arrive here, so a cancelled item always looks the same: its live attempt ended,
/// whatever was running asked to stop, and one line in the chronicle saying who ended it and why. Nothing here
/// saves — the cancellation commits with the change that caused it, and only then does the caller stop the
/// children, through <see cref="StopCancelled"/>.
/// </summary>
public sealed class WorkItemCanceller(JournalWriter journal, TimeProvider clock, AttemptOutcomes outcomes, RunningAttemptRegistry registry)
{
    /// <summary>Everything that is not finished for good, derived from the transition table so the two can never drift.</summary>
    private static readonly WorkItemStatus[] Finished = [.. WorkItemTransitions.Final];

    /// <summary>The attempts this scope has cancelled whose children are still to be stopped.</summary>
    private readonly List<string> _toStop = [];

    /// <summary>
    /// The caller has already established that the item may still be cancelled. A decision this item was waiting
    /// for — or one it had already been given — goes with it: a live approval about work that can never run is
    /// the one row that would make <c>approval.list</c> untrue. So does the wait of a waiting item, which the caller
    /// reads with <see cref="OpenWaits"/>: a wait left open about work that was stopped would be released by the next scan.
    /// A waiting item whose open wait the caller could not find was released between the caller's two reads, so the item
    /// it holds is no longer the one in the database: that is answered as the concurrent change it is, and a retry reads
    /// the released item and cancels it.
    /// </summary>
    public void Cancel(JasonDbContext db, WorkItem item, ActorRef actor, string? reason, Approval? live = null, WorkItemWait? wait = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Status == WorkItemStatus.Waiting && wait is null)
        {
            throw DomainErrors.ConcurrentUpdate();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (live is not null)
        {
            ApprovalGate.Resolve(db, journal, item, live, ApprovalStatus.Cancelled, actor, reason, now);
        }

        if (wait is not null)
        {
            OpenWaits.Close(wait, WaitStatus.Cancelled, now);
        }

        var attempt = WorkItemQueries.LiveAttempt(item);
        if (attempt is not null)
        {
            outcomes.CancelAttempt(attempt);

            // The database only says the attempt is over; the child process is stopped by whoever owns it, and
            // only in this runtime — a stale executor elsewhere learns the same thing from its next fenced call.
            // Not yet, though: a child stopped before this cancellation commits exits while the attempt is still
            // running in the database, and the handler watching it records that exit as how the attempt ended
            // and gives the work back to the queue. The cancellation then finds the item moved, answers
            // Conflict, and the work somebody stopped is handed out again. So the caller stops it once the
            // change has committed, as the lease enforcer does after its own verdict.
            _toStop.Add(attempt.PublicId);
        }

        var previous = item.Status;
        WorkItemTransitions.Apply(item, WorkItemStatus.Cancelled, now);
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemCancelled,
            campaign: null,
            key: "status",
            old: WorkItemJson.Status(previous),
            updated: WorkItemJson.Status(WorkItemStatus.Cancelled),
            reason: reason,
            workItem: item,
            attempt: attempt);
    }

    /// <summary>
    /// Stops the child of every attempt this scope cancelled. Called once the change that cancelled them has
    /// committed, and never before it; a cancellation whose change failed to commit stopped nothing, because it
    /// never happened. A child nobody stops here still learns from its next fenced call that its attempt is over,
    /// and the lease enforcer stops one that outlives its attempt.
    /// </summary>
    public void StopCancelled()
    {
        foreach (var attemptId in _toStop)
        {
            registry.TryKill(attemptId);
        }

        _toStop.Clear();
    }

    /// <summary>Everything a campaign still has open, or — with a contact — everything it still has open about that person.</summary>
    public async Task<int> CancelOpenAsync(JasonDbContext db, int campaignId, int? contactId, ActorRef actor, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.WorkItems.Include(w => w.Attempts).Where(w => w.CampaignId == campaignId && !Finished.Contains(w.Status));
        if (contactId is { } contact)
        {
            query = query.Where(w => w.ContactId == contact);
        }

        return await CancelAllAsync(db, await query.ToListAsync(cancellationToken).ConfigureAwait(false), actor, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Everything still open about one person, wherever it lives: archiving a contact is not a per-campaign act.</summary>
    public async Task<int> CancelOpenForContactAsync(JasonDbContext db, int contactId, ActorRef actor, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var items = await db.WorkItems
            .Include(w => w.Attempts)
            .Where(w => w.ContactId == contactId && !Finished.Contains(w.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await CancelAllAsync(db, items, actor, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The live decisions and open waits of a whole batch, read in one query each rather than one per item: archiving a
    /// campaign can cancel a great deal of work, and a query per item is how that becomes slow without anybody noticing.
    /// </summary>
    private async Task<int> CancelAllAsync(
        JasonDbContext db,
        List<WorkItem> items,
        ActorRef actor,
        string reason,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(item => item.Id).ToList();
        var live = await db.Approvals
            .Where(a => ids.Contains(a.WorkItemId) && (a.Status == ApprovalStatus.Pending || a.Status == ApprovalStatus.Approved))
            .ToDictionaryAsync(a => a.WorkItemId, cancellationToken)
            .ConfigureAwait(false);
        var waits = await OpenWaits.OfAsync(db, ids, cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            Cancel(db, item, actor, reason, live.GetValueOrDefault(item.Id), waits.GetValueOrDefault(item.Id));
        }

        return items.Count;
    }

}
