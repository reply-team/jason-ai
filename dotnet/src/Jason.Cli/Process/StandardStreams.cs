using System.Runtime.InteropServices;

namespace Jason.Cli.Process;

/// <summary>The CLI's own standard streams, and the children that must not inherit them.</summary>
public static class StandardStreams
{
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private const int HandleFlagInherit = 0x00000001;

    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>
    /// A new process on Windows is handed every handle its parent left inheritable, whether or not it is told
    /// to use it. When the CLI itself runs under a redirect — a shell capturing its output, a build step, a
    /// test — its own standard streams are such handles, so a process meant to outlive the CLI (the runtime, or
    /// the cleanup an uninstall leaves behind it) would hold them open for as long as it runs, and the caller
    /// would wait for an end of file that never comes. The CLI has no child that should ever speak through its
    /// streams, so they stop being inheritable before one starts.
    /// Unix needs none of this: the child's descriptors 0, 1 and 2 are replaced outright and everything else
    /// the runtime might have inherited is closed on exec.
    /// </summary>
    public static void KeepOutOfChildren()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var standardStream in new[] { StandardInputHandle, StandardOutputHandle, StandardErrorHandle })
        {
            var handle = GetStdHandle(standardStream);
            if (handle != IntPtr.Zero && handle != InvalidHandle)
            {
                // A failure here costs the caller nothing but the wait this avoids; there is nothing to report
                // it to, and the launch itself is unaffected.
                SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, int mask, int flags);
}
