using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Api;
using Jason.Runtime.Discovery;
using Jason.Runtime.Persistence;
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
        return services;
    }

    public static void MapSystemOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(Operations.Route(Operations.SystemInfo), (RuntimeInfo runtimeInfo, MigrationReport report, JasonPaths dataPaths) =>
            TypedResults.Ok(new SystemInfoResponse(
                runtimeInfo.RuntimeVersion,
                ApiVersion.Current,
                runtimeInfo.InstanceId,
                runtimeInfo.Pid,
                runtimeInfo.StartedAt,
                dataPaths.Root,
                new DatabaseInfo(report.AppliedMigrations),
                new DispatcherInfo(DispatcherState.Stopped, 0, 0, 0, null, 0))));

        app.MapOperation<ShutdownCoordinator, ShutdownRequest, ShutdownResponse>(
            Operations.SystemShutdown,
            (coordinator, _, _) => Task.FromResult(coordinator.RequestShutdown()));
    }
}
