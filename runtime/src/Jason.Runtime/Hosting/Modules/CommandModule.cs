using Jason.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The commands that actually perform work of each kind, registered as <c>ICommand</c>. Registered last, so a
/// test can add its own after the real one.
/// </summary>
public static class CommandModule
{
    public static IServiceCollection AddCommandModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICommand, AiRoleCommand>();
        return services;
    }
}
