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
/// <param name="PurgesData">Whether the explicit word was said. Nothing else stands for it.</param>
/// <param name="DataDirectory">Where it is, so the kept-it line can name it.</param>
public sealed record UninstallPlan(
    bool AutostartRegistered,
    string? AutostartArtifact,
    int? RuntimePid,
    IReadOnlyList<RootRemoval> Roots,
    IReadOnlyList<UnknownRoot> Unknown,
    PathEntryPlan? PathEntry,
    string? Executable,
    string? InstallDirectory,
    string? PreviousExecutable,
    string? ExtractedLibraries,
    bool PurgesData,
    string DataDirectory)
{
    /// <summary>How many files the receipts name that are still there and still ours.</summary>
    public int Removable => Roots.Sum(root => root.Files.Count(file => file.Present && !file.Edited));

    /// <summary>How many are there and are no longer ours. Kept unless <c>--force</c>.</summary>
    public int Edited => Roots.Sum(root => root.Files.Count(file => file.Present && file.Edited));
}
