using Jason.Contracts.Discovery;

namespace Jason.Cli.Installation;

/// <summary>
/// The package manager that installed this Jason, and what every verb needs to know about it: which manager and which
/// package, where it put the executable, the entry that stays put across its upgrades, and the commands that upgrade
/// and remove it. Null, where it is asked for, means the installation is Jason's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>The path says where; the record says whose.</b> A path in a manager's layout is not enough: a directory can be named
/// <c>Cellar</c> by anybody, and a copy of the executable can sit anywhere. An installation is a manager's only where the
/// manager's own record says so — Homebrew's receipt in the keg, winget's uninstall entry naming the package and a
/// directory that holds this executable. Everything else is Jason's own, and every verb treats it as it always has.
/// </para>
/// <para>
/// One rule, asked by every verb that would otherwise take "the installation" to be the running image: <c>jason update
/// apply</c> and <c>rollback</c>, which would replace a file the manager owns; <c>jason update check</c>, which names the
/// command that upgrades; <c>jason uninstall</c>, which would delete it; the <c>path</c> check, which would offer to put
/// the manager's directory on the PATH; and <c>jason runtime autostart enable</c>, which would register a keg that the
/// next <c>brew upgrade</c> deletes.
/// </para>
/// </remarks>
/// <param name="Manager">The manager's own name, as a person reads it: <c>Homebrew</c> or <c>winget</c>.</param>
/// <param name="Package">The package as that manager names it.</param>
/// <param name="Executable">The file the manager installed, with any link resolved.</param>
/// <param name="Directory">The directory the manager installed it into, which is the manager's.</param>
/// <param name="Link">The manager's own link to the executable, where there is one.</param>
/// <param name="StableEntry">
/// What names this installation across the manager's upgrades: the link where there is one, otherwise the executable in
/// the package's own directory. A registration names this, never the resolved keg.
/// </param>
/// <param name="UpgradeCommand">The one command that upgrades it, as that manager spells it.</param>
/// <param name="UninstallCommand">The one command that takes it off, as that manager spells it.</param>
public sealed record PackageOwner(
    string Manager,
    string Package,
    string Executable,
    string Directory,
    string? Link,
    string StableEntry,
    string UpgradeCommand,
    string UninstallCommand)
{
    public const string Homebrew = "Homebrew";

    public const string Winget = "winget";

    /// <summary>The package the tap's formula installs, which is also the name of its directory under <c>Cellar</c>.</summary>
    private const string Formula = "jason";

    /// <summary>The package identifier this product carries in winget's catalog.</summary>
    private const string WingetPackage = "Reply.Jason";

    /// <summary>
    /// The lines a person types to upgrade under this manager: the runtime stopped, the manager's upgrade, the runtime
    /// started. Three lines and never one. The manager knows nothing of the runtime Jason keeps running and replaces the
    /// image every plugin host that runtime starts is launched from — under Homebrew the old keg is deleted, under winget
    /// the executable is held while it runs.
    /// </summary>
    public IReadOnlyList<string> TypedUpgrade() => ["jason runtime stop", UpgradeCommand, "jason runtime start"];

    /// <summary>
    /// Who owns the installation this environment runs as, or null where it is Jason's own — which is the answer wherever
    /// the environment names no records, wherever there is no single installed file, and wherever no manager's record
    /// claims the file there is.
    /// </summary>
    /// <remarks>
    /// The file is the one <see cref="CliEnvironment.InstallPath"/> names, or the one this process runs as. Where the
    /// operating system reported a link rather than its target, the rule is asked about the image the link resolves to,
    /// and the link is passed beside it as what the process was launched by: deciding by the link would be deciding by a
    /// path, and a link can be put anywhere.
    /// </remarks>
    public static PackageOwner? Of(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (env.Managers is not { } records || (env.InstallPath ?? SelfExecutable.InstalledImage) is not { } installed)
        {
            return null;
        }

        var image = Resolved(installed);
        return Of(image, Same(image, installed) ? null : installed, records);
    }

    /// <summary>
    /// The rule itself, over what it depends on: the image, the path the process was launched by where that differs, and
    /// the managers' records.
    /// </summary>
    /// <param name="image">The installed executable, with any link resolved.</param>
    /// <param name="launched">The path the process was started by, unresolved, where the operating system reported one that differs.</param>
    /// <param name="records">What the managers recorded.</param>
    public static PackageOwner? Of(string image, string? launched, IManagerRecords records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentNullException.ThrowIfNull(records);

        return OfHomebrew(Path.GetFullPath(image), records) ?? OfWinget(Path.GetFullPath(image), launched, records);
    }

    /// <summary>
    /// <c>&lt;prefix&gt;/Cellar/jason/&lt;version&gt;/bin/jason</c>, whose keg carries Homebrew's receipt. The stable entry
    /// is <c>&lt;prefix&gt;/bin/jason</c>, the link <c>brew upgrade</c> repoints: the prefix is the <c>Cellar</c>'s parent,
    /// which is what <c>brew --prefix</c> answers.
    /// </summary>
    private static PackageOwner? OfHomebrew(string image, IManagerRecords records)
    {
        var bin = Path.GetDirectoryName(image);
        var keg = Parent(bin);
        var formula = Parent(keg);
        var cellar = Parent(formula);
        var prefix = Parent(cellar);

        if (!string.Equals(Path.GetFileNameWithoutExtension(image), Formula, StringComparison.Ordinal)
            || Name(bin) != "bin"
            || Name(formula) != Formula
            || Name(cellar) != "Cellar"
            || keg is null
            || prefix is null)
        {
            return null;
        }

        // Where, and now whose.
        if (!records.HomebrewReceipt(keg))
        {
            return null;
        }

        var link = Path.Combine(prefix, "bin", Path.GetFileName(image));
        return new PackageOwner(Homebrew, Formula, image, bin!, link, link, $"brew upgrade {Formula}", $"brew uninstall {Formula}");
    }

    /// <summary>
    /// <c>…\WinGet\Packages\Reply.Jason_&lt;source&gt;\jason.exe</c>, or a file in <c>…\WinGet\Links</c>, which an uninstall
    /// entry winget wrote for <c>Reply.Jason</c> says holds this executable. The stable entry is winget's link where there is
    /// one to this executable, and otherwise the executable in the package's own directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As a real <c>winget install</c> of the release from a local manifest recorded it: an entry under
    /// <see cref="ManagerRecords.UninstallKey"/> named <c>Reply.Jason__DefaultSource</c>, carrying
    /// <c>WinGetPackageIdentifier</c> and an <c>InstallLocation</c> naming the package directory of the same name. A
    /// community-source install carries its source in both names instead, by winget's rule; that form was not recorded,
    /// and the identifier with an underscore after it begins both.
    /// </para>
    /// <para>
    /// That install, on an ordinary account without Developer Mode, made no link under <c>Links</c> and put the package
    /// directory on the User Path, so its stable entry is the package directory's executable. A <c>winget upgrade</c> with the
    /// runtime stopped kept the entry, its install location and the directory, and replaced the executable in place.
    /// </para>
    /// </remarks>
    private static PackageOwner? OfWinget(string image, string? launched, IManagerRecords records)
    {
        var directory = Path.GetDirectoryName(image);
        var parent = Parent(directory);

        var inPackage = Name(directory)?.StartsWith(WingetPackage + "_", StringComparison.OrdinalIgnoreCase) == true
            && string.Equals(Name(parent), "Packages", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name(Parent(parent)), "WinGet", StringComparison.OrdinalIgnoreCase);
        var inLinks = string.Equals(Name(directory), "Links", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name(parent), "WinGet", StringComparison.OrdinalIgnoreCase);

        if (directory is null || (!inPackage && !inLinks))
        {
            return null;
        }

        // Where, and now whose: an entry winget wrote for this package, saying it installed it into a directory that holds
        // this very file. A copy in Links is in winget's layout and in no directory winget says is the package's.
        if (!records.WingetRegistrations(WingetPackage).Any(registration =>
                registration.Key.StartsWith(WingetPackage + "_", StringComparison.OrdinalIgnoreCase)
                && string.Equals(registration.PackageIdentifier, WingetPackage, StringComparison.Ordinal)
                && Holds(registration.InstallLocation, image)))
        {
            return null;
        }

        var root = inPackage ? Parent(parent)! : parent!;
        var link = Path.Combine(root, "Links", Path.GetFileName(image));
        var linked = LinksTo(link, image, launched);

        return new PackageOwner(
            Winget,
            WingetPackage,
            image,
            directory,
            linked ? link : null,
            linked ? link : image,
            $"winget upgrade {WingetPackage}",
            $"winget uninstall {WingetPackage}");
    }

    /// <summary>
    /// Whether <paramref name="link"/> is a link to <paramref name="image"/>: the process was launched by it, which the
    /// operating system reports only for a link it resolved to the image, or it resolves to the image on disk.
    /// </summary>
    private static bool LinksTo(string link, string image, string? launched)
    {
        if (launched is not null && Same(launched, link))
        {
            return true;
        }

        var target = Resolved(link);
        return !Same(target, link) && Same(target, image);
    }

    /// <summary>The file a path names with every link on the way resolved, or the path itself where it is no link or cannot be read.</summary>
    private static string Resolved(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static string? Parent(string? path) => path is null ? null : Path.GetDirectoryName(path);

    private static string? Name(string? path) => path is null ? null : Path.GetFileName(path);

    private static bool Same(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="file"/> is inside <paramref name="directory"/>.</summary>
    private static bool Holds(string? directory, string file)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var outer = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(outer, StringComparison.OrdinalIgnoreCase);
    }
}
