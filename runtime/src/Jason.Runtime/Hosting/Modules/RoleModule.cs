using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The roles registry: which roles exist and how each one is launched. Filled in with the role service.</summary>
public static class RoleModule
{
    public static IServiceCollection AddRoleModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }

    public static void MapRoleOperations(this IEndpointRouteBuilder app) => ArgumentNullException.ThrowIfNull(app);
}
