using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The first step of a scan: work whose moment has passed without anyone claiming it. Only unclaimed items are
/// given up — an attempt that is already running is allowed to finish, because stopping it would waste work
/// that is nearly done to honour a deadline that is already missed.
/// <para>
/// Work waiting for a person is unclaimed work, so it expires too, and the decision it was waiting for is ended
/// with it. A decision nobody made in time is not one anybody should still be offered.
/// </para>
/// </summary>
public sealed class Expirer(JournalWriter journal, TimeProvider clock)
{
    public async Task<int> ExpireAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = clock.GetUtcNow().UtcDateTime;
        // The ids first, the rows one at a time: a caller who cancelled one of them a moment ago must not cost
        // the rest of the backlog its expiry, and a whole batch that keeps failing would log an error a tick.
        var overdue = await db.WorkItems.AsNoTracking()
            .Where(w => (w.Status == WorkItemStatus.Created || w.Status == WorkItemStatus.AwaitingApproval)
                && w.DueAt != null && w.DueAt < now)
            .Select(w => w.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expired = 0;
        foreach (var id in overdue)
        {
            var item = await db.WorkItems
                .FirstOrDefaultAsync(
                    w => w.Id == id && (w.Status == WorkItemStatus.Created || w.Status == WorkItemStatus.AwaitingApproval),
                    ct)
                .ConfigureAwait(false);
            if (item is null)
            {
                continue;
            }

            if (await ApprovalGate.LiveAsync(db, item.Id, ct).ConfigureAwait(false) is { } live)
            {
                ApprovalGate.Resolve(db, journal, item, live, ApprovalStatus.Cancelled, Actors.Dispatcher, reason: null, now);
            }

            var dueAt = item.DueAt;
            WorkItemTransitions.Apply(item, WorkItemStatus.Expired, now);
            journal.Append(
                db,
                Actors.Dispatcher,
                JournalKinds.WorkItemExpired,
                campaign: null,
                key: "due_at",
                old: JsonSerializer.SerializeToNode(WorkItemMapper.Utc(dueAt), JasonJson.Options),
                workItem: item);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else changed the item between the backlog read and this write; theirs stands.
                db.ChangeTracker.Clear();
                continue;
            }

            expired++;
        }

        return expired;
    }
}
