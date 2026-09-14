using System.Reflection;
using System.Runtime.InteropServices;

namespace Jason.Runtime.Hosting;

/// <summary>
/// Turns this process into a background service process before hosting starts: nothing of it stays attached to
/// the terminal that launched it. The parent already spawned it with all three standard streams redirected, so
/// no console handle was inherited; this closes the other half of the arrangement by pointing descriptors 0, 1
/// and 2 at the null device, so a stray write from any library cannot block on a pipe nobody reads. On Unix the
/// process also leaves its parent's session, because the terminal's SIGHUP would otherwise end a runtime that
/// has to outlive the shell (macOS ships no <c>setsid</c> binary, so the process does it itself).
/// </summary>
public static class ProcessDetacher
{
    private const string LibC = "libc";

    /// <summary>glibc, then Apple's C library, then the plain names, so the first one the loader knows wins.</summary>
    private static readonly string[] LibCCandidates = ["libc.so.6", "libSystem.dylib", "libc.so", LibC];

    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    /// <summary>O_RDWR, which is 2 on both Linux and macOS.</summary>
    private const int OpenReadWrite = 2;

    /// <summary>Held for the life of the process: the standard handles point into it.</summary>
    private static FileStream? _nullDevice;

    /// <summary>Held for the life of the process: disposing the registration would restore the default action.</summary>
    private static PosixSignalRegistration? _hangUp;

    public static void Detach()
    {
        // Managed writes go nowhere even before the handles move, so nothing races the reopening below.
        Console.SetIn(TextReader.Null);
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);

        if (OperatingSystem.IsWindows())
        {
            DetachOnWindows();
        }
        else
        {
            DetachOnUnix();
        }
    }

    private static void DetachOnWindows()
    {
        var nullDevice = OpenNullDevice();
        if (nullDevice == IntPtr.Zero || nullDevice == new IntPtr(-1))
        {
            // Without a null device the inherited handles stay as the parent left them: already redirected
            // pipes that nobody reads. That is survivable, and the runtime log records nothing about it
            // because logging is not configured yet.
            return;
        }

        SetStdHandle(StandardInputHandle, nullDevice);
        SetStdHandle(StandardOutputHandle, nullDevice);
        SetStdHandle(StandardErrorHandle, nullDevice);
    }

    private static IntPtr OpenNullDevice()
    {
        try
        {
            _nullDevice = new FileStream("NUL", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return _nullDevice.SafeFileHandle.DangerousGetHandle();
        }
        catch (IOException)
        {
            return CreateFileW("NUL", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateFileW("NUL", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        }
    }

    private static void DetachOnUnix()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(ProcessDetacher).Assembly, ResolveLibC);
        }
        catch (InvalidOperationException)
        {
            // A resolver is already in place for this assembly; the declarations below will use it.
        }

        try
        {
            var nullDevice = Open("/dev/null", OpenReadWrite);
            if (nullDevice >= 0)
            {
                Duplicate(nullDevice, 0);
                Duplicate(nullDevice, 1);
                Duplicate(nullDevice, 2);
                if (nullDevice > 2)
                {
                    Close(nullDevice);
                }
            }

            // -1 means this process already leads its own session, which is the state we wanted anyway.
            SetSessionId();

            _hangUp = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            // No usable C library on this platform. The managed streams above are already silenced and the
            // parent spawned this process with all three standard streams redirected, so it still runs with
            // nothing attached to a terminal; it simply stays in its parent's session and can be hung up with
            // it. Nothing is logged because this runs before the runtime has a logger to write to.
        }
    }

    /// <summary>
    /// The declarations below name the C library the way its manual pages do. That plain name is not what the
    /// loader looks for on a glibc system, where the library the dynamic loader knows is <c>libc.so.6</c> and
    /// the bare <c>libc.so</c> is a linker script it cannot open, so the candidates are tried in order.
    /// </summary>
    private static IntPtr ResolveLibC(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibC, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in LibCCandidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int standardHandle, IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport(LibC, EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport(LibC, EntryPoint = "dup2", SetLastError = true)]
    private static extern int Duplicate(int oldFileDescriptor, int newFileDescriptor);

    [DllImport(LibC, EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fileDescriptor);

    [DllImport(LibC, EntryPoint = "setsid", SetLastError = true)]
    private static extern int SetSessionId();
}
