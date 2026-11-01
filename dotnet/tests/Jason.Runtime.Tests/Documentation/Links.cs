using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>One link that sends a reader where they cannot follow.</summary>
/// <param name="File">The file, repository-relative.</param>
/// <param name="Line">Its line.</param>
/// <param name="Rule">Which rule: <c>a</c> a relative link, <c>c</c> an address into this repository's files, <c>d</c> a page path named in the text.</param>
/// <param name="Target">What it points at: the resolved path.</param>
internal sealed record LinkViolation(string File, int Line, string Rule, string Target)
{
    /// <summary>The violation without its line, which moves as the text around it changes.</summary>
    public string Key => $"{File} [{Rule}] {Target}";

    public override string ToString() => $"{File}:{Line} [{Rule}] {Target}";
}

/// <summary>
/// The links of every Markdown file the tree holds. This repository is read on GitHub, so a link that lands on nothing
/// here is a dead end:
/// <list type="bullet">
/// <item>(a) a relative link resolves to a file or a directory the tree holds — a link inside fenced or inline code is
/// not a link;</item>
/// <item>(c) an address into <c>jason-ai</c>'s files names a path the tree holds, fenced code included;</item>
/// <item>(d) a page under <c>docs/</c> named in words — <c>docs/</c>, a path, <c>.md</c> — is a page the tree holds,
/// inline and fenced code included, since a reader looks for it all the same; a link is (a)'s to read.</item>
/// </list>
/// The release notes — the template and a release's own notes, together the body of every release — are a release's
/// page, not a file of the tree, so a relative link in them lands nowhere at all. Another repository of ours, named
/// anywhere, is <see cref="RepositoryHygiene"/>'s to find.
/// </summary>
internal static partial class Links
{
    public const string NotesTemplate = ".github/release-notes-template.md";

    /// <summary>A release's own notes, which the release puts into the template's body; a tree may hold none.</summary>
    public const string ReleaseNotes = "RELEASE-NOTES.md";

    public static IReadOnlyList<LinkViolation> Violations(string root)
    {
        var tree = new Tree(RepositoryTree.Files(root));
        var violations = new List<LinkViolation>();
        foreach (var file in tree.Files.Where(file => file.EndsWith(".md", StringComparison.Ordinal)))
        {
            var at = file is NotesTemplate or ReleaseNotes ? null : file;
            violations.AddRange(Read(root, file, at, tree));
        }

        return violations;
    }

