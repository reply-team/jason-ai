using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The public repository's check, "The published tree", which every pull request there must pass: nothing in the
/// change that points into the work that produced it, every plugin's JavaScript parsed, and every link kept inside
/// the tree. GitHub never runs it in this repository, so its steps are run here over git repositories these tests
/// build, and its link rules are held to the verdicts this repository's own guard gives.
/// </summary>
public sealed class PublicCheckTests
{
    private static readonly string[] Check = [".github", "publish", "public", ".github", "workflows", "check.yml"];

    /// <summary>
    /// Each kind of pointer the check refuses, spelled here in pieces so that this file is not one: a tracker key, an
    /// Atlassian address, each attribution trailer, a session link, and another repository of ours.
    /// </summary>
    public static TheoryData<string, string> Pointers() => new()
    {
        { "Closes " + "REPLY" + "-4242.", "a tracker key" },
        { "See https://example." + "atlassian" + ".net/browse/X-1.", "an Atlassian address" },
        { "Co-Authored-" + "By: Someone <someone@example.invalid>", "an attribution trailer" },
        { "Claude-" + "Session: https://example.invalid/x", "an attribution trailer" },
        { "Generated with [" + "Claude Code](https://example.invalid)", "an attribution trailer" },
        { "https://claude" + ".ai/code/session_x", "a session link" },
        { "Built from reply-team/jason-" + "internal.", "another repository of ours" },
    };

    [Fact]
    public void The_check_runs_on_every_pull_request_by_its_one_name_and_reads_nothing_it_may_not()
    {
        var text = Read(Check);
        var workflow = Parse(text);

        Assert.Equal(["pull_request"], Mapping(workflow, "on").Children.Keys.Select(key => key.ToString()));
        var permissions = Mapping(workflow, "permissions");
        Assert.Equal(["contents"], permissions.Children.Keys.Select(key => key.ToString()));
        Assert.Equal("read", Scalar(permissions, "contents"));
        Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);

        // The name a ruleset requires: renamed, every pull request there would wait for a check that never comes.
        var job = (YamlMappingNode)Assert.Single(Mapping(workflow, "jobs").Children).Value;
        Assert.Equal("The published tree", Scalar(job, "name"));

