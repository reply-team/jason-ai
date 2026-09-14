using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
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
/// </summary>
public sealed class Expirer(JournalWriter journal, TimeProvider clock)
{
    public async Task<int> ExpireAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = clock.GetUtcNow().UtcDateTime;
        var overdue = await db.WorkItems
            .Where(w => w.Status == WorkItemStatus.Created && w.DueAt != null && w.DueAt < now)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (overdue.Count == 0)
        {
            return 0;
        }

        foreach (var item in overdue)
        {
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
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return overdue.Count;
    }
}
