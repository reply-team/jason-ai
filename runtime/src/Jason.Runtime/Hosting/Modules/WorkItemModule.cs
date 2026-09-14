using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Work items as callers create, read and change them. Filled in with the work-item service and its verbs.</summary>
public static class WorkItemModule
{
    public static IServiceCollection AddWorkItemModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }

    public static void MapWorkItemOperations(this IEndpointRouteBuilder app) => ArgumentNullException.ThrowIfNull(app);
}
