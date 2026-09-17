using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins;
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

        // The section every reader of a plugin's budget goes through, so an edit the validator refuses costs
        // the edit and not the request that met it.
        services.AddSingleton(DispatcherModule.Seam<PluginsOptions>(PluginsOptions.Section));

        // The snapshot and the gate are the registry itself: one per process, outliving every request.
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<ReloadGate>();

        // Registered here so a test can replace them afterwards: the host composes modules before it lets a
        // test configure services, and the last registration wins.
        services.AddSingleton<ISearchPath, EnvironmentSearchPath>();
        // Constructed rather than activated: the locator's other constructor takes the two process facts it
        // decides from, which is a seam for tests and never something the container should try to satisfy.
        services.AddSingleton<IPluginHostLocator>(_ => new ProcessPathLocator());

        // Stateless and reentrant, and everything it reads is itself one per process, so one invoker serves the
        // whole runtime. It has to outlive a scope in any case: the command that invokes a plugin is held by the
        // dispatcher for as long as an attempt runs, which is longer than the scope that resolved it.
        services.AddSingleton<PluginInvoker>();

        services.AddScoped<ExecutableResolver>();
        services.AddScoped<ExternalIdStore>();
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
