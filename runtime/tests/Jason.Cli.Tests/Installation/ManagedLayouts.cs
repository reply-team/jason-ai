using Jason.Cli.Installation;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// The records a package manager keeps, as a test plants them: Homebrew's receipt is a file in a keg, so it is read
/// from the planted tree exactly as this machine's records read it; winget's registration lives in this account's
/// registry, which no test reads, so it is whatever the test names.
/// </summary>
public sealed class PlantedRecords(params WingetRegistration[] winget) : IManagerRecords
{
    public bool HomebrewReceipt(string keg) => ManagerRecords.ReadHomebrewReceipt(keg);

    public IReadOnlyList<WingetRegistration> WingetRegistrations(string packageIdentifier) =>
        [.. winget.Where(registration => registration.Key.StartsWith(packageIdentifier, StringComparison.OrdinalIgnoreCase))];
}

/// <summary>
/// Installations as Homebrew and winget lay them out, planted in a temporary tree: the directories, the executable, and
/// the manager's own record beside it, or one of the near misses that lack it.
/// </summary>
/// <remarks>
/// The winget shape is the one a real <c>winget install</c> of the release from a local manifest recorded, on an ordinary
/// account without Developer Mode: an uninstall entry under this account's <c>Uninstall</c> key named
/// <c>Reply.Jason__DefaultSource</c>, carrying <c>WinGetPackageIdentifier</c> = <c>Reply.Jason</c> and
/// <c>InstallLocation</c> = <c>%LOCALAPPDATA%\Microsoft\WinGet\Packages\Reply.Jason__DefaultSource</c>; that directory
/// holding <c>jason.exe</c>, <c>LICENSE</c>, <c>THIRD-PARTY-NOTICES</c> and a hidden database named for the entry; no link
/// under <c>Links</c>, the directory put on the User Path instead; and, across a <c>winget upgrade</c> made with the runtime
/// stopped, the entry, its install location and the directory as they were. A package from the community source carries
/// its source in the same names, <c>Reply.Jason_Microsoft.Winget.Source_8wekyb3d8bbwe</c> by winget's rule, which was not
/// recorded. The Homebrew shape is Homebrew's documented one; nobody here has a Mac to plant it from.
/// </remarks>
public sealed class ManagedLayouts : IDisposable
{
    private readonly TempTree _tree = new();

    /// <summary>The name winget gives a package from its community source, by its rule; the recorded install was from a local manifest.</summary>
    public const string CommunitySource = "Reply.Jason_Microsoft.Winget.Source_8wekyb3d8bbwe";

    /// <summary>The name a package installed from a local manifest was recorded under: the entry's key and its directory's name.</summary>
    public const string LocalManifest = "Reply.Jason__DefaultSource";

    /// <summary>What Homebrew writes into a keg when it installs one: a JSON object, naming the Homebrew that wrote it.</summary>
    public const string Receipt = """{"homebrew_version":"4.4.0","used_options":[],"installed_on_request":true,"source":{"tap":"reply-team/tap"}}""";

    public string Root => _tree.Root;

    /// <summary>
    /// <c>&lt;prefix&gt;/Cellar/jason/&lt;version&gt;/bin/jason</c>, with Homebrew's link to it in <c>&lt;prefix&gt;/bin</c> where
    /// asked: a file standing for the link, since what the rule reads is the path and the receipt, never the link.
    /// </summary>
    /// <param name="receipt">The receipt's text, or null for a keg with none.</param>
    /// <param name="withLink">Whether <c>&lt;prefix&gt;/bin/jason</c> is there, as <c>brew link</c> leaves it.</param>
    /// <param name="within">The keg's directory the executable is in: <c>bin</c>, where Homebrew puts what it links.</param>
    public HomebrewLayout Homebrew(string? receipt = Receipt, bool withLink = true, string version = "0.9.0", string within = "bin")
    {
        var prefix = _tree.NewDirectory("homebrew");
        var keg = Path.Combine(prefix, "Cellar", "jason", version);
        var executable = Path.Combine(keg, within, ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, $"jason {version}");

        if (receipt is not null)
        {
            File.WriteAllText(Path.Combine(keg, "INSTALL_RECEIPT.json"), receipt);
        }

        var link = Path.Combine(prefix, "bin", ReleaseAssets.ExecutableName);
        if (withLink)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            File.WriteAllText(link, $"jason {version}");
        }

