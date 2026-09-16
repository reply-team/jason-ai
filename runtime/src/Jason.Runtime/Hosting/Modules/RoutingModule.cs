using Jason.Runtime.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// Where a campaign's provider work goes: the active route snapshot, and the pure function that reads it.
/// Nothing here runs anything — routing decides which plugin performs an operation, and the decision is frozen
/// by a reload exactly as a plugin's grants are.
/// </summary>
public static class RoutingModule
{
    public static IServiceCollection AddRoutingModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The snapshot is the registry itself: one per process, outliving every request.
        return services.AddSingleton<RouteRegistry>();
    }
}
