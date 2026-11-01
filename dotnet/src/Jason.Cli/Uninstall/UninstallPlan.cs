using Jason.Cli.Installation;

namespace Jason.Cli.Uninstall;

/// <summary>One file a receipt names, and whether what is on disk is still what Jason wrote.</summary>
/// <param name="Path">The file, as an absolute path on this machine.</param>
/// <param name="Present">Whether it is still there. A file somebody already deleted is nothing to remove.</param>
/// <param name="Edited">
/// True when the bytes there now do not digest to what the record says was written — or when they could not
/// be read at all, which is the same answer for this purpose: we cannot show the file is ours. Such a file is
/// reported and kept without <c>--force</c>. The receipt says what Jason wrote; a digest that no longer
/// matches says somebody else wrote it since, and removing it would be the data-loss bug the installer
/// refuses to be, with no undo.
/// </param>
public sealed record FileRemoval(string Path, bool Present, bool Edited);

/// <summary>One root a deployment wrote into, and everything planned for it.</summary>
public sealed record RootRemoval(string Root, IReadOnlyList<string> Packs, IReadOnlyList<FileRemoval> Files);

/// <summary>
/// A root holding a record this build cannot read, and why.
/// </summary>
/// <remarks>
/// Never folded in with "there is no record here". The two look identical from a distance and are opposite
/// facts: one means Jason put nothing here, the other means Jason put something here and this build cannot
/// tell what. An uninstall that took the second for the first would remove nothing and report success.
/// </remarks>
public sealed record UnknownRoot(string Root, string Reason);

/// <summary>
/// Everything an uninstall will do, in the order it will do it, read before anything is touched.
/// </summary>
/// <param name="AutostartRegistered">Whether this account has a logon registration to take away.</param>
/// <param name="AutostartArtifact">Where the registration's document lives, for a person who wants to look.</param>
/// <param name="RuntimePid">The runtime this installation has running, or null where none is.</param>
/// <param name="RuntimeStartedAt">
/// When that runtime says it started, from the same descriptor: what tells it apart from a later process that has
/// its id.
/// </param>
/// <param name="Roots">The roots a deployment recorded, each with the paths its record names.</param>
/// <param name="Unknown">The roots nothing may guess at.</param>
/// <param name="PathEntry">How this account's PATH carries the install directory, or null where nothing does.</param>
/// <param name="Executable">The file this Jason is installed as, or null where nothing named one.</param>
/// <param name="InstallDirectory">The directory holding it, removed only if nothing else is left in it.</param>
/// <param name="PreviousExecutable">
/// The executable <c>install.ps1</c> leaves beside the new one when an upgrade found the old one running, or null
/// where there is none. The installer wrote it, so it goes with the executable; left behind, it made the install
/// directory "somebody else's".
/// </param>
/// <param name="ExtractedLibraries">
/// Where this build unpacked the native libraries it carries, or null where it unpacked none or cannot say
/// for certain which directory was its own.
/// </param>
/// <param name="OtherExtractedLibraries">
/// What every other build of this program unpacked beside <paramref name="ExtractedLibraries"/>, one directory each,
/// shaped as the host unpacks: earlier versions of this installation, or another installation of this account. Nothing
/// in such a directory says which; on Windows, where a copy running from one holds it, all of them go, and a build whose
/// directory is gone unpacks it again on its next start. Elsewhere, and wherever the host was told to unpack somewhere
/// else, none is named here.
/// </param>
/// <param name="OtherExtractedLibrariesLeft">
/// Why the other builds' directories are not in <paramref name="OtherExtractedLibraries"/> where some may be: elsewhere
/// than Windows how many there are, under which directory, and that they are removed by hand once nothing runs from them;
/// with the host told to unpack elsewhere, that they were not looked for. Null where nothing is left that way.
/// </param>
/// <param name="PurgesData">Whether the explicit word was said. Nothing else stands for it.</param>
/// <param name="Notices">
/// The notice files the installer keeps beside the executable, by their names there, where they are present. The
/// installer wrote them, so they go with the executable, and nothing else of that directory is taken for them.
/// </param>
/// <param name="DataDirectory">Where it is, so the kept-it line can name it.</param>
/// <param name="LeftTo">
/// The package manager that installed this Jason, where one did. Its executable, the directory it put it in and the PATH
/// entry that reaches it are the manager's, so none of them is in this plan to remove — the fields above that would name
/// them are empty — and this names them instead, with the command that takes them off.
/// </param>
public sealed record UninstallPlan(
    bool AutostartRegistered,
    string? AutostartArtifact,
    int? RuntimePid,
    DateTimeOffset? RuntimeStartedAt,
    IReadOnlyList<RootRemoval> Roots,
    IReadOnlyList<UnknownRoot> Unknown,
    PathEntryPlan? PathEntry,
    string? Executable,
    string? InstallDirectory,
    string? PreviousExecutable,
    string? ExtractedLibraries,
    bool PurgesData,
    string DataDirectory,
    IReadOnlyList<string>? NotificationKeys = null,
    string? NotificationKeysUnreadable = null,
    IReadOnlyList<string>? Notices = null,
    PackageOwner? LeftTo = null,
    IReadOnlyList<string>? OtherExtractedLibraries = null,
    string? OtherExtractedLibrariesLeft = null)
{
    /// <summary>How many files the receipts name that are still there and still ours.</summary>
    public int Removable => Roots.Sum(root => root.Files.Count(file => file.Present && !file.Edited));

    /// <summary>How many are there and are no longer ours. Kept unless <c>--force</c>.</summary>
    public int Edited => Roots.Sum(root => root.Files.Count(file => file.Present && file.Edited));
}
