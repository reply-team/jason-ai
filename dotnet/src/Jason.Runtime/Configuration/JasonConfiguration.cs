using Jason.Contracts.Discovery;
using Jason.Runtime.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
    /// input. After that, whatever has read its settings keeps them: the watch reads an edit as the loader's own reload
    /// does, which drops the file's old keys when the file does not parse, so it reports the failure to
    /// <paramref name="onReloadFailure"/> and tells no section of it. Telling them would publish what the provider
    /// holds, which is nothing, and every setting would silently go back to its default. Those dropped keys are also
    /// why a section first read after such a failure reads none of the file; the host, which knows when its sections
    /// are read, says so in what it reports.
    /// </para>
    /// <para>
    /// The loader's exception context does not say which of the two this is, so the build says: the file's first
    /// load happens inside <c>Build</c>, and every later one happens after it.
    /// </para>
    /// <para>
    /// The file's watch is the provider's own rather than the loader's, which reads the file and tells the sections in
    /// code a caller cannot wait for. It applies an edit as the loader's reload does: a file that is not there leaves no
    /// keys and every section is told, so each goes back to its default; one that does not parse is said as above; one
    /// that parses replaces the keys and every section is told. Reading the file, replacing the keys and telling the
    /// sections are one step, under a lock <see cref="ReadSettingsFile"/> takes too, so a read asked for while the
    /// watch applies an edit waits until every section has been told of it.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The configuration, owned by the caller: it watches the settings file, and on Linux that watch is an inotify
    /// instance from a per-user limit every process shares, released only when the configuration is disposed.
    /// </returns>
    public static ConfigurationRoot Build(JasonPaths paths, string? shippedSettingsDirectory, Action<SettingsFileFailure>? onReloadFailure = null)
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

        // The file watcher raises more than one notification for a single write, and the watch takes its next change
        // token before it waits out its reload delay, as the loader does, so a second notification that lands in the
        // wait makes it read the same file again. The person who saved it is told once: a failure that is the same as
        // the last one reported, for a file in the same state, is not said again. A file whose state cannot be read
        // has no state to compare, and is always said.
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

        builder.Add<SettingsFileSource>(source =>
        {
            source.FullPath = paths.UserSettingsFile;
            source.Path = paths.UserSettingsFile;
            source.Optional = true;

            // Watched by its provider, which a read can wait for, and not by the loader as well.
            source.ReloadOnChange = false;
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
        var configuration = (ConfigurationRoot)builder.Build();
        started = true;
        return configuration;
    }

    /// <summary>
    /// Reads the user's settings file now, rather than when its watch next fires, and tells every section read from it, as
    /// the watch does: once this returns, the next read of a section binds what the file says. For an act that has to
    /// work from the file as it stands — the watch says an edit only after a delay of its own, and a request sent
    /// straight after the edit would otherwise work from the file as it was. An edit the watch is still applying is
    /// finished first, every section told of it, and the file is read after it: of two edits saved close together,
    /// this holds the later one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file this read cannot use — one that is not there, one another handle holds, one that is not valid JSON — leaves
    /// the keys the configuration holds from the file as they were and tells no section, so the settings in force are
    /// kept; the failure is said through the handler <see cref="Build"/> was given, by its type and, for JSON, its
    /// position. A file that is not there is stricter here than at the watch, which reads it as every setting on its
    /// default: a save that removes the file and then renames a new one into place would otherwise be met in between.
    /// Where the configuration holds no keys from the file — a fresh installation, which has never had one — a file that
    /// is not there is nothing to keep, and nothing is said. Of the three, only a file that is there is handed back to
    /// the caller to refuse: one that is not there has nothing in it that could not be read.
    /// </para>
    /// <para>
    /// Those keys are the ones the configuration holds when this reads, which are not always the ones the file last held:
    /// once the watch has itself read a file that does not parse, it has dropped them, and this read does not bring them
    /// back. The sections bound before keep their values either way, since neither read tells them of a change.
    /// </para>
    /// <para>
    /// A section that no longer validates is that section's to say, when it is next read. None of this is thrown at the
    /// caller, which asked for the file to be read and not for its settings to be judged. A configuration with no
    /// settings file of its own is left as it is.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The failure of a file that is there and could not be used — not valid JSON, or held by another handle — for the
    /// caller to refuse what it was asked to do from the file; null when the file was read, and when it is not there,
    /// which refuses nothing.
    /// </returns>
    public static SettingsFileFailure? ReadSettingsFile(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration is not IConfigurationRoot root)
        {
            return null;
        }

        SettingsFileFailure? refused = null;
        foreach (var provider in root.Providers.OfType<SettingsFileProvider>())
        {
            refused ??= provider.Read();
        }

        return refused;
    }

    /// <summary>A failure that has been reported, and the state of the file it was reported for.</summary>
    private sealed record ReportedFailure(SettingsFileFailure Failure, DateTime WrittenAt, long Length);

    /// <summary>
    /// The settings file's source, whose provider disposes the file provider it watches through. The framework's
    /// provider leaves that to whoever made it, and nothing else holds it, so disposing the configuration left the
    /// watch — an inotify instance on Linux — to the finalizer.
    /// </summary>
    private sealed class SettingsFileSource : JsonConfigurationSource
    {
        /// <summary>
        /// The file's own path, which <see cref="SettingsFileProvider.Read"/> opens. <c>Path</c> is relative to the file
        /// provider's root once that is resolved, and the provider can say where a file is only while it is there.
        /// </summary>
        public string FullPath { get; set; } = string.Empty;

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            return new SettingsFileProvider(this);
        }
    }

    /// <summary>
    /// The settings file's provider. It watches the file itself, as the loader would, and applies an edit as the loader's
    /// reload does; <see cref="Read"/> and the watch take one lock through reading the file, replacing the keys and
    /// telling the sections. The loader's watch could not be waited for: its read and its telling are closed to a
    /// provider of its kind, so a read asked for while it told the sections of one edit replaced the keys with a later
    /// edit and returned with the sections still bound to the earlier one — the watch told them of the later edit only
    /// once it was done with the earlier.
    /// </summary>
    /// <remarks>
    /// The lock is held while every section is told, and a section is told by running what listens to it, so nothing in
    /// the runtime listens for a change to its settings: it reads them when it needs them. A section's binding and
    /// validators run under the lock as well, and none may read the settings file or wait for a reload.
    /// </remarks>
    private sealed class SettingsFileProvider : JsonConfigurationProvider
    {
        private readonly Lock _applying = new();
        private readonly IDisposable? _watch;

        public SettingsFileProvider(SettingsFileSource source)
            : base(source)
        {
            // Registered as the loader registers its own: each notification takes the next one before the delay is waited
            // out and the file read, so an edit written in the meantime makes the watch read the file once more.
            if (Source.FileProvider is not null)
            {
                _watch = ChangeToken.OnChange(() => Source.FileProvider?.Watch(Source.Path ?? string.Empty), Apply);
            }
        }

        /// <summary>
        /// The read <see cref="ReadSettingsFile"/> asks for. It opens and parses the file itself rather than through the
        /// loader, which reads a file that is not there as one with no keys and tells every section so; every failure
        /// is said where an edit's is. An edit the watch is applying is finished first.
        /// </summary>
        /// <returns>The failure of a file that is there and could not be used; null when it was read or is not there.</returns>
        public SettingsFileFailure? Read()
        {
            lock (_applying)
            {
                FileStream stream;
                try
                {
                    // Opened as the loader opens it: shared with a writer that has it open, and read synchronously.
                    stream = new FileStream(((SettingsFileSource)Source).FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
                }
                catch (Exception unopened) when (unopened is IOException or UnauthorizedAccessException)
                {
                    // A file that is not there refuses nothing. The keys held from it are kept, and that is said; with none
                    // held there is nothing to keep, and nothing to say.
                    if (unopened is FileNotFoundException or DirectoryNotFoundException)
                    {
                        if (Data.Count > 0)
                        {
                            Refuse(unopened);
                        }

                        return null;
                    }

                    return Refuse(unopened);
                }

                using (stream)
                {
                    try
                    {
                        Load(stream);
                    }
                    catch (Exception unparsed) when (unparsed is FormatException or IOException)
                    {
                        // The keys are replaced only once the whole file has parsed, so the ones held stay where they were.
                        return Refuse(unparsed);
                    }
                }

                Tell();
                return null;
            }
        }

        /// <summary>
        /// What the watch does with an edit, once the loader's reload delay has passed: the loader's own reload of the file.
        /// A file that is not there leaves no keys and every section is told; one that does not parse drops the keys, is
        /// said, and no section is told; one that parses replaces the keys and every section is told.
        /// </summary>
        private void Apply()
        {
            Thread.Sleep(Source.ReloadDelay);
            lock (_applying)
            {
                var file = Source.FileProvider?.GetFileInfo(Source.Path ?? string.Empty);
                if (file is null || !file.Exists)
                {
                    Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    Tell();
                    return;
                }

                Stream stream;
                try
                {
                    stream = file.PhysicalPath is { } path
                        ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan)
                        : file.CreateReadStream();
                }
                catch (Exception unopened) when (unopened is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be opened — another handle holds it, or it went between being found and being
                    // opened — changes nothing and says nothing, as the loader leaves it; the edit is read at the next
                    // notification, if one comes, or by the next read a reload makes.
                    return;
                }

                using (stream)
                {
                    try
                    {
                        Load(stream);
                    }
                    catch (Exception unparsed) when (unparsed is FormatException or IOException or UnauthorizedAccessException)
                    {
                        // The keys are dropped and the failure is said, as the loader does; the rest escape to the
                        // watcher with the keys kept.
                        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        Refuse(unparsed);
                        return;
                    }
                }

                Tell();
            }
        }

        /// <summary>
        /// Tells every section of the keys now held. A section that no longer validates is that section's to say when it
        /// is next read: the options system binds every section it holds as it is told of the change, and raises the
        /// refusal here once every section has been told, where it goes no further — the watch's notification would
        /// otherwise end in it.
        /// </summary>
        private void Tell()
        {
            try
            {
                OnReload();
            }
            catch (AggregateException refused) when (refused.Flatten().InnerExceptions.All(inner => inner is OptionsValidationException))
            {
            }
        }

        /// <summary>A file this provider cannot use, put through the handling the loader gives an edit it cannot read.</summary>
        private SettingsFileFailure Refuse(Exception failure)
        {
            Source.OnLoadException?.Invoke(new FileLoadExceptionContext { Provider = this, Exception = failure });
            return SettingsFileFailure.Of(failure);
        }

        protected override void Dispose(bool disposing)
        {
            // The watch first, so that no notification asks a file provider that is gone for the next one.
            _watch?.Dispose();
            base.Dispose(disposing);
            (Source.FileProvider as IDisposable)?.Dispose();
        }
    }

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
    /// so the user's settings file — watched by its provider — feeds <c>IOptionsMonitor</c> live.
    /// </summary>
    /// <param name="paths">
    /// Where the installed plugins are listed from, for a refusal whose path names one — by the binder or by a
    /// section's validator — and the data directory the harness root may never be in.
    /// </param>
    /// <param name="home">
    /// The home directory the harness root's default is made in, and the one folder the root may never be; null means the
    /// account's own.
    /// </param>
    public static IServiceCollection AddJasonOptions(this IServiceCollection services, IConfiguration configuration, JasonPaths paths, string? home = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(paths);
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
        services.AddSingleton<IValidateOptions<ResponderOptions>, BindingFailureValidator<ResponderOptions>>();
        services.AddSingleton<IValidateOptions<HarnessOptions>, BindingFailureValidator<HarnessOptions>>();

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
        services.AddSingleton<IValidateOptions<ResponderOptions>, ResponderOptionsValidator>();
        services.AddSingleton<IValidateOptions<NotificationsOptions>>(new NotificationsOptionsValidator(Installed));
        services.AddSingleton<IValidateOptions<HarnessOptions>>(new HarnessOptionsValidator(paths, home));

        Bound<RuntimeOptions>(services, configuration.GetSection(RuntimeOptions.Section), Installed).ValidateOnStart();
        Bound<LoggingOptions>(services, configuration.GetSection(LoggingOptions.Section), Installed).ValidateOnStart();
        Bound<DispatcherOptions>(services, configuration.GetSection(DispatcherOptions.Section), Installed).ValidateOnStart();
        Bound<RolesOptions>(services, configuration.GetSection(RolesOptions.Section), Installed).ValidateOnStart();
        Bound<UpdateOptions>(services, configuration.GetSection(UpdateOptions.Section), Installed).ValidateOnStart();
        Bound<InboundOptions>(services, configuration.GetSection(InboundOptions.Section), Installed).ValidateOnStart();
        Bound<ResponderOptions>(services, configuration.GetSection(ResponderOptions.Section), Installed).ValidateOnStart();
        Bound<HarnessOptions>(services, configuration.GetSection(HarnessOptions.Section), Installed).ValidateOnStart();

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

        services.AddSingleton(new SettingsFile(configuration));
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
