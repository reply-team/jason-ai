using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// Keeps each subscribed kind's position in step with the chains a load froze, inside the activation that freezes them:
/// the rows are added and removed as entities, so they commit with the activation line in one save, before the swap. A
/// load that is rejected never reaches here, so no position is lost to a bad file.
/// </summary>
public static class SubscriptionCursors
{
    /// <summary>
    /// A kind given its first subscriber is placed at the journal's head as the database holds it — before the
    /// activation's own line, which a subscriber to that kind therefore sees; a line another writer commits between this
    /// read and the save is seen one line early, never missed. A kind that keeps a subscriber keeps its position, and a
    /// kind left with none loses its row, so a subscriber that returns starts at the head again.
    /// </summary>
    public static async Task ReconcileAsync(JasonDbContext db, SubscriptionChains chains, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(chains);

        var held = await db.SubscriptionCursors.ToListAsync(cancellationToken).ConfigureAwait(false);
        var wanted = chains.Kinds.ToHashSet(StringComparer.Ordinal);
        foreach (var cursor in held.Where(cursor => !wanted.Contains(cursor.Kind)))
        {
            db.SubscriptionCursors.Remove(cursor);
        }

        var missing = chains.Kinds.Where(kind => !held.Any(cursor => string.Equals(cursor.Kind, kind, StringComparison.Ordinal))).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var head = await db.Journal.MaxAsync(entry => (int?)entry.Id, cancellationToken).ConfigureAwait(false) ?? 0;
        foreach (var kind in missing)
        {
            db.SubscriptionCursors.Add(new SubscriptionCursor { Kind = kind, AfterLine = head, Revision = 0, CreatedAt = now, UpdatedAt = now });
        }
    }
}
