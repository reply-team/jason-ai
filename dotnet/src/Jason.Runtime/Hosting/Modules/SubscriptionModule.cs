using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Subscriptions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// Where each subscribed journal kind stands, the records of its chains, and a person's rewind of its cursor. Running the
/// chains is the runner's, offered by the dispatcher's scan, and nothing here invokes a plugin.
/// </summary>
public static class SubscriptionModule
{
    public static IServiceCollection AddSubscriptionModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The service reads the plugin registry the plugin module registers; the journal writer is named here too,
        // because a registration a module depends on but never states is one a smaller composition silently lacks.
        services.TryAddScoped<JournalWriter>();
        return services.AddScoped<SubscriptionService>();
    }

    public static void MapSubscriptionOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<SubscriptionService, SubscriptionListRequest, Page<SubscriptionKindDto>>(
            Operations.SubscriptionList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<SubscriptionService, SubscriptionRunsRequest, Page<SubscriptionRunDto>>(
            Operations.SubscriptionRuns, (service, request, cancellationToken) => service.RunsAsync(request, cancellationToken));
        app.MapOperation<SubscriptionService, SubscriptionRewindRequest, SubscriptionKindDto>(
            Operations.SubscriptionRewind, (service, request, cancellationToken) => service.RewindAsync(request, cancellationToken));
    }
}
