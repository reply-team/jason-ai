using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Jason.Contracts.Files;
using Microsoft.Win32.SafeHandles;

namespace Jason.Contracts.Tests.Files;

/// <summary>
/// A directory a move was refused for, read for a hold or a permission. The directory is asked first, for what a rename of it
/// needs: refused it is a permission, and a handle another program has on it is a hold. The files under it are then looked
/// at for a hold only: a sharing or a lock violation. The opening of a file is made up where a row needs a refusal no
/// platform produces on demand, and real where Windows can be made to give it.
/// </summary>
public sealed class HeldFileReaderTests : IDisposable
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int AccessDenied = unchecked((int)0x80070005);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("jason-held-reader-");

    public void Dispose() => _root.Delete(recursive: true);

    private string File(string relative)
    {
        var path = Path.Combine(_root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "made up");
        return path;
    }

    /// <summary>An opening that refuses the named files with the given refusals, and lets every other file open.</summary>
    private static Action<string> Refusing(params (string File, Func<Exception> Refusal)[] refusals) =>
        file =>
        {
            foreach (var (name, refusal) in refusals)
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.Ordinal))
                {
                    throw refusal();
                }
            }
        };

    private HeldFileReading Read(Action<string> open) => HeldFileReader.Read(_root.FullName, open);

    [Fact]
    public void A_directory_whose_files_all_open_says_nothing()
    {
        File("a.txt");
        File(Path.Combine("sub", "b.txt"));

        Assert.Equal(HeldFileReading.Unknown, Read(Refusing()));
    }

    [Fact]
    public void A_directory_with_no_files_says_nothing()
    {
        Assert.Equal(HeldFileReading.Unknown, Read(Refusing()));
    }

    [Theory]
    [InlineData(SharingViolation)]
    [InlineData(LockViolation)]
    public void A_sharing_or_a_lock_violation_on_a_file_in_a_directory_below_is_a_hold(int hold)
    {
        File("a.txt");
        File(Path.Combine("sub", "deeper", "held.txt"));

        Assert.Equal(HeldFileReading.Held, Read(Refusing(("held.txt", () => new IOException("held", hold)))));
    }

    /// <summary>A file's own refusal does not refuse the rename of the directory it is in, so it is no evidence of a permission.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_access_denied_on_a_file_says_nothing(bool asIOException)
    {
        File("a.txt");
        File("refused.txt");

        var reading = Read(Refusing(("refused.txt", () => asIOException ? new IOException("denied", AccessDenied) : new UnauthorizedAccessException("denied"))));

        Assert.Equal(HeldFileReading.Unknown, reading);
    }

    /// <summary>A hold is what the wait is for, so a file held is found whether a file refused comes before it or after.</summary>
    [Theory]
    [InlineData("a-held.txt", "b-refused.txt")]
    [InlineData("b-held.txt", "a-refused.txt")]
    public void A_file_held_is_found_beside_a_file_refused_whichever_comes_first(string held, string refused)
    {
        File(held);
        File(refused);

        var reading = Read(Refusing(
            (held, () => new IOException("held", SharingViolation)),
            (refused, () => new UnauthorizedAccessException("denied"))));

        Assert.Equal(HeldFileReading.Held, reading);
    }

    /// <summary>A file that is gone by the time it is opened is not held, however the platform words it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_file_that_disappears_during_the_walk_says_nothing(bool refusedAsAccessDenied)
    {
        File("a.txt");
        File("vanishes.txt");

        var reading = Read(file =>
        {
            if (Path.GetFileName(file) == "vanishes.txt")
            {
                System.IO.File.Delete(file);
                throw refusedAsAccessDenied ? new UnauthorizedAccessException("denied") : new FileNotFoundException("gone", file);
            }
        });

        Assert.Equal(HeldFileReading.Unknown, reading);
    }

    /// <summary>Any other failure to open a file is not evidence of a hold.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(unchecked((int)0x80070070))]
    public void Any_other_failure_to_open_a_file_says_nothing(int hresult)
    {
        File("odd.txt");

        Assert.Equal(HeldFileReading.Unknown, Read(Refusing(("odd.txt", () => new IOException("other", hresult)))));
    }

    [Fact]
    public void A_directory_that_is_not_there_says_nothing()
    {
        Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(Path.Combine(_root.FullName, "missing"), _ => { }));
    }

    /// <summary>
    /// Whatever the holder lets others do with the file, a program with it open stops our open that shares with no one:
    /// a scanner that shares everything holds a file as much as one that shares only reading.
    /// </summary>
    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.ReadWrite | FileShare.Delete)]
    public void A_file_another_program_holds_open_is_a_hold(FileShare holderShares)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open file does not stop a rename, and the reader is asked only on Windows.");
        var held = File("held.txt");
        File("a.txt");

        using (new FileStream(held, FileMode.Open, FileAccess.Read, holderShares))
        {
            Assert.Equal(HeldFileReading.Held, HeldFileReader.Read(_root.FullName));
        }

        Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(_root.FullName));
    }

    /// <summary>
    /// A file a program deleted while another handle still held it keeps its name, and every open of it is answered with an
    /// access-denied. It is not a permission either: it is gone once that handle closes, which is what the wait is for.
    /// </summary>
    [Fact]
    public void A_file_deleted_while_another_program_holds_it_says_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows keeps the name of a file deleted while it is held.");
        var path = File("going.txt");
        File("a.txt");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose).Dispose();

            Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(_root.FullName));
        }
    }

    /// <summary>The write deny on a file is a refusal the file's own open gets, and no rename of its directory gets it.</summary>
    [Fact]
    public void A_file_this_account_may_not_write_says_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny on the file is Windows' own way to refuse an open the account could otherwise make.");
        File("a.txt");
        var denied = File("denied.txt");

        var undo = OperatingSystem.IsWindows() ? DenyWriting(denied) : throw new PlatformNotSupportedException();
        try
        {
            Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(_root.FullName));
        }
        finally
        {
            undo();
        }
    }

    /// <summary>What a rename of the directory needs is refused: that is the permission, and the one thing that reads as one.</summary>
    [Fact]
    public void A_directory_this_account_may_not_delete_is_a_permission_refusal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny of DELETE is Windows' own way to refuse a rename the account could otherwise make.");
        var package = Directory.CreateDirectory(Path.Combine(_root.FullName, "package"));
        File(Path.Combine("package", "a.txt"));

        var undo = OperatingSystem.IsWindows() ? DenyDelete(package) : throw new PlatformNotSupportedException();
        try
        {
            Assert.Equal(HeldFileReading.Refused, HeldFileReader.Read(package.FullName));
        }
        finally
        {
            undo();
        }

        Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(package.FullName));
    }

    /// <summary>A handle on the directory itself, one that does not share delete, stops its rename and is invisible to its files.</summary>
    [Fact]
    public void A_handle_another_program_has_on_the_directory_is_a_hold()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere a handle on a directory does not stop its rename.");
        File("a.txt");

        using (var handle = OperatingSystem.IsWindows() ? OpenDirectory(_root.FullName) : throw new PlatformNotSupportedException())
        {
            Assert.False(handle.IsInvalid);
            Assert.Equal(HeldFileReading.Held, HeldFileReader.Read(_root.FullName));
        }

        Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(_root.FullName));
    }

    /// <summary>
    /// A subdirectory this account cannot list is skipped, and a file held beyond it is still found: it refuses no rename, and
    /// a hold anywhere in the tree is what the wait is for.
    /// </summary>
    [Theory]
    [InlineData("a-closed", "z-open")]
    [InlineData("z-closed", "a-open")]
    public void A_subdirectory_that_cannot_be_listed_is_skipped_and_a_hold_beyond_it_is_found(string closed, string open)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny of listing is Windows' own way to make a directory unlistable to its owner.");
        File(Path.Combine(closed, "inside.txt"));
        var held = File(Path.Combine(open, "held.txt"));

        var undo = OperatingSystem.IsWindows() ? DenyListing(Path.Combine(_root.FullName, closed)) : throw new PlatformNotSupportedException();
        try
        {
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(HeldFileReading.Held, HeldFileReader.Read(_root.FullName));
            }

            Assert.Equal(HeldFileReading.Unknown, HeldFileReader.Read(_root.FullName));
        }
        finally
        {
            undo();
        }
    }

    /// <summary>Denies this account writing the file, and returns the undoing.</summary>
    [SupportedOSPlatform("windows")]
    private static Action DenyWriting(string path)
    {
        var file = new FileInfo(path);
        var deny = new FileSystemAccessRule(Account(), FileSystemRights.WriteData | FileSystemRights.AppendData, AccessControlType.Deny);
        var security = file.GetAccessControl();
        security.AddAccessRule(deny);
        file.SetAccessControl(security);
        return () =>
        {
            var now = file.GetAccessControl();
            now.RemoveAccessRule(deny);
            file.SetAccessControl(now);
        };
    }

    /// <summary>Denies this account listing the directory, and returns the undoing.</summary>
    [SupportedOSPlatform("windows")]
    private static Action DenyListing(string path)
    {
        var directory = new DirectoryInfo(path);
        var deny = new FileSystemAccessRule(Account(), FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = directory.GetAccessControl();
        security.AddAccessRule(deny);
        directory.SetAccessControl(security);
        return () =>
        {
            var now = directory.GetAccessControl();
            now.RemoveAccessRule(deny);
            directory.SetAccessControl(now);
        };
    }

    /// <summary>
    /// Denies this account the deletion of the directory, which is what a rename of it needs: on the directory itself, and as a
    /// child of its parent, which grants it otherwise. Returns the undoing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Action DenyDelete(DirectoryInfo directory)
    {
        var parent = directory.Parent!;
        var onDirectory = new FileSystemAccessRule(Account(), FileSystemRights.Delete, AccessControlType.Deny);
        var onChildren = new FileSystemAccessRule(Account(), FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);
        var directorySecurity = directory.GetAccessControl();
        directorySecurity.AddAccessRule(onDirectory);
        directory.SetAccessControl(directorySecurity);
        var parentSecurity = parent.GetAccessControl();
        parentSecurity.AddAccessRule(onChildren);
        parent.SetAccessControl(parentSecurity);
        return () =>
        {
            var parentNow = parent.GetAccessControl();
            parentNow.RemoveAccessRule(onChildren);
            parent.SetAccessControl(parentNow);
            var directoryNow = directory.GetAccessControl();
            directoryNow.RemoveAccessRule(onDirectory);
            directory.SetAccessControl(directoryNow);
        };
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier Account() => WindowsIdentity.GetCurrent().User!;

    /// <summary>A handle on a directory that shares reading and writing and not delete, the way a program working in it has one.</summary>
    [SupportedOSPlatform("windows")]
    private static SafeFileHandle OpenDirectory(string path) =>
        CreateFileW(path, GenericRead, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);

    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x1;
    private const uint ShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
