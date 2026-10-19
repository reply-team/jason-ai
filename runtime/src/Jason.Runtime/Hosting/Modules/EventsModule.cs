using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The record of the accounts whose events are read, and a person's request to read a span of one again. Reading itself
/// is the dispatcher's — its scan step and its reader — and nothing here reads an account or invokes a plugin.
/// </summary>
public static class EventsModule
{
    public static IServiceCollection AddEventsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The service reads the reader the dispatcher module registers, and the two settings seams it registers; the
        // journal writer is named here too, because a registration a module depends on but never states is one a
        // smaller composition silently lacks.
        services.TryAddScoped<JournalWriter>();
        return services.AddScoped<EventsService>();
    }

    public static void MapEventsOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<EventsService, EventsSourcesRequest, Page<EventSourceDto>>(
            Operations.EventsSources, (service, request, cancellationToken) => service.SourcesAsync(request, cancellationToken));
        app.MapOperation<EventsService, EventsBackfillRequest, EventBackfillDto>(
            Operations.EventsBackfill, (service, request, cancellationToken) => service.BackfillAsync(request, cancellationToken));
        app.MapOperation<EventsService, EventsSignalRequest, EventsSignalResponse>(
            Operations.EventsSignal, (service, request, cancellationToken) => service.SignalAsync(request, cancellationToken));
    }
}
