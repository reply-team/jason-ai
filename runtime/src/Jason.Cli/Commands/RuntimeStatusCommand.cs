using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime status</c>: read the descriptor, call <c>system.info</c>, print the exact response.
/// Any client-side failure is reported in the error envelope with exit code 3.
/// </summary>
public static class RuntimeStatusCommand
{
    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var descriptor = new DescriptorReader(env.Paths).Read();
        if (descriptor is null)
        {
            return Fail(env, CliErrors.NoDescriptor, $"No runtime endpoint descriptor at '{env.Paths.DescriptorFile}'. Is the runtime running?", retryable: true);
        }

        using var client = new RuntimeClient(descriptor, env.HttpHandler);
        RuntimeResponse response;
        try
        {
            response = await client.PostAsync(Operations.SystemInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer ({ex.Message}). The descriptor may be stale.", retryable: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(env, CliErrors.RuntimeUnreachable, $"The runtime at {descriptor.BaseUrl} did not answer in time.", retryable: true);
        }

        if (response.StatusCode == 401)
        {
            return Fail(env, CliErrors.Unauthorized, "The runtime rejected the capability token from the descriptor; the descriptor is stale.", retryable: true);
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(response.Body);
            return ExitCodes.ApiError;
        }

        SystemInfoResponse? info;
        try
        {
            info = JsonSerializer.Deserialize<SystemInfoResponse>(response.Body, JasonJson.Options);
        }
        catch (JsonException)
        {
            info = null;
        }

        if (info is null || !string.Equals(info.InstanceId, descriptor.InstanceId, StringComparison.Ordinal))
        {
            return Fail(env, CliErrors.StaleDescriptor, $"The descriptor names instance '{descriptor.InstanceId}' but the runtime answering at {descriptor.BaseUrl} is '{info?.InstanceId ?? "unknown"}'.", retryable: true);
        }

        if (human)
        {
            RenderHuman(env.Out, info, descriptor);
        }
        else
        {
            env.Out.WriteLine(response.Body);
        }

        return ExitCodes.Success;
    }

    private static int Fail(CliEnvironment env, string code, string message, bool retryable)
    {
        env.Out.WriteLine(CliErrors.Serialize(code, message, retryable));
        return ExitCodes.RuntimeUnavailable;
    }

    /// <summary>Shared with <c>runtime start</c>, which reports a running runtime exactly the way status does.</summary>
    internal static void RenderHuman(TextWriter output, SystemInfoResponse info, RuntimeDescriptor descriptor)
    {
        output.WriteLine($"Runtime:    running (pid {info.Pid})");
        output.WriteLine($"Version:    {info.RuntimeVersion}");
        output.WriteLine($"API:        {info.ApiVersion} at {descriptor.BaseUrl}");
        output.WriteLine($"Instance:   {info.InstanceId}");
        output.WriteLine($"Started:    {info.StartedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
        output.WriteLine($"Data dir:   {info.DataDir}");
        output.WriteLine($"Migrations: {info.Database.AppliedMigrations.Count} applied");
        output.WriteLine($"Dispatcher: {Dispatcher(info.Dispatcher)}");
        output.WriteLine($"Plugins:    {Plugins(info.Plugins)}");
        output.WriteLine($"Routes:     {Routes(info.Routes)}");
    }

    /// <summary>
    /// Where work is being sent. A runtime older than routing reports no section at all, and a runtime that
    /// routes nothing anywhere is the state worth spelling out: no campaign will reach a provider until
    /// somebody writes a route.
    /// </summary>
    private static string Routes(RoutesInfo? routes)
    {
        if (routes is null)
        {
            return "unknown";
        }

        if (routes is { GlobalDefaultPlugin: null, GlobalOverrideCount: 0, CampaignRouteCount: 0 })
        {
            return $"nothing is routed anywhere · snapshot {routes.SnapshotId}";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"snapshot {routes.SnapshotId} · default {routes.GlobalDefaultPlugin ?? "none"} · {Plural(routes.GlobalOverrideCount, "override")} · {Plural(routes.CampaignRouteCount, "campaign route")}");
    }

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    /// <summary>A runtime older than the dispatcher reports no section at all; say so rather than invent a state.</summary>
    private static string Dispatcher(DispatcherInfo? dispatcher)
    {
        if (dispatcher is null)
        {
            return "unknown";
        }

        var state = JsonNamingPolicy.SnakeCaseLower.ConvertName(dispatcher.State.ToString());
        var lastScan = dispatcher.LastScanAt is null
            ? "never"
            : dispatcher.LastScanAt.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        // The reviews this runtime has put on the queue since it started, beside the rest of the scan's
        // counters: a loop that is running and has summoned nothing is the thing somebody would want to see.
        var summons = dispatcher.Summons == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" · {dispatcher.Summons} summoned");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{state} · tick {dispatcher.TickSeconds} s · {dispatcher.RunningAttempts}/{dispatcher.MaxParallel} attempts · last scan {lastScan}{summons}");
    }

    /// <summary>
    /// What the plugin registry holds. A runtime older than the registry reports no section at all, and an
    /// empty registry after a refused load is the one state worth spelling out: nothing is loaded on purpose.
    /// </summary>
    private static string Plugins(PluginsInfo? plugins)
    {
        if (plugins is null)
        {
            return "unknown";
        }

        if (plugins is { ActiveCount: 0, LastReloadActivated: false })
        {
            return "none active · last reload rejected";
        }

        var loadedAt = plugins.LoadedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{plugins.ActiveCount} active · snapshot {plugins.SnapshotId} · loaded {loadedAt}");
    }
}
