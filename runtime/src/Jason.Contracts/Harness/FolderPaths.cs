namespace Jason.Contracts.Harness;

/// <summary>
/// How two folders of the harness are told apart, written once for the runtime, which records them, and for the CLI, which
/// finds one from the folder a shell stands in: by where each path leads, not by how it is spelled, and as the platform's
/// file systems compare names by default — Windows and macOS ignore case, Linux does not.
/// </summary>
public static class FolderPaths
{
    /// <summary>How many links one path is followed through before it is compared as it was reached: a chain that goes round is not followed for ever.</summary>
    private const int MaxLinks = 40;

    /// <summary>How two paths are compared here: as the file system of this platform would tell them apart by default.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The path as a full one, without a separator at its end: how a folder's path is recorded.</summary>
    public static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Whether two paths lead to one folder: each followed through its links and compared as the platform compares.</summary>
    public static bool SameFolder(string left, string right) => Same(Real(left), Real(right));

    /// <summary>Whether two paths, already where they lead, are one, as the platform compares them.</summary>
    public static bool Same(string left, string right) => string.Equals(left, right, Comparison);

    /// <summary>Below the folder, by whole names: <c>.jason-old</c> begins with <c>.jason</c> and is beside it, not inside.</summary>
    public static bool Inside(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// Where a path leads, as the file system finds it rather than as it is spelled: full, without a device prefix, and
    /// with every part of it that is a link — a junction or a symbolic link — replaced by where the link leads, however
    /// deep. A part that is not there yet, or cannot be read, is taken as written, and so is everything after it.
    /// </summary>
    public static string Real(string path)
    {
        var real = Normal(Undevice(path));
        for (var hops = 0; hops < MaxLinks && Linked(real) is { } further; hops++)
        {
            real = further;
        }

        return real;
    }

    /// <summary>
    /// A Windows device path — <c>\\?\C:\…</c>, <c>\\.\C:\…</c>, <c>\??\C:\…</c>, <c>\\?\UNC\server\share\…</c> — as the
    /// drive or share path behind its prefix, which turns off Windows' own reading of a path and names the same folder.
    /// One whose rest names no drive and no share is left a relative path. Elsewhere a path is as it is written.
    /// </summary>
    public static string Undevice(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var device = OperatingSystem.IsWindows()
            && path.Length >= 4
            && (path.StartsWith(@"\??\", StringComparison.Ordinal)
                || (IsSeparator(path[0]) && IsSeparator(path[1]) && (path[2] is '?' or '.') && IsSeparator(path[3])));
        if (!device)
        {
            return path;
        }

        var rest = path[4..];
        return rest.StartsWith(@"UNC", StringComparison.OrdinalIgnoreCase) && rest.Length > 3 && IsSeparator(rest[3])
            ? @"\\" + rest[4..]
            : rest;
    }

    /// <summary>The path with its first part that is a link replaced by where that link finally leads, or null when no part of it is one.</summary>
    private static string? Linked(string path)
    {
        var top = Path.GetPathRoot(path) ?? string.Empty;
        var parts = path[top.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var at = top;
        for (var part = 0; part < parts.Length; part++)
        {
            at = Path.Combine(at, parts[part]);
            try
            {
                if (new DirectoryInfo(at).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    return Normal(Undevice(Path.Combine([target.FullName, .. parts[(part + 1)..]])));
                }
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
