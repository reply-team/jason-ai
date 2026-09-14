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
        services
            .AddScoped<Expirer>()
            .AddScoped<LeaseEnforcer>()
            .AddScoped<EntryCommandResolver>()
            .AddScoped<Claimer>()
            .AddScoped<StartupRecovery>();

        // The pool is sized once and the counters are one per process, so both outlive any request scope.
        services.AddSingleton<HandlerPool>();
        services.AddSingleton<ScanRunner>();
        return services.AddHostedService<DispatcherService>();
    }
}
