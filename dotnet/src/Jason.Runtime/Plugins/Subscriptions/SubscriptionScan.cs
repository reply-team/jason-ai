using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// The step of a scan that keeps the subscribed kinds moving, last, after the notifier: it offers the subscription runner
/// each kind that has a position, a line after it, no chain in flight and a chain whose every member can run, the kind
/// whose next line is oldest first, while the runner has a free child. It reads the positions and the journal in one
/// query, and the line a kind waits at only until its wait has been said under the load; it never invokes a plugin,
/// never writes, and never waits for a chain.
/// </summary>
/// <remarks>
/// A draining dispatcher offers nothing: a drain waits for the work in flight to end, and a chain started now would be
/// new work. A chain under way goes on until it ends or the stop ends its child. A kind whose chain has a member that
/// cannot run on this machine waits at its line, and nothing of it is spent, until a load makes the member available or
/// takes it out of the chain; the scan has the runner say so once for each load.
/// </remarks>
public sealed class SubscriptionScan(PluginRegistry plugins, SubscriptionRunner runner, DispatcherStatus status)
{
    /// <returns>How many kinds were offered.</returns>
    public async Task<int> ScanAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var snapshot = plugins.Snapshot;
        if (status.State != DispatcherState.Running || snapshot.Subscriptions.Kinds.Count == 0 || !runner.HasFreeChild)
        {
            return 0;
        }

        // One read for every kind: each position with the id of the first line after it, found through the journal's
        // kind index and then its row id.
        var due = await db.SubscriptionCursors
            .AsNoTracking()
            .Select(cursor => new
            {
                cursor.Kind,
                Line = db.Journal.Where(line => line.Kind == cursor.Kind && line.Id > cursor.AfterLine).Min(line => (int?)line.Id),
            })
            .Where(row => row.Line != null)
            .OrderBy(row => row.Line)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var offered = 0;
        var waiting = new List<(string Kind, int Line, string PluginId)>();
        var free = true;
        foreach (var row in due)
        {
            var chain = snapshot.Subscriptions.For(row.Kind);
            if (chain.Count == 0)
            {
                continue;
            }

            if (chain.FirstOrDefault(id => snapshot.Find(id) is not { Status: PluginStatus.Valid }) is { } unavailable)
            {
                if (!runner.Noted(snapshot.Id, row.Kind))
                {
                    waiting.Add((row.Kind, row.Line!.Value, unavailable));
                }

                continue;
            }

            free = free && runner.HasFreeChild;
            if (free && runner.TryStart(row.Kind))
            {
                offered++;
            }
        }

        if (waiting.Count > 0)
        {
            // A kind passed over waits at its line, said once for each load; its line is read only until then.
            var ids = waiting.Select(wait => wait.Line).ToList();
            var lines = await db.Journal
                .AsNoTracking()
                .Where(line => ids.Contains(line.Id))
                .ToDictionaryAsync(line => line.Id, line => line.PublicId, ct)
                .ConfigureAwait(false);
            foreach (var (kind, line, pluginId) in waiting)
            {
                runner.NoteWaiting(snapshot.Id, kind, lines[line], pluginId);
            }
        }

        return offered;
    }
}
