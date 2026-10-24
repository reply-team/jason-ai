using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The step of a scan between the summon and the claim: waiting work goes back to the queue once what it waited for has
/// happened — the items it named have ended, all of them or any one, or its deadline has come. After the lease enforcer,
/// so an item whose last attempt was lost for good in this scan releases its waiter in the same scan; before the claim,
/// so what is released can be claimed in that scan too.
/// <para>
/// The condition is read from rows, never from an event: a wait whose items had all ended before it was written, or whose
/// deadline passed while the runtime was down, is simply ready at the next scan. Nothing here reads a context or anything
/// anybody wrote — statuses, identifiers and the clock only — and no campaign: a released item is ordinary created work,
/// and whether its campaign lets it run is the claim's to decide, as for any other.
/// </para>
/// </summary>
public sealed class Resumer(JournalWriter journal, TimeProvider clock)
{
    public async Task<int> ResumeAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = clock.GetUtcNow().UtcDateTime;
        var ready = await ReadyAsync(db, now, ct).ConfigureAwait(false);

        // One short write a wait, so an item cancelled a moment ago costs the others nothing.
        var resumed = 0;
        foreach (var row in ready)
        {
            var wait = await db.WorkItemWaits
                .Include(w => w.Attempt)
                .FirstOrDefaultAsync(w => w.Id == row.Id && w.Status == WaitStatus.Open, ct)
                .ConfigureAwait(false);
            var item = wait is null
                ? null
                : await db.WorkItems.FirstOrDefaultAsync(w => w.Id == wait.WorkItemId && w.Status == WorkItemStatus.Waiting, ct).ConfigureAwait(false);
            if (wait is null || item is null)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            var because = SnakeCaseEnumConverter<WaitReleaseReason>.Parse(row.Reason);
            OpenWaits.Release(db, journal, wait, item, because, Actors.Dispatcher, reason: null, now);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                resumed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The item was cancelled or expired between the read and this write; that stands.
            }

            db.ChangeTracker.Clear();
        }

        return resumed;
    }

    /// <summary>
    /// The open waits whose item is waiting and whose condition holds, oldest first, each with why it is over. The items
    /// come before the deadline: a wait whose items ended and whose deadline came by the same scan was over because of the
    /// items, which is what it waited for. Raw SQL because the condition is two correlated subqueries over the named items;
    /// its plan is pinned by a test — the waiting items by their status, each one's open wait by the index of the waits by
    /// item and status, each wait's items and each item by its key.
    /// </summary>
    internal static Task<List<ReadyWait>> ReadyAsync(JasonDbContext db, DateTime now, CancellationToken ct) =>
        db.Database.SqlQueryRaw<ReadyWait>(ReadySql, now).ToListAsync(ct);

    private static readonly string ReadySql = Compose();

    private static string Compose()
    {
        static string Text<TEnum>(TEnum value) where TEnum : struct, Enum => $"'{SnakeCaseEnumConverter<TEnum>.Format(value)}'";
        var ended = string.Join(", ", WorkItemTransitions.Terminal.Order().Select(Text));

        // The constants are our own vocabulary, written into the text rather than bound, so the planner sees the statuses it
        // searches the indexes by. The clock is the one parameter.
        return $$"""
            SELECT r.id AS "Id", r.reason AS "Reason" FROM (
              SELECT x.id,
                     CASE
                       WHEN x.mode = {{Text(WaitMode.All)}} AND NOT EXISTS (
                              SELECT 1 FROM work_item_wait_items i JOIN work_items c ON c.id = i.child_work_item_id
                              WHERE i.wait_id = x.id AND c.status NOT IN ({{ended}}))
                         THEN {{Text(WaitReleaseReason.ChildrenEnded)}}
                       WHEN x.mode = {{Text(WaitMode.Any)}} AND EXISTS (
                              SELECT 1 FROM work_item_wait_items i JOIN work_items c ON c.id = i.child_work_item_id
                              WHERE i.wait_id = x.id AND c.status IN ({{ended}}))
                         THEN {{Text(WaitReleaseReason.ChildrenEnded)}}
                       WHEN x.deadline <= {0} AND x.until IS NOT NULL THEN {{Text(WaitReleaseReason.MomentReached)}}
                       WHEN x.deadline <= {0} THEN {{Text(WaitReleaseReason.WaitCeiling)}}
                     END AS reason
              FROM work_item_waits x JOIN work_items w ON w.id = x.work_item_id
              WHERE x.status = {{Text(WaitStatus.Open)}} AND w.status = {{Text(WorkItemStatus.Waiting)}}
            ) AS r
            WHERE r.reason IS NOT NULL
            ORDER BY r.id
            """;
    }

    /// <summary>One wait that is over, and why, as the release reason's own word.</summary>
    internal sealed record ReadyWait(int Id, string Reason);
}