    private static List<LinkViolation> Read(string root, string file, string? at, Tree tree)
    {
        var text = File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))).ReplaceLineEndings("\n");
        var lines = new LineIndex(text);
        var prose = Prose(text);
        var violations = new List<LinkViolation>();
        // (a) Links, outside fenced and inline code.
        var links = InlineLink().Matches(prose).Select(match => (match.Index, match.Length, Target: match.Groups["target"].Value))
            .Concat(ReferenceDefinition().Matches(prose).Select(match => (match.Index, match.Length, Target: match.Groups["target"].Value)))
            .OrderBy(link => link.Index)
            .ToList();
        foreach (var (index, _, target) in links)
        {
            if (Resolve(target, at) is { } resolved && !tree.Holds(resolved))
            {
                violations.Add(new LinkViolation(file, lines.Of(index), "a", resolved));
            }
        }

        // (c) Addresses into this repository's files, everywhere.
        foreach (Match match in Address().Matches(text))
        {
            if (match.Groups["repository"].Value == "jason-ai"
                && FilePath(match.Groups["host"].Value, match.Groups["rest"].Value) is { } path
                && !tree.Holds(path))
            {
                violations.Add(new LinkViolation(file, lines.Of(match.Index), "c", path));
            }
        }

        // (d) Pages under docs/ named in words, fenced and inline code included, the links (a) already read taken out.
        var words = text.ToCharArray();
        foreach (var (index, length, _) in links)
        {
            Blank(words, index, length);
        }

        foreach (Match match in DocsPage().Matches(new string(words)))
        {
            if (!tree.Holds(match.Value))
            {
                violations.Add(new LinkViolation(file, lines.Of(match.Index), "d", match.Value));
            }
        }

        return violations;
    }

    /// <summary>
    /// Where a relative link lands in the tree, as a path from its root; <c>null</c> for an address, an
    /// anchor, or a link that leaves nothing to check. A relative link in the release notes lands nowhere at all.
    /// </summary>
    private static string? Resolve(string target, string? at)
    {
        if (Scheme().IsMatch(target) || target.StartsWith('#') || target.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var path = Uri.UnescapeDataString(target.Split('#', 2)[0].Split('?', 2)[0]);
        if (path.Length == 0)
        {
            return null;
        }

        if (at is null)
        {
            return $"(a relative link in the release notes) {path}";
        }

        var segments = new List<string>();
        string[] start = path.StartsWith('/') ? [] : at.Split('/')[..^1];
        foreach (var segment in start.Concat(path.Split('/')))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return $"(outside the repository) {path}";
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// The path an address into this repository's files names. The punctuation that ends a sentence or a
    /// clause, or closes emphasis, after an address in text is not part of it: GitHub's links leave it out, and so does
    /// this.
    /// </summary>
    private static string? FilePath(string host, string rest)
    {
        rest = rest.TrimEnd('.', '?', '!', ':', ';', '*', '_', '~');
        var match = host == "raw.githubusercontent.com" ? RawPath().Match(rest) : BlobPath().Match(rest);
        return match.Success ? Uri.UnescapeDataString(match.Groups["path"].Value.Split('#', 2)[0].Split('?', 2)[0]).TrimEnd('/') : null;
    }

    /// <summary>The text with fenced code and inline code blanked out, every offset and line kept.</summary>
    private static string Prose(string text)
    {
        var characters = text.ToCharArray();
        var fenced = false;
        var position = 0;
        foreach (var line in text.Split('\n'))
        {
            var fence = Fence().IsMatch(line);
            if (fenced || fence)
            {
                Blank(characters, position, line.Length);
            }
            else
            {
                foreach (Match code in InlineCode().Matches(line))
                {
                    Blank(characters, position + code.Index, code.Length);
                }
            }

            if (fence)
            {
                fenced = !fenced;
            }

            position += line.Length + 1;
        }

        return new string(characters);
    }

    private static void Blank(char[] characters, int index, int length)
    {
        for (var position = index; position < index + length; position++)
        {
            characters[position] = ' ';
        }
    }

    [GeneratedRegex(@"!?\[[^\]]*\]\(\s*<?(?<target>[^)\s>]+)>?(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:[ \t]*<?(?<target>[^\s>]+)>?", RegexOptions.Multiline)]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"https?://(?<host>github\.com|raw\.githubusercontent\.com)/reply-team/(?<repository>[A-Za-z0-9._-]+?)(?:\.git)?(?=$|[/\s)\]>""'`,#?])(?<rest>[^\s)\]>""'`,]*)", RegexOptions.Multiline)]
    private static partial Regex Address();

    [GeneratedRegex(@"^/(?:blob|tree|raw|edit)/[^/]+/(?<path>.+)$")]
    private static partial Regex BlobPath();

    [GeneratedRegex(@"^/[^/]+/(?<path>.+)$")]
    private static partial Regex RawPath();

    /// <summary><c>docs/</c> at the start of a path, never inside a longer one or an address, then a page.</summary>
    [GeneratedRegex(@"(?<![\w/.-])docs/(?:[\w.-]+/)*[\w.-]+\.md\b")]
    private static partial Regex DocsPage();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();

    [GeneratedRegex(@"^ {0,3}(?:```|~~~)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"`+[^`]*`+")]
    private static partial Regex InlineCode();

    /// <summary>The files the tree holds, and the directories that hold them.</summary>
    private sealed class Tree(IReadOnlyList<string> files)
    {
        private readonly HashSet<string> _files = files.ToHashSet(StringComparer.Ordinal);

        private readonly HashSet<string> _directories = files
            .SelectMany(file => file.Split('/')[..^1].Select((_, index) => string.Join('/', file.Split('/')[..(index + 1)])))
            .ToHashSet(StringComparer.Ordinal);

        public IReadOnlyList<string> Files => files;

        /// <summary>Whether a path is a file or a directory the tree holds; the empty path is its root.</summary>
        public bool Holds(string path) => path.Length == 0 || _files.Contains(path) || _directories.Contains(path);
    }

    /// <summary>Line numbers for offsets into one text.</summary>
    private sealed class LineIndex(string text)
    {
        private readonly int[] _starts = [0, .. text.Index().Where(pair => pair.Item == '\n').Select(pair => pair.Index + 1)];

        public int Of(int offset)
        {
            var found = Array.BinarySearch(_starts, offset);
            return (found >= 0 ? found : ~found - 1) + 1;
        }
    }
}
