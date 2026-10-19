using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>
/// The step of a scan that keeps the accounts' events read, between the lease enforcer and the inbound read: it finds
/// the accounts the routes bind at plugins that read their events, gives each its source the first time it is bound,
/// and hands the reader the most overdue read — one at most, and only when the reader is idle. It reads two snapshots,
/// the sources' rows and the clock; it never invokes a plugin and never waits for a read.
/// </summary>
/// <remarks>
/// A draining dispatcher hands out no new read: a drain waits for the work in flight to end, and a read started now would
/// be new work. A read under way goes on until the stop ends its child, and the page in flight is not kept. A source whose
/// account no route binds any more keeps its row and its cursor, is not read, and is read from that cursor once a route
/// binds it again.
/// </remarks>
public sealed class EventScan(
    RouteRegistry routes,
    PluginRegistry plugins,
    EventReader reader,
    DispatcherStatus status,
    LiveSettings<DispatcherOptions> settings,
    TimeProvider clock)
{
    /// <returns>1 when a read was handed over, 0 otherwise.</returns>
    public async Task<int> ScanAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var routeSnapshot = routes.Snapshot;
        var pluginSnapshot = plugins.Snapshot;
        var bound = EventAccounts.Bound(routeSnapshot, pluginSnapshot);
        if (bound.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var sources = await SourcesAsync(db, bound, now, ct).ConfigureAwait(false);
        if (status.State != DispatcherState.Running || !reader.Idle)
        {
            return 0;
        }

        var due = EventSchedule.MostOverdueFirst(sources.Values, source => reader.HasRead(source.Id), settings.Current.EventsCadenceSeconds, now);
        if (due.Count == 0)
        {
            return 0;
        }

        var source = due[0];
        var account = bound.First(entry => entry.PluginId == source.PluginId && entry.BindingIdentity == source.BindingIdentity);
        var read = new EventRead(
            source.Id,
            source.PublicId,
            source.PluginId,
            source.BindingIdentity,
            account.Binding,
            source.Cursor,
            EventSchedule.Plan(source, now),
            new PinnedPlugin(pluginSnapshot.Find(source.PluginId)!, pluginSnapshot.Id),
            routeSnapshot.Id);
        return reader.TryHandOver(read) ? 1 : 0;
    }

    /// <summary>
    /// The source of each bound account, inserting the ones bound for the first time. Another writer inserting the same
    /// one first is no fault: the rows are read again and the unique index has said which stands.
    /// </summary>
    private static async Task<Dictionary<(string, string), EventSource>> SourcesAsync(
        JasonDbContext db,
        IReadOnlyList<BoundAccount> bound,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await ReadAsync(db, bound, ct).ConfigureAwait(false);
        var missing = bound.Where(account => !existing.ContainsKey((account.PluginId, account.BindingIdentity))).ToList();
        if (missing.Count == 0)
        {
            return existing;
        }

        foreach (var account in missing)
        {
            db.EventSources.Add(new EventSource
            {
                PublicId = PublicId.New("evs"),
                PluginId = account.PluginId,
                BindingIdentity = account.BindingIdentity,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }

        return await ReadAsync(db, bound, ct).ConfigureAwait(false);
    }

    private static async Task<Dictionary<(string, string), EventSource>> ReadAsync(JasonDbContext db, IReadOnlyList<BoundAccount> bound, CancellationToken ct)
    {
        var pluginIds = bound.Select(account => account.PluginId).Distinct(StringComparer.Ordinal).ToList();
        var rows = await db.EventSources.AsNoTracking().Where(source => pluginIds.Contains(source.PluginId)).ToListAsync(ct).ConfigureAwait(false);
        var wanted = bound.Select(account => (account.PluginId, account.BindingIdentity)).ToHashSet();
        return rows.Where(row => wanted.Contains((row.PluginId, row.BindingIdentity))).ToDictionary(row => (row.PluginId, row.BindingIdentity));
    }
}
