namespace Jason.Cli.Files;

/// <summary>
/// The one way the CLI deletes a directory tree of its own: the uninstall's purge, and a skills fetch that was staged and
/// not finished.
/// </summary>
internal static class DirectoryTrees
{
    /// <summary>
    /// A directory and everything under it, if it is there. A caller that waits out a held file asks again after each
    /// try, by which time what was not held is already gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree is made deletable first. Windows refuses to delete a file or a directory marked read-only, and git marks
    /// the pack files of every clone it makes so; the skill packs in the data directory are such clones, and a purge
    /// stopped at the first of them. So on Windows the mark is cleared on everything in the tree. Elsewhere it is cleared
    /// on directories only: a directory without its write permission keeps what is in it, while a file goes whatever its
    /// own mode says, and changing the mode of a file this account does not own is refused. Nothing else is changed,
    /// with one exception: the mark belongs to the file, not to its name, so a file in the tree that has a second name
    /// outside it, a hard link such as a local clone makes, is no longer marked read-only under that name either.
    /// </para>
    /// <para>
    /// A link in the tree is removed there, as itself, before the delete, and is never gone through: what it points at
    /// is outside the tree and keeps its files and their marks. Left to the recursive delete, a junction was asked to
    /// be unmounted as if it were a volume's mount point, which Windows refuses an ordinary account and answers an
    /// elevated one with "the parameter is incorrect"; .NET removes the link after that refusal anyway, and then throws
    /// it, leaving the directories that held the link behind.
    /// </para>
    /// </remarks>
    public static void Delete(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var root = new DirectoryInfo(path);
        if (IsLink(root))
        {
            RemoveLink(root);
            return;
        }

        Deletable(root);
        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Clears the read-only mark under <paramref name="directory"/> and on it — a file's on Windows only — removing every
    /// link it meets.
    /// </summary>
    private static void Deletable(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false }))
        {
            if (IsLink(entry))
            {
                RemoveLink(entry);
                continue;
            }

            // Another kind of reparse point — a placeholder of a cloud folder, say — is neither entered nor changed:
            // it points nowhere else, and the recursive delete treats it as it always has.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            if (entry is DirectoryInfo inner)
            {
                Deletable(inner);
            }
            else if (OperatingSystem.IsWindows())
            {
                Writable(entry);
            }
        }

        Writable(directory);
    }

    /// <summary>
    /// The read-only mark cleared where it is set, as far as this account may clear it. A mark it may not clear is left to
    /// the delete, which then fails on that entry and says so.
    /// </summary>
    private static void Writable(FileSystemInfo entry)
    {
        try
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left to the delete, which names the entry it cannot remove.
        }
    }

    /// <summary>A symbolic link or a junction, as the data directory's belt reads one.</summary>
    private static bool IsLink(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && entry.LinkTarget is not null;

    /// <summary>
    /// The link itself. On Windows a link to a directory is a directory entry removed as one, never recursively; elsewhere
    /// every link is unlinked like a file.
    /// </summary>
    private static void RemoveLink(FileSystemInfo link)
    {
        if (OperatingSystem.IsWindows() && link is DirectoryInfo)
        {
            Directory.Delete(link.FullName, recursive: false);
        }
        else
        {
            File.Delete(link.FullName);
        }
    }
}
