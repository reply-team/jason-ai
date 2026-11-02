using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Execution;
using Jason.Runtime.WorkItems;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Notifications;
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
            .AddScoped<EventScan>()
            .AddScoped<EventStore>()
            .AddScoped<InboundSync>()
            .AddScoped<Summoner>()
            .AddScoped<Resumer>()
            .AddScoped<Claimer>()
            .AddScoped<Notifier>()
            .AddScoped<StartupRecovery>();

        // One implementation per host and no fallback, so a profile naming a host this build cannot start is a
        // refusal with a reason rather than a guess. The resolver holds no state of its own — it is a singleton
        // because it has nothing to keep, not because the answer is cached: whether a program is on this machine
        // is asked again for every agent item claimed, which is what makes installing one take effect without a
        // restart.
        // The summon creates work through the routine every caller creates work through, so the dispatcher
        // depends on the work-item module's service. Stated here as well as there: a registration a module
        // depends on but never names is one a smaller composition silently lacks.
        services.TryAddScoped<WorkItemService>();
        services.TryAddScoped<WorkItemCanceller>();

        services.AddSingleton<ProgramResolver>();
        services.AddSingleton<IAgentHost, ClaudeCodeHost>();

        // The plugin module registers the same seam for its own executable checks, and whichever module is added
        // first is the one that answers. Named here too because this module now needs it: a registration a
        // module depends on but never states is one a smaller composition silently lacks.
        services.TryAddSingleton<ISearchPath, EnvironmentSearchPath>();

        // The rule is a pure function over the published catalog and the frozen plugin snapshot: one per process, like both.
        services.AddSingleton(provider => new UnansweredEnd(OperationCatalog.Find, UnansweredEnd.RanUnder(provider.GetRequiredService<PluginRegistry>())));

        // What the loop and system.info both read the settings through, so the last value that validated is one
        // value and the complaint about an edit that did not is said once. The roles section is guarded beside
        // it: a mistyped entry command is an edit like any other, and a claim that threw over one would cost the
        // scan rather than the setting.
        services.AddSingleton(Seam<DispatcherOptions>(DispatcherOptions.Section));
        services.AddSingleton(Seam<RolesOptions>(RolesOptions.Section));
        services.AddSingleton(Seam<ManagerOptions>(ManagerOptions.Section));
        services.AddSingleton(Seam<InboundOptions>(InboundOptions.Section));
        services.AddSingleton(Seam<ResponderOptions>(ResponderOptions.Section));

        // Named here because the summon creates work through the work-item service, and that service reads the
        // plugin section for a provider item's timeout floor. The plugin module registers the same seam; either
        // may be first.
        services.TryAddSingleton(Seam<PluginsOptions>(PluginsOptions.Section));

        // The pool is sized once and the counters are one per process, so both outlive any request scope.
        services.AddSingleton<HandlerPool>();

        // The accounts' events: the store writes through the journal, finds campaigns by the rules of this version, and the
        // reader invokes plugins through the invoker the plugin module registers — a test registers its own first. One
        // reader, which is both what the scan hands a read to and the service that reads off the scan.
        services.TryAddScoped<JournalWriter>();
        services.TryAddSingleton<IEventAttribution>(_ => EventAttributionSequence.OfThisVersion());
        services.TryAddScoped<IEventsInvoker, PluginEventsInvoker>();
        services.AddSingleton(provider => new EventReader(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<EventReader>>()));
        services.AddHostedService(provider => provider.GetRequiredService<EventReader>());

        // Which skipped campaigns have been said, once per campaign and reason in the life of the process.
        services.AddSingleton<InboundSkips>();
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
