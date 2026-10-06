using Jason.Contracts.Discovery;
using Jason.Runtime.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>What the runtime needs before its host exists: the port to listen on, how to log, and how long to drain.</summary>
public sealed record JasonSettings(RuntimeOptions Runtime, LoggingOptions Logging, DispatcherOptions Dispatcher);

/// <summary>
/// What is known of a settings file that could not be read: what kind of failure it was and, for a file that is
/// not valid JSON, where the parser stopped. Nothing of the file's content, and never the parser's message — it
/// quotes what was typed, and this is said into a log and onto a person's screen.
/// </summary>
/// <param name="ExceptionType">
/// <c>JsonException</c> for a file that is not valid JSON, otherwise the type of the failure underneath
/// whatever wrapped it. The parser's own exception type is internal to the framework, so its base is named.
/// </param>
/// <param name="LineNumber">The line the parser stopped at, counting from 1 as an editor does; null where there is no position.</param>
/// <param name="BytePositionInLine">The byte within that line, counting from 1; null where there is no position.</param>
public sealed record SettingsFileFailure(string ExceptionType, long? LineNumber, long? BytePositionInLine)
{
    /// <summary>The failure behind a load exception, however many layers the framework put around it.</summary>
    public static SettingsFileFailure Of(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            // The parser counts from zero; a person reading "line 0" in a file they have open would look in the
            // wrong place by one.
            if (inner is System.Text.Json.JsonException { LineNumber: { } line, BytePositionInLine: { } column })
            {
                return new SettingsFileFailure(nameof(System.Text.Json.JsonException), line + 1, column + 1);
            }
        }

        var root = exception;
        while (root.InnerException is { } deeper)
        {
            root = deeper;
        }

        return new SettingsFileFailure(root.GetType().Name, null, null);
    }

    /// <summary>The failure as a clause of a sentence: <c>JsonException at line 2, byte 3</c>, or the type alone.</summary>
    public string Describe() => LineNumber is { } line && BytePositionInLine is { } column
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ExceptionType} at line {line}, byte {column}")
        : ExceptionType;
}

public static class JasonConfiguration
{
    public const string EnvironmentPrefix = "JASON_";

    /// <summary>
    /// The runtime's configuration: the shipped defaults, the user's <c>settings.json</c> — watched, so a hand edit
    /// reaches a running runtime — and the <c>JASON_</c> environment variables.
    /// </summary>
    /// <param name="paths">Where the user's settings file is.</param>
    /// <param name="shippedSettingsDirectory">Directory holding the shipped <c>appsettings.json</c>; null = none.</param>
    /// <param name="onReloadFailure">
    /// Told of an edit that left the settings file unreadable, once for each state the file is left in however
    /// many times the watcher fires, on the thread that watches the file. Handed in rather than written here
    /// because this runs before the runtime has a log, and an edit can only follow it: the caller supplies one
    /// that logs once there is somewhere to write.
    /// </param>
    /// <remarks>
    /// <para>
    /// A file that cannot be read is refused differently at start and after it. At start there are no settings to
    /// fall back to, so this throws an <see cref="InvalidSettingsException"/> naming the file and the position — with no
    /// inner exception, since nothing runs before this that could log it and the framework's own message quotes the
    /// input. After that, whatever has read its settings keeps them: the failure is reported to
    /// <paramref name="onReloadFailure"/> and the framework's refusal is left to stand. It is not ignored, though
    /// ignoring is what the loader offers: an ignored failure is still a reload, and the reload publishes what the
    /// provider holds, which is nothing — the file's old keys are dropped before it is read — so every setting would
    /// silently go back to its default. Those dropped keys are also why a section first read after such a failure
    /// reads none of the file; the host, which knows when its sections are read, says so in what it reports.
    /// </para>
    /// <para>
    /// The loader's exception context does not say which of the two this is, so the build says: the file's first
    /// load happens inside <c>Build</c>, and every later one happens after it.
    /// </para>
    /// </remarks>
    public static IConfigurationRoot Build(JasonPaths paths, string? shippedSettingsDirectory, Action<SettingsFileFailure>? onReloadFailure = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var builder = new ConfigurationBuilder();
        if (shippedSettingsDirectory is not null)
        {
            builder.AddJsonFile(Path.Combine(shippedSettingsDirectory, "appsettings.json"), optional: true, reloadOnChange: false);
        }

        var started = false;
        var gate = new Lock();
        ReportedFailure? reported = null;

        // The file watcher raises more than one notification for a single write, and the loader registers its next
        // change token before it waits out its reload delay, so a second notification that lands in the wait makes
        // it read the same file again. The person who saved it is told once: a failure that is the same as the last
        // one reported, for a file in the same state, is not said again. A file whose state cannot be read has no
        // state to compare, and is always said.
        bool FirstReport(SettingsFileFailure failure)
        {
            ReportedFailure? now = null;
            try
            {
                var file = new FileInfo(paths.UserSettingsFile);
                if (file.Exists)
                {
                    now = new ReportedFailure(failure, file.LastWriteTimeUtc, file.Length);
                }
            }
            catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
            {
            }

            lock (gate)
            {
                if (now is not null && now == reported)
                {
                    return false;
                }

                reported = now;
                return true;
            }
        }

        builder.AddJsonFile(source =>
        {
            source.Path = paths.UserSettingsFile;
            source.Optional = true;
            source.ReloadOnChange = true;
            source.ResolveFileProvider();
            source.OnLoadException = context =>
            {
                var failure = SettingsFileFailure.Of(context.Exception);
                if (!started)
                {
                    throw new InvalidSettingsException($"the settings file at '{paths.UserSettingsFile}' could not be read ({failure.Describe()}).");
                }

                if (onReloadFailure is not null && FirstReport(failure))
                {
                    onReloadFailure(failure);
                }
            };
        });
        builder.AddEnvironmentVariables(EnvironmentPrefix);
        var configuration = builder.Build();
        started = true;
        return configuration;
    }

