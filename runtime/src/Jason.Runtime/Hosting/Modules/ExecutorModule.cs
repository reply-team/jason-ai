using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The three operations a running executor calls, fenced by its attempt id. Filled in with the executor service.</summary>
public static class ExecutorModule
{
    public static IServiceCollection AddExecutorModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }

    public static void MapExecutorOperations(this IEndpointRouteBuilder app) => ArgumentNullException.ThrowIfNull(app);
}
