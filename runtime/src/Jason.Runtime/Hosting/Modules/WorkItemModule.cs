using Jason.Contracts.Api;
using Jason.Runtime.Api;
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
        return services.AddScoped<WorkItemService>().AddScoped<WorkItemCanceller>();
    }

    public static void MapWorkItemOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<WorkItemService, WorkItemCreateRequest, WorkItemDto>(
            Operations.WorkItemCreate, (service, request, cancellationToken) => service.CreateAsync(request, cancellationToken));
        app.MapOperation<WorkItemService, WorkItemGetRequest, WorkItemDto>(
            Operations.WorkItemGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<WorkItemService, WorkItemListRequest, Page<WorkItemSummaryDto>>(
            Operations.WorkItemList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<WorkItemService, WorkItemUpdateRequest, WorkItemDto>(
            Operations.WorkItemUpdate, (service, request, cancellationToken) => service.UpdateAsync(request, cancellationToken));
        app.MapOperation<WorkItemService, WorkItemCancelRequest, WorkItemDto>(
            Operations.WorkItemCancel, (service, request, cancellationToken) => service.CancelAsync(request, cancellationToken));
    }
}
