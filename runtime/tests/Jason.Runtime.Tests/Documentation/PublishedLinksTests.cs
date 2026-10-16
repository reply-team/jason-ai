namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The links and the paths in everything a release publishes. Its readers can open only what is published: a link
/// to a page that stays here, or a path named that they will never see, is a dead end in the public repository.
/// </summary>
public sealed class PublishedLinksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", "published-links-" + Guid.NewGuid().ToString("N"));

    public PublishedLinksTests()
    {
        Directory.CreateDirectory(_root);
        Write(".github/publish/open-set.txt", "README.md\ndocs/public.md\nplugins/\n");
        Write("docs/public.md", "# Public\n");
        Write("docs/private.md", "# Private\n");
        Write("plugins/p/README.md", "# A plugin\n");
        Write(".github/publish/public/SECURITY.md", "# Security\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Nothing_published_sends_a_reader_where_they_cannot_follow()
    {
        var root = Publication.RepositoryRoot();
        var found = Links.Violations(root, OpenSet.Lines(root), Links.NamedOnPurpose);

        Assert.True(
            found.Count == 0,
            $"Published text that sends a reader where they cannot follow ({found.Count}):\n"
            + string.Join('\n', found.Select(violation => "  " + violation)));
    }

    [Theory]
    [InlineData("[p](docs/private.md)", "README.md [a] docs/private.md")]
    [InlineData("[p][r]\n\n[r]: docs/private.md", "README.md [a] docs/private.md")]
    [InlineData("![p](docs/private.md)", "README.md [a] docs/private.md")]
    [InlineData("[p](docs/nothing.md)", "README.md [a] docs/nothing.md")]
    [InlineData("[p](../outside.md)", "README.md [a] (outside the repository) ../outside.md")]
    [InlineData("<https://github.com/reply-team/jason-internal/blob/main/README.md>", "README.md [b] reply-team/jason-internal")]
    [InlineData("```sh\ngit clone https://github.com/reply-team/jason-internal.git\n```", "README.md [b] reply-team/jason-internal")]
    [InlineData("The source lives in reply-team/jason-internal.", "README.md [b] reply-team/jason-internal")]
    [InlineData("```\ncd jason-internal\n```", "README.md [b] jason-internal")]
    [InlineData("```\nhttps://github.com/reply-team/jason-ai/blob/v0.6.0/docs/private.md\n```", "README.md [c] docs/private.md")]
    [InlineData("[p](https://github.com/reply-team/jason-ai/tree/main/runtime/src)", "README.md [c] runtime/src")]
    [InlineData("https://raw.githubusercontent.com/reply-team/jason-ai/main/CLAUDE.md", "README.md [c] CLAUDE.md")]
    [InlineData("The design is at https://github.com/reply-team/jason-ai/blob/main/docs/private.md.", "README.md [c] docs/private.md")]
    [InlineData("```text\nThe instructions are\n<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/nothing.md>. Read all of it\n```", "README.md [c] docs/nothing.md")]
    [InlineData("```\n[p](docs/private.md)\n```", "README.md [d] docs/private.md")]
    [InlineData("See `docs/private.md`.", "README.md [d] docs/private.md")]
    [InlineData("See private.md.", "README.md [d] private.md")]
    [InlineData("Run `dotnet build runtime/Jason.slnx`.", "README.md [d] runtime/Jason.slnx")]
    [InlineData("The guide is CLAUDE.md, and the SDK is pinned in global.json.", "README.md [d] CLAUDE.md", "README.md [d] global.json")]
    public void A_published_text_that_leaves_what_is_published_is_named(string text, params string[] expected)
    {
        Write("README.md", text + "\n");

        Assert.Equal(expected, Found());
    }

    [Theory]
    [InlineData("[p](docs/public.md) [p](plugins/p/README.md) [p](plugins/p) [p](#here) [p](https://example.com/x.md)")]
    [InlineData("[p](./docs/public.md#a-heading) [p](docs/../docs/public.md) [v](VERSION)")]
    [InlineData("https://github.com/reply-team/jason-ai/releases/latest/download/install.sh and https://github.com/reply-team/jason-ai.git")]
    [InlineData("[install.sh](https://jasonharness.ai/releases/latest/download/install.sh), `jasonharness.ai` and <https://jasonharness.ai/releases/tag/v0.7.0>")]
    [InlineData("reply-team/jason-ai, jason-linux-x64.tar.gz and jason-internals-of-a-plugin name nothing private.")]
    [InlineData("https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/public.md and https://github.com/reply-team/reply-cli")]
    [InlineData("The note is published at https://github.com/reply-team/jason-ai/blob/main/docs/public.md. So is https://github.com/reply-team/jason-ai/blob/main/VERSION: read it.")]
    [InlineData("`[not a link](../outside.md)` is code, and code links nowhere")]
    [InlineData("skills/runtime/private.md is another directory's page, and notes.md names no page of docs/.")]
    [InlineData("The page is at https://github.com/reply-team/jason-ai/blob/main/docs/public.md; read it.")]
    [InlineData("```text\nThe instructions are\n<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/public.md>. Read all of it\n```")]
    public void A_published_text_that_stays_in_what_is_published_is_not(string text)
    {
        Write("README.md", text + "\n");

        Assert.Empty(Found());
    }

    /// <summary>
    /// Another repository of ours is read in every published file, not only in Markdown: a plugin's module, a manifest or
    /// an install script is published text too, and its readers can open no private repository either.
    /// </summary>
    [Fact]
    public void Another_repository_of_ours_named_in_a_published_file_that_is_not_markdown_is_named()
    {
        var bare = "jason-" + "internal";
        Write("README.md", "# Readme\n");
        Write("plugins/p/main.js", $"// Built from {bare}, see reply-team/{bare}.\n");

        Assert.Equal([$"plugins/p/main.js [b] {bare}", $"plugins/p/main.js [b] reply-team/{bare}"], Found());
    }

    /// <summary>
    /// And so is one in a file the public repository holds of its own: its workflows are published beside the open set,
    /// read there by the same readers.
    /// </summary>
    [Fact]
    public void Another_repository_of_ours_named_in_a_public_repository_file_that_is_not_markdown_is_named()
    {
        var bare = "jason-" + "internal";
        Write("README.md", "# Readme\n");
        Write(".github/publish/public/.github/workflows/check.yml", $"# What reaches the public repository, built from {bare}.\n");

        Assert.Equal([$".github/publish/public/.github/workflows/check.yml [b] {bare}"], Found());
    }

    /// <summary>The public repository's own files land at its root, so their links are read from there.</summary>
    [Fact]
    public void A_public_repository_file_is_read_as_it_is_at_that_repository_s_root()
    {
        Write("README.md", "# Readme\n");
        Write(".github/publish/public/CONTRIBUTING.md", "[readme](README.md), [security](SECURITY.md), [plugins](plugins/p/README.md), [gone](CHANGES.md)\n");

        Assert.Equal([".github/publish/public/CONTRIBUTING.md [a] CHANGES.md"], Found());
    }

    /// <summary>The release notes are the body of a public release: a relative link there resolves nowhere.</summary>
    [Fact]
    public void The_release_notes_are_read_and_a_relative_link_in_them_lands_nowhere()
    {
        Write("README.md", "# Readme\n");
        Write(Links.NotesTemplate, "[why](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/private.md) and [this](docs/public.md)\n");

        Assert.Equal(
            [".github/release-notes-template.md [a] (a relative link in the release notes) docs/public.md", ".github/release-notes-template.md [c] docs/private.md"],
            Found());
    }

    /// <summary>A page named on purpose is excused exactly as often as it is listed, and no more.</summary>
    [Fact]
    public void A_path_named_on_purpose_is_excused_by_its_exact_count()
    {
        Write("README.md", "# Readme\n");
        Write("plugins/p/README.md", "As docs/private.md says.\n");
        var once = new Dictionary<string, int>(StringComparer.Ordinal) { ["plugins/p/README.md"] = 1 };

        Assert.Empty(Found(once));

        Write("plugins/p/README.md", "As docs/private.md says, and private.md again.\n");

        Assert.Equal(
            ["plugins/p/README.md [d] named on purpose 1 time(s), found 2", "plugins/p/README.md [d] docs/private.md", "plugins/p/README.md [d] private.md"],
            Found(once));
    }

    private string[] Found(IReadOnlyDictionary<string, int>? namedOnPurpose = null) =>
        [.. Links.Violations(_root, OpenSet.Lines(_root), namedOnPurpose ?? new Dictionary<string, int>()).Select(violation => violation.Key)];

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
