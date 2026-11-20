using Jason.Cli.Uninstall;
using Microsoft.Win32;

namespace Jason.Cli.Installation;

/// <summary>
/// The <c>jason</c> a terminal opened from now on finds by name, read as that terminal reads it, and whether it is this one.
/// Read only: nothing it finds is written, moved or removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows.</b> A new terminal's Path is the machine's Path followed by the account's, each expanded where the registry
/// stores it as <c>REG_EXPAND_SZ</c> and taken as written where it is <c>REG_SZ</c>; in each folder, in that order, the
/// name is tried with each extension <c>PATHEXT</c> lists, in its order. So an entry this run appended to the account's Path
/// is read after every folder before it, the machine's included, and a <c>jason</c> earlier on either is what answers.
/// </para>
/// <para>
/// <b>macOS.</b> The PATH a terminal has: the one the caller names, or this process's own, with <c>~/.local/bin</c> in
/// front where the profile lines put it there, which they do by prepending it. The first regular file named <c>jason</c>
/// that may be run is what answers.
/// </para>
/// </remarks>
internal static class NewTerminal
{
    /// <summary>The extensions a Windows shell tries where <c>PATHEXT</c> names none.</summary>
    private const string DefaultExtensions = ".COM;.EXE;.BAT;.CMD";

    /// <summary>The file a new terminal of this account runs for <c>jason</c> on Windows, or null where it finds none.</summary>
    public static string? FindsOnWindows(SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(places);

        var account = OperatingSystem.IsWindows() ? places.AccountPathValue.Read() : null;
        var folders = Entries(places.MachinePath()).Concat(Entries(account));
        var extensions = (places.PathExtensions ?? Environment.GetEnvironmentVariable("PATHEXT") ?? DefaultExtensions)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var folder in folders)
        {
            foreach (var extension in extensions)
            {
                if (File(folder, "jason" + extension) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The file a new terminal runs for <c>jason</c> on macOS, or null where it finds none: the first on
    /// <paramref name="terminalPath"/>, with <paramref name="localBin"/> in front where <paramref name="inFront"/>.
    /// </summary>
    public static string? FindsOnMac(string? terminalPath, string localBin, bool inFront)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localBin);

        IEnumerable<string> folders = MacFolders(terminalPath);
        if (inFront)
        {
            folders = folders.Prepend(localBin);
        }

        foreach (var folder in folders)
        {
            if (File(folder, "jason") is { } found && MayRun(found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="found"/> runs <paramref name="image"/>: the same file, a link to it followed, or the
    /// application's wrapper naming it.
    /// </summary>
    public static bool Runs(string? found, string? image)
    {
        if (found is null || image is null)
        {
            return false;
        }

        if (SameFile(found, image))
        {
            return true;
        }

        return CommandFiles.IsWrapperFile(found)
            && CommandFiles.ReadSmall(found) is { } text
            && CommandFiles.WrapperTarget(text) is { } wrapped
            && SameFile(wrapped.Executable, image);
    }

    /// <summary>
    /// What is said where a new terminal finds another file than this jason, and what removes it: a command-line install of
    /// Jason's moves into Jason Desktop by its own update; a package manager's is that manager's to take off; any other
    /// file is the person's, and is never touched.
    /// </summary>
    public static string Problem(string found, string carried, bool commandLineInstall, PackageOwner? owner)
    {
        var first = $"A terminal opened from now on finds {found} first by the name jason, not this jason ({carried})";
        return owner is not null
            ? $"{first}: {owner.Manager} installed it, and `{owner.UninstallCommand}` takes it off. It was left as it is."
            : commandLineInstall
                ? $"{first}: it is a command-line install of Jason, and its own `jason update apply` moves it into Jason Desktop. It was left as it is."
                : $"{first}. It is not a file of Jason's, so it was left as it is: remove it, or take its folder off the PATH.";
    }

    /// <summary>
    /// The folders of a macOS PATH, separated by ':'. Where this runs on Windows - a test of the macOS route - a drive letter
    /// is joined back to the folder it begins, which the separator split from it.
    /// </summary>
    private static List<string> MacFolders(string? path)
    {
        var folders = new List<string>();
        foreach (var piece in (path ?? string.Empty).Split(':'))
        {
            if (OperatingSystem.IsWindows() && folders.Count > 0 && folders[^1].Length == 1 && char.IsAsciiLetter(folders[^1][0]) && piece.StartsWith('\\'))
            {
                folders[^1] += ":" + piece;
                continue;
            }

            folders.Add(piece);
        }

        return [.. folders.Where(folder => folder.Length > 0)];
    }

    /// <summary>The folders a stored Path names, as Windows reads them: expanded where its kind is <c>REG_EXPAND_SZ</c>.</summary>
    private static IEnumerable<string> Entries(StoredPath? stored)
    {
        if (stored is null)
        {
            return [];
        }

        var expand = PathEntry.ReadAs(stored.Kind == RegistryValueKind.ExpandString);
        return stored.Raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(expand);
    }

    /// <summary>The file of that name in that folder, where there is one; a folder no path may name is skipped, as a shell skips it.</summary>
    private static string? File(string folder, string name)
    {
        try
        {
            var file = Path.Combine(folder, name);
            if (!System.IO.File.Exists(file))
            {
                return null;
            }

            // By the name the folder holds it under: an extension PATHEXT spells in capitals finds a file whose own is not.
            var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!);
            return directory.EnumerateFiles(name).FirstOrDefault()?.FullName ?? Path.GetFullPath(file);
        }
        catch (Exception unusable) when (unusable is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether a file may be run: off Windows, an execute bit of any kind; a host that keeps none takes it as one.</summary>
    private static bool MayRun(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        try
        {
            return (System.IO.File.GetUnixFileMode(file) & Execute) != 0;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether two paths are one file, links followed; case is the system's own.</summary>
    private static bool SameFile(string left, string right)
    {
        try
        {
            var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(Final(left), Final(right), comparison);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The full path with a link in the file, or else in its folder - a junction, a manager's link - followed to its target.</summary>
    private static string Final(string path)
    {
        var full = Path.GetFullPath(path);
        if (new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName is { } target)
        {
            return Path.GetFullPath(target);
        }

        var folder = Path.GetDirectoryName(full);
        return folder is not null && new DirectoryInfo(folder).ResolveLinkTarget(returnFinalTarget: true)?.FullName is { } linked
            ? Path.Combine(Path.GetFullPath(linked), Path.GetFileName(full))
            : full;
    }
}
