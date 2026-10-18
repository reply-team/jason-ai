using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting.Modules;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;

namespace Jason.Runtime.Hosting;

/// <summary>
/// Composes one runtime process: data directory → configuration → instance lock → logging → database
/// migration → capability token → Kestrel on 127.0.0.1 → descriptor. The descriptor is published last so a
/// client never finds an endpoint that is not yet listening.
/// </summary>
public static class RuntimeHost
{
    public static async Task<RunningRuntime> StartAsync(JasonPaths paths, RuntimeHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        options ??= new RuntimeHostOptions();

        try
        {
            DataDirectoryLayout.Ensure(paths);
        }
        catch (Exception cause) when (cause is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // The first thing this process does, and it runs before logging exists -- so an exception escaping
            // here killed the runtime with nothing written anywhere and a CLI pointing at an empty log
            // directory. A first run that cannot proceed owes a sentence, not a stack trace and six empty
            // directories.
            throw new DataDirectoryUnusableException(paths.Root, cause);
        }

        // Declared ahead of the configuration, which is built before there is a log: the handler it is given is for
        // an edit made while the runtime runs, and by then the logger is the one assigned below. An edit in the
        // moment between the two has nowhere to be said, and the next one is not affected by that.
        Logger? logger = null;

        // Whether the runtime has read its settings yet: set once the host has started. Validating every section fills
        // the cache the options system keeps for each, but a service that reads a section once, as the handler pool reads
        // the dispatcher's, reads it through a second cache that is first filled as the host makes its services. Until
        // all of that is done an unreadable edit is not one the runtime keeps out. A failed reload empties the file's keys
        // before it fails, and nothing outside the framework can put them back, so whatever is read for the first time
        // after such an edit reads none of them and starts on its defaults. Reading every section right after the
        // configuration is built would close that window, but the sections are read through the host's own services,
        // which exist only after the lock, the log and the database; reading them any earlier would be reading through a
        // second set the runtime never uses. So the window is said as what it does rather than said to keep the settings
        // it had.
        var settingsRead = false;
        var configuration = JasonConfiguration.Build(
            paths,
            options.ShippedSettingsDirectory,
            failure => ReportUnreadableSettings(logger, failure, Volatile.Read(ref settingsRead)));
        options.ConfigurationBuilt?.Invoke(configuration);

        // From here the configuration holds a watch on the settings file, so every way out of this start that does not
        // hand it to the running runtime disposes it — a refused start included, which a test process repeats many times.
        JasonSettings settings;
        InstanceLock instanceLock;
        try
        {
            settings = JasonConfiguration.Load(configuration);

            // The lock comes first: a second runtime must fail with RuntimeAlreadyRunningException, not with a
            // sharing violation on the rolling log file the first runtime already holds open.
            instanceLock = InstanceLock.Acquire(paths);
        }
        catch
        {
            configuration.Dispose();
            throw;
        }

        WebApplication? app = null;
        var started = false;
        try
        {
            logger = JasonLogging.Create(paths, settings.Logging, options.ConsoleLogging);
            var info = RuntimeInfo.Create();
            logger.Information("Jason runtime {Version} starting as {InstanceId} (pid {Pid}) with data directory {DataDir}", info.RuntimeVersion, info.InstanceId, info.Pid, paths.Root);
            if (options.ConsoleAtStart is { } console)
            {
                // Written down once there is somewhere to write it: whether this Windows honoured the executable's
                // consoleAllocationPolicy, which nobody can otherwise tell after the fact.
                logger.Information(ProcessDetacher.LogTemplate, ProcessDetacher.Word(console));
            }

            var migration = new DatabaseMigrator(paths).Migrate();
            logger.Information("Database ready: {Applied} migrations applied, {New} newly applied, backup {Backup}", migration.AppliedMigrations.Count, migration.NewlyApplied.Count, migration.BackupFile ?? "none");

            var token = CapabilityToken.Generate();
            app = BuildApplication(paths, configuration, settings, options, info, migration, token, logger, out var seams);
            // A section refused as the host starts is a settings refusal like the ones before the log began, and is said
            // the same way: the refusal writer is where a person who started this runtime in the background hears it,
            // whether or not this start has written to the log by then.
            try
            {
                // Every section validated before the host makes a single service of its own. The host would validate
                // them as it starts, but only after making its hosted services, and one of those reading a section
                // first would refuse that section alone; validated here, every section refused is said at once.
                app.Services.GetService<IStartupValidator>()?.Validate();
            }
            catch (Exception refused) when (InvalidSettingsException.FromStart(refused) is { } settingsRefused)
            {
                // The host is not started, so it says nothing of this; the log line is the runtime's own.
                logger.Error("The runtime did not start: its settings were refused: {Failure}", settingsRefused.Failure);
                throw settingsRefused;
            }

            // Every seam reads its section once, now that every section has validated, so each holds last good settings
            // before an edit can reach it. Some are read as the host starts its services, but not all of them in every
            // runtime: with no dispatch loop nothing read the roles, and a refused edit that reached that seam first found
            // nothing to keep.
            foreach (var seam in seams)
            {
                ((ISettingsSeam)app.Services.GetRequiredService(seam)).Seed();
            }

            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception refused) when (InvalidSettingsException.FromStart(refused) is { } settingsRefused)
            {
                // Logged by the host already, as the reason it failed to start.
                throw settingsRefused;
            }

            started = true;
            Volatile.Write(ref settingsRead, true);

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException("Kestrel started without reporting a listening address.");
            app.Services.GetRequiredService<ListeningEndpoint>().Port = new Uri(address).Port;
            var publisher = new DescriptorPublisher(paths) { Publishing = options.DescriptorPublishing ?? DescriptorPublisher.PublishRule };
            RuntimeDescriptor descriptor;
            try
            {
                descriptor = publisher.Publish(info, new Uri(address), token, cancellationToken);
            }
            catch (DescriptorUnpublishableException unpublishable)
            {
                // The path and a closed phrase, never the exception: its sentence is said where the start was asked for.
                logger.Error("The runtime did not start: its endpoint descriptor at {Descriptor} could not be published: {Why}", paths.DescriptorFile, unpublishable.Why);
                throw;
            }

            logger.Information("Runtime API listening on {BaseUrl}; descriptor published at {Descriptor}", descriptor.BaseUrl, paths.DescriptorFile);

            return new RunningRuntime(app, descriptor, paths, publisher, instanceLock, configuration, logger);
        }
        catch
        {
            if (app is not null)
            {
                try
                {
                    if (started)
                    {
                        // Stopped before it is disposed, as a running runtime is. Disposed while started, its background
                        // services are cancelled with the host never told to stop, and the host logs each as a service that
                        // failed and says it is stopping because of it — beside a refusal that has nothing to do with either.
                        await app.StopAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception stopping) when (stopping is AggregateException or OperationCanceledException)
                {
                    // What the host throws when a service would not stop, or not in time. The start's own failure is what
                    // is said; a stop that failed after it does not take its place.
                }
                finally
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
            }

            configuration.Dispose();
            instanceLock.Dispose();
            if (logger is not null)
            {
                await logger.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>
    /// One line for an edit that left the settings file unreadable: the kind of failure and where the parser
    /// stopped, as separate properties so the log can be searched by them. Never the exception, whose message
    /// quotes what was typed.
    /// </summary>
    /// <param name="settingsRead">
    /// Whether the runtime had read its settings when the edit failed. Only then does it keep the settings it had;
    /// before, any setting not yet in force starts on its default — the host may have read one already to build itself,
    /// and the part of the runtime that works from it still reads it afresh — and a restart once the file can be read is
    /// what puts all of them right, since some are read only at start.
    /// </param>
    private static void ReportUnreadableSettings(Logger? logger, SettingsFileFailure failure, bool settingsRead)
    {
        if (failure.LineNumber is null || failure.BytePositionInLine is null)
        {
            if (settingsRead)
            {
                logger?.Warning("The settings file could not be read ({ExceptionType}); the runtime keeps the settings it had", failure.ExceptionType);
            }
            else
            {
                logger?.Warning(
                    "The settings file could not be read ({ExceptionType}) while the runtime was starting, so any setting not yet in force starts on its default; restart it once the file can be read",
                    failure.ExceptionType);
            }

            return;
        }

        if (settingsRead)
        {
            logger?.Warning(
                "The settings file could not be read ({ExceptionType} at line {LineNumber}, byte {BytePositionInLine}); the runtime keeps the settings it had",
                failure.ExceptionType,
                failure.LineNumber,
                failure.BytePositionInLine);
        }
        else
        {
            logger?.Warning(
                "The settings file could not be read ({ExceptionType} at line {LineNumber}, byte {BytePositionInLine}) while the runtime was starting, so any setting not yet in force starts on its default; restart it once the file can be read",
                failure.ExceptionType,
                failure.LineNumber,
                failure.BytePositionInLine);
        }
    }

    /// <summary>Foreground mode (<c>jason runtime run</c>): start, wait for Ctrl+C or a host shutdown, stop.</summary>
    public static async Task<int> RunAsync(JasonPaths paths, RuntimeHostOptions? options, CancellationToken cancellationToken)
    {
        var refusals = options?.Refusals ?? Console.Error;
        RunningRuntime runtime;
        try
        {
            runtime = await StartAsync(paths, options, cancellationToken).ConfigureAwait(false);
        }
        // A stored value the start could not read is a refusal too: its sentence names the column and says what to do,
        // and it quotes nothing, so it can be said where a person will read it — found however the host wrapped it, inside
        // another failure or among several. So are settings the start cannot use: the file and the position, or the
        // setting's path and its type, refused before there is a log to read. And so is an endpoint descriptor that could not be
        // put in place, the last step of a start: without it no client could find this runtime.
        catch (Exception refused) when (refused is RuntimeAlreadyRunningException or DataDirectoryUnusableException or InvalidSettingsException
            or DescriptorUnpublishableException
            || UnreadableStoredValueException.In(refused) is not null)
        {
            // Said where whoever started this can hear it. A detached runtime's own standard error is the null
            // device by now, and the reason it will not start used to go there.
            try
            {
                await refusals.WriteLineAsync(UnreadableStoredValueException.In(refused)?.Message ?? refused.Message).ConfigureAwait(false);
                await refusals.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Nobody can hear it: on a Windows that ignored the executable's policy this was a console, and the
                // detacher has let go of it. The refusal is still the answer.
            }

            return 1;
        }
        finally
        {
            if (options?.Refusals is { } handedIn)
            {
                try
                {
                    await handedIn.DisposeAsync().ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The same console, closed from under a writer that still meant to flush into it.
                }
            }
        }

        await using (runtime.ConfigureAwait(false))
        {
            await runtime.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private static WebApplication BuildApplication(
        JasonPaths paths,
        IConfigurationRoot configuration,
        JasonSettings settings,
        RuntimeHostOptions options,
        RuntimeInfo info,
        MigrationReport migration,
        string token,
        Logger logger,
        out IReadOnlyList<Type> seams)
    {
        // The builder sets up its default appsettings sources as it is created, with reload on change, and that starts a
        // watch on the content root — the whole data directory, every subdirectory included — before the sources are
        // cleared below; clearing them does not stop it. The one setting that decides it is read from these arguments
        // at creation, and they outrank a DOTNET_ variable saying otherwise.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = ["--hostBuilder:reloadConfigOnChange=false"],
            ContentRootPath = paths.Root,
        });

        // The runtime's own settings come from the standalone configuration root below, so the web host reads no
        // ambient configuration: no DOTNET_/ASPNETCORE_ variables, no appsettings.json next to the binary. One
        // empty in-memory source stays behind because UseUrls and friends write host settings through it.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [HostDefaults.ContentRootKey] = paths.Root,
        });
        var configuredPort = options.Port ?? settings.Runtime.Port;
        var port = configuredPort == 0 ? 0 : PortProbe.FirstFree(configuredPort);
        if (configuredPort != 0 && port != configuredPort)
        {
            logger.Information("Port {ConfiguredPort} is taken; listening on {Port} instead (0 means the OS chooses)", configuredPort, port);
        }

        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton(new ListeningEndpoint(configuredPort));

        // The options bind against the standalone root, not builder.Configuration, which was cleared above.
        builder.Services.AddJasonOptions(configuration, paths);

        // Stopping has to outlast the dispatcher's drain, or the host would cut short the very wait it asked for.
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(Math.Max(0, settings.Dispatcher.DrainSeconds) + 5));
        builder.Services.AddSerilog(logger, dispose: false);
        builder.Services.ConfigureHttpJsonOptions(o => JasonJson.Apply(o.SerializerOptions));
        builder.Services.AddSingleton(info);
        builder.Services.AddSingleton(migration);
        builder.Services.AddSingleton(paths);
        builder.Services.AddDbContext<JasonDbContext>(o => JasonDbContext.Configure(o, paths.DatabaseFile));
        builder.Services.AddSingleton(options.Clock ?? TimeProvider.System);
        builder.Services.AddSingleton(new RuntimeSecrets(token));
        builder.Services.AddSingleton<TokenRedactor>();
        builder.Services.AddSingleton<RunningAttemptRegistry>();
        builder.Services.AddSingleton<DispatcherStatus>();

        // How every validating site matches a schema's patterns: the real match under the dialect's own timeout. It is
        // a service so a test can replace it for one host; nothing in the runtime chooses another. The warm-up is the
        // first hosted service registered, and hosted services start in order, so the build's own patterns have been
        // matched once before a manifest is read or the dispatcher scans.
        builder.Services.AddSingleton(PatternTiming.Default);
        builder.Services.AddHostedService<PatternWarmUp>();
        builder.Services.AddScoped<JournalWriter>();
        builder.Services.AddScoped<AttemptOutcomes>();
        builder.Services.AddScoped<ProviderOutcomeRecorder>();
        builder.Services.AddSystemModule().AddCampaignModule().AddContactModule();
        builder.Services.AddWorkItemModule().AddExecutorModule().AddRoleModule().AddPluginModule().AddRoutingModule().AddDispatcherModule().AddNotificationsModule().AddCommandModule().AddUiModule();
        builder.Services.AddApprovalModule().AddDecisionModule().AddReportModule().AddProfileModule().AddRoleNoteModule();
        builder.Services.AddUpdateModule(options.FeedHandler);

        // Last, so a test's registration wins over the runtime's own for the services that resolve by "the last one".
        options.ConfigureServices?.Invoke(builder.Services);

        // Every settings seam registered, whichever module registered it, for the start to read once.
        seams = [.. builder.Services
            .Select(service => service.ServiceType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LiveSettings<>))
            .Distinct()];

