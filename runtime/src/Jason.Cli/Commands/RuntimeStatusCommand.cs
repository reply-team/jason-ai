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
    }
}
