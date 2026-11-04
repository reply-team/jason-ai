using System.ComponentModel;
using Jason.Cli.Files;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Session;

/// <summary>
/// How a new terminal window is started on this machine — the seventh seam whose default is the refusal, for the reason
/// <see cref="Process.IUrlOpener"/> gives: a verb that started a terminal for itself would open a window, and the
/// person's own agent host in it, on whoever runs the suite. It also says which operating system the terminal is
/// composed for and where that system keeps its own programs, so a test names both and every row runs everywhere.
/// </summary>
public interface ITerminalStarter
{
    /// <summary>The operating system the launch is composed for.</summary>
    TerminalPlatform Platform { get; }

    /// <summary>The system directory, where Windows keeps the console host; null elsewhere.</summary>
    string? SystemDirectory { get; }

    /// <summary>True when the terminal process started; false with a problem worded to follow "did not start".</summary>
    bool Start(TerminalLaunch launch, out string? problem);
}

/// <summary>
/// The terminal of this machine, started as every child of Jason's is: through <see cref="ChildProcess"/>, with an argument
/// list and no shell, by the start <see cref="TerminalStartInfo"/> composes. On Linux and macOS the terminal's three
/// standard streams are pipes of its own, whose ends this process closes as soon as it has started (on macOS all but the
/// opener's standard error, see <see cref="MacOpener"/>), so a caller that captures the CLI's output is not kept waiting
/// until the person closes the window. On Windows and Linux the terminal is never waited for. On macOS the script
/// Terminal runs is written first, into a folder of its own under the temporary folder that only this account can read,
/// and it removes itself and that folder when it runs; <c>/usr/bin/open</c> hands the script to Terminal and exits, so it
/// is waited for, for ten seconds at most, and an exit other than zero is a start that failed: the folder is removed and
/// the problem returned, with the first line the opener wrote to its standard error.
/// </summary>
/// <remarks>
/// No test runs this class, since every run would open a window on whoever runs the suite; the suite holds that by a
/// guard, and what it starts is composed by <see cref="TerminalPlan"/>, which the tests do run.
/// </remarks>
public sealed class PlatformTerminalStarter : ITerminalStarter
{
    public TerminalPlatform Platform { get; } = OperatingSystem.IsWindows()
        ? TerminalPlatform.Windows
        : OperatingSystem.IsMacOS() ? TerminalPlatform.MacOs : TerminalPlatform.Linux;

    public string? SystemDirectory { get; } = OperatingSystem.IsWindows() ? Environment.SystemDirectory : null;

    /// <summary>How long macOS's opener is waited for: it exits once Terminal has the script, and a hang is not a refusal.</summary>
    private static readonly TimeSpan OpenerWait = TimeSpan.FromSeconds(10);

    public bool Start(TerminalLaunch launch, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(launch);
        problem = null;
        var argv = launch.Argv.ToList();
        string? folder = null;
        try
        {
            if (launch.Script is { } script && launch.ScriptName is { } name)
            {
                folder = WriteScript(script, name, out var path);
                argv[^1] = path;
            }

            var start = TerminalStartInfo.For(Platform, argv, launch.WorkingDirectory);
            using var process = ChildProcess.Start(start);
            if (process is null)
            {
                problem = "no process was started";
            }
            else if (launch.Script is not null)
            {
                problem = MacOpener.Problem(process, Path.GetFileName(argv[0]), OpenerWait);
            }
            else if (start.RedirectStandardOutput)
            {
                // Nothing is read: the window keeps its own console, and the pipes only keep the CLI's from it.
                process.StandardInput.Close();
                process.StandardOutput.Close();
                process.StandardError.Close();
            }
        }
        catch (Exception refused) when (refused is Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            problem = refused.Message;
        }

        if (problem is not null && folder is not null)
        {
            TryDelete(folder);
        }

        return problem is null;
    }

    /// <summary>The script in a fresh folder: the folder is made for this account alone, and the script may only be run by it.</summary>
    private static string WriteScript(string script, string name, out string path)
    {
        var folder = Directory.CreateTempSubdirectory("jason-session-").FullName;
        path = Path.Combine(folder, name);
        File.WriteAllText(path, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return folder;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            DirectoryTrees.Delete(folder);
        }
        catch (Exception kept) when (kept is IOException or UnauthorizedAccessException)
        {
            // A script that cannot be removed was never run; the temporary folder is the system's to clear.
        }
    }
}

/// <summary>
/// macOS's <c>/usr/bin/open</c>, once started on the three pipes <see cref="TerminalStartInfo"/> gives it: it hands the
/// script to Terminal and exits, so it is waited for, and an exit other than zero within the wait is a start that failed.
/// </summary>
public static class MacOpener
{
    /// <summary>How long the error line is waited for after the opener exits, when its pipe is at its end.</summary>
    private static readonly TimeSpan LineWait = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The problem, worded to follow "did not start", or null where the opener exited with zero or is still running. An
    /// exit other than zero carries the first line the opener wrote to its standard error, where it says why.
    /// </summary>
    public static string? Problem(System.Diagnostics.Process process, string program, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(process);
        // One thing is read: the first line of the opener's standard error, asked for before the wait so that line never
        // waits on a reader, and taken only for a refusal. Its input and output are closed unread.
        process.StandardInput.Close();
        process.StandardOutput.Close();
        var firstLine = process.StandardError.ReadLineAsync();
        if (!process.WaitForExit(wait) || process.ExitCode == 0)
        {
            return null;
        }

        var problem = $"{program} exited with code {process.ExitCode}";
        return FirstLine(firstLine) is { Length: > 0 } line ? $"{problem}: {line}" : problem;
    }

    private static string? FirstLine(Task<string?> read)
    {
        try
        {
            return read.Wait(LineWait) ? read.Result?.Trim() : null;
        }
        catch (AggregateException)
        {
            return null;
        }
    }
}

/// <summary>
/// The start a terminal is given, made at Jason's one door. On Windows no stream is redirected: the console host opens a
/// console of its own, and what it does with redirected handles has not been read. On Linux and macOS the door's kind
/// with all three streams redirected, so the terminal holds none of the CLI's own: a caller that captures the CLI's
/// output (an agent's shell tool, a script) reads its end of file when the CLI exits, not when the window closes.
/// </summary>
public static class TerminalStartInfo
{
    public static System.Diagnostics.ProcessStartInfo For(TerminalPlatform platform, IReadOnlyList<string> argv, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        if (argv.Count == 0)
        {
            throw new ArgumentException("A terminal cannot be started from nothing.", nameof(argv));
        }

        var start = platform == TerminalPlatform.Windows
            ? ChildProcess.StartInfo(argv[0])
            : ChildProcess.StartInfo(argv[0], ChildKind.SystemToolWithInput);
        foreach (var argument in argv.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        start.WorkingDirectory = workingDirectory;
        return start;
    }
}