        var app = builder.Build();

        // Request logging goes through this runtime's logger, not Serilog's static global one, which is never
        // configured here. The default template carries {RequestPath}, so every line names its operation — by the
        // route it matched, never the path as sent: the line is written for every request, before the host and the
        // token are checked, and a path and a method are whatever a local client chose to send.
        app.UseSerilogRequestLogging(requestLogging =>
        {
            requestLogging.Logger = logger;
            requestLogging.GetMessageTemplateProperties = (context, _, elapsedMs, statusCode) => RequestLine.Properties(context, elapsedMs, statusCode);
        });
        app.Use(LoopbackHostFilter.Middleware);
        app.Use(OriginRule.Middleware);
        app.Use(BearerTokenAuthentication.Middleware(token));

        app.MapSystemOperations();
        app.MapCampaignOperations();
        app.MapContactOperations();
        app.MapWorkItemOperations();
        app.MapExecutorOperations();
        app.MapRoleOperations();
        app.MapApprovalOperations();
        app.MapDecisionOperations();
        app.MapReportOperations();
        app.MapProfileOperations();
        app.MapRoleNoteOperations();
        app.MapPluginOperations();
        app.MapRouteOperations();
        app.MapUiOperations();
        app.MapUi();

        // An explicit catch-all pattern: the default fallback pattern is "{*path:nonfile}", and every operation
        // name contains a dot, so a mistyped operation would look like a file request and escape the fallback.
        app.MapFallback("{*path}", context => ErrorResults.WriteAsync(context, StatusCodes.Status404NotFound, "not_found", $"Unknown operation. Operations are invoked as POST /{ApiVersion.Current}/{{noun}}.{{verb}}.", retryable: false));

        return app;
    }
}
