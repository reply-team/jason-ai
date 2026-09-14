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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int standardHandle, IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    // The libc declarations keep the names of the manual pages they stand for.
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static extern int Duplicate(int oldFileDescriptor, int newFileDescriptor);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fileDescriptor);

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int SetSessionId();
}
