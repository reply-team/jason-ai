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
                TakeOwnership(security, file.GetAccessControl().GetOwner(typeof(SecurityIdentifier)), user);
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                file.SetAccessControl(security);
                break;
            }

            case DirectoryInfo directory:
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                TakeOwnership(security, directory.GetAccessControl().GetOwner(typeof(SecurityIdentifier)), user);
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

    /// <summary>
    /// Sets the owner, and only when it is not the owner already.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owning an object on Windows does not carry <c>WRITE_OWNER</c> — only <c>READ_CONTROL</c> and
    /// <c>WRITE_DAC</c>. So setting the owner to the owner it already has, a call that changes nothing, is
    /// <b>refused</b> on any object whose rights reach this account through an inherited <c>Modify</c> rather
    /// than Full Control. That is every directory outside the user profile, and <c>JASON_DATA_DIR</c> may name
    /// any directory at all: the whole runtime died here, before its logging existed to say so.
    /// </para>
    /// <para>
    /// Dropping the unnecessary call weakens nothing that is checked. What protects the capability token is
    /// the protected DACL above plus <see cref="VerifyRestrictedToCurrentUser"/>, and
    /// <see cref="IsRestrictedOnWindows"/> reads access rules only — it has never looked at the owner.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static void TakeOwnership(FileSystemSecurity security, IdentityReference? owner, SecurityIdentifier user)
    {
        if (TakesOwnership(owner, user, WindowsIdentity.GetCurrent().Owner))
        {
            // It belongs to somebody else, and taking it is the point. If this account may not, the refusal
            // is a real one and the caller reports it rather than the process ending on it.
            security.SetOwner(user);
        }
    }

    /// <summary>
    /// Whether an object owned by <paramref name="owner"/> belongs to somebody else — the one case the owner is
    /// changed.
    /// </summary>
    /// <remarks>
    /// Not only "is it this user". A token has a default owner of its own, and for an elevated administrator
    /// that is the Administrators group rather than the user: everything such a process creates is owned by the
    /// group. Read as somebody else's, a directory this very process had just made was given to the user, and
    /// where its rights stopped short of <c>WRITE_OWNER</c> that was refused — on the Windows CI runner, which
    /// runs as an elevated administrator, and never on a desk where the suite runs unelevated. An owner this
    /// token assigns itself is this account's already; the owner holds <c>WRITE_DAC</c> implicitly, and the
    /// protected DACL is what guards the token.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal static bool TakesOwnership(IdentityReference? owner, SecurityIdentifier user, SecurityIdentifier? tokenOwner) =>
        !user.Equals(owner) && !(tokenOwner is not null && tokenOwner.Equals(owner));

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
