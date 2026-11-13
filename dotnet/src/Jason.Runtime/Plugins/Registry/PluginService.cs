using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Subscriptions;
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
    SubscriptionOrderSource subscriptions,
    ReloadGate gate,
    SettingsFile settingsFile,
    TimeProvider clock,
    ILogger<PluginService> logger)
{
    public const int MaxReasonLength = 2000;

    // The reason is free text typed by a person or an agent, so it goes to the journal and never to a log: the logs
    // hold no value that arrived in a request body.
    private static readonly Action<ILogger, string, int, Exception?> Activated = LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(1, nameof(Activated)),
        "Plugin reload activated snapshot {SnapshotId} with {Plugins} plugins");

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
            // The grants and the global routes are resolved from the settings file as it stands. Its watch tells the
            // runtime of an edit only after a delay of its own, and a reload sent straight after an edit used to resolve
            // what the file held before it. A file that is there and cannot be read rejects the reload, as a refused
            // Plugins section does; one that is not there refuses nothing, and the settings in force are resolved.
            var unreadable = settingsFile.Read();
            var load = unreadable is null
                ? await loader.LoadAsync(SnapshotSource.Reload, cancellationToken).ConfigureAwait(false)
                : loader.Unreadable(SnapshotSource.Reload, unreadable);
            if (load.Snapshot is null)
            {
                // The previous snapshot stays: a partly working registry would hide the problem, and the fix is
                // always the same — repair the package and reload again.
                throw Reject(load.Report);
            }

            // Every route and the order of every kind's subscribers are checked against the candidate plugin set
            // before either registry is touched, so a route or an order naming a plugin this load does not have keeps
            // both snapshots exactly where they were. Both are asked before the answer, which names whatever is wrong.
            var candidate = await activator.BuildAsync(load.Snapshot, GlobalRouteSource.FromSettings, cancellationToken).ConfigureAwait(false);
            var chains = subscriptions.Build(load.Snapshot);
            if (candidate.Snapshot is null || chains.Chains is null)
            {
                // A route whose binding no pattern could finish measuring is unanswered rather than wrong: alone, nothing
                // is activated or recorded as rejected and the reload is answered as one to send again; beside a real
                // problem it is left out of the refusal, which names only what is wrong.
                throw candidate.Unmeasured && chains.Problems.Count == 0
                    ? DomainErrors.ValidationTimedOut(candidate.Problems)
                    : Reject(load.Report with { Activated = false, Routes = candidate.Refusals, Subscriptions = chains.Problems });
            }

            var snapshot = load.Snapshot with { Subscriptions = chains.Chains };

            // The record first, the swap second: the swap is in memory and cannot fail, the save can. A reload that
            // could not be written down leaves the previous snapshot active and is simply repeated, instead of a new
            // snapshot running with no trace of when it began.
            JournalActivation(journal, db, actor, snapshot, candidate.Snapshot, reason);
            await SubscriptionCursors.ReconcileAsync(db, chains.Chains, clock.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            registry.Replace(snapshot, load.Report);
            activator.Activate(candidate.Snapshot);
            Activated(logger, snapshot.Id, snapshot.Plugins.Count, null);
            return PluginMapper.ToDto(snapshot, candidate.Snapshot.Id, load.Report, activated: true);
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
    /// invocation pinning leans on: which package, at which digest, was active when. The journal kinds the load froze
    /// travel as counts only — how many each plugin is granted, how many have a chain, how many plugins are in one —
    /// never a kind or an order: <c>plugin.list</c> says which.
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
                ["subscriptions"] = plugin.Grants.Subscriptions.Count,
            });
        }

        var chains = snapshot.Subscriptions;

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
                    ["override_count"] = RouteCounts.GlobalOverrides(routes),
                    ["campaign_route_count"] = RouteCounts.CampaignRoutes(routes),
                },
                ["subscriptions"] = new JsonObject
                {
                    ["kind_count"] = chains.Kinds.Count,
                    ["subscriber_count"] = chains.Kinds.SelectMany(chains.For).Distinct(StringComparer.Ordinal).Count(),
                },
            },
            reason: reason);
    }
}
