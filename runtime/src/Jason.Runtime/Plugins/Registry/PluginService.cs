using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The registry as an operation: read it, or reload it. A reload is explicit and atomic — every candidate is
/// validated, and the new snapshot replaces the old one only when the whole candidate set is loadable. There are
/// no filesystem watchers: what is installed becomes what is active because somebody said so.
/// </summary>
public sealed class PluginService(
    JasonDbContext db,
    JournalWriter journal,
    PluginRegistry registry,
    PluginLoader loader,
    RouteRegistry routes,
    RouteActivator activator,
    ReloadGate gate,
    ILogger<PluginService> logger)
{
    public const int MaxReasonLength = 2000;

    private static readonly Action<ILogger, string, int, string?, Exception?> Activated = LoggerMessage.Define<string, int, string?>(
        LogLevel.Information,
        new EventId(1, nameof(Activated)),
        "Plugin reload activated snapshot {SnapshotId} with {Plugins} plugins; reason {Reason}");

    private static readonly Action<ILogger, int, Exception?> Rejected = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(2, nameof(Rejected)),
        "Plugin reload rejected with {Problems} problems; the previous snapshot stays active");

    public PluginRegistryDto List() =>
        PluginMapper.ToDto(registry.Snapshot, routes.Snapshot.Id, registry.LastReload, registry.LastReload?.Activated ?? true);

    public async Task<PluginRegistryDto> ReloadAsync(PluginReloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        if (request.Reason is not null && request.Reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var load = await loader.LoadAsync(SnapshotSource.Reload, cancellationToken).ConfigureAwait(false);
            if (load.Snapshot is null)
            {
                // The previous snapshot stays: a partly working registry would hide the problem, and the fix is
                // always the same — repair the package and reload again.
                throw Reject(load.Report);
            }

            // Every route is checked against the candidate plugin set before either registry is touched, so a
            // route naming a plugin this load does not have keeps both snapshots exactly where they were.
            var candidate = await activator.BuildAsync(load.Snapshot, GlobalRouteSource.FromSettings, cancellationToken).ConfigureAwait(false);
            if (candidate.Snapshot is null)
            {
                throw Reject(load.Report with { Activated = false, Routes = candidate.Problems });
            }

            // The record first, the swap second: the swap is in memory and cannot fail, the save can. A reload that
            // could not be written down leaves the previous snapshot active and is simply repeated, instead of a new
            // snapshot running with no trace of when it began.
            JournalActivation(journal, db, actor, load.Snapshot, candidate.Snapshot, reason);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            registry.Replace(load.Snapshot, load.Report);
            activator.Activate(candidate.Snapshot);
            Activated(logger, load.Snapshot.Id, load.Snapshot.Plugins.Count, reason, null);
            return PluginMapper.ToDto(load.Snapshot, candidate.Snapshot.Id, load.Report, activated: true);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>
    /// A load that will change nothing: the report is kept so that <c>plugin.list</c> can say why, and the
    /// problems are answered to the caller. Both halves of the registry stay exactly as they were.
    /// </summary>
    private DomainException Reject(ReloadReport report)
    {
        registry.Record(report);
        var details = PluginMapper.ToDetails(report);
        Rejected(logger, details.Count, null);
        return DomainErrors.PluginReloadRejected(details);
    }

    /// <summary>
    /// One global entry per activated snapshot, shared with the load at startup. It is the audit trail that
    /// invocation pinning leans on: which package, at which digest, was active when.
    /// </summary>
    public static void JournalActivation(
        JournalWriter journal,
        JasonDbContext db,
        ActorRef actor,
        PluginSnapshot snapshot,
        RouteSnapshot routes,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(routes);

        var plugins = new JsonArray();
        foreach (var plugin in snapshot.Plugins)
        {
            plugins.Add(new JsonObject
            {
                ["id"] = plugin.Manifest.Id,
                ["version"] = plugin.Manifest.Version,
                ["digest"] = plugin.Digest,
                ["status"] = SnakeCase.Convert(plugin.Status.ToString()),
            });
        }

        journal.Append(
            db,
            actor,
            JournalKinds.PluginsReloaded,
            campaign: null,
            key: snapshot.Id,
            updated: new JsonObject
            {
                ["snapshot_id"] = snapshot.Id,
                ["source"] = SnakeCase.Convert(snapshot.Source.ToString()),
                ["plugins"] = plugins,

                // The routes the same act froze. Which package was active when is only half of "what was
                // running": the other half is where each operation was being sent.
                ["routes"] = new JsonObject
                {
                    ["snapshot_id"] = routes.Id,
                    ["global_default"] = routes.Global.Default?.PluginId,
                    ["override_count"] = routes.Global.Operations.Count,
                    ["campaign_route_count"] = routes.Campaigns.Values.Sum(set => set.Operations.Count + (set.Default is null ? 0 : 1)),
                },
            },
            reason: reason);
    }
}
