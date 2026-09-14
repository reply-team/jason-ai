using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Jason.Runtime.Discovery;

/// <summary>
/// Owner-only permissions for the files that carry the capability token. Unix: mode 0600 (files) /
/// 0700 (directories). Windows: a protected DACL with a single Allow rule for the current user.
/// </summary>
public static class FilePermissions
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = OwnerOnlyFile | UnixFileMode.UserExecute;
    private const UnixFileMode NotOwner =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static void RestrictFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictOnWindows(new FileInfo(path));
        }
        else
        {
            File.SetUnixFileMode(path, OwnerOnlyFile);
        }
    }

    public static void RestrictDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictOnWindows(new DirectoryInfo(path));
        }
        else
        {
            File.SetUnixFileMode(path, OwnerOnlyDirectory);
        }
    }

    public static bool IsRestrictedToCurrentUser(string path) =>
        OperatingSystem.IsWindows()
            ? IsRestrictedOnWindows(path)
            : (File.GetUnixFileMode(path) & NotOwner) == 0;

    public static void VerifyRestrictedToCurrentUser(string path)
    {
        if (!IsRestrictedToCurrentUser(path))
        {
            throw new SecurityException($"'{path}' is accessible to other accounts; refusing to expose the capability token through it.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictOnWindows(FileSystemInfo target)
    {
        var user = CurrentUser();
        switch (target)
        {
            case FileInfo file:
            {
                var security = new FileSecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.SetOwner(user);
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                file.SetAccessControl(security);
                break;
            }

            case DirectoryInfo directory:
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.SetOwner(user);
                security.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                directory.SetAccessControl(security);
                break;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRestrictedOnWindows(string path)
    {
        var user = CurrentUser();
        AuthorizationRuleCollection rules = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            : new FileInfo(path).GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        var anyAllow = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            anyAllow = true;
            if (!rule.IdentityReference.Equals(user))
            {
                return false;
            }
        }

        return anyAllow;
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentUser() =>
        WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows identity has no security identifier.");
}
