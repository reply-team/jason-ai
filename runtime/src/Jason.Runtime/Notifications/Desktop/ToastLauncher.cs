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

/// <summary>Runs one toast launch, bounded.</summary>
public interface IToastLauncher
{
    Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken);
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

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var start = Jason.Contracts.Discovery.ChildProcess.StartInfo(launch.Command[0]);
        foreach (var argument in launch.Command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        // UTF-8 without a byte-order mark both ways, as the plugin host's child is spoken to: left to itself, stdin is
        // written in the parent's console code page, which a runtime started at logon does not even have. What holds
        // for the toast host is narrower: its payload and its outcome are escaped JSON, ASCII on the wire, so the OEM code
        // page its fresh console reads stdin and writes stdout through never meets a byte above 127. Its stderr is its own
        // words, and a localized Windows message in them is written in that page: the kept tail may read it wrong.
        start.StandardInputEncoding = Utf8;
        start.StandardOutputEncoding = Utf8;
        start.StandardErrorEncoding = Utf8;
        start.Environment.Clear();
        foreach (var (name, value) in launch.Environment)
        {
            start.Environment[name] = value;
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("The process did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new LaunchResult(false, false, null, string.Empty, string.Empty, ex.Message);
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
                    Kill(process);
                    await Jason.Runtime.Plugins.Invocation.ChildProcess.EndedWithinAsync(process, KillGraceMs).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new LaunchResult(true, true, null, await Settled(stdout).ConfigureAwait(false), await Settled(stderr).ConfigureAwait(false));
                }
            }

            return new LaunchResult(true, false, process.ExitCode, await Settled(stdout).ConfigureAwait(false), await Settled(stderr).ConfigureAwait(false));
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
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
