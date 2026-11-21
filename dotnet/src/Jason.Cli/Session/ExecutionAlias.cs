using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Jason.Cli.Session;

/// <summary>
/// The program an App Execution Alias stands for, read from the alias's own reparse point and never by starting it. Windows
/// keeps such an alias in the account's WindowsApps folder (Windows Terminal's <c>wt.exe</c> among them) as a file whose
/// reparse data, of the tag <c>IO_REPARSE_TAG_APPEXECLINK</c>, holds a version and then text strings ending in
/// <c>\0</c>: the package's family name, the application's id, and the full path of the program in the package's folder,
/// whose name carries the package's version (<c>Microsoft.WindowsTerminal_1.24.12741.0_x64__8wekyb3d8bbwe</c>).
/// .NET's own link reading does not follow this tag, so it is read here.
/// </summary>
public static class ExecutionAlias
{
    /// <summary>The reparse tag of an App Execution Alias.</summary>
    public const uint Tag = 0x8000001B;

    /// <summary>
    /// The program path an alias's reparse data names, or null where the data is not an App Execution Alias's of the version
    /// read here (3), or is cut short.
    /// </summary>
    public static string? TargetOf(ReadOnlySpan<byte> data)
    {
        // The reparse header: the tag, the length of what follows the eight bytes of the header, and two reserved bytes;
        // then the alias's own version, then its strings.
        if (data.Length < 12 || BitConverter.ToUInt32(data[..4]) != Tag || BitConverter.ToUInt32(data[8..12]) != 3)
        {
            return null;
        }

        var length = BitConverter.ToUInt16(data[4..6]);
        if (length < 4 || 8 + length > data.Length || (length - 4) % 2 != 0)
        {
            return null;
        }

        var strings = Encoding.Unicode.GetString(data[12..(8 + length)]).Split('\0');
        return strings.Length >= 3 && strings[2] is { Length: > 0 } target && Path.IsPathFullyQualified(target) ? target : null;
    }

    /// <summary>
    /// The program the alias at <paramref name="path"/> stands for, or null where it is no such alias or cannot be read: the
    /// file is opened for no access at all, as its reparse point and not what it names, so nothing is started.
    /// </summary>
    public static string? Target(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var handle = CreateFileW(path, 0, ShareAll, IntPtr.Zero, OpenExisting, OpenReparsePoint | BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new byte[MaximumReparseData];
        return DeviceIoControl(handle, GetReparsePoint, IntPtr.Zero, 0, buffer, buffer.Length, out var returned, IntPtr.Zero)
            ? TargetOf(buffer.AsSpan(0, returned))
            : null;
    }

    private const uint ShareAll = 0x1 | 0x2 | 0x4;

    private const uint OpenExisting = 3;

    private const uint OpenReparsePoint = 0x00200000;

    private const uint BackupSemantics = 0x02000000;

    private const uint GetReparsePoint = 0x000900A8;

    private const int MaximumReparseData = 16 * 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
