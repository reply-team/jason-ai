namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// Every file of this repository is read publicly, so the tree carries nothing that points into the work that produced
/// it, and no link in it leads nowhere. Each rule is run on a tree a test builds before it is trusted with this one.
/// </summary>
public sealed class RepositoryHygieneTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", "repository-hygiene-" + Guid.NewGuid().ToString("N"));

    public RepositoryHygieneTests()
    {
        Directory.CreateDirectory(_root);
        Write("README.md", "# Readme\n");
        Write("docs/public.md", "# Public\n");
        Write("plugins/p/README.md", "# A plugin\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    [Critical(Family.B4, "nothing in the tree points into the work that produced it")]
    public void Nothing_in_the_tree_points_into_the_work_that_produced_it()
    {
        var found = RepositoryHygiene.Findings(RepositoryTree.Root());

        Assert.True(
            found.Count == 0,
            $"The tree points into the work that produced it ({found.Count}):\n" + string.Join('\n', found.Select(finding => "  " + finding)));
    }

    [Fact]
    [Critical(Family.B4, "no link leaves the tree")]
    public void No_link_in_the_tree_leads_nowhere()
    {
        var found = Links.Violations(RepositoryTree.Root());

        Assert.True(
            found.Count == 0,
            $"Links that lead nowhere ({found.Count}):\n" + string.Join('\n', found.Select(violation => "  " + violation)));
    }

    /// <summary>Each pattern, planted in a file of a tree, is named by its file and its kind; a source file is read like a page.</summary>
    [Theory]
    // Critical: a tracker key turns the guard red.
    [InlineData("Fixes " + "REPLY" + "-12345.", "a tracker key", "REPLY" + "-12345", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B4)])]
    [InlineData("(" + "REPLY" + "-7)", "a tracker key", "REPLY" + "-7")]
    [InlineData("The branch is fix-" + "REPLY" + "-12345-thing.", "a tracker key", "REPLY" + "-12345")]
    [InlineData("wi_" + "REPLY" + "-7 is an id.", "a tracker key", "REPLY" + "-7")]
    [InlineData("See https://example." + "atlassian" + ".net/browse/X-1.", "an Atlassian address", "atlassian" + ".net")]
    [InlineData("See https://EXAMPLE." + "ATLASSIAN" + ".NET/browse/X-1.", "an Atlassian address", "ATLASSIAN" + ".NET")]
    [InlineData("Co-Authored" + "-By: Somebody <somebody@example.invalid>", "an attribution trailer", "Co-Authored" + "-By:")]
    [InlineData("co-authored" + "-by: somebody <somebody@example.invalid>", "an attribution trailer", "co-authored" + "-by:")]
    [InlineData("Claude" + "-Session: https://example.invalid/x", "an attribution trailer", "Claude" + "-Session:")]
    [InlineData("Generated with [" + "Claude Code](https://example.invalid)", "an attribution trailer", "Generated with [" + "Claude")]
    [InlineData("https://claude" + ".ai/code/session_x", "a session link", "claude" + ".ai/code")]
    [InlineData("See https://github.com/reply-team/jason-" + "internal/blob/main/README.md.", "another repository of ours", "reply-team/jason-" + "internal")]
    [InlineData("git clone https://github.com/reply-team/jason-" + "secret.git", "another repository of ours", "reply-team/jason-" + "secret")]
    [InlineData("cd jason-" + "internal", "the private repository by name", "jason-" + "internal")]
    public void A_pointer_planted_in_a_file_is_named(string text, string kind, string matched)
    {
        Write("src/Thing.cs", "// a comment\n// " + text + "\n");

        Assert.Equal([$"src/Thing.cs [{kind}] {matched}"], Hygiene());
    }

    /// <summary>And what only resembles one passes: a register id, a key built from a reply, the public repository, a word.</summary>
    [Theory]
    [InlineData("ASM-REPLY-" + "001 and DEC-REPLY-" + "001 are the architecture register's ids.")]
    [InlineData("`wi_01-reply-501-1` is an idempotency key; a REPLY" + "-ish word is not a key.")]
    [InlineData("reply-team/jason-ai, jason-linux-x64.tar.gz and jason-" + "internals-of-a-plugin name nothing private.")]
    [InlineData("Atlassian makes a tracker, and a Co-Authored" + "-By line is what the hygiene rule strips.")]
    [InlineData("claude.ai is an address; generated with care.")]
    public void What_only_resembles_a_pointer_is_not_named(string text)
    {
        Write("src/Thing.cs", text + "\n");

        Assert.Empty(Hygiene());
    }

    /// <summary>A text saved as UTF-16 starts with its byte order mark and holds NUL bytes, and it is read as text all the same.</summary>
    [Theory]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    public void A_text_file_in_utf_16_is_read(string encoding)
    {
        var path = Path.Combine(_root, "scripts", "planted.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# Fixes " + "REPLY" + "-999\n", encoding == "utf-16le" ? System.Text.Encoding.Unicode : System.Text.Encoding.BigEndianUnicode);

        Assert.Equal(["scripts/planted.ps1 [a tracker key] REPLY" + "-999"], Hygiene());
    }

    /// <summary>A binary file holds no words, so a key in its bytes is not read.</summary>
    [Fact]
    public void A_binary_file_is_not_read()
    {
        var path = Path.Combine(_root, "image.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x00, .. System.Text.Encoding.ASCII.GetBytes("REPLY" + "-12345")]);

        Assert.Empty(Hygiene());
    }

    /// <summary>
    /// In a repository the guard reads what git holds or would hold: a new file before it is added is read, and a file
    /// git ignores — build output, a developer's packages — is not.
    /// </summary>
    [Fact]
    public void In_a_repository_an_ignored_file_is_not_read_and_a_new_one_is()
    {
        using var trees = new GitTrees();
        var root = trees.NewRepository("tree", (".gitignore", "ignored/\n"), ("README.md", "# Readme\n"));
        GitTrees.Write(root, "ignored/a.txt", "REPLY" + "-1\n");
        GitTrees.Write(root, "new.txt", "REPLY" + "-2\n");

        Assert.Equal(["new.txt [a tracker key] REPLY" + "-2"], RepositoryHygiene.Findings(root).Select(finding => finding.Key));
    }

    [Theory]
    [InlineData("[p](docs/nothing.md)", "README.md [a] docs/nothing.md")]
    [InlineData("[p][r]\n\n[r]: docs/nothing.md", "README.md [a] docs/nothing.md")]
    [InlineData("![p](docs/nothing.md)", "README.md [a] docs/nothing.md")]
    [InlineData("[p](../outside.md)", "README.md [a] (outside the repository) ../outside.md")]
    [InlineData("```\nhttps://github.com/reply-team/jason-ai/blob/v0.6.0/docs/nothing.md\n```", "README.md [c] docs/nothing.md")]
    [InlineData("[p](https://github.com/reply-team/jason-ai/tree/main/src/nothing)", "README.md [c] src/nothing")]
    [InlineData("https://raw.githubusercontent.com/reply-team/jason-ai/main/NOTHING.md", "README.md [c] NOTHING.md")]
    [InlineData("The design is at https://github.com/reply-team/jason-ai/blob/main/docs/nothing.md.", "README.md [c] docs/nothing.md")]
    [InlineData("Read **https://github.com/reply-team/jason-ai/blob/main/docs/nothing.md**.", "README.md [c] docs/nothing.md")]
    [InlineData("Read _https://github.com/reply-team/jason-ai/blob/main/docs/nothing.md_.", "README.md [c] docs/nothing.md")]
    [InlineData("Read ~~https://github.com/reply-team/jason-ai/blob/main/docs/nothing.md~~.", "README.md [c] docs/nothing.md")]
    [InlineData("Is it *https://github.com/reply-team/jason-ai/blob/main/docs/nothing.md*?", "README.md [c] docs/nothing.md")]
    [InlineData("It is https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/nothing.md!", "README.md [c] docs/nothing.md")]
    [InlineData("And here: https://github.com/reply-team/jason-ai/blob/main/docs/nothing.md:", "README.md [c] docs/nothing.md")]
    [InlineData("https://github.com/reply-team/jason-ai/blob/main/docs/public.md!#here", "README.md [c] docs/public.md!")]
    [InlineData("```text\nThe instructions are\n<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/nothing.md>. Read all of it\n```", "README.md [c] docs/nothing.md")]
    [InlineData("See docs/nothing.md.", "README.md [d] docs/nothing.md")]
    [InlineData("See `docs/nothing.md`, and docs/contracts/nothing.md.", "README.md [d] docs/nothing.md", "README.md [d] docs/contracts/nothing.md")]
    [InlineData("```\n[p](docs/nothing.md)\n```", "README.md [d] docs/nothing.md")]
    [InlineData("```sh\ncat docs/nothing.md\n```", "README.md [d] docs/nothing.md")]
    public void A_link_that_leads_nowhere_is_named(string text, params string[] expected)
    {
        Write("README.md", text + "\n");

        Assert.Equal(expected, LinksFound());
    }

    [Theory]
    [InlineData("[p](docs/public.md) [p](plugins/p/README.md) [p](plugins/p) [p](#here) [p](https://example.com/x.md) [root](.)")]
    [InlineData("[p](./docs/public.md#a-heading) [p](docs/../docs/public.md)")]
    [InlineData("https://github.com/reply-team/jason-ai/releases/latest/download/install.sh and https://github.com/reply-team/jason-ai.git")]
    [InlineData("`[not a link](../outside.md)` is code, and code links nowhere")]
    [InlineData("```\n[p](../outside.md)\n```")]
    [InlineData("https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/public.md and https://github.com/reply-team/reply-cli")]
    [InlineData("The page is at https://github.com/reply-team/jason-ai/blob/main/docs/public.md; read it.")]
    [InlineData("Read **https://github.com/reply-team/jason-ai/blob/main/docs/public.md**, _https://github.com/reply-team/jason-ai/blob/main/plugins/p/README.md_ and ~~https://github.com/reply-team/jason-ai/blob/main/README.md~~. Is it *https://github.com/reply-team/jason-ai/blob/main/docs/public.md*? It is https://raw.githubusercontent.com/reply-team/jason-ai/main/README.md! And here: https://github.com/reply-team/jason-ai/blob/main/docs/public.md:")]
    [InlineData("```text\nThe instructions are\n<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/public.md>. Read all of it\n```")]
    [InlineData("See docs/public.md and `docs/public.md`; skills/runtime/docs/x.md is another directory's page, and notes.md names no page of docs/.")]
    [InlineData("```sh\ncat docs/public.md\n```")]
    public void A_link_that_lands_in_the_tree_is_not_named(string text)
    {
        Write("README.md", text + "\n");

        Assert.Empty(LinksFound());
    }

    /// <summary>A relative link is read from its own file's directory.</summary>
    [Fact]
    public void A_relative_link_is_read_from_its_own_file_s_directory()
    {
        Write("plugins/p/README.md", "[up](../../docs/public.md), [here](README.md), [gone](CHANGES.md)\n");

        Assert.Equal(["plugins/p/README.md [a] plugins/p/CHANGES.md"], LinksFound());
    }

    /// <summary>The release notes are a release's page, not a file of the tree: a relative link there lands nowhere.</summary>
    [Fact]
    public void A_relative_link_in_the_release_notes_lands_nowhere()
    {
        Write(Links.NotesTemplate, "[why](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/nothing.md) and [this](docs/public.md)\n");
        Write(Links.ReleaseNotes, "# Jason 0.2.0\n\n## Known issues\n\n[this](docs/public.md).\n");

        Assert.Equal(
            [
                ".github/release-notes-template.md [a] (a relative link in the release notes) docs/public.md",
                ".github/release-notes-template.md [c] docs/nothing.md",
                "RELEASE-NOTES.md [a] (a relative link in the release notes) docs/public.md",
            ],
            LinksFound());
    }

    private string[] Hygiene() => [.. RepositoryHygiene.Findings(_root).Select(finding => finding.Key)];

    private string[] LinksFound() => [.. Links.Violations(_root).Select(violation => violation.Key)];

    private void Write(string relative, string text) => GitTrees.Write(_root, relative, text);
}