        return new HomebrewLayout(prefix, keg, executable, link);
    }

    /// <summary>
    /// A directory that is merely named <c>Cellar</c>: the keg's shape, somewhere that is not a Homebrew prefix, and no
    /// receipt in it.
    /// </summary>
    public string MerelyNamedCellar()
    {
        var executable = Path.Combine(_tree.NewDirectory("projects"), "Cellar", "jason", "0.9.0", "bin", ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "jason 0.9.0");
        return executable;
    }

    /// <summary>
    /// <c>…\WinGet\Packages\&lt;name&gt;\jason.exe</c>, where <c>…\WinGet</c> stands for
    /// <c>%LOCALAPPDATA%\Microsoft\WinGet</c>, and the registration winget writes for it where asked.
    /// </summary>
    public WingetLayout Winget(string name = LocalManifest)
    {
        var winget = Path.Combine(_tree.NewDirectory("Microsoft"), "WinGet");
        var package = Path.Combine(winget, "Packages", name);
        var executable = Path.Combine(package, ReleaseAssets.ExecutableName);
        Directory.CreateDirectory(package);
        File.WriteAllText(executable, "jason 0.9.0");

        // Beside the executable, as recorded: the archive's two notices under their own names, and winget's own database
        // for the package, hidden.
        File.WriteAllText(Path.Combine(package, "LICENSE"), "the runtime's licence");
        File.WriteAllText(Path.Combine(package, "THIRD-PARTY-NOTICES"), "the third-party notices");
        var database = Path.Combine(package, name + ".db");
        File.WriteAllText(database, string.Empty);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(database, FileAttributes.Hidden);
        }

        Directory.CreateDirectory(Path.Combine(winget, "Links"));

        return new WingetLayout(winget, package, executable, Path.Combine(winget, "Links", ReleaseAssets.ExecutableName), name);
    }

    /// <summary>The error Windows gives a symbolic link it refuses for want of a privilege: <c>ERROR_PRIVILEGE_NOT_HELD</c>.</summary>
    private const int PrivilegeNotHeld = unchecked((int)0x80070522);

    /// <summary>
    /// Makes <paramref name="link"/> a symbolic link to <paramref name="target"/>, or answers false where this account may
    /// not make one — and only there. A symbolic link needs a privilege an ordinary Windows account does not hold; a CI
    /// runner does, and so does every other platform. Any other failure is the test's own and is thrown, so a fact is never
    /// skipped for a reason that is not the privilege.
    /// </summary>
    public static bool TryLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException refused) when (OperatingSystem.IsWindows() && refused.HResult == PrivilegeNotHeld)
        {
            return false;
        }
    }

    /// <summary>What a fact that needs <see cref="TryLink"/> says when it is skipped.</summary>
    public const string NoLinkPrivilege = "A file symbolic link needs a privilege this account does not hold (ERROR_PRIVILEGE_NOT_HELD).";

    public void Dispose() => _tree.Dispose();
}

/// <param name="Prefix">What <c>brew --prefix</c> answers.</param>
/// <param name="Keg">The version's own directory under <c>Cellar/jason</c>.</param>
/// <param name="Executable">The file Homebrew installed, inside the keg.</param>
/// <param name="Link">Where Homebrew links it, in the prefix's <c>bin</c>.</param>
public sealed record HomebrewLayout(string Prefix, string Keg, string Executable, string Link);

/// <param name="Root">What stands for <c>%LOCALAPPDATA%\Microsoft\WinGet</c>.</param>
/// <param name="Package">The package's own directory under <c>Packages</c>.</param>
/// <param name="Executable">The file winget installed, inside it.</param>
/// <param name="Link">Where winget links it, under <c>Links</c>; nothing is there until a test puts something.</param>
/// <param name="Name">The package directory's name, which the recorded uninstall entry carries too.</param>
public sealed record WingetLayout(string Root, string Package, string Executable, string Link, string Name)
{
    /// <summary>The registration winget writes for this package, as recorded, under its own name unless a key is named.</summary>
    public WingetRegistration Registration(string? installLocation = null, string identifier = "Reply.Jason", string? key = null) =>
        new(key ?? Name, identifier, installLocation ?? Package);
}
