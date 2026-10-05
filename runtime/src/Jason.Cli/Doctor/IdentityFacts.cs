using System.Security;
using System.Security.Principal;
using System.Text;

namespace Jason.Cli.Doctor;

/// <summary>
/// The names this installation goes by, which a diagnostic bundle must not carry to somebody else: where the account's
/// files live, what the account is called, what the machine is called and what domain the account belongs to.
/// </summary>
/// <param name="Home">The account's home directory, as the operating system spells it.</param>
/// <param name="User">The account's name.</param>
/// <param name="Machine">The machine's name.</param>
/// <param name="Domain">
/// The domain the account signs in to. A machine that belongs to no domain reports its own name there, which is the
/// machine's name and not a second one, so the rewriter ignores a domain that is the machine's name.
/// </param>
/// <param name="Sid">
/// The account's security identifier, on a system that has one. Any account's identifier of the common kind is
/// recognised by its shape; this one is named so that an account whose identifier has another shape is rewritten too.
/// </param>
public sealed record IdentityFacts(string Home, string User, string Machine, string? Domain, string? Sid)
{
    // A record prints its members, and these are the names a diagnostic must never print: an instance that is logged,
    // or an environment that is, says what it is and nothing of whose it is.
    private bool PrintMembers(StringBuilder builder) => false;

    /// <summary>
    /// The installation's data directory, where it is known. Outside the home directory it is a name of its own — a
    /// directory such as <c>D:\Clients\Acme\jason</c> says whose work an installation does — and inside it, it already
    /// reads as a path in home. Not read from the machine: the data directory is the installation's, and whoever holds
    /// its paths says which it is.
    /// </summary>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// The data directory the runtime answering keeps, where it is not <see cref="DataDirectory"/>: the other installation
    /// of the two a bundle exists to tell apart, a name of its own for the same reason. Inside the home directory it reads
    /// as a path in home, and where it is <see cref="DataDirectory"/> it is that and nothing else.
    /// </summary>
    public string? RuntimeDataDirectory { get; init; }

    /// <summary>
    /// What this machine says about this account. A name the operating system cannot give is left empty rather than
    /// guessed, and an empty name rewrites nothing.
    /// </summary>
    public static IdentityFacts ForThisMachine() =>
        new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName,
            Read(() => Environment.MachineName) ?? string.Empty,
            Read(() => Environment.UserDomainName),
            ThisAccountsSid());

    // Both of these throw when the system has no answer, and a diagnostic that cannot start is of no use to anyone.
    private static string? Read(Func<string> name)
    {
        try
        {
            return name();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ThisAccountsSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch (Exception unreadable) when (unreadable is SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
