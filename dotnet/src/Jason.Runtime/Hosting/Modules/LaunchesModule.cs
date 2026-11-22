using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Launches;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The daily limits of launches as a person speaks to them: a lift of today's, on their word. Nothing inside the runtime
/// calls it: the claim holds work at the limits and lets it go at the installation's midnight.
/// </summary>
public static class LaunchesModule
{
    public static IServiceCollection AddLaunchesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The day a lift covers is the one the claim counts: the same zone, read through the same seam. The dispatcher
        // module names both too; either may be composed first.
        services.TryAddSingleton(DispatcherModule.Seam<RuntimeOptions>(RuntimeOptions.Section));
        services.TryAddSingleton<InstallationDay>();
        return services.AddScoped<LaunchesService>();
    }

    public static void MapLaunchesOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<LaunchesService, LaunchesLiftRequest, LaunchesLiftDto>(
            Operations.LaunchesLift, (service, request, cancellationToken) => service.LiftAsync(request, cancellationToken));
    }
}
