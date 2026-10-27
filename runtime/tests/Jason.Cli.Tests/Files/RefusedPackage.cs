using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Jason.Cli.Tests.Files;

/// <summary>
/// A package directory this account is refused on Windows, the way a package placed by an elevated process is: its rename
/// is denied. What a move of the directory then comes back with is an access-denied that no program holds open, which is
/// what a permission refusal looks like and a hold does not.
/// </summary>
internal static class RefusedPackage
{
    /// <summary>
    /// Denies this account the deletion of <paramref name="directory"/>, which is what a rename of it needs: on the
    /// directory itself, and as a child of its parent, which grants it otherwise. Returns the undoing, which a test runs
    /// before it deletes the tree.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static Action Deny(string directory)
    {
        var account = WindowsIdentity.GetCurrent().User!;
        var package = new DirectoryInfo(directory);
        var parent = package.Parent!;
        var onPackage = new FileSystemAccessRule(account, FileSystemRights.Delete, AccessControlType.Deny);
        var onChildren = new FileSystemAccessRule(account, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);

        var packageSecurity = package.GetAccessControl();
        packageSecurity.AddAccessRule(onPackage);
        package.SetAccessControl(packageSecurity);
        var parentSecurity = parent.GetAccessControl();
        parentSecurity.AddAccessRule(onChildren);
        parent.SetAccessControl(parentSecurity);
        return () =>
        {
            var parentNow = parent.GetAccessControl();
            parentNow.RemoveAccessRule(onChildren);
            parent.SetAccessControl(parentNow);
            var packageNow = package.GetAccessControl();
            packageNow.RemoveAccessRule(onPackage);
            package.SetAccessControl(packageNow);
        };
    }
}
