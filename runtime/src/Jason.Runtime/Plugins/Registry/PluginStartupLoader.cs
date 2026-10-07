using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
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

    // The same event, for a load that gave out reading a stored value — a campaign's route whose binding no longer
    // parses, or holds a key twice or a value of the wrong shape: the column, what kind of failure and where, and no
    // exception, because what a reader quotes is what the binding held.
    private static readonly Action<ILogger, string, Exception?> StartupFailedToParse = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(2, nameof(StartupFailed)),
        "The plugin load at startup failed; the runtime continues with no plugins active: {Failure}");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var (load, candidate) = await BuildAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            if (Unmeasured(load, candidate))
            {
                // Nothing in the load was found wrong — only a binding pattern that could not finish its probe, or a
                // binding a pattern could not finish measuring: this machine's cost at this moment, never a verdict. A
                // reload answers those in its own terms, but at startup nobody is there to send it again, and failing
                // closed would leave every provider item to fail `no_route` until somebody did. So the whole load is
                // built once more, on an engine that is warm by now, and only a second such load fails closed.
                (load, candidate) = await BuildAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }

            if (load.Snapshot is null)
            {
                registry.Record(load.Report);
                StartupRejected(logger, load.Report.Candidates.Sum(report => report.Problems.Count), null);
                return;
            }

            // The same all-or-nothing rule a reload follows, and for the same reason: a bad route leaves both
            // registries empty rather than a plugin set active with routes nobody checked. The report names what is
            // wrong, and an unmeasured binding beside a real problem is not.
            if (candidate!.Snapshot is null)
            {
                var named = candidate.Unmeasured ? candidate.Problems : candidate.Refusals;
                registry.Record(load.Report with { Activated = false, Routes = named });
                StartupRejected(logger, named.Count, null);
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
            scope.ServiceProvider.GetRequiredService<RouteActivator>().Activate(candidate.Snapshot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (ParseFailure.Is(exception))
            {
                StartupFailedToParse(logger, ParseFailure.Describe(exception), null);
            }
            else
            {
                StartupFailed(logger, exception);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>One load and, where it produced a plugin set, the route set that load would activate with it.</summary>
    private static async Task<(LoadResult Load, RouteCandidate? Candidate)> BuildAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var load = await services.GetRequiredService<PluginLoader>()
            .LoadAsync(SnapshotSource.Startup, cancellationToken)
            .ConfigureAwait(false);

        var candidate = load.Snapshot is null
            ? null
            : await services.GetRequiredService<RouteActivator>()
                .BuildAsync(load.Snapshot, GlobalRouteSource.FromSettings, cancellationToken)
                .ConfigureAwait(false);

        return (load, candidate);
    }

    /// <summary>
    /// Whether nothing held this load back but patterns that could not finish: a package refused only for its binding
    /// probe, or a route set refused only for unmeasured bindings. Anything else — a broken package, a section the
    /// validator refuses, a route that really is wrong — is deterministic, and a second load could change only the report.
    /// </summary>
    private static bool Unmeasured(LoadResult load, RouteCandidate? candidate)
    {
        if (load.Snapshot is not null)
        {
            return candidate is { Unmeasured: true };
        }

        var problems = load.Report.Candidates
            .Where(report => report.Status == CandidateStatus.Invalid)
            .SelectMany(report => report.Problems)
            .ToList();

        return load.Report.Settings is not { Count: > 0 } && problems.Count > 0 && problems.All(problem => problem.Unmeasured);
    }
}
