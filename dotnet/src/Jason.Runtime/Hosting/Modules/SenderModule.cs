using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Senders;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The provider's accounts a send can go from: answered by the plugin the campaign's route for the read names, when somebody asks.</summary>
public static class SenderModule
{
    public static IServiceCollection AddSenderModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<SenderReadService>();
        services.TryAddScoped<IProviderReadInvoker, PluginProviderReadInvoker>();
        return services;
    }

    public static void MapSenderOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<SenderReadService, SenderListRequest, SenderListResponse>(Operations.SenderList, (service, request, ct) => service.ListAsync(request, ct));
    }
}
