using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Update;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The runtime talking about itself: what it is, and how to stop it. No business state.</summary>
public static class SystemModule
{
    /// <summary>The instance facts this module answers with are composed during startup and registered by the host.</summary>
    public static IServiceCollection AddSystemModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ShutdownCoordinator>();
        services.AddSingleton<DrainCoordinator>();
        return services;
    }

    public static void MapSystemOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            Operations.Route(Operations.SystemInfo),
            (RuntimeInfo runtimeInfo,
             MigrationReport report,
             JasonPaths dataPaths,
             DispatcherStatus dispatcher,
             LiveSettings<DispatcherOptions> dispatcherSettings,
             RunningAttemptRegistry running,
             PluginRegistry plugins,
             RouteRegistry routes,
             UpdateAdvertisement update) =>
                TypedResults.Ok(new SystemInfoResponse(
                    runtimeInfo.RuntimeVersion,
                    ApiVersion.Current,
                    runtimeInfo.InstanceId,
                    runtimeInfo.Pid,
                    runtimeInfo.StartedAt,
                    dataPaths.Root,
                    new DatabaseInfo(report.AppliedMigrations),
                    // Through the settings, not the file: an operator whose edit was refused asks this operation
                    // what the runtime is working from, and is answered with what it is actually working from.
                    dispatcher.Snapshot(dispatcherSettings.Current, running.Count),
                    new PluginsInfo(
                        plugins.Snapshot.Plugins.Count,
                        plugins.Snapshot.Id,
                        PluginMapper.Utc(plugins.Snapshot.LoadedAt),
                        plugins.LastReload?.Activated),
                    new RoutesInfo(
                        routes.Snapshot.Id,
                        routes.Snapshot.ActivatedAt,
                        routes.Snapshot.Global.Default?.PluginId,
                        routes.Snapshot.Global.Operations.Count,
                        routes.Snapshot.Campaigns.Values.Sum(set => set.Operations.Count + (set.Default is null ? 0 : 1))),
                    // Null until a check has succeeded: "has not looked" and "looked and found nothing newer"
                    // are different answers, and a status line has to be able to give either.
                    update.Current)));

        app.MapOperation<ShutdownCoordinator, ShutdownRequest, ShutdownResponse>(
            Operations.SystemShutdown,
            (coordinator, _, _) => Task.FromResult(coordinator.RequestShutdown()));

        // Stop claiming, and claim again. Both are answers rather than errors when there is nothing to do,
        // because the caller that repeats one is an applier resuming an update it was interrupted in.
        app.MapOperation<DrainCoordinator, DrainRequest, DrainResponse>(
            Operations.SystemDrain,
            (coordinator, _, _) => Task.FromResult(coordinator.Drain()));

        app.MapOperation<DrainCoordinator, DrainRequest, DrainResponse>(
            Operations.SystemResume,
            (coordinator, _, _) => Task.FromResult(coordinator.Resume()));
    }
}
