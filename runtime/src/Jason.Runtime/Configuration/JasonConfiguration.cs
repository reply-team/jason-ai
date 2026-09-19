using Jason.Contracts.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>What the runtime needs before its host exists: the port to listen on, how to log, and how long to drain.</summary>
public sealed record JasonSettings(RuntimeOptions Runtime, LoggingOptions Logging, DispatcherOptions Dispatcher);

public static class JasonConfiguration
{
    public const string EnvironmentPrefix = "JASON_";

    public static IConfigurationRoot Build(JasonPaths paths, string? shippedSettingsDirectory)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var builder = new ConfigurationBuilder();
        if (shippedSettingsDirectory is not null)
        {
            builder.AddJsonFile(Path.Combine(shippedSettingsDirectory, "appsettings.json"), optional: true, reloadOnChange: false);
        }

        builder.AddJsonFile(paths.UserSettingsFile, optional: true, reloadOnChange: true);
        builder.AddEnvironmentVariables(EnvironmentPrefix);
        return builder.Build();
    }

    /// <summary>
    /// The pre-host load of what the host needs before it exists. Only the port and the logging settings are
    /// validated here — everything else is validated on start by the same validators, so no rule is spelled
    /// twice and a dispatcher setting is reported by the options system rather than by two different errors.
    /// </summary>
    public static JasonSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var runtime = Bind<RuntimeOptions>(configuration, RuntimeOptions.Section);
        var logging = Bind<LoggingOptions>(configuration, LoggingOptions.Section);
        var dispatcher = Bind<DispatcherOptions>(configuration, DispatcherOptions.Section);

        var failures = new List<string>();
        Collect(failures, new RuntimeOptionsValidator().Validate(null, runtime));
        Collect(failures, new LoggingOptionsValidator().Validate(null, logging));
        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Invalid Jason configuration: " + string.Join(" ", failures));
        }

        return new JasonSettings(runtime, logging, dispatcher);
    }

    /// <summary>
    /// Binds every option class to its section with validation on start. Binding through
    /// <see cref="OptionsBuilder{TOptions}.Bind(IConfiguration)"/> also registers the change-token source, so
    /// the user's settings file — added with <c>reloadOnChange</c> — feeds <c>IOptionsMonitor</c> live.
    /// </summary>
    public static IServiceCollection AddJasonOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<RuntimeOptions>, RuntimeOptionsValidator>();
        services.AddSingleton<IValidateOptions<LoggingOptions>, LoggingOptionsValidator>();
        services.AddSingleton<IValidateOptions<DispatcherOptions>, DispatcherOptionsValidator>();

        // A second validator of the same section, not a rule folded into the first: the lease a provider attempt
        // is claimed under has to outlast the slowest operation this build publishes, and that is a relationship
        // between two sections rather than a range one of them owns.
        services.AddSingleton<IValidateOptions<DispatcherOptions>, ProviderOpBudgetValidator>();
        services.AddSingleton<IValidateOptions<RolesOptions>, RolesOptionsValidator>();
        services.AddSingleton<IValidateOptions<ManagerOptions>, ManagerOptionsValidator>();
        services.AddSingleton<IValidateOptions<PluginsOptions>, PluginsOptionsValidator>();
        services.AddSingleton<IValidateOptions<RoutesOptions>, RoutesOptionsValidator>();
        services.AddSingleton<IValidateOptions<UpdateOptions>, UpdateOptionsValidator>();

        services.AddOptions<RuntimeOptions>().Bind(configuration.GetSection(RuntimeOptions.Section)).ValidateOnStart();
        services.AddOptions<LoggingOptions>().Bind(configuration.GetSection(LoggingOptions.Section)).ValidateOnStart();
        services.AddOptions<DispatcherOptions>().Bind(configuration.GetSection(DispatcherOptions.Section)).ValidateOnStart();
        services.AddOptions<RolesOptions>().Bind(configuration.GetSection(RolesOptions.Section)).ValidateOnStart();
        services.AddOptions<UpdateOptions>().Bind(configuration.GetSection(UpdateOptions.Section)).ValidateOnStart();

        // Bound by hand for the reason ManagerOptions.Fill gives: a list the binder appends to would turn a
        // narrowed set of triggers into a widened one.
        var manager = configuration.GetSection(ManagerOptions.Section);
        services.AddSingleton<IOptionsChangeTokenSource<ManagerOptions>>(new ConfigurationChangeTokenSource<ManagerOptions>(manager));
        services.AddSingleton<IConfigureOptions<ManagerOptions>>(new ConfigureOptions<ManagerOptions>(options => ManagerOptions.Fill(manager, options)));
        services.AddOptions<ManagerOptions>().ValidateOnStart();
        services.AddOptions<PluginsOptions>().Bind(configuration.GetSection(PluginsOptions.Section)).ValidateOnStart();

        // Routes are read rather than bound: a route carries a JSON binding, and the standard binder cannot make
        // a JsonObject at all. The change-token source is the same one Bind would have registered, so a hand
        // edit still reaches a running runtime, and the validator is still what decides whether it may.
        var routes = configuration.GetSection(RoutesOptions.Section);
        services.AddSingleton<IOptionsChangeTokenSource<RoutesOptions>>(new ConfigurationChangeTokenSource<RoutesOptions>(routes));
        services.AddSingleton<IConfigureOptions<RoutesOptions>>(new ConfigureOptions<RoutesOptions>(options => RoutesOptions.Fill(routes, options)));
        services.AddOptions<RoutesOptions>().ValidateOnStart();

        return services;
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var options = new T();
        configuration.GetSection(section).Bind(options);
        return options;
    }

    private static void Collect(List<string> failures, ValidateOptionsResult result)
    {
        if (result.Failed && result.Failures is not null)
        {
            failures.AddRange(result.Failures);
        }
    }
}
