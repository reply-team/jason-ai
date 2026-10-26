using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>One place where published text sends a reader somewhere they cannot follow.</summary>
/// <param name="File">The file as it is in this repository.</param>
/// <param name="Line">Its line.</param>
/// <param name="Rule">Which rule: <c>a</c> a relative link, <c>b</c> another repository of ours, <c>c</c> a link into the
/// public repository at a path it does not publish, <c>d</c> a path that is not published, named in text.</param>
/// <param name="Target">What it points at: the resolved path, the repository, or the words.</param>
internal sealed record LinkViolation(string File, int Line, string Rule, string Target)
{
    /// <summary>The violation without its line, which moves as the text around it changes.</summary>
    public string Key => $"{File} [{Rule}] {Target}";

    public override string ToString() => $"{File}:{Line} [{Rule}] {Target}";
}

/// <summary>
/// The rules every published text is held to. Everything the open set publishes, the public repository's own files
/// (read as they are at that repository's root, where they land) and the release notes — the template and a release's
/// own notes, together the body of every public release — are read; a reader of any of them can open only what is published, so:
/// <list type="bullet">
/// <item>(a) a relative link resolves to a published path that exists — a link inside fenced code is not a link;</item>
/// <item>(b) nothing names a <c>reply-team/jason-*</c> repository other than <c>jason-ai</c>, nor the private repository
/// by its name alone — in an address or in words, fenced code included, and in every published file, not only the pages:
/// a plugin's module, a manifest or an install script is published text too;</item>
/// <item>(c) an address into <c>jason-ai</c>'s files names a published path, fenced code included;</item>
/// <item>(d) no text names a path that is not published — a page of <c>docs/</c> outside the open set, the runtime's
/// source, the files that build it — except where <see cref="NamedOnPurpose"/> says so, by exact count.</item>
/// </list>
/// </summary>
internal static partial class Links
{
    public const string NotesTemplate = ".github/release-notes-template.md";

    /// <summary>A release's own notes, which the release puts into the template's body; a tree may hold none.</summary>
    public const string ReleaseNotes = "RELEASE-NOTES.md";

