using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The commands that actually perform work of each kind, registered as <c>ICommand</c>. Registered last, so a
/// test can add its own after the real one. Filled in with the launcher that starts a role's entry command.
/// </summary>
public static class CommandModule
{
    public static IServiceCollection AddCommandModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
