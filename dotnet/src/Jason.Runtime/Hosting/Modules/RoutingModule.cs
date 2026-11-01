using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// Where a campaign's provider work goes: the active route snapshot, the pure function that reads it, and the
/// four operations that read and change it. Nothing here runs anything — routing decides which plugin performs
/// an operation, and the decision is frozen by a reload exactly as a plugin's grants are.
/// </summary>
public static class RoutingModule
{
    public static IServiceCollection AddRoutingModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The snapshot is the registry itself: one per process, outliving every request. The activator reads
        // the campaign rows, so it lives for as long as a request does.
        services.AddSingleton<RouteRegistry>();
        services.AddScoped<RouteActivator>();
        return services.AddScoped<RouteService>();
    }

    public static void MapRouteOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<RouteService, RouteResolveRequest, RouteResolutionDto>(
            Operations.RouteResolve, (service, request, cancellationToken) => service.ResolveAsync(request, cancellationToken));
        app.MapOperation<RouteService, RouteListRequest, RoutesDto>(
            Operations.RouteList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<RouteService, RouteSetRequest, RoutesDto>(
            Operations.RouteSet, (service, request, cancellationToken) => service.SetAsync(request, cancellationToken));
        app.MapOperation<RouteService, RouteUnsetRequest, RoutesDto>(
            Operations.RouteUnset, (service, request, cancellationToken) => service.UnsetAsync(request, cancellationToken));
    }
}
