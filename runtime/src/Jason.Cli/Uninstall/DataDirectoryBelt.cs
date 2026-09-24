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
/// temporary directory, and the installation itself. Refused rather than narrowed, and said before the plan is
/// acted on, because the one thing a person who typed this needs to hear is that the directory they named is
/// not a data directory.
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

        return null;
    }

    /// <summary>Whether <paramref name="outer"/> is <paramref name="path"/> or one of its ancestors.</summary>
    private static bool Holds(string outer, string path) =>
        (Trimmed(path) + Path.DirectorySeparatorChar).StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Trimmed(path), outer, StringComparison.OrdinalIgnoreCase);

    private static string Trimmed(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;

        // A root keeps its separator -- `C:\` and `/` -- and everything else loses a trailing one, so two spellings
        // of one directory compare equal.
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
}
