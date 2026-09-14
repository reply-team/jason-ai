using Jason.Runtime.WorkItems;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Work items as callers create, read and change them, and the cancellation the campaign and contact slices share.</summary>
public static class WorkItemModule
{
    public static IServiceCollection AddWorkItemModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<WorkItemCanceller>();
    }

    public static void MapWorkItemOperations(this IEndpointRouteBuilder app) => ArgumentNullException.ThrowIfNull(app);
}
