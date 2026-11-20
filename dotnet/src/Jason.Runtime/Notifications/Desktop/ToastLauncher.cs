using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// How one toast launch ended: whether the process started at all, whether it was killed at its bound, its exit code,
/// what it wrote on stdout, and the end of what it wrote on stderr.
/// </summary>
public sealed record LaunchResult(bool Started, bool TimedOut, int? ExitCode, string Stdout, string StderrTail, string? StartError = null)
{
    /// <summary>The last non-empty line of stderr: what a program says last is usually why it stopped.</summary>
    public string? LastStderrLine => StderrTail
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault();
}

/// <summary>
/// A toast whose program stays until the person clicks the notification or dismisses it: what it said by the time it
/// was answered, and, where it stayed, its end to come and the way to end it sooner.
/// </summary>
public sealed class WaitingLaunch(LaunchResult said, Task<LaunchResult>? end, Action close)
{
    /// <summary>What the program said by then; where it had already ended, its whole end.</summary>
    public LaunchResult Said { get; } = said;

    /// <summary>Its end, with the end of what it wrote on stdout; null where it had ended by the time it was answered.</summary>
    public Task<LaunchResult>? End { get; } = end;

    /// <summary>Ends the program, and with it the notification's click; its end then comes as a killed program's.</summary>
    public void Close() => close();
}

/// <summary>Runs one toast launch, bounded.</summary>
public interface IToastLauncher
{
    Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a program that stays: answered once it wrote a line of stdout that <paramref name="shown"/> takes, ended, or
    /// outlived <paramref name="bound"/>, and never killed at the bound. One still running is handed back with its end to
    /// come, which no cancellation of this call ends: only <see cref="WaitingLaunch.Close"/> does.
    /// </summary>
    Task<WaitingLaunch> LaunchWaitingAsync(ToastLaunch launch, TimeSpan bound, Func<string, bool> shown, CancellationToken cancellationToken);
}

/// <summary>
/// The one process launch behind the toast channel, through the one door every child of this product goes through: no
/// window, no shell, the environment cleared and set to the launch's own, the payload written on stdin and stdin closed,
/// and the whole tree killed at the bound.
/// </summary>
public sealed class ProcessToastLauncher : IToastLauncher
{
    /// <summary>The most of stdout kept: an outcome is one short line.</summary>
    public const int MaxStdout = 64 * 1024;

    /// <summary>The end of stderr kept: enough to say why a program stopped.</summary>
    public const int StderrTail = 2_000;

    /// <summary>How long a killed child is waited for before it is left to the operating system.</summary>
    private const int KillGraceMs = 2_000;

    public async Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        // UTF-8 without a byte-order mark both ways, the door's rule for a child that speaks to Jason: left to itself,
        // stdin is written in the parent's console code page, which a runtime started at logon does not even have. What
        // holds for the toast host is narrower: its payload and its outcome are escaped JSON, ASCII on the wire, so the
        // OEM code page its fresh console reads stdin and writes stdout through never meets a byte above 127. Its stderr
        // is its own words, and a localized Windows message in them is written in that page: the kept tail may read it
        // wrong.
        //
        // A program the door refuses, one named by anything but its full path, never started either, as one that is not there.
        var (started, refused) = Start(launch);
        if (started is not { } process)
        {
            return new LaunchResult(false, false, null, string.Empty, string.Empty, refused);
        }

