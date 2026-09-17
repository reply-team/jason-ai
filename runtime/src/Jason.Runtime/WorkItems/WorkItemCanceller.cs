using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The one way work stops on purpose. A caller cancelling a single item, an archived campaign and a contact
/// nobody may touch any more all arrive here, so a cancelled item always looks the same: its live attempt ended,
/// whatever was running asked to stop, and one line in the chronicle saying who ended it and why. Nothing here
/// saves — the cancellation commits with the change that caused it.
/// </summary>
public sealed class WorkItemCanceller(JournalWriter journal, TimeProvider clock, AttemptOutcomes outcomes, RunningAttemptRegistry registry)
{
    /// <summary>Everything that is not finished for good, derived from the transition table so the two can never drift.</summary>
    private static readonly WorkItemStatus[] Finished = [.. WorkItemTransitions.Final];

    /// <summary>
    /// The caller has already established that the item may still be cancelled. A decision this item was waiting
    /// for — or one it had already been given — goes with it: a live approval about work that can never run is
    /// the one row that would make <c>approval.list</c> untrue.
    /// </summary>
    public void Cancel(JasonDbContext db, WorkItem item, ActorRef actor, string? reason, Approval? live = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var now = clock.GetUtcNow().UtcDateTime;
        if (live is not null)
        {
            ApprovalGate.Resolve(db, journal, item, live, ApprovalStatus.Cancelled, actor, reason, now);
        }

        var attempt = WorkItemQueries.LiveAttempt(item);
        if (attempt is not null)
        {
            outcomes.CancelAttempt(attempt);

            // The database only says the attempt is over; the child process is stopped by whoever owns it, and
            // only in this runtime — a stale executor elsewhere learns the same thing from its next fenced call.
            registry.TryKill(attempt.PublicId);
        }

        var previous = item.Status;
        WorkItemTransitions.Apply(item, WorkItemStatus.Cancelled, now);
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemCancelled,
            campaign: null,
            key: "status",
            old: Status(previous),
            updated: Status(WorkItemStatus.Cancelled),
            reason: reason,
            workItem: item,
            attempt: attempt);
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
    /// The live decisions of a whole batch, read in one query rather than one per item: archiving a campaign can
    /// cancel a great deal of work, and a query per item is how that becomes slow without anybody noticing.
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

        foreach (var item in items)
        {
            Cancel(db, item, actor, reason, live.GetValueOrDefault(item.Id));
        }

        return items.Count;
    }

    private static JsonNode? Status(WorkItemStatus status) => JsonSerializer.SerializeToNode(status, JasonJson.Options);
}
