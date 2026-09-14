using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The runtime talking about itself: what it is, and how to stop it. No business state.</summary>
public static class SystemModule
{
    /// <summary>The instance facts this module answers with are composed during startup and registered by the host.</summary>
    public static IServiceCollection AddSystemModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ShutdownCoordinator>();
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
             IOptionsMonitor<DispatcherOptions> dispatcherOptions,
             RunningAttemptRegistry running,
             PluginRegistry plugins) =>
                TypedResults.Ok(new SystemInfoResponse(
                    runtimeInfo.RuntimeVersion,
                    ApiVersion.Current,
                    runtimeInfo.InstanceId,
                    runtimeInfo.Pid,
                    runtimeInfo.StartedAt,
                    dataPaths.Root,
                    new DatabaseInfo(report.AppliedMigrations),
                    dispatcher.Snapshot(dispatcherOptions.CurrentValue, running.Count),
                    new PluginsInfo(
                        plugins.Snapshot.Plugins.Count,
                        plugins.Snapshot.Id,
                        PluginMapper.Utc(plugins.Snapshot.LoadedAt),
                        plugins.LastReload?.Activated))));

        app.MapOperation<ShutdownCoordinator, ShutdownRequest, ShutdownResponse>(
            Operations.SystemShutdown,
            (coordinator, _, _) => Task.FromResult(coordinator.RequestShutdown()));
    }
}