    /// <summary>A failure that has been reported, and the state of the file it was reported for.</summary>
    private sealed record ReportedFailure(SettingsFileFailure Failure, DateTime WrittenAt, long Length);

    /// <summary>
    /// The pre-host load of what the host needs before it exists. Only the port and the logging settings are
    /// validated here — everything else is validated on start by the same validators, so no rule is spelled
    /// twice and a dispatcher setting is reported by the options system rather than by two different errors.
    /// A value of the wrong type in any of the three sections is refused here, though: the host is built from
    /// them, and a section the binder stopped reading halfway is not one to build it from.
    /// </summary>
    /// <remarks>
    /// The refusal is an <see cref="InvalidSettingsException"/>, a sentence with nothing underneath it. This runs before
    /// there is a log, so what it throws is what the person starting the runtime reads, and the binder's own exception
    /// repeats what was typed.
    /// </remarks>
    public static JasonSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var runtime = Bind<RuntimeOptions>(configuration, RuntimeOptions.Section);
        var logging = Bind<LoggingOptions>(configuration, LoggingOptions.Section);
        var dispatcher = Bind<DispatcherOptions>(configuration, DispatcherOptions.Section);

        var failures = new List<string>();
        Collect(failures, new BindingFailureValidator<RuntimeOptions>().Validate(null, runtime));
        Collect(failures, new BindingFailureValidator<LoggingOptions>().Validate(null, logging));
        Collect(failures, new BindingFailureValidator<DispatcherOptions>().Validate(null, dispatcher));
        Collect(failures, new RuntimeOptionsValidator().Validate(null, runtime));
        Collect(failures, new LoggingOptionsValidator().Validate(null, logging));
        if (failures.Count > 0)
        {
            throw new InvalidSettingsException(string.Join(" ", failures));
        }

