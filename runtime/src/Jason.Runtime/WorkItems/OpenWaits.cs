using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// The open wait of a waiting item, read and closed by whatever stops the item: a cancellation or an expiry ends the
/// wait with the item, so nothing is left for the resume step to hand back. A wait closed this way is never released —
/// it keeps no release reason — because what it waited for did not happen; the item was stopped instead.
/// </summary>
public static class OpenWaits
{
    /// <summary>The open wait of one item, tracked, or null for an item that is not waiting.</summary>
    public static Task<WorkItemWait?> OfAsync(JasonDbContext db, int workItemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.WorkItemWaits.FirstOrDefaultAsync(w => w.WorkItemId == workItemId && w.Status == WaitStatus.Open, ct);
    }

    /// <summary>The open waits of a batch, by item, in one query rather than one per item.</summary>
    public static Task<Dictionary<int, WorkItemWait>> OfAsync(JasonDbContext db, IReadOnlyCollection<int> workItemIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.WorkItemWaits
            .Where(w => workItemIds.Contains(w.WorkItemId) && w.Status == WaitStatus.Open)
            .ToDictionaryAsync(w => w.WorkItemId, ct);
    }

    /// <summary>
    /// Releases a wait and sends its item back to <c>created</c> in one change, journaled as <c>workitem_resumed</c> with
    /// why — the one routine for the scan's resume step and for a person's <c>workitem.resume</c>, so a wait is never
    /// released with its item left waiting, where no scan would find it again. The scan passes the dispatcher and no
    /// reason; a person's resume passes them and the reason they gave.
    /// </summary>
    public static void Release(
        JasonDbContext db,
        JournalWriter journal,
        WorkItemWait wait,
        WorkItem item,
        WaitReleaseReason because,
        ActorRef actor,
        string? reason,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(journal);
        wait.Status = WaitStatus.Released;
        wait.ReleasedAt = now;
        wait.ReleaseReason = because;
        WorkItemTransitions.Apply(item, WorkItemStatus.Created, now);
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemResumed,
            campaign: null,
            key: wait.Attempt?.PublicId,
            updated: new JsonObject { ["because"] = SnakeCaseEnumConverter<WaitReleaseReason>.Format(because) },
            reason: reason,
            workItem: item,
            attempt: wait.Attempt);
    }

    /// <summary>Closes a wait with its item: <c>cancelled</c> or <c>expired</c>, at the moment the item stopped.</summary>
    public static void Close(WorkItemWait wait, WaitStatus how, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(wait);
        if (how is not (WaitStatus.Cancelled or WaitStatus.Expired))
        {
            throw new ArgumentException($"A wait closes with its item as cancelled or expired, not as {how}.", nameof(how));
        }

        wait.Status = how;
        wait.ReleasedAt = now;
    }
}
