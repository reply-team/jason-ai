namespace Jason.Cli.Uninstall;

/// <summary>
/// The data directories <c>--purge-data</c> refuses outright, before anything at all is removed.
/// </summary>
/// <remarks>
/// <para>
/// <c>JASON_DATA_DIR</c> may name any directory, and a typo or a stale export is enough to point it at <c>.</c>,
/// at somebody's home directory or at the root of a drive. The purge removes only the entries this product puts
/// in a data directory, so even there it would not take everything — but it would take a <c>config</c> or a
/// <c>logs</c> of somebody else's that happened to sit beside them, and a verb that has to be told the data
/// directory is gone has no business guessing that a home directory is one.
/// </para>
/// <para>
/// So a directory that holds any of these is refused: a file-system root, this account's profile, the system's
/// temporary directory, and the installation itself — and a repository's working tree, whose <c>plugins</c> and
/// <c>skills</c> share names with what Jason keeps. Refused rather than narrowed, and said before the plan is
/// acted on, because the one thing a person who typed this needs to hear is that the directory they named is not
/// a data directory.
/// </para>
/// <para>
/// <b>Compared as the file system finds them, not as they are spelled.</b> The comparison was of strings, so the
/// profile through a junction, or behind the <c>\\?\</c> prefix that turns off Windows' path parsing, was another
/// directory and got past.
/// </para>
/// </remarks>
public static class DataDirectoryBelt
{
    /// <summary>Why a purge of that directory is refused, or null where it is not.</summary>
    /// <param name="dataDirectory">The directory the purge would empty.</param>
    /// <param name="profile">This account's profile directory, or null where there is none.</param>
    /// <param name="temporary">The system's temporary directory.</param>
    /// <param name="installDirectory">The directory the executable is installed in, or null where nothing names one.</param>
    public static string? Refusal(string dataDirectory, string? profile, string temporary, string? installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporary);

        var data = Trimmed(dataDirectory);

        if (string.Equals(data, Trimmed(Path.GetPathRoot(data) ?? data), StringComparison.OrdinalIgnoreCase))
        {
            return $"'{dataDirectory}' is the root of a file system, not a data directory.";
        }

        foreach (var (what, path) in new[] { ("this account's profile", profile), ("the system's temporary directory", temporary), ("the installation", installDirectory) })
        {
            if (path is { Length: > 0 } && Holds(data, path))
            {
                return $"'{dataDirectory}' holds {what}, '{Trimmed(path)}', so it is not a data directory.";
            }
        }

        if (Path.Exists(Path.Combine(data, ".git")))
        {
            return $"'{dataDirectory}' holds '.git': it is a repository's working tree, not a data directory.";
        }

        return null;
    }

    /// <summary>Whether <paramref name="outer"/> is <paramref name="path"/> or one of its ancestors.</summary>
    private static bool Holds(string outer, string path) =>
        (Trimmed(path) + Path.DirectorySeparatorChar).StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Trimmed(path), outer, StringComparison.OrdinalIgnoreCase);

    private static string Trimmed(string path)
    {
        var full = Resolved(path, hops: 0);
        var root = Path.GetPathRoot(full) ?? string.Empty;

        // A root keeps its separator -- `C:\` and `/` -- and everything else loses a trailing one, so two spellings
        // of one directory compare equal.
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>
    /// A path as the file system finds it: full, without the prefix that only turns off Windows' parsing, and with
    /// every link along it followed, however deep.
    /// </summary>
    private static string Resolved(string path, int hops)
    {
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                full = @"\\" + full[8..];
            }
            else if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                full = full[4..];
            }
        }

        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);

            // A chain of links that goes round is not followed for ever; a directory it cannot read is taken as
            // spelled, which is what the comparison did before it followed anything.
            if (hops >= 40)
            {
                continue;
            }

            try
            {
                if (new DirectoryInfo(current).LinkTarget is not null && new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    current = Resolved(target.FullName, hops + 1);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return current;
    }
}
