using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The load that happens because the runtime started. It follows exactly the same path as a reload, and it
/// never stops the runtime: a candidate set that cannot be activated leaves the registry empty, the diagnostics
/// on record and campaigns, contacts and AI work entirely unaffected. The fix is a repair and a reload.
/// </summary>
public sealed class PluginStartupLoader(
    IServiceScopeFactory scopes,
    PluginRegistry registry,
    ILogger<PluginStartupLoader> logger) : IHostedService
{
    private static readonly Action<ILogger, int, Exception?> StartupRejected = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(1, nameof(StartupRejected)),
        "The plugin load at startup was rejected with {Problems} problems; no plugins are active");

    private static readonly Action<ILogger, Exception?> StartupFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2, nameof(StartupFailed)),
        "The plugin load at startup failed; the runtime continues with no plugins active");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var load = await scope.ServiceProvider.GetRequiredService<PluginLoader>()
                .LoadAsync(SnapshotSource.Startup, cancellationToken)
                .ConfigureAwait(false);

            if (load.Snapshot is null)
            {
                registry.Record(load.Report);
                StartupRejected(logger, load.Report.Candidates.Sum(candidate => candidate.Problems.Count), null);
                return;
            }

            // The same all-or-nothing rule a reload follows, and for the same reason: a bad route leaves both
            // registries empty rather than a plugin set active with routes nobody checked.
            var activator = scope.ServiceProvider.GetRequiredService<RouteActivator>();
            var candidate = await activator.BuildAsync(load.Snapshot, GlobalRouteSource.FromSettings, cancellationToken).ConfigureAwait(false);
            if (candidate.Snapshot is null)
            {
                registry.Record(load.Report with { Activated = false, Routes = candidate.Problems });
                StartupRejected(logger, candidate.Problems.Count, null);
                return;
            }

            // The runtime is the actor: nobody asked for this load, the process starting is what caused it. The
            // record is written before the swap, like a reload's: a snapshot nobody could write down is not active.
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            PluginService.JournalActivation(
                scope.ServiceProvider.GetRequiredService<JournalWriter>(),
                db,
                Actors.Runtime,
                load.Snapshot,
                candidate.Snapshot,
                reason: null);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            registry.Replace(load.Snapshot, load.Report);
            activator.Activate(candidate.Snapshot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StartupFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
