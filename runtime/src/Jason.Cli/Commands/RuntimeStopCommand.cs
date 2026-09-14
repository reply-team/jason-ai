using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Discovery;
using Jason.Cli.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime stop</c>: ask the runtime to shut down, then wait until it really is gone. The verb only
/// returns success once the descriptor has been removed and the process has left the process table, so a
/// script may start the next runtime on the very next line.
/// </summary>
public static class RuntimeStopCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var descriptor = new DescriptorReader(env.Paths).Read();
        if (descriptor is null)
        {
            env.Out.WriteLine(CliErrors.Serialize(CliErrors.NoDescriptor, $"No runtime endpoint descriptor at '{env.Paths.DescriptorFile}'. Is the runtime running?", retryable: true));
            return ExitCodes.RuntimeUnavailable;
        }

        var (exitCode, response) = await OperationRunner.SendAsync(env, Operations.SystemShutdown, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exitCode;
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(response.Body);
            return ExitCodes.ApiError;
        }

        // The acknowledgement names the instance that is going down; the descriptor is the fallback for a
        // runtime that answered something this version of the CLI cannot read.
        var acknowledgement = Parse(response.Body);
        var instanceId = acknowledgement?.InstanceId ?? descriptor.InstanceId;
        var pid = acknowledgement?.Pid ?? descriptor.Pid;

        var waited = timeout ?? DefaultTimeout;
        if (await WaitForExitAsync(env, pid, waited, poll ?? DefaultPoll, cancellationToken).ConfigureAwait(false))
        {
            env.Out.WriteLine(human ? $"Runtime stopped (instance {instanceId}, pid {pid})." : response.Body);
            return ExitCodes.Success;
        }

        env.Out.WriteLine(CliErrors.Serialize(
            CliErrors.ShutdownTimeout,
            $"The runtime acknowledged the shutdown but had not exited after {Seconds(waited)} s.",
            retryable: true));
        return ExitCodes.ApiError;
    }

    /// <summary>Gone means both signs of life are gone: no endpoint descriptor, and no process behind the pid.</summary>
    private static async Task<bool> WaitForExitAsync(CliEnvironment env, int pid, TimeSpan timeout, TimeSpan poll, CancellationToken cancellationToken)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (!File.Exists(env.Paths.DescriptorFile) && !processes.IsRunning(pid))
            {
                return true;
            }

            if (elapsed.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ShutdownResponse? Parse(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ShutdownResponse>(body, JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Seconds(TimeSpan span) => span.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
