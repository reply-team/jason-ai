using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Api;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The plugin registry: what is installed, what the user granted it, and the explicit reload that makes a new
/// set of packages the active one. No plugin JavaScript runs anywhere near this process.
/// </summary>
public static class PluginModule
{
    public static IServiceCollection AddPluginModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The snapshot and the gate are the registry itself: one per process, outliving every request.
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<ReloadGate>();

        // Registered here so a test can replace them afterwards: the host composes modules before it lets a
        // test configure services, and the last registration wins.
        services.AddSingleton<ISearchPath, EnvironmentSearchPath>();
        services.AddSingleton<IPluginHostLocator, ProcessPathLocator>();

        services.AddScoped<ExecutableResolver>();
        services.AddScoped<PluginLoader>();
        services.AddScoped<PluginService>();
        return services.AddHostedService<PluginStartupLoader>();
    }

    public static void MapPluginOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<PluginService, PluginListRequest, PluginRegistryDto>(
            Operations.PluginList, (service, _, _) => Task.FromResult(service.List()));
        app.MapOperation<PluginService, PluginReloadRequest, PluginRegistryDto>(
            Operations.PluginReload, (service, request, cancellationToken) => service.ReloadAsync(request, cancellationToken));
    }
}
