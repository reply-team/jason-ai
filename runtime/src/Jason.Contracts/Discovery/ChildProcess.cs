using System.Diagnostics;

namespace Jason.Contracts.Discovery;

/// <summary>
/// The one place this product makes a <see cref="ProcessStartInfo"/>: every child it starts has no window of its
/// own and does not go through the shell.
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
/// list, and neither survives the shell. <c>NoChildGetsAWindowTests</c> reads the compiled product and holds it to
/// this door: nothing else makes a start info, nothing sets either property, nothing starts a process from a
/// bare file name, and nothing reaches into a process for the start info it was given.
/// </para>
/// </remarks>
public static class ChildProcess
{
    /// <summary>A start for <paramref name="fileName"/> with no window and no shell; the caller adds the rest.</summary>
    public static ProcessStartInfo StartInfo(string fileName) =>
        new(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
}
