using Jason.Cli.Installation;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// Which Windows processes a carried update refuses as elevated: a token elevated above the account's ordinary one, or
/// another account than the session's. An administrator with no split token, User Account Control off as on a hosted build
/// machine, runs with the account's ordinary token and is not refused.
/// </summary>
public class ElevationTests
{
    [Theory]
    // An administrator's elevated terminal under User Account Control: the full half of a split token.
    [InlineData(Elevation.TokenKind.Full, "person", "PC", "person", "PC", false, true)]
    // The same account's ordinary terminal: the limited half.
    [InlineData(Elevation.TokenKind.Limited, "person", "PC", "person", "PC", false, false)]
    // An administrator with User Account Control off: one token, privileged, and the account's ordinary one.
    [InlineData(Elevation.TokenKind.Default, "runner", "HOST", "runner", "HOST", true, false)]
    // A standard account: one token, not privileged.
    [InlineData(Elevation.TokenKind.Default, "person", "PC", "person", "PC", false, false)]
    // Another account's terminal in this session, over the shoulder or with runas, whatever its token.
    [InlineData(Elevation.TokenKind.Default, "admin", "PC", "person", "PC", true, true)]
    [InlineData(Elevation.TokenKind.Limited, "person", "DOMAIN", "person", "PC", false, true)]
    // The same account, its name told in another case.
    [InlineData(Elevation.TokenKind.Default, "Person", "pc", "person", "PC", false, false)]
    // A session with no account, a service's: nothing to compare with.
    [InlineData(Elevation.TokenKind.Default, "runner", "HOST", "", "", true, false)]
    [InlineData(Elevation.TokenKind.Default, "runner", "HOST", null, null, true, false)]
    // A token whose kind could not be read: whether the process is privileged at all decides.
    [InlineData(null, "person", "PC", "person", "PC", true, true)]
    [InlineData(null, "person", "PC", "person", "PC", false, false)]
    public void Only_a_token_above_the_accounts_ordinary_one_or_another_account_is_elevated(
        Elevation.TokenKind? kind, string? user, string? domain, string? sessionUser, string? sessionDomain, bool privileged, bool elevated) =>
        Assert.Equal(elevated, Elevation.Decide(kind, user, domain, sessionUser, sessionDomain, privileged));

    /// <summary>This process, a test's own, is read without failing; what it says depends on how the test was started.</summary>
    [Fact]
    public void This_process_is_read() => _ = Elevation.OfThisProcess();
}
