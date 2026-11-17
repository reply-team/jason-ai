using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Jason.Cli.Uninstall;

/// <summary>
/// Windows: what an uninstall reads of a process beyond its name, which <see cref="System.Diagnostics.Process"/> does not
/// answer: the process that started it, and the file it runs by the least right there is to ask. Reads only; nothing here
/// opens a process for more than <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsProcessTree
{
    private const uint SnapProcess = 0x00000002;

    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>
    /// The process that started <paramref name="pid"/>, read from a snapshot of the system's processes, and only where it
    /// started no later than <paramref name="pid"/> did: Windows keeps the creator's number, and a process that took that
    /// number after the creator ended started after its supposed child. Null where either is gone or cannot be read.
    /// </summary>
    public static int? ParentOf(int pid)
    {
        int? parent = null;
        using (var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0))
        {
            if (snapshot.IsInvalid)
            {
                return null;
            }

            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                if (entry.ProcessId == (uint)pid)
                {
                    parent = unchecked((int)entry.ParentProcessId);
                    break;
                }
            }
        }

        if (parent is not { } id || id <= 0 || id == pid)
        {
            return null;
        }

        return StartedAt(id) is { } parentStarted && StartedAt(pid) is { } started && parentStarted <= started ? id : null;
    }

    /// <summary>The full path of the file process <paramref name="pid"/> runs, or null where it cannot be read.</summary>
    public static string? ImageOf(int pid)
    {
        using var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[32768];
        var size = buffer.Length;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    private static DateTime? StartedAt(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception unread) when (unread is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary><c>PROCESSENTRY32W</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] name, ref int size);
}
