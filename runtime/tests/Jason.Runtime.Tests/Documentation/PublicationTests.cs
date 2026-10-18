using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// What a release publishes to the public repository, held here because this repository is where it is written: the
/// open set, the public repository's own files, and the links in all of it. Everything published is read by people
/// who cannot open anything that is not, so nothing published may send them there.
/// </summary>
public sealed partial class PublicationTests
{
    [Fact]
    public void The_open_set_is_a_sorted_list_of_published_paths_that_exist()
    {
        var root = Publication.RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, Publication.OpenSetFile)), $"{Publication.OpenSetFile} does not exist.");

        var problems = OpenSet.Problems(OpenSet.Lines(root), path => Publication.KindOf(root, path));

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Theory]
    [InlineData("runtime/", "the runtime's source is never published")]
    [InlineData("runtime/src/Jason.App/", "the runtime's source is never published")]
    [InlineData(".github/workflows/", "the pipeline is never published")]
    [InlineData(".github/publish/public/", "the pipeline is never published")]
    [InlineData("CLAUDE.md", "CLAUDE.md is never published")]
    [InlineData("global.json", "global.json is never published")]
    [InlineData("dotnet-tools.json", "dotnet-tools.json is never published")]
    [InlineData("plugins/TestResults/", "TestResults is never published")]
    [InlineData("docs/", "docs/ is published only as")]
    [InlineData("docs/routing.md", "docs/ is published only as")]
    [InlineData("docs/contracts/operations/", "docs/ is published only as")]
    [InlineData("skills/*.md", "is a pattern")]
    [InlineData("/README.md", "is not a plain path")]
    [InlineData("plugins/../runtime/", "is not a plain path")]
    [InlineData("# the skills", "is a comment")]
    [InlineData(" install/", "has whitespace around")]
    [InlineData("missing/", "does not exist")]
    public void The_open_set_refuses_an_entry_that_would_publish_the_wrong_thing(string entry, string said)
    {
        var problems = OpenSet.Problems(Sorted([.. Valid, entry]), Kind);

        Assert.Contains(problems, problem => problem.Contains(said, StringComparison.Ordinal));
    }

    [Fact]
    public void A_valid_list_has_no_problems()
    {
        var problems = OpenSet.Problems(Valid, Kind);

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void The_open_set_refuses_a_list_out_of_order_twice_listed_or_with_a_blank_line()
    {
        Assert.Contains(OpenSet.Problems([.. Valid.Reverse()], Kind), problem => problem.Contains("ordinal order", StringComparison.Ordinal));
        Assert.Contains(OpenSet.Problems(Sorted([.. Valid, "README.md"]), Kind), problem => problem.Contains("listed twice", StringComparison.Ordinal));
        Assert.Contains(OpenSet.Problems([.. Valid, string.Empty], Kind), problem => problem.Contains("is blank", StringComparison.Ordinal));
    }

    [Fact]
    public void The_open_set_refuses_a_file_named_as_a_directory_and_a_directory_named_as_a_file()
    {
        Assert.Contains(OpenSet.Problems(Sorted([.. Valid, "LICENSE/"]), Kind), problem => problem.Contains("names a file but ends with '/'", StringComparison.Ordinal));
        Assert.Contains(OpenSet.Problems(Sorted([.. Valid, "plugins"]), Kind), problem => problem.Contains("names a directory", StringComparison.Ordinal));
    }

    [Fact]
    public void The_open_set_names_every_public_document()
    {
        var problems = OpenSet.Problems([.. Valid.Where(line => line != "docs/plugins.md")], Kind);

        Assert.Contains(problems, problem => problem.Contains("'docs/plugins.md' is missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("LICENSE-RUNTIME.md")]
    [InlineData("THIRD-PARTY-NOTICES")]
    [InlineData("TRADEMARKS.md")]
    public void The_open_set_names_every_licence_text(string licence)
    {
        var problems = OpenSet.Problems([.. Valid.Where(line => line != licence)], Kind);

        Assert.Contains($"'{licence}' is missing: a release publishes every licence text it ships.", problems);
    }

    /// <summary>
    /// The public repository's own files are written here and copied to its root by every release, so they are a
    /// fixed list: a file added to the directory is a file published, and that is a decision, not a side effect.
    /// </summary>
    [Fact]
    public void The_public_repository_s_own_files_are_exactly_these()
    {
        var directory = Path.Combine(Publication.RepositoryRoot(), Publication.PublicOwnDirectory);
        Assert.True(Directory.Exists(directory), $"{Publication.PublicOwnDirectory} does not exist.");

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);

        Assert.Equal([".github/workflows/check.yml", ".github/workflows/release.yml", ".gitignore", "CONTRIBUTING.md", "SECURITY.md"], files);
    }

    /// <summary>
    /// The public repository's <c>.gitignore</c> lands at its root beside everything a release publishes, so it must
    /// ignore none of it: an ignored path would be published by the release and then dropped by the next clone.
    /// </summary>
    [Fact]
    public void The_public_repository_s_gitignore_ignores_nothing_a_release_publishes()
    {
        var root = Publication.RepositoryRoot();
        var patterns = File.ReadAllLines(Path.Combine(root, Publication.PublicOwnDirectory, ".gitignore"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        // The patterns this test can read: a name, a directory name, or a name with one leading '*'. Anything else —
        // a path, a negation, '**', '?' — is a rule this reading would get wrong, so it is refused rather than guessed.
        var unread = patterns.Where(pattern => !ReadablePattern().IsMatch(pattern)).ToList();
        Assert.True(unread.Count == 0, $"Patterns this test cannot read: {string.Join(", ", unread)}");

        var ignored = OpenSet.Lines(root)
            .SelectMany(entry => entry.EndsWith('/') ? OpenSet.Files(root, entry) : [entry])
            .Where(path => patterns.Any(pattern => Ignores(pattern, path)))
            .ToList();

        Assert.True(ignored.Count == 0, $"The public .gitignore ignores published paths:\n{string.Join('\n', ignored)}");
    }

    [Theory]
    [InlineData("node_modules/", "plugins/p/node_modules/x.js", true)]
    [InlineData("node_modules/", "plugins/node_modules", false)]
    [InlineData(".env", "skills/.env", true)]
    [InlineData("*.local.json", "install/feeds/a.local.json", true)]
    [InlineData("*.md", "README.md", true)]
    [InlineData(".vs/", "docs/contracts/README.md", false)]
    public void A_gitignore_pattern_is_read_as_git_reads_it(string pattern, string path, bool ignored) =>
        Assert.Equal(ignored, Ignores(pattern, path));

    /// <summary>How git reads the three shapes of pattern this file uses, against a file's path.</summary>
    private static bool Ignores(string pattern, string path)
    {
        var segments = path.Split('/');
        if (pattern.EndsWith('/'))
        {
            // A directory pattern: some directory on the way to the file has that name.
            return segments[..^1].Contains(pattern.TrimEnd('/'), StringComparer.Ordinal);
        }

        return pattern.StartsWith('*')
            ? segments.Any(segment => segment.EndsWith(pattern[1..], StringComparison.Ordinal))
            : segments.Contains(pattern, StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^(?:\*)?[A-Za-z0-9._-]+/?$")]
    private static partial Regex ReadablePattern();

    /// <summary>
    /// Where a person writes for help and where a vulnerability is reported is one address, said in two places — the
    /// README's "Getting help" and SECURITY.md — so changing it is one line in each, and neither can drift alone.
    /// </summary>
    [Fact]
    public void The_help_address_is_one_address_in_both_places_that_give_it()
    {
        var root = Publication.RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("\n## Getting help\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "README.md has no \"Getting help\" section.");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var help = readme[start..(end < 0 ? readme.Length : end)];
        var security = File.ReadAllText(Path.Combine(root, Publication.PublicOwnDirectory, "SECURITY.md"));

        var inHelp = Assert.Single(Address().Matches(help)).Value;
        var inSecurity = Assert.Single(Address().Matches(security)).Value;
        Assert.Equal(inHelp, inSecurity);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Address();

    /// <summary>What an outsider reads names nothing they cannot open: no tracker key, and no other repository of ours.</summary>
    [Theory]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("SECURITY.md")]
    public void The_public_repository_s_own_texts_name_nothing_an_outsider_cannot_open(string file)
    {
        var text = File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), Publication.PublicOwnDirectory, file));

        Assert.DoesNotMatch(@"\bREPLY-[0-9]+", text);
        Assert.DoesNotContain("atlassian", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"reply-team/jason-(?!ai\b)", text);
    }

    /// <summary>A list that keeps every rule, over a tree where every path exists.</summary>
    private static readonly string[] Valid = Sorted([.. OpenSet.Documents, .. OpenSet.Licences, "README.md", "install/"]);

    private static string[] Sorted(string[] lines) => [.. lines.Order(StringComparer.Ordinal)];

    /// <summary>
    /// A tree in which every path exists but one: the files and directories the valid list names are what they are
    /// whatever the entry's spelling, and any other path is a directory when it ends with '/'.
    /// </summary>
    private static PathKind Kind(string path) => path.TrimEnd('/') switch
    {
        "missing" => PathKind.Missing,
        "README.md" or "LICENSE" or "docs/INSTALL.md" or "docs/golden-path.md" or "docs/plugins.md" => PathKind.File,
        "plugins" or "install" or "docs/contracts" => PathKind.Directory,
        _ => path.EndsWith('/') ? PathKind.Directory : PathKind.File,
    };
}
