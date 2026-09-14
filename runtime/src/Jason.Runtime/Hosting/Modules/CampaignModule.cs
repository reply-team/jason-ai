using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Campaigns, their context and the chronicle that records what happened to them.</summary>
public static class CampaignModule
{
    public static IServiceCollection AddCampaignModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }

    public static void MapCampaignOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
    }
}
