using System.Security.AccessControl;
using System.Security.Principal;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

/// <summary>
/// The rights a data directory really has, rather than the rights a temporary directory happens to have.
/// </summary>
/// <remarks>
/// <para>
/// Every test data directory in this repository is under <c>Path.GetTempPath()</c>, where the profile's
/// inherited Full Control makes every call here succeed, and CI's smoke tests use the same. So the suite had
/// never exercised a directory the account lacks <c>WRITE_OWNER</c> on — which is every directory outside the
/// profile whose rights arrive through an inherited <c>Modify</c>, and <c>JASON_DATA_DIR</c> may name any
/// directory at all.
/// </para>
/// <para>
/// So this builds that directory rather than hoping for one, and it needs no elevation to do it: an owner
/// always holds <c>WRITE_DAC</c> implicitly, so it can give itself a DACL that does not carry
/// <c>WRITE_OWNER</c>.
/// </para>
/// </remarks>
public class RestrictedDirectoryTests
{
    /// <summary>
    /// Owning an object does not carry the right to give it away. Windows grants an owner
    /// <c>READ_CONTROL</c> and <c>WRITE_DAC</c> implicitly and <b>not</b> <c>WRITE_OWNER</c>, so setting the
    /// owner to the owner it already has — a call that changes nothing — is refused, and the runtime died on
    /// it before its logging was configured.
    /// </summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void A_directory_whose_rights_stop_short_of_taking_ownership_is_still_restricted()
    {
        // Skipped, not passed: a test that returned off Windows reported a pass it had not earned.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Ownership and DACLs are Windows's; elsewhere the directory's mode is the whole story.");

        using var tree = new TempTree();
        var directory = Directory.CreateDirectory(Path.Combine(tree.Root, "data", "run"));
        Narrow(directory);

        FilePermissions.RestrictDirectory(directory.FullName);

        // The property that actually protects the capability token, and the one this call is for: a DACL
        // with one Allow rule, for this account. It never reads the owner, which is why setting one was
        // never what the security rested on.
        Assert.True(FilePermissions.IsRestrictedToCurrentUser(directory.FullName));
    }

    /// <summary>And the same for a file, which is the descriptor the token is written into.</summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void A_file_in_such_a_directory_is_still_restricted()
    {
        // Skipped, not passed: a test that returned off Windows reported a pass it had not earned.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Ownership and DACLs are Windows's; elsewhere the directory's mode is the whole story.");

        using var tree = new TempTree();
        var directory = Directory.CreateDirectory(Path.Combine(tree.Root, "data", "run"));
        Narrow(directory);
        var file = Path.Combine(directory.FullName, "runtime.json");
        File.WriteAllText(file, "{}");

        FilePermissions.RestrictFile(file);

        Assert.True(FilePermissions.IsRestrictedToCurrentUser(file));
    }

    /// <summary>
    /// An ordinary directory is unchanged by all this: the rights are still exactly this account's, and
    /// nobody else's rule survives the protection.
    /// </summary>
    [Fact]
    public void An_ordinary_directory_is_restricted_as_before()
    {
        using var tree = new TempTree();
        var directory = Directory.CreateDirectory(Path.Combine(tree.Root, "plain"));

        FilePermissions.RestrictDirectory(directory.FullName);

        Assert.True(FilePermissions.IsRestrictedToCurrentUser(directory.FullName));
    }

    /// <summary>
    /// An object owned by the owner this token gives everything it creates is this account's already, and is
    /// not taken: for an elevated administrator that owner is the Administrators group, not the user.
    /// </summary>
    /// <remarks>
    /// The two tests above failed on the Windows CI runner, which runs as an elevated administrator, and passed
    /// on every desk: there the directory a test had just created belonged to the group, was read as somebody
    /// else's, and giving it to the user was refused. The runner is where that is proved end to end; this is
    /// the rule, held where the suite runs unelevated too.
    /// </remarks>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void An_object_owned_by_this_tokens_own_default_owner_is_not_taken()
    {
        // Skipped, not passed: a test that returned off Windows reported a pass it had not earned.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Ownership and DACLs are Windows's; elsewhere the directory's mode is the whole story.");

        var user = new SecurityIdentifier("S-1-5-21-1000-2000-3000-1001");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var somebodyElse = new SecurityIdentifier("S-1-5-21-1000-2000-3000-1002");

        Assert.False(FilePermissions.TakesOwnership(administrators, user, tokenOwner: administrators), "an elevated administrator's own directory was taken.");
        Assert.False(FilePermissions.TakesOwnership(user, user, tokenOwner: user), "the user's own directory was taken.");
        Assert.True(FilePermissions.TakesOwnership(somebodyElse, user, tokenOwner: user), "somebody else's directory was left with them.");
        Assert.True(FilePermissions.TakesOwnership(administrators, user, tokenOwner: user), "an unelevated account treated the group as itself.");

        // And the case the rule was written for first: an elevated process -- whose token owner is the group --
        // meeting a directory the user already owns. Nothing held it: taking the user's own clause away kept every
        // test here green, and CI's elevated runner passes through the group's clause instead.
        Assert.False(FilePermissions.TakesOwnership(user, user, tokenOwner: administrators), "an elevated process took a directory the user already owns.");
    }

    /// <summary>
    /// Give ourselves a protected DACL of exactly <c>Modify</c>. We own it, so we hold <c>WRITE_DAC</c>
    /// implicitly and may do this; <c>Modify</c> does not carry <c>WRITE_OWNER</c>, so afterwards
    /// <c>SetOwner</c> is refused. That is the machine state a data directory outside the user profile is in.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Narrow(DirectoryInfo directory)
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var narrowed = directory.GetAccessControl();
        narrowed.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        narrowed.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(narrowed);
    }
}
