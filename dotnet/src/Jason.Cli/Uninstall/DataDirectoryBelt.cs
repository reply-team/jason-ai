using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;

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
/// <c>skills</c> share names with what Jason keeps, and a folder of the harness root, a folder inside one, and a folder
/// holding one where the purge would reach it, or a folder there that cannot be looked into, since the root is the
/// person's and no purge ever touches it. Refused rather than narrowed, and said before the plan is acted on, because
/// the one thing a person who typed this needs to hear is that the directory they named is not a data directory.
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

        return HarnessRefusal(dataDirectory, data);
    }

    /// <summary>
    /// Why the purge of <paramref name="data"/> would reach a folder of the harness root, or might, or null where it would not.
    /// <c>JASON_DATA_DIR</c> naming the root, a project's folder or a campaign's, or a folder of the person's own inside
    /// one, would have the purge remove whatever there bears a name Jason keeps — a campaign called "Logs" has the folder
    /// <c>logs</c> — and one naming a folder that holds the root inside a name Jason keeps, <c>D:\Sales</c> with the root at
    /// <c>D:\Sales\work\Jason</c>, would have it remove the root whole.
    /// </summary>
    /// <remarks>
    /// So a marker refuses in the data directory and in every directory above it, as spelled and as the file system finds
    /// it — the walk up every look for a level around a path takes, one look a folder and no listing; in each folder directly
    /// in it; and anywhere in a folder the purge removes whole, which the purge walks to delete anyway, so the look reaches
    /// as far as the delete and no further: not through a link, which the delete never goes through, and not into a folder
    /// the purge keeps. All of it is looked at before anything is removed, so a refusal removes nothing. A marker is known
    /// by being there — one nobody can read, or another program holds open, is still somebody's folder. And a folder the look
    /// cannot enter refuses too, after the look has gone on past it to name a marker if there is one: what the purge cannot
    /// see it does not remove.
    /// </remarks>
    private static string? HarnessRefusal(string dataDirectory, string data)
    {
        var marker = $"{HarnessMarker.Directory}/{HarnessMarker.FileName}";

        foreach (var start in new[] { FolderPaths.Normal(dataDirectory), data })
        {
            var itself = true;
            foreach (var folder in FolderPaths.AndAbove(start))
            {
                if (HarnessMarker.IsIn(folder))
                {
                    return itself
                        ? $"'{dataDirectory}' holds {marker}: it is a folder of the harness root, which is the person's, not a data directory."
                        : $"'{dataDirectory}' is inside '{folder}', a folder of the harness root by its {marker}, which is the person's, so it is not a data directory.";
                }

                itself = false;
            }
        }

        var unseen = new List<string>();
        foreach (var folder in Folders(new DirectoryInfo(data), unseen))
        {
            var found = HarnessMarker.IsIn(folder.FullName) ? folder.FullName
                : JasonPaths.OwnEntries.Contains(folder.Name, StringComparer.Ordinal) && !IsLink(folder) ? MarkedUnder(folder, unseen)
                : null;
            if (found is not null)
            {
                return $"'{dataDirectory}' holds '{found}', a folder of the harness root by its {marker}, which is the person's, so it is not a data directory.";
            }
        }

        return unseen.Count > 0
            ? $"'{dataDirectory}' could not be looked through: this account cannot list '{unseen[0]}', so whether what the purge would remove holds a folder of the harness root, known by its {marker}, is not known."
            : null;
    }

    /// <summary>
    /// The first folder under <paramref name="top"/> with a marker, in name order and depth first, or null; each folder it
    /// cannot enter is added to <paramref name="unseen"/>, and the look goes on past it.
    /// </summary>
    private static string? MarkedUnder(DirectoryInfo top, List<string> unseen)
    {
        var pending = new Stack<DirectoryInfo>(Folders(top, unseen).Reverse());
        while (pending.TryPop(out var folder))
        {
            if (IsLink(folder))
            {
                continue;
            }

            if (HarnessMarker.IsIn(folder.FullName))
            {
                return folder.FullName;
            }

            foreach (var child in Folders(folder, unseen).Reverse())
            {
                pending.Push(child);
            }
        }

        return null;
    }

    /// <summary>
    /// The folders directly in <paramref name="folder"/>, in name order. None where it is not there, since a folder gone
    /// holds nothing to remove; and none where it cannot be listed, which goes into <paramref name="unseen"/> instead.
    /// </summary>
    private static IReadOnlyList<DirectoryInfo> Folders(DirectoryInfo folder, List<string> unseen)
    {
        try
        {
            return [.. folder.EnumerateDirectories().OrderBy(child => child.Name, StringComparer.Ordinal)];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            unseen.Add(folder.FullName);
            return [];
        }
    }

    /// <summary>A symbolic link or a junction: what the delete removes as itself and never goes through.</summary>
    private static bool IsLink(DirectoryInfo folder)
    {
        try
        {
            return folder.Attributes.HasFlag(FileAttributes.ReparsePoint) && folder.LinkTarget is not null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            // Not known to be a link, so looked into as a folder: the safer of the two for a look that refuses.
            return false;
        }
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