        // Pinned by its text: node may be absent where these tests run.
        Assert.Equal("find plugins -name '*.js' -print0 | xargs -0 -r -n1 node --check", Step("Every plugin's JavaScript parses").Trim());
    }

    [Theory]
    [MemberData(nameof(Pointers))]
    public void A_pointer_in_an_added_line_is_refused_and_named_by_its_file_and_line(string pointer, string kind)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        var basis = repository.Head();
        repository.Commit("README.md", "# Jason\n\n" + pointer + "\n");

        var run = Hygiene(repository, basis, "Jason 0.6.0", "A release.");

        Assert.NotEqual(0, run.Exit);
        Assert.Contains($"README.md:3: {kind}", run.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Pointers))]
    public void A_pointer_in_the_title_or_the_body_is_refused_and_named_as_such(string pointer, string kind)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        var basis = repository.Head();
        repository.Commit("README.md", "# Jason, again\n");

        var titled = Hygiene(repository, basis, pointer, "A release.");
        var bodied = Hygiene(repository, basis, "Jason 0.6.0", "A release.\n\n" + pointer);

        Assert.NotEqual(0, titled.Exit);
        Assert.Contains($"the title: {kind}", titled.Stdout, StringComparison.Ordinal);
        Assert.NotEqual(0, bodied.Exit);
        Assert.Contains($"the body: {kind}", bodied.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Pointers))]
    public void A_pointer_the_change_removes_is_not_one_it_adds(string pointer, string kind)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n\n" + pointer + "\n");
        var basis = repository.Head();
        repository.Commit("README.md", "# Jason\n");

        var run = Hygiene(repository, basis, "Jason 0.6.0", "A release.");

        Assert.True(run.Exit == 0, $"{kind}: {run.Stdout}{run.Stderr}");
    }

    /// <summary>
    /// The public repository's own files, its workflows among them, added whole as one change pass: the patterns are
    /// written so that the file holding them does not match itself, and the public repository's texts name nothing.
    /// </summary>
    [Fact]
    public void The_public_repository_s_own_files_added_whole_pass_their_own_check()
    {
        using var repository = new Repository();
        repository.Commit("VERSION", "0.6.0\n");
        var basis = repository.Head();
        var own = Path.Combine(RepositoryRoot(), Publication.PublicOwnDirectory);
        foreach (var file in Directory.EnumerateFiles(own, "*", SearchOption.AllDirectories))
        {
            repository.Write(Path.GetRelativePath(own, file).Replace('\\', '/'), File.ReadAllText(file));
        }

        repository.Commit("VERSION", "0.6.1\n");

        var run = Hygiene(repository, basis, "Jason 0.6.1", "Merging this tags reply-team/jason-ai's v0.6.1.");

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
    }

    /// <summary>
    /// The pull request a release opens passes the check it meets there: its title, and the body the release writes
    /// for it from what it staged.
    /// </summary>
    [Fact]
    public void A_release_s_own_pull_request_passes_the_check()
    {
        using var repository = new Repository();
        repository.Commit("docs/contracts/operations/campaign.get.json", "{\"operation\":\"campaign.get\"}\n");
        var basis = repository.Head();
        repository.Write("docs/contracts/operations/campaign.get.json", "{\"operation\":\"campaign.get\",\"version\":2}\n");
        repository.Write("skills/runtime/operating-jason/SKILL.md", "---\nname: operating-jason\n---\n");
        repository.Write("VERSION", "0.6.0\n");
        Git(repository.Root, "add", "-A");
        var body = Path.Combine(repository.Root, "..", "body.md");
        var written = Script(
            new Dictionary<string, string> { ["GITHUB_OUTPUT"] = string.Empty },
            Path.Combine(RepositoryRoot(), ".github", "scripts", "Write-PublishBody.ps1"),
            "-Target", repository.Root, "-Version", "0.6.0", "-Body", body);
        Assert.True(written.Exit == 0, written.Stderr);
        Git(repository.Root, "commit", "--quiet", "-m", "Jason 0.6.0");

        var run = Hygiene(repository, basis, "Jason 0.6.0", File.ReadAllText(body));

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
    }

    /// <summary>
    /// The link step gives the verdicts this repository's guard gives, rule for rule, over the guard's own rows: a
    /// relative link, another repository of ours, and an address into this one. The fourth rule — a private path
    /// named in words — has nothing to find in a tree that holds no private path, so it is not the check's.
    /// </summary>
    /// <remarks>
    /// The punctuation that ends a sentence or closes emphasis after an address is not part of it. Windows drops a
    /// trailing dot from a path before it looks the path up, so there an address that ends a sentence is found with or
    /// without its dot; the other marks are not dropped, and the check itself runs on Linux.
    /// </remarks>
    [Theory]
    [InlineData("[p](docs/private.md)")]
    [InlineData("[p][r]\n\n[r]: docs/private.md")]
    [InlineData("![p](docs/private.md)")]
    [InlineData("[p](docs/nothing.md)")]
    [InlineData("[p](../outside.md)")]
    [InlineData("<https://github.com/reply-team/jason-internal/blob/main/README.md>")]
    [InlineData("```sh\ngit clone https://github.com/reply-team/jason-internal.git\n```")]
    [InlineData("The source lives in reply-team/jason-internal.")]
    [InlineData("```\ncd jason-internal\n```")]
    [InlineData("```\nhttps://github.com/reply-team/jason-ai/blob/v0.6.0/docs/private.md\n```")]
    [InlineData("[p](https://github.com/reply-team/jason-ai/tree/main/runtime/src)")]
    [InlineData("https://raw.githubusercontent.com/reply-team/jason-ai/main/CLAUDE.md")]
    [InlineData("[p](docs/public.md) [p](plugins/p/README.md) [p](plugins/p) [p](#here) [p](https://example.com/x.md)")]
    [InlineData("[p](./docs/public.md#a-heading) [p](docs/../docs/public.md) [v](VERSION)")]
    [InlineData("https://github.com/reply-team/jason-ai/releases/latest/download/install.sh and https://github.com/reply-team/jason-ai.git")]
    [InlineData("reply-team/jason-ai, jason-linux-x64.tar.gz and jason-internals-of-a-plugin name nothing private.")]
    [InlineData("https://github.com/reply-team/jason-ai/blob/main/docs/public.md and https://github.com/reply-team/reply-cli")]
    [InlineData("`[not a link](../outside.md)` is code, and code links nowhere")]
    [InlineData("```\n[p](docs/private.md)\n```")]
    [InlineData("The page is published at https://github.com/reply-team/jason-ai/blob/main/docs/public.md.")]
    [InlineData("Read **https://github.com/reply-team/jason-ai/blob/main/docs/public.md**, _https://github.com/reply-team/jason-ai/blob/main/plugins/p/README.md_ and ~~https://github.com/reply-team/jason-ai/blob/main/README.md~~. Is it *https://github.com/reply-team/jason-ai/blob/main/docs/public.md*? It is https://raw.githubusercontent.com/reply-team/jason-ai/main/README.md! And here: https://github.com/reply-team/jason-ai/blob/main/docs/public.md:")]
    [InlineData("https://github.com/reply-team/jason-ai/blob/main/docs/public.md!#here")]
    public void The_link_step_gives_the_verdicts_this_repository_s_guard_gives(string text)
    {
        // The guard's view: this repository, with a page it does not publish.
        using var tree = new TempTree();
        var source = tree.NewDirectory("private");
        WriteTo(source, ".github/publish/open-set.txt", "README.md\ndocs/public.md\nplugins/\n");
        WriteTo(source, "docs/public.md", "# Public\n");
        WriteTo(source, "docs/private.md", "# Private\n");
        WriteTo(source, "plugins/p/README.md", "# A plugin\n");
        WriteTo(source, "README.md", text + "\n");
        var guard = Links.Violations(source, OpenSet.Lines(source), new Dictionary<string, int>())
            .Where(violation => violation.Rule != "d")
            .Select(violation => violation.Key)
            .ToList();

        // The check's view: the public repository, which holds only what was published.
        using var repository = new Repository();
        repository.Write("README.md", text + "\n");
        repository.Write("docs/public.md", "# Public\n");
        repository.Write("plugins/p/README.md", "# A plugin\n");
        repository.Commit("VERSION", "0.6.0\n");

        var run = LinkStep(repository);
        var check = run.Stdout.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.StartsWith("::error::", StringComparison.Ordinal))
            .Select(line => System.Text.RegularExpressions.Regex.Replace(line["::error::".Length..], @"^(?<file>[^:]+):\d+ ", "${file} "))
            .ToList();

        Assert.Equal(guard, check);
        Assert.Equal(guard.Count == 0, run.Exit == 0);
    }

    /// <summary>
    /// The link step passes what a release publishes from this tree as it is: the open set, the public repository's own
    /// files and VERSION, projected by the release's own script and read as the check there reads them. The rows above
    /// hold the step to the guard one way of writing a link at a time; this holds it to every text a release will
    /// actually publish, so a way of writing one that no row has thought of fails here and not on a release's own pull
    /// request.
    /// </summary>
    [Fact]
    public void The_link_step_passes_what_a_release_publishes_from_this_tree()
    {
        using var repository = new Repository();
        var projected = Script(
            new Dictionary<string, string>(),
            Path.Combine(RepositoryRoot(), ".github", "scripts", "Publish-OpenSet.ps1"),
            "-Source", RepositoryRoot(), "-Target", repository.Root, "-Version", "0.6.0");
        Assert.True(projected.Exit == 0, projected.Stderr);
        Git(repository.Root, "add", "-A");
        Git(repository.Root, "commit", "--quiet", "-m", "Jason 0.6.0");

        var run = LinkStep(repository);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
    }

    /// <summary>The public repository's workflows are read there by everyone, so they name no other repository of ours.</summary>
    [Theory]
    [InlineData("check.yml")]
    [InlineData("release.yml")]
    public void Neither_public_workflow_names_another_repository_of_ours(string workflow)
    {
        var text = Read([.. Check[..^1], workflow]);

        Assert.DoesNotMatch(@"reply-team/jason-(?!ai(?![\w-]))[\w-]+", text);
        Assert.DoesNotContain("jason-" + "internal", text, StringComparison.Ordinal);
    }

    private static string Step(string name)
    {
        var job = (YamlMappingNode)Assert.Single(Mapping(Parse(Read(Check)), "jobs").Children).Value;
        var step = Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>().Single(step => Scalar(step, "name") == name);
        return Scalar(step, "run")!;
    }

    private static (int Exit, string Stdout, string Stderr) Hygiene(Repository repository, string basis, string title, string body) =>
        repository.Run(Step("Nothing internal in the change"), new Dictionary<string, string> { ["BASE"] = basis, ["TITLE"] = title, ["BODY"] = body });

    private static (int Exit, string Stdout, string Stderr) LinkStep(Repository repository) =>
        repository.Run(Step("Every link stays in the tree"), new Dictionary<string, string>());

    private static void WriteTo(string root, string path, string text) => GitTrees.Write(root, path, text);

    /// <summary>A git repository in a temporary tree, and a step's PowerShell run inside it.</summary>
    private sealed class Repository : IDisposable
    {
        private readonly GitTrees tree = new();

        public Repository()
        {
            Root = tree.NewDirectory("public");
            Git(Root, "init", "--quiet");
        }

        public string Root { get; }

        public void Write(string path, string text) => WriteTo(Root, path, text);

        public void Commit(string path, string text)
        {
            Write(path, text);
            Git(Root, "add", "-A");
            Git(Root, "commit", "--quiet", "-m", "A change");
        }

        public string Head() => Git(Root, "rev-parse", "HEAD").Trim();

        public (int Exit, string Stdout, string Stderr) Run(string script, Dictionary<string, string> environment)
        {
            var step = Path.Combine(tree.Root, Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(step, script);
            var harness = Path.Combine(tree.Root, Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(harness, $"$ErrorActionPreference = 'Stop'\nSet-Location -LiteralPath '{Root}'\ntry {{ & '{step}'; if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) {{ exit $LASTEXITCODE }} }} catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}\n");
            environment["GIT_CONFIG_NOSYSTEM"] = "1";
            return Pwsh(environment, "-File", harness);
        }

        public void Dispose() => tree.Dispose();
    }
}
