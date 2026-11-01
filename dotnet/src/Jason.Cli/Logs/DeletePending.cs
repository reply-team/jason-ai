using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Jason.Cli.Logs;

/// <summary>
/// Whether a file's name is only what Windows keeps of a file whose delete is still pending: one deleted the older way —
/// marked to go when the last handle to it closes — while another handle still holds it. The name stays in its directory
/// until that handle is closed, and every open of it is refused meanwhile with the error a file nobody may read gets, so
/// the two cannot be told apart by that error, nor by anything the listing or the file's attributes say. The system's own
/// answer to the open can: it says the delete is pending.
/// </summary>
/// <remarks>
/// A file deleted the way the runtime's own log retention deletes one, while a reader shares deletion, leaves no name
/// behind on a current Windows: it is gone at once, and the listing does not show it. This answers for the other case.
/// Asked directly of the call that opens a file by its system name, which answers with that status rather than leaving
/// it to be read back afterwards. Elsewhere a deleted file's name goes at once, so the answer there is always no.
/// </remarks>
internal static class DeletePending
{
    /// <summary>The status a file's open is answered with while it waits for its last handle to close.</summary>
    private const int StatusDeletePending = unchecked((int)0xC0000056);

    /// <summary>The right to read a file's attributes, which is all the open asks for.</summary>
    private const uint FileReadAttributes = 0x0080;

    /// <summary>Reading, writing and deleting shared, so the question shuts nobody else out.</summary>
    private const uint ShareAll = 0x0007;

    /// <summary>Names compared without regard to case, as Windows compares them.</summary>
    private const uint CaseInsensitive = 0x0040;

    /// <summary>True when the file at <paramref name="path"/> is deleted and only its name is left until it is let go.</summary>
    public static bool Is(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var name = SystemName(Path.GetFullPath(path));
        var text = Marshal.StringToHGlobalUni(name);
        var unicode = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(
                new UnicodeString { Length = (ushort)(name.Length * 2), MaximumLength = (ushort)((name.Length * 2) + 2), Buffer = text },
                unicode,
                fDeleteOld: false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                ObjectName = unicode,
                Attributes = CaseInsensitive,
            };

            var status = NtOpenFile(out var handle, FileReadAttributes, ref attributes, out _, ShareAll, 0);
            handle.Dispose();
            return status == StatusDeletePending;
        }
        finally
        {
            Marshal.FreeHGlobal(unicode);
            Marshal.FreeHGlobal(text);
        }
    }

    /// <summary>The path as the system names it: <c>\??\C:\…</c>, and <c>\??\UNC\server\share\…</c> for a share.</summary>
    private static string SystemName(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return @"\??\" + fullPath[4..];
        }

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? @"\??\UNC\" + fullPath[2..] : @"\??\" + fullPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtOpenFile(
        out SafeFileHandle handle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock,
        uint shareAccess,
        uint openOptions);
}
