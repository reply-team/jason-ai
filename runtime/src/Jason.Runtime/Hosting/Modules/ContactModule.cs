using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Contacts, their channels, campaign membership and the global suppression list.</summary>
public static class ContactModule
{
    public static IServiceCollection AddContactModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }

    public static void MapContactOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
    }
}