        using (process)
        {
            var stdout = ReadAsync(process.StandardOutput, MaxStdout, keepEnd: false);
            var stderr = ReadAsync(process.StandardError, StderrTail, keepEnd: true);

            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timer.CancelAfter(bound);
            try
            {
                // A child that ends without reading its stdin breaks the pipe under this write. That is not a timeout:
                // what the child said is in its exit code and its stderr, which the wait below still collects.
                try
                {
                    if (launch.Stdin is { } payload)
                    {
                        await process.StandardInput.WriteAsync(payload.AsMemory(), timer.Token).ConfigureAwait(false);
                    }

                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The child stopped reading.
                }

                await process.WaitForExitAsync(timer.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    Kill(process, launch.KillsTree);
                    await Jason.Runtime.Plugins.Invocation.ChildProcess.EndedWithinAsync(process, KillGraceMs).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new LaunchResult(true, true, null, await Settled(stdout).ConfigureAwait(false), await Settled(stderr).ConfigureAwait(false));
                }
            }

            return new LaunchResult(true, false, process.ExitCode, await Settled(stdout).ConfigureAwait(false), await Settled(stderr).ConfigureAwait(false));
        }
    }

    /// <remarks>
    /// Its stdout is read line by line as it comes, keeping the last <see cref="MaxStdout"/> characters: what such a program
    /// says that matters, the action a click sent, comes at its end.
    /// </remarks>
    public async Task<WaitingLaunch> LaunchWaitingAsync(ToastLaunch launch, TimeSpan bound, Func<string, bool> shown, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(shown);
        var (started, refused) = Start(launch);
        if (started is not { } process)
        {
            return new WaitingLaunch(new LaunchResult(false, false, null, string.Empty, string.Empty, refused), null, () => { });
        }

        try
        {
            if (launch.Stdin is { } payload)
            {
                await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child stopped reading.
        }

        var said = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var kept = new StringBuilder();
        string Kept()
        {
            lock (kept)
            {
                return kept.ToString();
            }
        }

        var stdout = Task.Run(
            async () =>
            {
                while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                {
                    lock (kept)
                    {
                        kept.Append(line).Append('\n');
                        if (kept.Length > MaxStdout)
                        {
                            kept.Remove(0, kept.Length - MaxStdout);
                        }
                    }

                    if (shown(line))
                    {
                        said.TrySetResult();
                    }
                }

                return string.Empty;
            },
            CancellationToken.None);
        // Read line by line too, so what it said by the bound is there to hand back: libnotify 0.8.3 says only there that it
        // showed nothing.
        var complained = new StringBuilder();
        string Complained()
        {
            lock (complained)
            {
                return complained.ToString();
            }
        }

        var stderr = Task.Run(
            async () =>
            {
                while (await process.StandardError.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                {
                    lock (complained)
                    {
                        complained.Append(line).Append('\n');
                        if (complained.Length > StderrTail)
                        {
                            complained.Remove(0, complained.Length - StderrTail);
                        }
                    }
                }

                return string.Empty;
            },
            CancellationToken.None);
        var exited = process.WaitForExitAsync(CancellationToken.None);

        async Task<LaunchResult> EndAsync()
        {
            using (process)
            {
                await exited.ConfigureAwait(false);
                await Settled(stdout).ConfigureAwait(false);
                await Settled(stderr).ConfigureAwait(false);
                return new LaunchResult(true, false, process.ExitCode, Kept(), Complained());
            }
        }

        try
        {
            await Task.WhenAny(said.Task, exited, Task.Delay(bound, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested && !exited.IsCompleted)
            {
                Kill(process, launch.KillsTree);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return exited.IsCompleted
            ? new WaitingLaunch(await EndAsync().ConfigureAwait(false), null, () => { })
            : new WaitingLaunch(new LaunchResult(true, false, null, Kept(), Complained()), EndAsync(), () => Kill(process, launch.KillsTree));
    }

    /// <summary>The program started through the one door, or none and why not.</summary>
    private static (Process? Process, string? Refused) Start(ToastLaunch launch)
    {
        try
        {
            var start = Jason.Contracts.Discovery.ChildProcess.StartInfo(launch.Command[0], Jason.Contracts.Discovery.ChildKind.SpeaksToJason);
            foreach (var argument in launch.Command.Skip(1))
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment.Clear();
            foreach (var (name, value) in launch.Environment)
            {
                start.Environment[name] = value;
            }

            return (Jason.Contracts.Discovery.ChildProcess.Start(start) ?? throw new InvalidOperationException("The process did not start."), null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or ArgumentException)
        {
            return (null, ex.Message);
        }
    }

    private static void Kill(Process process, bool tree)
    {
        try
        {
            process.Kill(entireProcessTree: tree);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone, or not ours to end: the wait that follows says which.
        }
    }

    /// <summary>A stream read to its end, keeping its first or its last characters up to a limit.</summary>
    private static async Task<string> ReadAsync(StreamReader reader, int limit, bool keepEnd)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            kept.Append(buffer, 0, read);
            if (kept.Length > limit)
            {
                if (keepEnd)
                {
                    kept.Remove(0, kept.Length - limit);
                }
                else
                {
                    kept.Length = limit;
                }
            }
        }

        return kept.ToString();
    }

    /// <summary>What a reader had when the child ended, waiting a moment for pipes a killed tree may still hold.</summary>
    private static async Task<string> Settled(Task<string> reading) =>
        await Task.WhenAny(reading, Task.Delay(KillGraceMs)).ConfigureAwait(false) == reading
            ? await reading.ConfigureAwait(false)
            : string.Empty;
}
