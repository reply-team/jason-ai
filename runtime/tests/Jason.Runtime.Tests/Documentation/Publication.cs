namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// What a release publishes from this repository: the open set, which <c>.github/publish/open-set.txt</c> lists, and
/// the public repository's own files under <c>.github/publish/public/</c>, which land at that repository's root.
/// </summary>
internal static class Publication
{
    public const string OpenSetFile = ".github/publish/open-set.txt";

    public const string PublicOwnDirectory = ".github/publish/public";

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>A repository-relative path, with forward slashes, as it is on disk under <paramref name="root"/>.</summary>
    public static PathKind KindOf(string root, string relative)
    {
        var full = Path.Combine(root, relative.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(full) ? PathKind.Directory : File.Exists(full) ? PathKind.File : PathKind.Missing;
    }
}

internal enum PathKind
{
    Missing,
    File,
    Directory,
}

/// <summary>The open set's rules, as one function a test can hand any list to.</summary>
internal static class OpenSet
{
    /// <summary>
    /// The only entries under <c>docs/</c>: with the root <c>README.md</c>, the public documents, and the Runtime API's
    /// recorded exchanges under <c>docs/api/</c>. The
    /// rest of <c>docs/</c> describes the runtime's design and is not published.
    /// </summary>
    public static readonly string[] Documents = ["docs/INSTALL.md", "docs/api/", "docs/contracts/", "docs/golden-path.md", "docs/plugins.md", "docs/ui.md"];

    /// <summary>
    /// The licence texts at the root: the MIT licence of the published tree, the runtime's own licence, the third-party
    /// notices and the note on the marks. Every archive carries the runtime's licence and the notices beside the
    /// executable, the pages link the runtime's licence in the public tree, and that licence links the note there, so the
    /// public tree holds all four.
    /// </summary>
    public static readonly string[] Licences = ["LICENSE", "LICENSE-RUNTIME.md", "THIRD-PARTY-NOTICES", "TRADEMARKS.md"];

    private static readonly string[] NeverPublished = ["CLAUDE.md", "DEVELOPMENT.md", "global.json", "dotnet-tools.json", "TestResults"];

    /// <summary>
    /// The files under a directory entry as a release publishes them: what git holds and does not ignore, when the root
    /// is a repository; every file on disk otherwise (the trees the tests build). A developer's <c>ui/node_modules</c>
    /// is ignored here and never published, so no guard reads it.
    /// </summary>
    public static IReadOnlyList<string> Files(string root, string entry)
    {
        var relativeRoot = entry.TrimEnd('/');
        var full = Path.Combine(root, relativeRoot);
        if (!Directory.Exists(full))
        {
            return [];
        }

        if (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git")))
        {
            var listed = Workflows.Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", relativeRoot);
            return [.. listed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];
        }

        return [.. Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The file's lines as written, a final newline not counted as an empty last line.</summary>
    public static IReadOnlyList<string> Lines(string root)
    {
        var text = File.ReadAllText(Path.Combine(root, Publication.OpenSetFile));
        var lines = text.Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    /// <summary>Every way the list breaks the rules, one sentence each; empty when it keeps them all.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<string> lines, Func<string, PathKind> kind)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var at = $"line {index + 1}";

            if (line.Length == 0)
            {
                problems.Add($"{at} is blank.");
                continue;
            }

            if (line != line.Trim())
            {
                problems.Add($"{at} has whitespace around '{line.Trim()}'.");
                continue;
            }

            if (line.StartsWith('#'))
            {
                problems.Add($"{at} is a comment; the list holds paths only.");
                continue;
            }

            if (line.IndexOfAny(['*', '?', '[', ']', '{', '}']) >= 0)
            {
                problems.Add($"{at}, '{line}', is a pattern; the list names paths, not globs.");
                continue;
            }

            var segments = line.TrimEnd('/').Split('/');
            if (line.StartsWith('/') || line.Contains('\\') || segments.Any(segment => segment is "" or "." or ".."))
            {
                problems.Add($"{at}, '{line}', is not a plain path relative to the repository root.");
                continue;
            }

            if (!seen.Add(line))
            {
                problems.Add($"{at}, '{line}', is listed twice.");
            }

            if (previous is not null && string.CompareOrdinal(previous, line) > 0)
            {
                problems.Add($"{at}, '{line}', comes after '{previous}': the list is kept in ordinal order.");
            }

            previous = line;

            if (segments[0] == "runtime")
            {
                problems.Add($"{at}, '{line}': the runtime's source is never published.");
            }

            if (segments[0] == ".github")
            {
                problems.Add($"{at}, '{line}': the pipeline is never published; the public repository's own files live under .github/publish/public/.");
            }

            foreach (var segment in segments.Where(segment => NeverPublished.Contains(segment, StringComparer.Ordinal)))
            {
                problems.Add($"{at}, '{line}': {segment} is never published.");
            }

            if (segments[0] == "docs" && !Documents.Contains(line, StringComparer.Ordinal))
            {
                problems.Add($"{at}, '{line}': docs/ is published only as {string.Join(", ", Documents)}.");
            }

            switch (kind(line))
            {
                case PathKind.Missing:
                    problems.Add($"{at}, '{line}', does not exist.");
                    break;
                case PathKind.File when line.EndsWith('/'):
                    problems.Add($"{at}, '{line}', names a file but ends with '/'.");
                    break;
                case PathKind.Directory when !line.EndsWith('/'):
                    problems.Add($"{at}, '{line}', names a directory; a directory entry ends with '/'.");
                    break;
            }
        }

        foreach (var document in Documents.Where(document => !seen.Contains(document)))
        {
            problems.Add($"'{document}' is missing: it is one of the public documents.");
        }

        foreach (var licence in Licences.Where(licence => !seen.Contains(licence)))
        {
            problems.Add($"'{licence}' is missing: a release publishes every licence text it ships.");
        }

        return problems;
    }
}
