using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Jason.Contracts.Files;

/// <summary>
/// Tells a lock from a permission refusal, for a move of a directory that Windows answered with an access-denied. The answer
/// alone says nothing, so the directory is asked for what a rename of it needs, and the files under it for a hold.
/// </summary>
/// <remarks>
/// <para>
/// A rename of a directory needs DELETE on it, or delete-child on its parent, and nothing in the files under it: a refusal a
/// file gives says nothing about the directory's. So the directory itself is opened for DELETE, sharing everything, with
/// backup semantics, which is how a directory is opened, and closed at once. An access-denied there is the rename's own
/// refusal: <see cref="HeldFileReading.Refused"/>. A sharing violation there is a handle another program has on the
/// directory: <see cref="HeldFileReading.Held"/>.
/// </para>
/// <para>
/// Failing that, each file under it is opened for reading and writing, sharing with no one, and closed at once. A sharing or a
/// lock violation on any file is a <see cref="HeldFileReading.Held"/>. Every other outcome, an access-denied, a file or a
/// directory that is gone by the time the walk reaches it, a directory that cannot be listed, is ignored: a directory this
/// account cannot list is walked past, so a hold beyond it is still found. What is left is
/// <see cref="HeldFileReading.Unknown"/>. The reader changes nothing.
/// </para>
/// </remarks>
public static class HeldFileReader
{
    /// <summary>ERROR_SHARING_VIOLATION, as .NET carries it in an exception's HResult.</summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    /// <summary>ERROR_LOCK_VIOLATION, as .NET carries it in an exception's HResult.</summary>
    private const int LockViolation = unchecked((int)0x80070021);

    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;

    private const uint Delete = 0x00010000;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>Reads the directory <paramref name="directory"/> and the files under it.</summary>
    public static HeldFileReading Read(string directory) => Read(directory, OpenAndClose);

    /// <summary>The same, with the opening of a file given, so a test can make any file refuse in any way.</summary>
    internal static HeldFileReading Read(string directory, Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(open);

        if (OperatingSystem.IsWindows() && ReadDirectory(directory) is { } reading)
        {
            return reading;
        }

        try
        {
            var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(directory, "*", walk))
            {
                if (IsHeld(file, open))
                {
                    return HeldFileReading.Held;
                }
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A directory that is gone, or any other failure of the walk, is no evidence of a hold.
        }

        return HeldFileReading.Unknown;
    }

    /// <summary>What opening the directory for its deletion says: refused, held, or nothing.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static HeldFileReading? ReadDirectory(string directory)
    {
        using var handle = CreateFileW(LongPath(Path.GetFullPath(directory)), Delete, ShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (!handle.IsInvalid)
        {
            return null;
        }

        return Marshal.GetLastWin32Error() switch
        {
            ErrorAccessDenied => HeldFileReading.Refused,
            ErrorSharingViolation => HeldFileReading.Held,
            _ => null,
        };
    }

    /// <summary>A path of a drive written so that the call takes it however long it is.</summary>
    private static string LongPath(string fullPath) =>
        fullPath.Length >= 248 && fullPath.Length > 2 && fullPath[1] == ':' ? @"\\?\" + fullPath : fullPath;

    private static bool IsHeld(string file, Action<string> open)
    {
        try
        {
            open(file);
            return false;
        }
        catch (IOException refusal) when (refusal.HResult is SharingViolation or LockViolation)
        {
            return true;
        }
        catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void OpenAndClose(string file) =>
        new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
