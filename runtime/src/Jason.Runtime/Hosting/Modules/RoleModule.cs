using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Roles;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The roles registry: which roles exist and how each one is launched.</summary>
public static class RoleModule
{
    public static IServiceCollection AddRoleModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<RoleService>();
    }

    public static void MapRoleOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<RoleService, RoleListRequest, Page<RoleDto>>(
            Operations.RoleList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<RoleService, RoleAddRequest, RoleDto>(
            Operations.RoleAdd, (service, request, cancellationToken) => service.AddAsync(request, cancellationToken));
    }
}
