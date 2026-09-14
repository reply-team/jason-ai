using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

public class FilePermissionsTests
{
    [Fact]
    public void A_restricted_file_is_recognised_as_restricted()
    {
        using var dir = new TempDataDir();
        var file = Path.Combine(dir.Paths.Root, "secret.json");
        File.WriteAllText(file, "{}");

        FilePermissions.RestrictFile(file);

        Assert.True(FilePermissions.IsRestrictedToCurrentUser(file));
        FilePermissions.VerifyRestrictedToCurrentUser(file); // must not throw
    }

    [Fact]
    public void A_file_readable_by_others_is_rejected()
    {
        using var dir = new TempDataDir();
        var file = Path.Combine(dir.Paths.Root, "leaky.json");
        File.WriteAllText(file, "{}");
        FilePermissions.RestrictFile(file);
        MakeReadableByEveryone(file);

        Assert.False(FilePermissions.IsRestrictedToCurrentUser(file));
        Assert.Throws<SecurityException>(() => FilePermissions.VerifyRestrictedToCurrentUser(file));
    }

    [Fact]
    public void A_directory_can_be_restricted_too()
    {
        using var dir = new TempDataDir();
        var sub = Path.Combine(dir.Paths.Root, "run");
        Directory.CreateDirectory(sub);

        FilePermissions.RestrictDirectory(sub);

        Assert.True(FilePermissions.IsRestrictedToCurrentUser(sub));
    }

    private static void MakeReadableByEveryone(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new FileInfo(file);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}
