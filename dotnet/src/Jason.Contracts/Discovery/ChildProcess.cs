using System.Diagnostics;
using System.Text;

namespace Jason.Contracts.Discovery;

/// <summary>
/// The one place this product makes a <see cref="ProcessStartInfo"/>: every child it starts has no window of its
/// own, does not go through the shell, and has the standard streams its kind calls for.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else stands between a console child and a window on somebody's desktop. The runtime a logon task
/// starts has no console — the executable declares <c>consoleAllocationPolicy</c> <c>detached</c> — and a
/// console program started by a process with no console gets one of its own, visible, unless it is told not to.
/// Measured on Windows 11 24H2 from a logon task: the child started without the flag opened a terminal window for
/// its whole life, and the one started with it opened nothing. Under 0.1.0's task the runtime ran in a session
/// with no desktop, where a forgotten flag showed nothing; in the person's own session it would.
/// </para>
/// <para>
/// <c>UseShellExecute</c> is false because every caller redirects a standard stream or hands over an argument
/// list, and neither survives the shell.
/// </para>
/// <para>
/// The streams are chosen here too, by <see cref="ChildKind"/>. Left to itself, .NET reads and writes a redirected
/// stream on Windows in the console's code page, and a child started without a window has a console of its own on
/// the system's legacy page: a name sent to a program that reads UTF-8 arrived with its letters outside ASCII
/// replaced and an emoji lost, and an agent's transcript came back with replacement characters. A child that speaks
/// to Jason is therefore spoken to in UTF-8 without a byte-order mark — a preamble is three bytes a node program's
/// <c>JSON.parse</c> refuses — and a system tool is read in the console's page, which is what it writes.
/// </para>
/// <para>
/// A start is made here too, under <see cref="StartErrorMode"/>: on Windows a file named like a program that is no
/// program image fails with its error code and puts no box on the desktop.
/// </para>
/// <para>
/// On Windows a child that is Windows PowerShell, <c>powershell.exe</c> by its file name, is started without the
/// <c>PSModulePath</c> this process inherited (<see cref="ModulePathVariable"/>). A PowerShell 7 session puts its own module
/// folders first in that variable, and every program it starts inherits it, this one included; Windows PowerShell 5.1
/// started with that value finds some of its own commands - <c>Get-ExecutionPolicy</c>, for one - in PowerShell 7's modules
/// and cannot load them. Started without the variable it builds its own path, from the user's and the machine's settings
/// and its own folder; an empty value it would keep empty, so the variable is removed rather than emptied. PowerShell 7
/// itself, <c>pwsh</c>, and every other program keep the variable as it is; elsewhere nothing is removed, since a
/// <c>powershell</c> there is PowerShell 7.
/// </para>
/// <para>
/// <c>NoChildGetsAWindowTests</c> reads the compiled product and holds it to this door: nothing else makes a start
/// info or starts a process, nothing sets the window, the shell, a redirect or an encoding, nothing starts a process
/// from a bare file name, and nothing reaches into a process for the start info it was given.
/// </para>
/// </remarks>
public static class ChildProcess
{
    /// <summary>The one encoding a child that speaks to Jason is read and written in.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The variable Windows PowerShell finds its modules by, which no Windows PowerShell child of Jason's inherits.</summary>
    public const string ModulePathVariable = "PSModulePath";

    /// <summary>
    /// A start for <paramref name="fileName"/> with no window, no shell and no stream redirected; on Windows, for Windows
    /// PowerShell, without the inherited <see cref="ModulePathVariable"/>.
    /// </summary>
    public static ProcessStartInfo StartInfo(string fileName)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows() && IsWindowsPowerShell(fileName))
        {
            start.Environment.Remove(ModulePathVariable);
        }

        return start;
    }

    /// <summary>
    /// Whether <paramref name="program"/> is Windows PowerShell: <c>powershell.exe</c>, or <c>powershell</c>, by its file name
    /// wherever it stands, whatever its case. PowerShell 7 is <c>pwsh</c> and is not.
    /// </summary>
    public static bool IsWindowsPowerShell(string? program)
    {
        var name = Path.GetFileName(program);
        return string.Equals(name, "powershell.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A start for <paramref name="fileName"/> with no window and no shell, and the standard streams of a child of
    /// that <paramref name="kind"/>; the caller adds the rest. A system tool is started by its full path, and a name
    /// that is not one is refused here, before anything is started (<see cref="IsFullPath"/>).
    /// </summary>
    /// <exception cref="ArgumentException">A system-tool kind with a <paramref name="fileName"/> that is not a full path.</exception>
    public static ProcessStartInfo StartInfo(string fileName, ChildKind kind)
    {
        if (kind is ChildKind.SystemTool or ChildKind.SystemToolWithInput && !IsFullPath(fileName))
        {
            throw new ArgumentException($"A system tool is started by its full path, and '{fileName}' is not one.", nameof(fileName));
        }

        var start = StartInfo(fileName);
        switch (kind)
        {
            case ChildKind.SpeaksToJason:
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardInputEncoding = Utf8;
                start.StandardOutputEncoding = Utf8;
                start.StandardErrorEncoding = Utf8;
                break;

            case ChildKind.SystemTool:
            case ChildKind.SystemToolWithInput:
                start.RedirectStandardInput = kind == ChildKind.SystemToolWithInput;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "The door knows a child that speaks to Jason and a system tool.");
        }

        return start;
    }

    /// <summary>
    /// Whether <paramref name="fileName"/> names a program by its full path, as <see cref="Path.IsPathFullyQualified(string)"/>
    /// answers: from the root on macOS and Linux, with its drive or share on Windows. A bare name, a relative path, and on
    /// Windows a path from the top of the current drive (<c>\Windows\System32\tool.exe</c>) or relative to a drive's current
    /// folder (<c>C:tool.exe</c>) are not.
    /// </summary>
    public static bool IsFullPath(string? fileName) => !string.IsNullOrEmpty(fileName) && Path.IsPathFullyQualified(fileName);

    /// <summary>
    /// Starts a child from a start this class made, under <see cref="StartErrorMode"/>: every child is started here,
    /// as every start is made here.
    /// </summary>
    public static Process? Start(ProcessStartInfo start)
    {
        using (StartErrorMode.Enter())
        {
            return Process.Start(start);
        }
    }

    /// <summary>
    /// Starts <paramref name="process"/>, whose start this class made, for a caller that holds the process before it
    /// starts.
    /// </summary>
    public static bool Start(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        using (StartErrorMode.Enter())
        {
            return process.Start();
        }
    }
}