        return new JasonSettings(runtime, logging, dispatcher);
    }

    /// <summary>
    /// Binds every option class to its section with validation on start. A value of the wrong type is a
    /// validation failure like any other (<see cref="SafeBinding"/>), so it is refused wherever a value out of
    /// range would be, and never escapes as the binder's own exception. Each section's change-token source is
    /// registered here, the one <see cref="OptionsBuilder{TOptions}.Bind(IConfiguration)"/> would have registered,
    /// so the user's settings file — added with <c>reloadOnChange</c> — feeds <c>IOptionsMonitor</c> live.
    /// </summary>
    /// <param name="paths">
    /// Where the installed plugins are listed from, for a refusal whose path names one — by the binder or by a
    /// section's validator.
    /// </param>
    public static IServiceCollection AddJasonOptions(this IServiceCollection services, IConfiguration configuration, JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paths);

        IReadOnlySet<string> Installed() => SafeBinding.InstalledPluginIds(paths);

        // Ahead of each section's own validators, so that a section that could not be read is said to be that
        // before anything is said about the values in it.
        services.AddSingleton<IValidateOptions<RuntimeOptions>, BindingFailureValidator<RuntimeOptions>>();
        services.AddSingleton<IValidateOptions<LoggingOptions>, BindingFailureValidator<LoggingOptions>>();
        services.AddSingleton<IValidateOptions<DispatcherOptions>, BindingFailureValidator<DispatcherOptions>>();
        services.AddSingleton<IValidateOptions<RolesOptions>, BindingFailureValidator<RolesOptions>>();
        services.AddSingleton<IValidateOptions<ManagerOptions>, BindingFailureValidator<ManagerOptions>>();
        services.AddSingleton<IValidateOptions<PluginsOptions>, BindingFailureValidator<PluginsOptions>>();
        services.AddSingleton<IValidateOptions<UpdateOptions>, BindingFailureValidator<UpdateOptions>>();
        services.AddSingleton<IValidateOptions<NotificationsOptions>, BindingFailureValidator<NotificationsOptions>>();
        services.AddSingleton<IValidateOptions<RoutesOptions>, BindingFailureValidator<RoutesOptions>>();
        services.AddSingleton<IValidateOptions<InboundOptions>, BindingFailureValidator<InboundOptions>>();

        services.AddSingleton<IValidateOptions<RuntimeOptions>, RuntimeOptionsValidator>();
        services.AddSingleton<IValidateOptions<LoggingOptions>, LoggingOptionsValidator>();
        services.AddSingleton<IValidateOptions<DispatcherOptions>, DispatcherOptionsValidator>();

        // A second validator of the same section, not a rule folded into the first: the lease a provider attempt
        // is claimed under has to outlast the slowest operation this build publishes, and that is a relationship
        // between two sections rather than a range one of them owns.
        services.AddSingleton<IValidateOptions<DispatcherOptions>, ProviderOpBudgetValidator>();
        services.AddSingleton<IValidateOptions<RolesOptions>, RolesOptionsValidator>();
        services.AddSingleton<IValidateOptions<ManagerOptions>, ManagerOptionsValidator>();

        // The three sections whose paths carry a key a person chose are told what is installed: a key that names an
        // installed plugin is then said as it was typed, as a settings name, a published operation or a public id
        // already is, and any other key is written as a placeholder (SettingKey holds the whole rule).
        services.AddSingleton<IValidateOptions<PluginsOptions>>(new PluginsOptionsValidator(Installed));
        services.AddSingleton<IValidateOptions<RoutesOptions>>(new RoutesOptionsValidator(Installed));
        services.AddSingleton<IValidateOptions<UpdateOptions>, UpdateOptionsValidator>();
        services.AddSingleton<IValidateOptions<InboundOptions>, InboundOptionsValidator>();
        services.AddSingleton<IValidateOptions<NotificationsOptions>>(new NotificationsOptionsValidator(Installed));

        Bound<RuntimeOptions>(services, configuration.GetSection(RuntimeOptions.Section), Installed).ValidateOnStart();
        Bound<LoggingOptions>(services, configuration.GetSection(LoggingOptions.Section), Installed).ValidateOnStart();
        Bound<DispatcherOptions>(services, configuration.GetSection(DispatcherOptions.Section), Installed).ValidateOnStart();
        Bound<RolesOptions>(services, configuration.GetSection(RolesOptions.Section), Installed).ValidateOnStart();
        Bound<UpdateOptions>(services, configuration.GetSection(UpdateOptions.Section), Installed).ValidateOnStart();
        Bound<InboundOptions>(services, configuration.GetSection(InboundOptions.Section), Installed).ValidateOnStart();

        // The scalars and each named plugin's Enabled are bound; each binding is JSON, which the binder cannot make,
        // so it is read afterwards from the same section, the way a route's binding is.
        var notifications = configuration.GetSection(NotificationsOptions.Section);
        Bound<NotificationsOptions>(services, notifications, Installed)
            .PostConfigure(options => NotificationsOptions.FillBindings(notifications, options))
            .ValidateOnStart();

        // Bound by hand for the reason ManagerOptions.Fill gives: a list the binder appends to would turn a
        // narrowed set of triggers into a widened one.
        var manager = configuration.GetSection(ManagerOptions.Section);
        services.AddSingleton<IOptionsChangeTokenSource<ManagerOptions>>(new ConfigurationChangeTokenSource<ManagerOptions>(manager));
        services.AddSingleton<IConfigureOptions<ManagerOptions>>(new ConfigureOptions<ManagerOptions>(options => ManagerOptions.Fill(manager, options)));
        services.AddOptions<ManagerOptions>().ValidateOnStart();
        Bound<PluginsOptions>(services, configuration.GetSection(PluginsOptions.Section), Installed).ValidateOnStart();

        // Routes are read rather than bound: a route carries a JSON binding, and the standard binder cannot make
        // a JsonObject at all. The change-token source is the same one Bind would have registered, so a hand
        // edit still reaches a running runtime, and the validator is still what decides whether it may.
        var routes = configuration.GetSection(RoutesOptions.Section);
        services.AddSingleton<IOptionsChangeTokenSource<RoutesOptions>>(new ConfigurationChangeTokenSource<RoutesOptions>(routes));
        services.AddSingleton<IConfigureOptions<RoutesOptions>>(new ConfigureOptions<RoutesOptions>(options => RoutesOptions.Fill(routes, options)));
        services.AddOptions<RoutesOptions>().ValidateOnStart();

        return services;
    }

    /// <summary>
    /// One section bound the way <see cref="OptionsBuilder{TOptions}.Bind(IConfiguration)"/> binds it, except that a
    /// value of the wrong type is left for the section's <see cref="BindingFailureValidator{TOptions}"/> to refuse
    /// rather than thrown out of whatever happened to read the section first.
    /// </summary>
    private static OptionsBuilder<TOptions> Bound<TOptions>(
        IServiceCollection services,
        IConfigurationSection section,
        Func<IReadOnlySet<string>> installedPluginIds)
        where TOptions : class
    {
        services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(new ConfigurationChangeTokenSource<TOptions>(section));
        return services.AddOptions<TOptions>().Configure(options => SafeBinding.Bind(section, options, installedPluginIds));
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : class, new()
    {
        var options = new T();

        // None of these three sections has a key a person chooses, so no plugin's id can be in a path of theirs.
        SafeBinding.Bind(configuration.GetSection(section), options, SafeBinding.NoInstalledPlugins);
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
