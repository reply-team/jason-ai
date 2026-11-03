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
/// list and no shell. On Windows and Linux the terminal is never waited for. On macOS the script Terminal runs is written
/// first, into a folder of its own under the temporary folder that only this account can read, and it removes itself and
/// that folder when it runs; <c>/usr/bin/open</c> hands the script to Terminal and exits, so it is waited for, for ten
/// seconds at most, and an exit other than zero is a start that failed: the folder is removed and the problem returned.
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

            var start = ChildProcess.StartInfo(argv[0]);
            foreach (var argument in argv.Skip(1))
            {
                start.ArgumentList.Add(argument);
            }

            start.WorkingDirectory = launch.WorkingDirectory;
            using var process = ChildProcess.Start(start);
            if (process is null)
            {
                problem = "no process was started";
            }
            else if (launch.Script is not null && process.WaitForExit(OpenerWait) && process.ExitCode != 0)
            {
                problem = $"{Path.GetFileName(argv[0])} exited with code {process.ExitCode}";
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
