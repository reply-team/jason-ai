using System.Diagnostics;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime start</c>: make sure a runtime is running and say which one. A runtime that already
/// answers is reported as it stands — starting twice is not an error, and a second process on the same data
/// directory would only lose a fight over the lock. Otherwise this launches the service in the background and
/// waits for it to publish an endpoint of its own. Nothing about autostart is registered anywhere.
/// </summary>
public static class RuntimeStartCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var reader = new DescriptorReader(env.Paths);
        var descriptor = reader.Read();
        if (descriptor is not null && await ReportAsync(env, human, descriptor, cancellationToken).ConfigureAwait(false))
        {
            return ExitCodes.Success;
        }

        // Whatever the descriptor claimed, nobody is answering for it: the instance it names is the one the new
        // runtime must not be confused with.
        var staleInstance = descriptor?.InstanceId;
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        using var child = processes.Launch(env.Paths);

        var waited = timeout ?? DefaultTimeout;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var published = reader.Read();
            if (published is not null
                && !string.Equals(published.InstanceId, staleInstance, StringComparison.Ordinal)
                && await ReportAsync(env, human, published, cancellationToken).ConfigureAwait(false))
            {
                return ExitCodes.Success;
            }

            if (child.HasExited)
            {
                return Fail(env, $"The runtime process exited with code {child.ExitCode} before publishing an endpoint descriptor. See the log files under '{env.Paths.LogsDirectory}'.", retryable: false);
            }

            if (elapsed.Elapsed >= waited)
            {
                return Fail(env, $"The runtime did not publish an endpoint descriptor within {RuntimeStopCommand.Seconds(waited)} s. See the log files under '{env.Paths.LogsDirectory}'.", retryable: true);
            }

            await Task.Delay(poll ?? DefaultPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks the runtime the descriptor names who it is, and prints the answer when it is the same instance.
    /// Anything else — no answer, a rejected token, a different instance — means there is nothing running to
    /// report, and is not written anywhere: the caller is still deciding, and its own answer comes later.
    /// </summary>
    private static async Task<bool> ReportAsync(CliEnvironment env, bool human, RuntimeDescriptor descriptor, CancellationToken cancellationToken)
    {
        using var unspoken = new StringWriter();
        var (_, response) = await OperationRunner.SendAsync(env with { Out = unspoken }, Operations.SystemInfo, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (response is null || !response.IsSuccess)
        {
            return false;
        }

        var info = Parse(response);
        if (info is null || !string.Equals(info.InstanceId, descriptor.InstanceId, StringComparison.Ordinal))
        {
            return false;
        }

        if (human)
        {
            RuntimeStatusCommand.RenderHuman(env.Out, info, descriptor);
        }
        else
        {
            env.Out.WriteLine(response.Body);
        }

        return true;
    }

    private static SystemInfoResponse? Parse(RuntimeResponse response)
    {
        try
        {
            return JsonSerializer.Deserialize<SystemInfoResponse>(response.Body, JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Fail(CliEnvironment env, string message, bool retryable)
    {
        env.Out.WriteLine(CliErrors.Serialize(CliErrors.RuntimeStartFailed, message, retryable));
        return ExitCodes.ApiError;
    }
}
