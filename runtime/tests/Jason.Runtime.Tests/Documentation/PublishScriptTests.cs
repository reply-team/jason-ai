using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The script a release runs to make a checkout of the public repository what the release publishes, run over trees
/// these tests build: the open set and nothing else, <c>LICENSE</c> as its MIT block, <c>VERSION</c>, the public
/// repository's own files at its root, and a result that depends on nothing but those inputs. The script deletes
/// everything in the checkout it is given, so every refusal is also held to leave that checkout as it was.
/// </summary>
public sealed class PublishScriptTests
{
    private const string Version = "0.6.0";

    private static readonly string[] Listed = ["LICENSE", "README.md", "docs/contracts/", "install/", "skills/"];

    /// <summary>The private tree's files besides <c>LICENSE</c>, which is the real one: some listed, some never published.</summary>
    private static readonly Dictionary<string, string> PrivateFiles = new(StringComparer.Ordinal)
    {
        ["README.md"] = "# Jason\n",
        ["docs/contracts/README.md"] = "The contracts.\n",
        ["docs/contracts/operations/campaign.get.json"] = "{\"operation\":\"campaign.get\"}\n",
        ["docs/routing.md"] = "How an operation finds its plugin, which is not published.\n",
        ["install/install.sh"] = "#!/bin/sh\necho install\n",
        ["install/install.ps1"] = "Write-Output install\n",
        ["skills/.claude-plugin/plugin.json"] = "{}\n",
        ["skills/runtime/operating-jason/SKILL.md"] = "---\nname: operating-jason\n---\n",
        ["runtime/src/Jason.App/Program.cs"] = "class Program {}\n",
        ["CLAUDE.md"] = "Working in this repository.\n",
        ["global.json"] = "{}\n",
        [".github/workflows/ci.yml"] = "name: CI\n",
        [".github/publish/public/.gitignore"] = "bin/\n",
        [".github/publish/public/CONTRIBUTING.md"] = "Changes are not taken here.\n",
        [".github/publish/public/.github/workflows/check.yml"] = "name: Check\n",
    };

    /// <summary>Exactly the files the public tree holds after a run over <see cref="PrivateFiles"/>.</summary>
    private static readonly string[] Projection =
    [
        ".github/workflows/check.yml",
        ".gitignore",
        "CONTRIBUTING.md",
        "LICENSE",
        "README.md",
        "VERSION",
        "docs/contracts/README.md",
        "docs/contracts/operations/campaign.get.json",
        "install/install.ps1",
        "install/install.sh",
        "skills/.claude-plugin/plugin.json",
        "skills/runtime/operating-jason/SKILL.md",
    ];

    private static string ScriptPath => Path.Combine(RepositoryRoot(), ".github", "scripts", "Publish-OpenSet.ps1");

    [Fact]
    public void The_public_tree_becomes_the_projection_and_nothing_else()
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        var target = Public(
            tree,
            ("NOTES.md", "A file nobody published.\n"),
            ("docs/contracts/operations/campaign.gone.json", "{}\n"),
            ("README.md", "# An older Jason\n"));
        var git = State(target, insideGit: true);

        var run = Publish(source, target);

        Assert.True(run.Exit == 0, run.Stderr);

        // No file the private tree holds outside the list, no file it no longer holds, and no file a person left.
        Assert.Equal(Projection, Files(target));
        foreach (var path in Projection.Where(path => path is not "LICENSE" and not "VERSION"))
        {
            var from = PrivateFiles.ContainsKey(path) ? path : ".github/publish/public/" + path;
            Assert.Equal(File.ReadAllBytes(At(source, from)), File.ReadAllBytes(At(target, path)));
        }

