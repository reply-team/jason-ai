using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Preferences;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The client's named preferences. Three verbs: a person sets a key's value whole or takes it back, and anyone lists every
/// key the catalog knows, set or not.
/// </summary>
public static class PreferenceModule
{
    public static IServiceCollection AddPreferenceModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<PreferenceService>();
    }

    public static void MapPreferenceOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<PreferenceService, PreferenceSetRequest, PreferenceDto>(
            Operations.PreferenceSet, (service, request, cancellationToken) => service.SetAsync(request, cancellationToken));
        app.MapOperation<PreferenceService, PreferenceUnsetRequest, PreferenceDto>(
            Operations.PreferenceUnset, (service, request, cancellationToken) => service.UnsetAsync(request, cancellationToken));
        app.MapOperation<PreferenceService, PreferenceListRequest, Page<PreferenceDto>>(
            Operations.PreferenceList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
    }
}