    /// <summary>
    /// Published text that names an unpublished page on purpose, and how many times: a verified skill, whose text
    /// changes only with a new reading by a host, and a released changelog entry, which is history.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> NamedOnPurpose = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["skills/runtime/operating-the-installation/SKILL.md"] = 2,
        ["plugins/ntfy/CHANGELOG.md"] = 1,
    };

    public static IReadOnlyList<LinkViolation> Violations(string root, IReadOnlyList<string> openSet, IReadOnlyDictionary<string, int> namedOnPurpose)
    {
        var published = new Published(root, openSet);
        var violations = new List<LinkViolation>();

        foreach (var (file, at) in published.Texts())
        {
            violations.AddRange(Read(root, file, at, published, namedOnPurpose));
        }

        // (b) in every other file a release publishes. The public check's hygiene step reads every line a change adds
        // there, so this is its twin here: the two read the private repository's name in the same files.
        foreach (var file in published.OtherTexts())
        {
            var text = File.ReadAllText(Path.Combine(root, file)).ReplaceLineEndings("\n");
            var lines = new LineIndex(text);
            foreach (Match match in OtherRepository().Matches(text))
            {
                violations.Add(new LinkViolation(file, lines.Of(match.Index), "b", match.Value));
            }
        }

        return violations;
    }

    private static List<LinkViolation> Read(string root, string file, string? at, Published published, IReadOnlyDictionary<string, int> namedOnPurpose)
    {
        var text = File.ReadAllText(Path.Combine(root, file)).ReplaceLineEndings("\n");
        var lines = new LineIndex(text);
        var prose = Prose(text);
        var violations = new List<LinkViolation>();

        // (a) Links, outside fenced and inline code.
        var links = InlineLink().Matches(prose).Select(match => (match.Index, match.Length, Target: match.Groups["target"].Value))
            .Concat(ReferenceDefinition().Matches(prose).Select(match => (match.Index, match.Length, Target: match.Groups["target"].Value)))
            .ToList();
        foreach (var (index, _, target) in links)
        {
            if (Resolve(target, at) is { } resolved && !published.Holds(resolved))
            {
                violations.Add(new LinkViolation(file, lines.Of(index), "a", resolved));
            }
        }

        // (b) Another repository of ours, named anywhere: in an address or in words, fenced code included.
        foreach (Match match in OtherRepository().Matches(text))
        {
            violations.Add(new LinkViolation(file, lines.Of(match.Index), "b", match.Value));
        }

        // (c) Addresses into the public repository's files, everywhere.
        foreach (Match match in Address().Matches(text))
        {
            if (match.Groups["repository"].Value == "jason-ai"
                && FilePath(match.Groups["host"].Value, match.Groups["rest"].Value) is { } path
                && !published.Holds(path))
            {
                violations.Add(new LinkViolation(file, lines.Of(match.Index), "c", path));
            }
        }

        // (d) Paths named in text, fenced code included, the links (a) already read taken out.
        var words = text.ToCharArray();
        foreach (var (index, length, _) in links)
        {
            for (var position = index; position < index + length; position++)
            {
                if (words[position] != '\n')
                {
                    words[position] = ' ';
                }
            }
        }

        var named = new List<LinkViolation>();
        var unlinked = new string(words);
        foreach (Match match in Page().Matches(unlinked))
        {
            var name = match.Groups["name"].Value + ".md";
            var path = match.Groups["docs"].Success ? "docs/" + name : null;
            if (path is not null ? !published.Holds(path) : published.IsUnpublishedPage(name))
            {
                named.Add(new LinkViolation(file, lines.Of(match.Index), "d", path ?? name));
            }
        }

        named.AddRange(Unpublished().Matches(unlinked).Select(match => new LinkViolation(file, lines.Of(match.Index), "d", match.Value.TrimEnd('.', ','))));

        if (!namedOnPurpose.TryGetValue(file, out var expected))
        {
            violations.AddRange(named);
        }
        else if (named.Count != expected)
        {
            violations.Add(new LinkViolation(file, 0, "d", $"named on purpose {expected} time(s), found {named.Count}"));
            violations.AddRange(named);
        }

        return violations;
    }

    /// <summary>
    /// Where a relative link lands in the published tree, as a path from its root; <c>null</c> for an address, an
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
    /// The published path an address into the public repository's files names. The punctuation that ends a sentence or a
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

    /// <summary>
    /// <c>reply-team/jason-</c> and any name but <c>ai</c>, or the private repository's name on its own.
    /// </summary>
    [GeneratedRegex(@"reply-team/jason-(?!ai(?![\w-]))[\w-]+|(?<![\w/-])jason-internal(?![\w-])")]
    private static partial Regex OtherRepository();

    [GeneratedRegex(@"^/(?:blob|tree|raw|edit)/[^/]+/(?<path>.+)$")]
    private static partial Regex BlobPath();

    [GeneratedRegex(@"^/[^/]+/(?<path>.+)$")]
    private static partial Regex RawPath();

    [GeneratedRegex(@"(?<![\w/.-])(?<docs>docs/)?(?<name>[\w.-]+)\.md\b")]
    private static partial Regex Page();

    [GeneratedRegex(@"(?<![\w/.-])(?:runtime/[\w./-]*|CLAUDE\.md|global\.json|Jason\.slnx|dotnet-tools\.json)")]
    private static partial Regex Unpublished();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();

    [GeneratedRegex(@"^ {0,3}(?:```|~~~)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"`+[^`]*`+")]
    private static partial Regex InlineCode();

    /// <summary>The published tree, as this repository holds it.</summary>
    private sealed class Published(string root, IReadOnlyList<string> openSet)
    {
        private readonly string _own = Path.Combine(root, Publication.PublicOwnDirectory);

        private readonly HashSet<string> _unpublishedPages = Directory.Exists(Path.Combine(root, "docs"))
            ? Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md")
                .Select(file => Path.GetFileName(file))
                .Where(name => !openSet.Contains("docs/" + name, StringComparer.Ordinal))
                .ToHashSet(StringComparer.Ordinal)
            : [];

        /// <summary>Every published text, as its file here and the path it is published at (none for the notes).</summary>
        public IEnumerable<(string File, string? At)> Texts()
        {
            foreach (var entry in openSet)
            {
                if (entry.EndsWith('/'))
                {
                    foreach (var relative in OpenSet.Files(root, entry).Where(file => file.EndsWith(".md", StringComparison.Ordinal)))
                    {
                        yield return (relative, relative);
                    }
                }
                else if (entry.EndsWith(".md", StringComparison.Ordinal))
                {
                    yield return (entry, entry);
                }
            }

            if (Directory.Exists(_own))
            {
                foreach (var file in Directory.EnumerateFiles(_own, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var at = Path.GetRelativePath(_own, file).Replace(Path.DirectorySeparatorChar, '/');
                    yield return ($"{Publication.PublicOwnDirectory}/{at}", at);
                }
            }

            if (File.Exists(Path.Combine(root, NotesTemplate)))
            {
                yield return (NotesTemplate, null);
            }

            if (File.Exists(Path.Combine(root, ReleaseNotes)))
            {
                yield return (ReleaseNotes, null);
            }
        }

        /// <summary>
        /// Every published file that is not a page and holds text, as its file here: what rule (b) reads beyond the pages.
        /// A file with a NUL byte in its first 8 KB is binary, and holds no words.
        /// </summary>
        public IEnumerable<string> OtherTexts()
        {
            var files = new List<string>();
            foreach (var entry in openSet)
            {
                var full = Path.Combine(root, entry.TrimEnd('/'));
                if (entry.EndsWith('/'))
                {
                    files.AddRange(OpenSet.Files(root, entry).Select(relative => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))));
                }
                else if (File.Exists(full))
                {
                    files.Add(full);
                }
            }

            if (Directory.Exists(_own))
            {
                files.AddRange(Directory.EnumerateFiles(_own, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            }

            return files
                .Where(file => !file.EndsWith(".md", StringComparison.Ordinal) && !Binary(file))
                .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
        }

        private static bool Binary(string file)
        {
            using var stream = File.OpenRead(file);
            var head = new byte[8192];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return Array.IndexOf(head, (byte)0, 0, read) >= 0;
        }

        /// <summary>Whether a path is published and there to be read.</summary>
        public bool Holds(string path)
        {
            if (path == "VERSION")
            {
                return true;
            }

            var own = Path.Combine(_own, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(own) || Directory.Exists(own))
            {
                return true;
            }

            var listed = openSet.Any(entry => entry.EndsWith('/')
                ? path == entry.TrimEnd('/') || path.StartsWith(entry, StringComparison.Ordinal)
                : path == entry);
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            return listed && (File.Exists(full) || Directory.Exists(full));
        }

        /// <summary>Whether a bare page name is one of <c>docs/</c>'s pages that is not published.</summary>
        public bool IsUnpublishedPage(string name) => _unpublishedPages.Contains(name);
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
