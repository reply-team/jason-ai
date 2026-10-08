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
/// returns success once the acknowledged process has left the process table, so a script may start the next runtime
/// on the very next line. The runtime removes its endpoint descriptor before it exits; one it could not remove is said
/// on standard error, and the stop still succeeds, because the runtime it stopped is gone.
/// </summary>
public static class RuntimeStopCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var reader = new DescriptorReader(env.Paths);
        var look = reader.Look();
        if (look.Descriptor is not { } descriptor)
        {
            env.Out.WriteLine(CliErrors.Serialize(CliErrors.NoDescriptor, reader.NoRuntime(look), retryable: !look.Denied));
            return ExitCodes.RuntimeUnavailable;
        }

        var (exitCode, response) = await OperationRunner.SendAsync(env, Operations.SystemShutdown, RequestBody.Empty(), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exitCode;
        }

        if (!response.IsSuccess)
        {
            env.Out.WriteLine(OperationRunner.ErrorText(response));
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
            if (LeftBehind(env, instanceId) is { } note)
            {
                env.Error.WriteLine(note);
            }

            return ExitCodes.Success;
        }

        env.Out.WriteLine(CliErrors.Serialize(
            CliErrors.ShutdownTimeout,
            $"The runtime acknowledged the shutdown but had not exited after {Seconds(waited)} s.",
            retryable: true));
        return ExitCodes.ApiError;
    }

    /// <summary>
    /// Gone means the acknowledged process has ended, by that alone. Whether a process is running can be wrong only
    /// towards "running", where its pid has been reused, never towards "gone". The descriptor is not waited for: the
    /// runtime removes it before it exits, and one still there afterwards is one it could not remove, which a program
    /// holding it may keep there for as long as it likes.
    /// </summary>
    private static async Task<bool> WaitForExitAsync(CliEnvironment env, int pid, TimeSpan timeout, TimeSpan poll, CancellationToken cancellationToken)
    {
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (!processes.IsRunning(pid))
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

    /// <summary>
    /// What a descriptor still there after the stopped runtime has gone says, read once: nothing where there is none; a
    /// runtime started since where it names another instance; and otherwise, named or unreadable, the stopped runtime's
    /// own, which it could not remove. A holder's handle does not stop the read. The CLI cannot tell a held descriptor
    /// from a read-only one, so the line names both and sends the person to the runtime's log, which says which.
    /// </summary>
    private static string? LeftBehind(CliEnvironment env, string instanceId)
    {
        if (!File.Exists(env.Paths.DescriptorFile))
        {
            return null;
        }

        var look = new DescriptorReader(env.Paths).Look();
        if (look.Descriptor is { } other && !string.Equals(other.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return $"A runtime with another instance id, '{other.InstanceId}', has started since; this stop ended only the one it acknowledged.";
        }

        // Gone between the two looks: removed after all, and nothing to say.
        if (look.Descriptor is null && !look.Denied && !File.Exists(env.Paths.DescriptorFile))
        {
            return null;
        }

        return $"Its endpoint descriptor at '{env.Paths.DescriptorFile}' was left behind: the runtime could not remove it. "
            + "Another program may be holding it, or it may be read-only; the runtime's log says which. A CLI reading it "
            + "finds no runtime at its address and says so, and the next start replaces it once the file is free.";
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
