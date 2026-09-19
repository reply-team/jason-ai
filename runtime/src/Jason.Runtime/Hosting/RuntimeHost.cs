using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
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

        DataDirectoryLayout.Ensure(paths);
        var configuration = JasonConfiguration.Build(paths, options.ShippedSettingsDirectory);
        var settings = JasonConfiguration.Load(configuration);

        // The lock comes first: a second runtime must fail with RuntimeAlreadyRunningException, not with a
        // sharing violation on the rolling log file the first runtime already holds open.
        var instanceLock = InstanceLock.Acquire(paths);
        Logger? logger = null;
        WebApplication? app = null;
        try
        {
            logger = JasonLogging.Create(paths, settings.Logging, options.ConsoleLogging);
            var info = RuntimeInfo.Create();
            logger.Information("Jason runtime {Version} starting as {InstanceId} (pid {Pid}) with data directory {DataDir}", info.RuntimeVersion, info.InstanceId, info.Pid, paths.Root);

            var migration = new DatabaseMigrator(paths).Migrate();
            logger.Information("Database ready: {Applied} migrations applied, {New} newly applied, backup {Backup}", migration.AppliedMigrations.Count, migration.NewlyApplied.Count, migration.BackupFile ?? "none");

            var token = CapabilityToken.Generate();
            app = BuildApplication(paths, configuration, settings, options, info, migration, token, logger);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException("Kestrel started without reporting a listening address.");
            var descriptor = new DescriptorPublisher(paths).Publish(info, new Uri(address), token);
            logger.Information("Runtime API listening on {BaseUrl}; descriptor published at {Descriptor}", descriptor.BaseUrl, paths.DescriptorFile);

            return new RunningRuntime(app, descriptor, paths, instanceLock, logger);
        }
        catch
        {
            if (app is not null)
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }

            instanceLock.Dispose();
            if (logger is not null)
            {
                await logger.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>Foreground mode (<c>jason runtime run</c>): start, wait for Ctrl+C or a host shutdown, stop.</summary>
    public static async Task<int> RunAsync(JasonPaths paths, RuntimeHostOptions? options, CancellationToken cancellationToken)
    {
        RunningRuntime runtime;
        try
        {
            runtime = await StartAsync(paths, options, cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeAlreadyRunningException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
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
        Logger logger)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = paths.Root });

        // The runtime's own settings come from the standalone configuration root below, so the web host reads no
        // ambient configuration: no DOTNET_/ASPNETCORE_ variables, no appsettings.json next to the binary. One
        // empty in-memory source stays behind because UseUrls and friends write host settings through it.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [HostDefaults.ContentRootKey] = paths.Root,
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Runtime.Port}");

        // The options bind against the standalone root, not builder.Configuration, which was cleared above.
        builder.Services.AddJasonOptions(configuration);

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
        builder.Services.AddScoped<JournalWriter>();
        builder.Services.AddScoped<AttemptOutcomes>();
        builder.Services.AddScoped<ProviderOutcomeRecorder>();
        builder.Services.AddSystemModule().AddCampaignModule().AddContactModule();
        builder.Services.AddWorkItemModule().AddExecutorModule().AddRoleModule().AddPluginModule().AddRoutingModule().AddDispatcherModule().AddCommandModule();
        builder.Services.AddApprovalModule().AddDecisionModule().AddReportModule().AddProfileModule().AddRoleNoteModule();
        builder.Services.AddUpdateModule(options.FeedHandler);

        // Last, so a test's registration wins over the runtime's own for the services that resolve by "the last one".
        options.ConfigureServices?.Invoke(builder.Services);

        var app = builder.Build();

        // Request logging goes through this runtime's logger, not Serilog's static global one, which is never
        // configured here. The default template carries {RequestPath}, so every line names its operation.
        app.UseSerilogRequestLogging(requestLogging => requestLogging.Logger = logger);
        app.Use(LoopbackHostFilter.Middleware);
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

        // An explicit catch-all pattern: the default fallback pattern is "{*path:nonfile}", and every operation
        // name contains a dot, so a mistyped operation would look like a file request and escape the fallback.
        app.MapFallback("{*path}", context => ErrorResults.WriteAsync(context, StatusCodes.Status404NotFound, "not_found", $"Unknown operation. Operations are invoked as POST /{ApiVersion.Current}/{{noun}}.{{verb}}.", retryable: false));

        return app;
    }
}
