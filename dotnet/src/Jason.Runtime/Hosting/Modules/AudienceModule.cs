using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Audiences;
using Jason.Runtime.Contacts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>A campaign's audience reads: answered by the plugin the campaign's route names, when somebody asks.</summary>
public static class AudienceModule
{
    public static IServiceCollection AddAudienceModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AudienceReadService>();
        services.TryAddScoped<IProviderReadInvoker, PluginProviderReadInvoker>();
        return services;
    }

    public static void MapAudienceOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<AudienceReadService, AudiencePreviewRequest, AudiencePreviewResponse>(Operations.AudiencePreview, (service, request, ct) => service.PreviewAsync(request, ct));
        app.MapOperation<AudienceReadService, AudienceFilterValuesRequest, AudienceFilterValuesResponse>(Operations.AudienceFilterValues, (service, request, ct) => service.FilterValuesAsync(request, ct));
        app.MapOperation<AudienceReadService, AudienceAvailabilityRequest, AudienceAvailabilityResponse>(Operations.AudienceAvailability, (service, request, ct) => service.AvailabilityAsync(request, ct));
    }
}
