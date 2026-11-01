namespace Jason.Runtime.Tests.Documentation;

/// <summary>This repository's working tree, as the guards that read the whole of it see it.</summary>
internal static class RepositoryTree
{
    /// <summary>The repository's root: the nearest directory above the test assembly that holds <c>global.json</c>.</summary>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// The files under <paramref name="under"/> (the whole tree when it is empty), as repository-relative paths with
    /// forward slashes, in ordinal order: what git holds or would hold and does not ignore, when the root is a
    /// repository; every file on disk otherwise (the trees the tests build). A developer's <c>ui/node_modules</c> or
    /// build output is ignored here, so no guard reads it; a file deleted from the disk but not yet from the index is
    /// left out.
    /// </summary>
    public static IReadOnlyList<string> Files(string root, string under = "")
    {
        var relativeRoot = under.TrimEnd('/');
        var full = relativeRoot.Length == 0 ? root : Path.Combine(root, relativeRoot);
        if (!Directory.Exists(full))
        {
            return [];
        }

        if (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git")))
        {
            string[] arguments = relativeRoot.Length == 0
                ? ["ls-files", "-z", "--cached", "--others", "--exclude-standard"]
                : ["ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", relativeRoot];
            var listed = Workflows.Git(root, arguments);
            return [.. listed.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .Where(file => File.Exists(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))))
                .Order(StringComparer.Ordinal)];
        }

        return [.. Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Whether a file holds text: a file that starts with a UTF-16 byte order mark is text, whatever NUL bytes its
    /// characters carry, and is read through that mark; any other file with a NUL byte in its first 8 KB is binary, and
    /// holds no words.
    /// </summary>
    public static bool IsText(string root, string relative)
    {
        using var stream = File.OpenRead(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var head = new byte[8192];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var utf16 = read >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF));
        return utf16 || Array.IndexOf(head, (byte)0, 0, read) < 0;
    }
}
