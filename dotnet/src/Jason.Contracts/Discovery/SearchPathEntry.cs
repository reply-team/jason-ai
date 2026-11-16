namespace Jason.Contracts.Discovery;

/// <summary>
/// Whether a folder can be one entry of PATH. The entries are separated by one character, <c>;</c> on Windows and
/// <c>:</c> elsewhere, and no reader has an escape for it (POSIX: "The prefixes shall be separated by a &lt;colon&gt;"),
/// so a folder whose name holds it is read as two entries, neither of them the folder, by every shell and every host.
/// The status check, <c>jason session open</c> and the runtime's launch of a role read this one rule; what an installer
/// cannot write into a profile or a registry value is that installer's own limit, never this one, since such a folder
/// works on PATH when a person puts it there.
/// </summary>
public static class SearchPathEntry
{
    /// <summary>The character that separates the entries of PATH on Windows, or on every other system.</summary>
    public static char Separator(bool windows) => windows ? ';' : ':';

    /// <summary>
    /// Why <paramref name="folder"/> can be no entry of a PATH separated by <paramref name="separator"/>, worded to name
    /// the folder first, or null where it can be one.
    /// </summary>
    public static string? Problem(string folder, char separator)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return folder.Contains(separator, StringComparison.Ordinal)
            ? $"{folder} holds '{separator}', which separates the entries of PATH, so no PATH entry can name it"
            : null;
    }

    /// <summary>The same, by this system's separator.</summary>
    public static string? Problem(string folder) => Problem(folder, Path.PathSeparator);
}
