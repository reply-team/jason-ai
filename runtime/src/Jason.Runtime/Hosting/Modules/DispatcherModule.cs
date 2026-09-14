using Jason.Runtime.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The scan loop that expires, enforces leases and claims work. It has no operations of its own — it is
/// observed through <c>system.info</c>.
/// </summary>
public static class DispatcherModule
{
    public static IServiceCollection AddDispatcherModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddScoped<Expirer>()
            .AddScoped<LeaseEnforcer>()
            .AddScoped<EntryCommandResolver>()
            .AddScoped<Claimer>();
    }
}
