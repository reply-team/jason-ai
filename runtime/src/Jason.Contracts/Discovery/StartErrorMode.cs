using System.Runtime.InteropServices;

namespace Jason.Contracts.Discovery;

/// <summary>
/// The Windows error mode a child is started under: the loader's error boxes off, on the starting thread, for the
/// length of the start. Elsewhere it does nothing.
/// </summary>
/// <remarks>
/// <para>
/// A file named like a program that is no program image — a broken download, a vendor CLI cut short, a test's
/// stand-in — makes the parent's own <c>CreateProcess</c> put a modal box on the desktop, "Unsupported 16-Bit
/// Application", unless the starting thread's error mode carries <c>SEM_FAILCRITICALERRORS</c>. Measured on
/// Windows 11 24H2: with the process and the thread at mode 0 the box appeared, owned by the starting process; with
/// that one flag on the starting thread no box appeared. Either way the start failed with the same error, 216, and
/// the same message, so a caller reports exactly what it reported before. <c>SEM_NOOPENFILEERRORBOX</c>, which keeps
/// the box <c>OpenFile</c> shows for a missing file away as well, is set beside it. A runtime a logon task starts
/// runs where nobody may be at the desktop to close a box, and until somebody does, nothing else in it starts: the
/// box belongs to the starting thread and <c>CreateProcess</c> waits on it, and .NET calls <c>CreateProcess</c>
/// under one lock per process, so every other start waits behind that one (measured: a second thread's start of a
/// real program waited as long as the box stayed up).
/// </para>
/// <para>
/// The thread's mode rather than the process's, for two measured reasons. The process's mode is read by every
/// thread at once, and the runtime starts children from several threads while others load libraries and read files
/// of their own, so a process-wide mode set and restored around one start would also change, and race with, work
/// that is not a start. And a child inherits its parent's process mode, never the thread's: with the thread's, a
/// child that does start is started exactly as before, where the process's would hand the flags on to every child.
/// </para>
/// </remarks>
internal readonly struct StartErrorMode : IDisposable
{
    /// <summary><c>SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX</c>, added to the starting thread's mode.</summary>
    public const uint Quiet = SemFailCriticalErrors | SemNoOpenFileErrorBox;

    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoOpenFileErrorBox = 0x8000;

    /// <summary>The thread's mode before <see cref="Enter"/> changed it, or nothing when it did not.</summary>
    private readonly uint? _previous;

    private StartErrorMode(uint previous) => _previous = previous;

    /// <summary>Turns the loader's error boxes off on this thread, until the answer is disposed.</summary>
    public static StartErrorMode Enter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return default;
        }

        // A mode Windows refuses to set leaves the start to go ahead as it did before this existed.
        var previous = GetThreadErrorMode();
        return SetThreadErrorMode(previous | Quiet, out _) ? new StartErrorMode(previous) : default;
    }

    /// <summary>Gives the thread back the mode it had.</summary>
    public void Dispose()
    {
        if (_previous is { } previous)
        {
            SetThreadErrorMode(previous, out _);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);
}
