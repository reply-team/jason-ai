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
/// <c>NoChildGetsAWindowTests</c> reads the compiled product and holds it to this door: nothing else makes a start
/// info or starts a process, nothing sets the window, the shell, a redirect or an encoding, nothing starts a process
/// from a bare file name, and nothing reaches into a process for the start info it was given.
/// </para>
/// </remarks>
public static class ChildProcess
{
    /// <summary>The one encoding a child that speaks to Jason is read and written in.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>A start for <paramref name="fileName"/> with no window, no shell and no stream redirected.</summary>
    public static ProcessStartInfo StartInfo(string fileName) =>
        new(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

    /// <summary>
    /// A start for <paramref name="fileName"/> with no window and no shell, and the standard streams of a child of
    /// that <paramref name="kind"/>; the caller adds the rest.
    /// </summary>
    public static ProcessStartInfo StartInfo(string fileName, ChildKind kind)
    {
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
