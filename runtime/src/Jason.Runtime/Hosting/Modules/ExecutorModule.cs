using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Execution;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The three operations a running executor calls, fenced by its attempt id.</summary>
public static class ExecutorModule
{
    public static IServiceCollection AddExecutorModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ExecutorService>();
    }

    public static void MapExecutorOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<ExecutorService, WorkItemHeartbeatRequest, HeartbeatResponse>(
            Operations.WorkItemHeartbeat, (service, request, cancellationToken) => service.HeartbeatAsync(request, cancellationToken));
        app.MapOperation<ExecutorService, WorkItemSetResultRequest, WorkItemDto>(
            Operations.WorkItemSetResult, (service, request, cancellationToken) => service.SetResultAsync(request, cancellationToken));
        app.MapOperation<ExecutorService, WorkItemCompleteRequest, WorkItemDto>(
            Operations.WorkItemComplete, (service, request, cancellationToken) => service.CompleteAsync(request, cancellationToken));
    }
}
