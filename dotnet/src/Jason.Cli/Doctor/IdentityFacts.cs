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
    /// The installation's data directory, where it is known. Unless it is the default, <c>~/.jason</c>, it is a name of
    /// its own wherever it stands — a directory such as <c>D:\Clients\Acme\jason</c> or <c>~\Clients\Acme\jason</c> says
    /// whose work an installation does — and the default reads as a path in home. Not read from the machine: the data
    /// directory is the installation's, and whoever holds its paths says which it is.
    /// </summary>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// The data directory the runtime answering keeps, where it is not <see cref="DataDirectory"/>: the other installation
    /// of the two a bundle exists to tell apart, a name of its own for the same reason, unless it is the default; and where
    /// it is <see cref="DataDirectory"/> it is that and nothing else.
    /// </summary>
    public string? RuntimeDataDirectory { get; init; }

    /// <summary>
    /// The harness root the runtime answering reports, where it is known: a name of its own wherever it stands, for the same
    /// reason a data directory is — <c>~\Clients\Acme\Jason</c> says whose work it holds. Whether it is the default the
    /// runtime's report says beside it.
    /// </summary>
    public string? HarnessRoot { get; init; }

    /// <summary>
    /// The directories this installation's executable is in: the one running and the one <c>jason</c> resolves to on the
    /// PATH, where they are known. Outside the home directory such a folder is a name of its own — <c>D:\Tools\Acme</c> says
    /// whose machine it is — unless a package manager's own prefix holds it, which names nobody. Not read from the machine
    /// here: whoever composes a bundle says which they are.
    /// </summary>
    public IReadOnlyCollection<string> InstallDirectories { get; init; } = [];

    /// <summary>
    /// The names of a person's own a bundle met — plugins, roles and skill packs Jason did not ship — each with its alias.
    /// Learned while a bundle is composed, never read from the machine.
    /// </summary>
    public NameAliases Aliases { get; init; } = NameAliases.None;

    /// <summary>
    /// The skill sources of a person's own a bundle met — a fork's address or a folder given with <c>--source</c> — written
    /// as one mark wherever they stand. Learned from the skills records, never read from the machine.
    /// </summary>
    public IReadOnlyCollection<string> SkillSources { get; init; } = [];

    /// <summary>
    /// The refs those sources were taken at, written as the same mark only where a fact quotes a deployment — after
    /// <c>at</c> or <c>from</c> — since a ref is often an ordinary word: a branch called <c>main</c> is not every
    /// <c>main</c> in a bundle. Learned from the skills records, never read from the machine.
    /// </summary>
    public IReadOnlyCollection<string> SkillRefs { get; init; } = [];

    /// <summary>
    /// What this machine says about this account. A name the operating system cannot give is left empty rather than
    /// guessed, and an empty name rewrites nothing.
    /// </summary>
    public static IdentityFacts ForThisMachine() =>
        new(
            ThisAccountsHome(),
            Environment.UserName,
            Read(() => Environment.MachineName) ?? string.Empty,
            Read(() => Environment.UserDomainName),
            ThisAccountsSid());

    /// <summary>
    /// The home directory a bundle writes paths from — the one the environment names, or this account's — read alone, for
    /// a sentence that writes a path from home. The account's names are not read for it.
    /// </summary>
    public static string HomeFor(IdentityFacts? named) => named?.Home ?? ThisAccountsHome();

    private static string ThisAccountsHome() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
