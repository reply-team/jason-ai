using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Files;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Update;
using Jason.Runtime.Api;
using Jason.Runtime.Audiences;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Execution;
using Jason.Runtime.Harness;
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
/// Composes one runtime process: data directory → configuration → instance lock → logging → an update left
/// part-way → database migration → capability token → Kestrel on 127.0.0.1 → descriptor. The descriptor is published
/// last so a client never finds an endpoint that is not yet listening, and off the caller's thread, so a reader holding it keeps
/// the start's task from completing, never the caller from going on.
/// </summary>
public static class RuntimeHost
{
    public static Task<RunningRuntime> StartAsync(JasonPaths paths, RuntimeHostOptions? options = null, CancellationToken cancellationToken = default) =>
        StartAsync(paths, options, watchUnhandled: false, cancellationToken);

    /// <param name="watchUnhandled">
    /// Whether the runtime writes the line an unhandled exception ending the process leaves (<see cref="UnhandledExceptionLine"/>):
    /// the runtime mode's own start, which owns its process, and never a runtime started inside another program's.
    /// </param>
    private static async Task<RunningRuntime> StartAsync(JasonPaths paths, RuntimeHostOptions? options, bool watchUnhandled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        options ??= new RuntimeHostOptions();

        // Before anything is written: a process that does not compose text would key one profile two ways.
        TextComposition.EnsureThisProcessComposes();

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
        UnhandledExceptionLine? unhandled = null;
        var started = false;
        try
        {
            logger = JasonLogging.Create(paths, settings.Logging, options.ConsoleLogging);

            // From the moment there is a log until it is closed: an exception nothing catches, on any thread, ends the
            // process, and the log is where a person looks for why.
            if (watchUnhandled)
            {
                unhandled = UnhandledExceptionLine.Watch(logger);
            }

            var info = RuntimeInfo.Create();
            logger.Information("Jason runtime {Version} starting as {InstanceId} (pid {Pid}) with data directory {DataDir}", info.RuntimeVersion, info.InstanceId, info.Pid, paths.Root);
            if (options.ConsoleAtStart is { } console)
            {
                // Written down once there is somewhere to write it: whether this Windows honoured the executable's
                // consoleAllocationPolicy, which nobody can otherwise tell after the fact.
                logger.Information(ProcessDetacher.LogTemplate, ProcessDetacher.Word(console));
            }

            // Before the database is touched: a run nobody marked as the update's, around an update that has moved the
            // installed build aside, would migrate it with a build whose last steps were never taken.
            RefuseAroundAnUnfinishedUpdate(paths, options.UpdateLaunch, logger);

            MigrationReport migration;
            try
            {
                migration = new DatabaseMigrator(paths) { Log = logger }.Migrate();
            }
            catch (MigrationFailedException failed)
            {
                // Its sentence is said where the start was asked for. The log keeps which migration failed, what kind of
                // failure it was and where the copy is, never the failure's own words.
                if (failed.Created)
                {
                    logger.Error("The runtime did not start: migration {Migration} failed ({ExceptionType}) on a database this start made", failed.Migration, failed.InnerException?.GetType().Name);
                }
                else if (failed.Restored)
                {
                    logger.Error("The runtime did not start: migration {Migration} failed ({ExceptionType}), and the database was put back from {Backup}", failed.Migration, failed.InnerException?.GetType().Name, failed.Backup);
                }
                else
                {
                    logger.Error("The runtime did not start: migration {Migration} failed ({ExceptionType}), and the database could not be put back from {Backup}", failed.Migration, failed.InnerException?.GetType().Name, failed.Backup);
                }

                switch (failed.StoppedStart)
                {
                    case StoppedStart.PutBack:
                        logger.Warning("A start stopped while migrating was found; its backup {Backup} was put back", failed.StoppedStartBackup);
                        break;
                    case StoppedStart.NotPutBack:
                        logger.Warning("A start stopped while migrating was found; its backup {Backup} could not be put back", failed.StoppedStartBackup);
                        break;
                    case StoppedStart.ChronicleMoved:
                        logger.Warning("A start stopped while migrating was found; its backup {Backup} was not put back: the chronicle moved", failed.StoppedStartBackup);
                        break;
                    case StoppedStart.BackupGone:
                        logger.Warning("A start stopped while migrating was found; its backup {Backup} was not put back: it is not there", failed.StoppedStartBackup);
                        break;
                }

                throw;
            }

            if (migration.StaleMigrationLockCleared)
            {
                // By its kind alone, so a diagnostic bundle shows that a start stopped part way came before this one.
                logger.Warning("A migration lock that a start stopped part way left taken was cleared before migrating");
            }

            logger.Information("Database ready: {Applied} migrations applied, {New} newly applied, backup {Backup}", migration.AppliedMigrations.Count, migration.NewlyApplied.Count, migration.BackupFile ?? "none");
            if (migration.LinkedInKeys is { } linkedIn)
            {
                // Counts alone: which rows were left is what the register and the contacts say, never the log.
                logger.Information(
                    "LinkedIn profile links re-keyed: {Stored} stored, {Keys} are keys, {Refused} left as they stood because the rule refuses them, {LeftBeside} left as they stood beside the one holding their key, {SharedKeys} keys held by more than one contact",
                    linkedIn.Stored,
                    linkedIn.Keys,
                    linkedIn.Refused,
                    linkedIn.LeftBeside,
                    linkedIn.SharedKeys);
            }

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
            // The wait for a reader to let go of the descriptor is timed by the clock the services carry, as theirs are.
            var publisher = new DescriptorPublisher(paths)
            {
                Publishing = options.DescriptorPublishing ?? DescriptorPublisher.PublishRule,
                Clock = app.Services.GetRequiredService<TimeProvider>(),
                Pause = options.DescriptorPause ?? HeldFileRetry.Sleep,
            };
            RuntimeDescriptor descriptor;
            try
            {
                // On a thread of the pool, never the caller's: a held descriptor makes the publish sleep between its tries
                // for as long as its rule allows, and nothing before it is sure to have yielded, so done in place it would
                // hold whoever called this start for that long. The start's cancellation still reaches the publish through
                // the token it is handed, and only there, so a publish that finds nothing held goes ahead as before.
                descriptor = await Task.Run(() => publisher.Publish(info, new Uri(address), token, cancellationToken), CancellationToken.None).ConfigureAwait(false);
            }
            catch (DescriptorUnpublishableException unpublishable)
            {
                // The path and a closed phrase, never the exception: its sentence is said where the start was asked for.
                logger.Error("The runtime did not start: its endpoint descriptor at {Descriptor} could not be published: {Why}", paths.DescriptorFile, unpublishable.Why);
                throw;
            }

            logger.Information("Runtime API listening on {BaseUrl}; descriptor published at {Descriptor}", descriptor.BaseUrl, paths.DescriptorFile);

            return new RunningRuntime(app, descriptor, paths, publisher, instanceLock, configuration, logger, unhandled);
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
            unhandled?.Dispose();
            if (logger is not null)
            {
                await logger.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>
    /// Refuses a start made around an update that stopped part-way, unless the update made it. A runtime registered to
    /// start at sign-in runs this mode, and so do the apply's own start and the rollback's: from the step that moves the
    /// installed build aside on, the install path may hold a build whose last steps were never taken, and only the
    /// update's own run, marked by <see cref="UpdateLaunch"/>, is the one that takes them. A ledger that cannot be read
    /// cannot say which step it is at and is refused the same way, as <c>jason runtime start</c> refuses it. Before that
    /// step nothing has been replaced, and the installed build starts.
    /// </summary>
    /// <remarks>
    /// After the instance lock and the log, so the refusal is written down and a second runtime is still told it is the
    /// second; before the database, the token and the descriptor, so a refused run leaves none of them, and the start's own
    /// way out gives the lock and the log back. Logged by the code, the step and the ledger's path, an installation path,
    /// and never by the ledger's text or the mark's value, which come from whatever wrote them.
    /// </remarks>
    private static void RefuseAroundAnUnfinishedUpdate(JasonPaths paths, string? mark, Logger logger)
    {
        var unfinished = UnfinishedUpdate.Read(paths);
        if (unfinished is null)
        {
            return;
        }

        var step = unfinished.Ledger is { } ledger ? UpdateLaunch.At(ledger.Step) : "unknown";
        if (UpdateLaunch.Marks(mark))
        {
            logger.Information("Started by the update's own launch; its ledger is at {Step}", step);
            return;
        }

        if (!unfinished.Replacing)
        {
            logger.Information("An update stopped at {Step}, before anything was replaced; the installed build starts", step);
            return;
        }

        var (code, message) = unfinished.Refusal("Nothing was started.");
        if (unfinished.Ledger is null)
        {
            logger.Error("The runtime did not start: the update ledger {Ledger} cannot be read ({Code})", unfinished.Path, code);
        }
        else
        {
            logger.Error("The runtime did not start: an update stopped at {Step} and this start is not its own ({Code}); its ledger is {Ledger}", step, code, unfinished.Path);
        }

        throw new UnfinishedUpdateRefusalException(code, message);
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
            runtime = await StartAsync(paths, options, watchUnhandled: true, cancellationToken).ConfigureAwait(false);
        }
        // A stored value the start could not read is a refusal too: its sentence names the column and says what to do,
        // and it quotes nothing, so it can be said where a person will read it — found however the host wrapped it, inside
        // another failure or among several. So are settings the start cannot use: the file and the position, or the
        // setting's path and its type, refused before there is a log to read. And so is an endpoint descriptor that could not be
        // put in place, the last step of a start: without it no client could find this runtime. And so is a process that does
        // not compose text, refused before anything else. And so is a migration that failed, with where the database was put
        // back from.
        catch (Exception refused) when (refused is RuntimeAlreadyRunningException or DataDirectoryUnusableException or InvalidSettingsException
            or DescriptorUnpublishableException or TextCompositionUnavailableException or MigrationFailedException
            or UnfinishedUpdateRefusalException
            || UnreadableStoredValueException.In(refused) is not null)
        {
            // Said where whoever started this can hear it. A detached runtime's own standard error is the null
            // device by now, and the reason it will not start used to go there. An update's refusal is said in the
            // envelope `jason runtime start` says it in, with its code.
            try
            {
                await refusals.WriteLineAsync(
                    refused is UnfinishedUpdateRefusalException update ? update.Envelope : UnreadableStoredValueException.In(refused)?.Message ?? refused.Message).ConfigureAwait(false);
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
        var home = options.Home?.Invoke(paths) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        builder.Services.AddJasonOptions(configuration, paths, home);
        builder.Services.AddSingleton(new HarnessHome(home));

        // Stopping has to outlast the dispatcher's drain, or the host would cut short the very wait it asked for.
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(Math.Max(0, settings.Dispatcher.DrainSeconds) + 5));
        builder.Services.AddSerilog(logger, dispose: false);
        builder.Services.ConfigureHttpJsonOptions(o => JasonJson.Apply(o.SerializerOptions));
        builder.Services.AddSingleton(info);
        builder.Services.AddSingleton(migration);
        builder.Services.AddSingleton(paths);
        builder.Services.AddDbContext<JasonDbContext>((services, o) => JasonDbContext.Configure(
            o,
            paths.DatabaseFile,
            options.JournalLinesStrict ? JournalLines.Strict : JournalLines.Logged(services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JournalLineInterceptor>>())));
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
        builder.Services.AddScoped<SentFacts>();
        builder.Services.AddScoped<SearchLanding>();
        builder.Services.AddScoped<ProviderOutcomeRecorder>();
        builder.Services.AddSystemModule().AddCampaignModule().AddProjectModule().AddPreferenceModule().AddContactModule().AddAudienceModule().AddSenderModule();
        builder.Services.AddWorkItemModule().AddExecutorModule().AddRoleModule().AddPluginModule().AddRoutingModule().AddDispatcherModule().AddEventsModule().AddSubscriptionModule().AddNotificationsModule().AddCommandModule().AddUiModule();
        builder.Services.AddApprovalModule().AddBlockModule().AddDecisionModule().AddReplyModule().AddReportModule().AddProfileModule().AddRoleNoteModule();
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
        // Before the host, origin and bearer checks, so the bearer check can tell an upgrade from a plain request; a
        // socket is accepted only by the stream's own handler, after all three.
        app.UseWebSockets();
        app.Use(LoopbackHostFilter.Middleware);
        app.Use(OriginRule.Middleware);
        app.Use(BearerTokenAuthentication.Middleware(token));

        app.MapSystemOperations();
        app.MapCampaignOperations();
        app.MapProjectOperations();
        app.MapPreferenceOperations();
        app.MapContactOperations();
        app.MapAudienceOperations();
        app.MapSenderOperations();
        app.MapWorkItemOperations();
        app.MapExecutorOperations();
        app.MapRoleOperations();
        app.MapApprovalOperations();
        app.MapBlockOperations();
        app.MapDecisionOperations();
        app.MapReplyOperations();
        app.MapReportOperations();
        app.MapProfileOperations();
        app.MapRoleNoteOperations();
        app.MapPluginOperations();
        app.MapRouteOperations();
        app.MapEventsOperations();
        app.MapSubscriptionOperations();
        app.MapUiOperations();

        // The one route that answers a GET: a WebSocket upgrade is a GET by its protocol.
        app.MapGet(Streams.JournalRoute, JournalStream.HandleAsync);
        app.MapUi();

        // An explicit catch-all pattern: the default fallback pattern is "{*path:nonfile}", and every operation
        // name contains a dot, so a mistyped operation would look like a file request and escape the fallback.
        app.MapFallback("{*path}", context => ErrorResults.WriteAsync(context, StatusCodes.Status404NotFound, "not_found", $"Unknown operation. Operations are invoked as POST /{ApiVersion.Current}/{{noun}}.{{verb}}.", retryable: false));

        return app;
    }
}
