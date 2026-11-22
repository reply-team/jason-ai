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

    /// <summary>
    /// The verb: the stop, and on success the note that a start is owed taken back, whoever typed it
    /// (<see cref="Installation.StartOwed"/>). The routines that stop a runtime on their own account call
    /// <see cref="RunAsync"/>, which leaves it.
    /// </summary>
    public static async Task<int> RunAsDeliberateAsync(CliEnvironment env, bool human, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var exit = await RunAsync(env, human, cancellationToken).ConfigureAwait(false);
        if (exit == ExitCodes.Success)
        {
            Installation.StartOwed.Remove(env.Paths);
            if (StartsAtLogon(env))
            {
                env.Error.WriteLine(UntilTheNextLogon);
            }
        }

        return exit;
    }

    /// <summary>What a stop a person or an agent asks for says on stderr where the runtime is registered to start at logon.</summary>
    internal const string UntilTheNextLogon =
        "It stays stopped until the next logon, which starts it again; `jason runtime autostart disable` stops that.";

    /// <summary>Whether this account has the runtime registered to start at logon, as <c>runtime autostart status</c> reads it; no where that cannot be read.</summary>
    private static bool StartsAtLogon(CliEnvironment env)
    {
        try
        {
            return RuntimeAutostartCommands.Registrar(env).Read().Registered;
        }
        catch (Autostart.AutostartException)
        {
            return false;
        }
    }

    /// <summary>
    /// The verb with <c>--start-owed</c>: a stop made on behalf of an install or an update, which leaves the note that a
    /// start is owed rather than taking it back, since the runtime it stops is one nobody asked to stop. The note is
    /// written just before the shutdown is asked, so it stands from the stop on, the caller killed during the wait
    /// included. Where the stop does not see the runtime end, the note is read as <c>jason install</c>'s stop reads it
    /// (<see cref="Installation.StartOwed.After"/>): taken back only where the runtime refused the shutdown and goes on
    /// running, and kept where it ended, took the shutdown and has not ended, or was sent it and no answer came, a cancel
    /// inside the send among them. It takes back only what it wrote: where an older note
    /// already stands it writes none and leaves that one, whatever the stop comes to. Where no runtime answers to this data directory
    /// nothing is stopped and nothing is written: no start is owed for a runtime that was not running, and a data
    /// directory that is not there is not made.
    /// </summary>
    public static async Task<int> RunOnBehalfAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (new DescriptorReader(env.Paths).Look().Descriptor is null)
        {
            return await RunAsync(env, human, cancellationToken, timeout, poll).ConfigureAwait(false);
        }

        // An older note already standing is not this stop's: it is neither written over, which would lose its moment, nor
        // taken back below, since a start was owed before this stop and still is.
        var wrote = false;
        string? unwritten = null;
        if (!Installation.StartOwed.Read(env.Paths).Stands)
        {
            try
            {
                Installation.StartOwed.Write(env.Paths, (env.Clock ?? TimeProvider.System).GetUtcNow());
                wrote = true;
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                unwritten = refused.Message;
            }
        }

        bool? taken = null;
        var exit = await RunAsync(env, human, cancellationToken, timeout, poll, answered: answer => taken = answer).ConfigureAwait(false);
        if (exit != ExitCodes.Success && wrote
            && Installation.StartOwed.After(env.Paths, env.Processes ?? RuntimeProcessControl.Instance, stopSent: true, taken) == Installation.StopLeft.GoesOn)
        {
            // It refused the shutdown and goes on running, so no start is owed: the note this stop wrote is taken back, and
            // only that one.
            Installation.StartOwed.Remove(env.Paths);
        }
        else if (exit == ExitCodes.Success && unwritten is not null)
        {
            env.Error.WriteLine($"The note that a start is owed could not be written at {env.Paths.StartOwedFile} ({unwritten}), so nothing that reads it will start the runtime this stopped: `jason runtime start` starts one.");
        }

        return exit;
    }

    /// <param name="answered">
    /// Called once the runtime has answered the shutdown: with true where it took it, before its exit is waited for, since
    /// from then on it is going down whatever becomes of the wait; with false where it refused it. Not called where no answer
    /// came, so a caller that sent the shutdown and heard neither does not know whether it was taken.
    /// </param>
    public static async Task<int> RunAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? timeout = null, TimeSpan? poll = null, Action<bool>? answered = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var reader = new DescriptorReader(env.Paths);
        var look = reader.Look();
        if (look.Descriptor is not { } descriptor)
        {
            CliErrors.Print(env.Out, human, ExitCodes.RuntimeUnavailable, CliErrors.Serialize(CliErrors.NoDescriptor, reader.NoRuntime(look), retryable: !look.Denied));
            return ExitCodes.RuntimeUnavailable;
        }

        var (exitCode, response) = await OperationRunner.SendAsync(env, Operations.SystemShutdown, RequestBody.Empty(), human, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return exitCode;
        }

        if (!response.IsSuccess)
        {
            answered?.Invoke(false);
            CliErrors.Print(env.Out, human, ExitCodes.ApiError, OperationRunner.ErrorText(response));
            return ExitCodes.ApiError;
        }

        // The acknowledgement names the instance that is going down; the descriptor is the fallback for a
        // runtime that answered something this version of the CLI cannot read.
        answered?.Invoke(true);
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

        CliErrors.Print(env.Out, human, ExitCodes.ApiError, CliErrors.Serialize(
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