        Assert.Equal(git, State(target, insideGit: true));
    }

    [Fact]
    public void The_published_licence_is_the_real_licence_from_its_one_MIT_line_to_the_end()
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        var target = Public(tree);

        var run = Publish(source, target);

        Assert.True(run.Exit == 0, run.Stderr);
        var real = File.ReadAllText(Path.Combine(RepositoryRoot(), "LICENSE"));
        var line = Assert.Single(Regex.Matches(real, "^MIT License$", RegexOptions.Multiline));
        var published = File.ReadAllBytes(At(target, "LICENSE"));
        Assert.Equal(Encoding.UTF8.GetBytes(real[line.Index..]), published);

        // The paragraph above that line is about this repository, which the public one is not.
        Assert.NotEqual(Encoding.UTF8.GetBytes(real), published);
    }

    [Theory]
    [InlineData("This text is not the MIT License.\n")]
    [InlineData("MIT License\n\nThe first.\n\nMIT License\n\nThe second.\n")]
    public void A_licence_without_exactly_one_MIT_line_is_refused_and_the_public_tree_is_left_as_it_was(string licence)
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        File.WriteAllText(At(source, "LICENSE"), licence);
        var target = Public(tree, ("README.md", "# An older Jason\n"));
        var before = Everything(target);

        var run = Publish(source, target);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("MIT License", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(before, Everything(target));
    }

    [Theory]
    [InlineData("0.6.0")]
    [InlineData("1.0.0-rc.1")]
    public void VERSION_is_the_version_and_a_newline_and_the_public_repository_s_own_files_land_at_its_root(string version)
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        var target = Public(tree);

        var run = Publish(source, target, version);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(Encoding.UTF8.GetBytes(version + "\n"), File.ReadAllBytes(At(target, "VERSION")));
        foreach (var path in new[] { ".gitignore", "CONTRIBUTING.md", ".github/workflows/check.yml" })
        {
            Assert.Equal(File.ReadAllBytes(At(source, ".github/publish/public/" + path)), File.ReadAllBytes(At(target, path)));
        }

        Assert.False(Directory.Exists(At(target, ".github/publish")), "The public repository's own files were copied under the path they are kept at here.");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/contracts/README.md")]
    [InlineData("LICENSE")]
    [InlineData("VERSION")]
    public void A_public_repository_s_own_file_at_a_path_the_release_already_publishes_is_refused(string path)
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        Write(source, ".github/publish/public/" + path, "A second file at the same path.\n");
        var target = Public(tree, ("README.md", "# An older Jason\n"));
        var before = Everything(target);

        var run = Publish(source, target);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains($"'{path}'", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(before, Everything(target));
    }

    [Fact]
    public void The_install_script_keeps_its_executable_bit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Only a platform with an execute bit has one to keep; the release publishes from Linux.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tree = new GitTrees();
        var source = Private(tree);
        File.SetUnixFileMode(At(source, "install/install.sh"), (UnixFileMode)Convert.ToInt32("755", 8));
        var target = Public(tree);

        var run = Publish(source, target);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.True(File.GetUnixFileMode(At(target, "install/install.sh")).HasFlag(UnixFileMode.UserExecute), "install.sh lost its executable bit.");
    }

    [Fact]
    public void The_same_inputs_give_the_same_tree_and_a_second_run_changes_nothing()
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        var first = Public(tree, ("NOTES.md", "One kind of leftover.\n"));
        var second = Public(tree, ("install/old.sh", "Another kind.\n"), ("README.md", "# An older Jason\n"));

        Assert.True(Publish(source, first).Exit == 0);
        Assert.True(Publish(source, second).Exit == 0);
        Assert.Equal(State(first, insideGit: false), State(second, insideGit: false));

        Git(first, "add", "-A");
        Git(first, "commit", "--quiet", "-m", "Jason 0.6.0");
        var again = Publish(source, first);

        Assert.True(again.Exit == 0, again.Stderr);
        Assert.Equal(string.Empty, Git(first, "status", "--porcelain"));
    }

    [Fact]
    public void The_script_reads_the_open_set_exactly_as_these_tests_read_it()
    {
        var root = RepositoryRoot();

        var run = Script(new Dictionary<string, string>(), ScriptPath, "-Source", root, "-ListOnly");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(OpenSet.Lines(root), run.Stdout.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("runtime/", "is never published")]
    [InlineData(".github/workflows/", "is never published")]
    [InlineData("plugins/../runtime/", "is not a plain path")]
    [InlineData("/README.md", "is not a plain path")]
    [InlineData("skills/*.md", "is a pattern")]
    [InlineData("# the skills", "is a comment")]
    [InlineData(" install/", "has whitespace")]
    [InlineData("missing/", "does not exist")]
    [InlineData("", "is blank")]
    public void The_script_refuses_an_entry_it_must_never_publish_and_leaves_the_public_tree_as_it_was(string entry, string said)
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        File.AppendAllText(At(source, Publication.OpenSetFile), entry + "\n");
        var target = Public(tree, ("README.md", "# An older Jason\n"));
        var before = Everything(target);

        var run = Publish(source, target);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(said, run.Stderr, StringComparison.Ordinal);
        Assert.Equal(before, Everything(target));
    }

    [Fact]
    public void A_target_that_is_not_a_checkout_is_refused_and_keeps_every_file()
    {
        using var tree = new GitTrees();
        var source = Private(tree);
        var target = tree.NewDirectory("public");
        Write(target, "NOTES.md", "Whatever this directory holds is not a release's to delete.\n");

        var run = Publish(source, target);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("is not a checkout", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(["NOTES.md"], Files(target));
    }

    private static (int Exit, string Stdout, string Stderr) Publish(string source, string target, string version = Version) =>
        Script(new Dictionary<string, string>(), ScriptPath, "-Source", source, "-Target", target, "-Version", version);

    /// <summary>A private tree: the list, the real <c>LICENSE</c>, and <see cref="PrivateFiles"/>.</summary>
    private static string Private(GitTrees tree)
    {
        var root = tree.NewDirectory("private");
        Write(root, Publication.OpenSetFile, string.Join('\n', Listed) + "\n");
        File.Copy(Path.Combine(RepositoryRoot(), "LICENSE"), At(root, "LICENSE"));
        foreach (var (path, text) in PrivateFiles)
        {
            Write(root, path, text);
        }

        return root;
    }

    /// <summary>A checkout of the public repository: a git repository whose one commit holds <paramref name="files"/>.</summary>
    private static string Public(GitTrees tree, params (string Path, string Text)[] files) => tree.NewRepository("public", files);

    private static void Write(string root, string path, string text) => GitTrees.Write(root, path, text);

    private static string At(string root, string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The files outside <c>.git</c>, as sorted relative paths with forward slashes.</summary>
    private static string[] Files(string root) =>
        [.. State(root, insideGit: false).Select(line => line[..line.IndexOf(' ', StringComparison.Ordinal)])];

    /// <summary>Every file of the checkout, <c>.git</c> included.</summary>
    private static List<string> Everything(string root) => [.. State(root, insideGit: true), .. State(root, insideGit: false)];

    /// <summary>
    /// Every file inside <c>.git</c>, or every file outside it: its relative path, a hash of its bytes and, where there
    /// is one, its mode, one line each in ordinal order.
    /// </summary>
    private static List<string> State(string root, bool insideGit)
    {
        var start = insideGit ? Path.Combine(root, ".git") : root;
        return [.. Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories)
            .Select(full => (Full: full, Relative: Path.GetRelativePath(root, full).Replace('\\', '/')))
            .Where(file => insideGit || !file.Relative.StartsWith(".git/", StringComparison.Ordinal))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => $"{file.Relative} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Full)))} {Mode(file.Full)}")];
    }

    private static string Mode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return "-";
        }

        return File.GetUnixFileMode(path).ToString();
    }
}
