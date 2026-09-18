using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            .AddScoped<AgentPreflight>()
            .AddScoped<AgentLaunchPlanner>()
            .AddScoped<Claimer>()
            .AddScoped<StartupRecovery>();

        // One implementation per host and no fallback, so a profile naming a host this build cannot start is a
        // refusal with a reason rather than a guess. Resolving a program is a question about the machine and is
        // asked once per process.
        services.AddSingleton<ProgramResolver>();
        services.AddSingleton<IAgentHost, ClaudeCodeHost>();

        // The plugin module registers the same seam for its own executable checks, and whichever module is added
        // first is the one that answers. Named here too because this module now needs it: a registration a
        // module depends on but never states is one a smaller composition silently lacks.
        services.TryAddSingleton<ISearchPath, EnvironmentSearchPath>();

        // The rule is a pure function over the published catalog: one per process, like the catalog itself.
        services.AddSingleton(new UnansweredEnd(OperationCatalog.Find));

        // What the loop and system.info both read the settings through, so the last value that validated is one
        // value and the complaint about an edit that did not is said once. The roles section is guarded beside
        // it: a mistyped entry command is an edit like any other, and a claim that threw over one would cost the
        // scan rather than the setting.
        services.AddSingleton(Seam<DispatcherOptions>(DispatcherOptions.Section));
        services.AddSingleton(Seam<RolesOptions>(RolesOptions.Section));

        // The pool is sized once and the counters are one per process, so both outlive any request scope.
        services.AddSingleton<HandlerPool>();
        services.AddSingleton<ScanRunner>();
        return services.AddHostedService<DispatcherService>();
    }

    /// <summary>
    /// One section behind one seam. The section name is passed rather than derived from the type, because what
    /// an operator is told to open is the name they wrote in the file.
    /// </summary>
    internal static Func<IServiceProvider, LiveSettings<TOptions>> Seam<TOptions>(string section)
        where TOptions : class =>
        services => new LiveSettings<TOptions>(
            services.GetRequiredService<IOptionsMonitor<TOptions>>(),
            services.GetRequiredService<ILogger<LiveSettings<TOptions>>>(),
            section);
}
