using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The workflows a public repository runs on other people's pull requests. None runs on <c>pull_request_target</c>,
/// which runs this repository's workflow with its secrets and a token that can write, over whatever a fork brings.
/// <c>hygiene.yml</c> reads every pull request's added lines, title and body for what the tree's own files never carry,
/// with the patterns the tree is held to; it holds a token that can only read and is handed no secret, and the title and
/// the body reach its script as text. Its step is run here over git repositories these tests build.
/// </summary>
public sealed class HygieneWorkflowTests
{
    private static readonly string[] WorkflowDirectory = [".github", "workflows"];
    private static readonly string[] Hygiene = [.. WorkflowDirectory, "hygiene.yml"];

    /// <summary>The step that reads the change, and the job that runs it, by name.</summary>
    private const string Reading = "Nothing internal in the change";

    /// <summary>
    /// Each kind of pointer the check refuses, with the kind it is named by: the rows the tree's own guard plants in a file,
    /// so the check and the guard are held to the same cases.
    /// </summary>
    public static TheoryData<string, string> Pointers() =>
        [.. Rows(nameof(RepositoryHygieneTests.A_pointer_planted_in_a_file_is_named)).Select(row => ((string)row[0]!, (string)row[1]!))];

    /// <summary>What only resembles a pointer, as the tree's own guard passes it, each with a body that says what it does.</summary>
    public static TheoryData<string, string> Resemblances() =>
        [.. Rows(nameof(RepositoryHygieneTests.What_only_resembles_a_pointer_is_not_named)).Select(row => ((string)row[0]!, "What it does."))];

    /// <summary>
    /// No workflow runs on <c>pull_request_target</c>, in any of the three shapes <c>on</c> takes: a pull request from a
    /// fork would run this repository's workflow, with its secrets and a token that can write, over the fork's code.
    /// </summary>
    [Fact]
    public void No_workflow_runs_on_pull_request_target()
    {
        var files = Directory.EnumerateFiles(Path.Combine([RepositoryRoot(), .. WorkflowDirectory]))
            .Where(file => file.EndsWith(".yml", StringComparison.Ordinal) || file.EndsWith(".yaml", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Contains(files, file => Path.GetFileName(file) == "hygiene.yml");

        foreach (var file in files)
        {
            var triggers = Triggers(Parse(File.ReadAllText(file)));
            Assert.True(triggers.Count > 0, $"{Path.GetFileName(file)} names no trigger that this test can read");
            Assert.True(
                !triggers.Contains("pull_request_target"),
                $"{Path.GetFileName(file)} runs on pull_request_target: {string.Join(", ", triggers)}");
        }
    }

    /// <summary>
    /// The check runs whenever a pull request is opened, pushed to, reopened or edited, with a token that reads the
    /// repository and nothing more, handed no secret, and a checkout that keeps no credential: the merge commit and its
    /// first parent. The title and the body reach the script through its environment alone; the script itself carries no
    /// expression, so nothing a pull request says is ever written into it. Its job's name is its own.
    /// </summary>
    [Fact]
    public void The_check_runs_on_every_change_to_a_pull_request_and_holds_nothing_it_may_not()
    {
        var text = Read(Hygiene);
        var workflow = Parse(text);

        var on = Mapping(workflow, "on");
        Assert.Equal(["pull_request"], on.Children.Keys.Select(key => key.ToString()));
        Assert.Equal(
            ["opened", "synchronize", "reopened", "edited"],
            Assert.IsType<YamlSequenceNode>(Node(Mapping(on, "pull_request"), "types")).Children.Select(type => type.ToString()));

        Assert.Equal(["contents=read"], Grants(Mapping(workflow, "permissions")));
        var job = Assert.Single(Mapping(workflow, "jobs").Children.Values.Cast<YamlMappingNode>());
        Assert.Equal(Reading, Scalar(job, "name"));
        Assert.Equal(["contents=read"], Grants(Mapping(job, "permissions")));
        Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("github.token", text, StringComparison.Ordinal);

        var steps = Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>().ToList();
        var checkout = Assert.Single(steps, step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true);
        var with = Mapping(checkout, "with");
        Assert.Equal(["fetch-depth=2", "persist-credentials=false"], Grants(with));
        Assert.Same(checkout, steps[0]);

        var step = Assert.Single(steps, step => Scalar(step, "name") == Reading);
        Assert.Equal("pwsh", Scalar(step, "shell"));
        Assert.Equal(
            ["BODY=${{ github.event.pull_request.body }}", "TITLE=${{ github.event.pull_request.title }}"],
            Grants(Mapping(step, "env")));
        Assert.DoesNotContain("${{", Scalar(step, "run")!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check reads with the patterns every file of the tree is held to, <see cref="RepositoryHygiene.Patterns"/>, each
    /// by the same words and in the same order, and ignoring case where that pattern does, the one option a pattern may
    /// carry: a change that passes the check leaves a tree that passes the tree's own guard.
    /// </summary>
    [Fact]
    public void The_check_reads_with_the_patterns_the_tree_is_held_to()
    {
        Assert.All(RepositoryHygiene.Patterns, pattern => Assert.Equal(RegexOptions.None, pattern.Pattern.Options & ~RegexOptions.IgnoreCase));
        Assert.Equal(
            RepositoryHygiene.Patterns.Select(pattern =>
                $"{pattern.Kind} = {(pattern.Pattern.Options.HasFlag(RegexOptions.IgnoreCase) ? "(?i)" : string.Empty)}{pattern.Pattern}"),
            Patterns().Select(pattern => $"{pattern.Kind} = {pattern.Pattern}"));
    }

    [Theory]
    [MemberData(nameof(Pointers))]
    public void A_pointer_in_an_added_line_is_refused_and_named_by_its_file_and_line(string pointer, string kind)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        repository.Commit("README.md", "# Jason\n\n" + pointer + "\n");

        var run = repository.Check("A change", "What it does.");

        Assert.NotEqual(0, run.Exit);
        Assert.Contains($"README.md:3: {kind}", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The change is read with the attributes of its base: a <c>.gitattributes</c> the change itself adds, saying no file
    /// is to be shown as text, would otherwise turn every added line into "Binary files differ" and hide it from the check.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pointers))]
    public void A_pointer_is_read_even_when_the_change_says_no_file_is_text(string pointer, string kind)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        repository.Write(".gitattributes", "* -diff\n");
        repository.Commit("README.md", "# Jason\n\n" + pointer + "\n");

        var run = repository.Check("A change", "What it does.");

        Assert.NotEqual(0, run.Exit);
        Assert.Contains($"README.md:3: {kind}", run.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Pointers))]
    public void A_pointer_in_the_title_or_the_body_is_refused_and_named_as_such(string pointer, string kind)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        repository.Commit("README.md", "# Jason, again\n");

        var titled = repository.Check(pointer, "What it does.");
        var bodied = repository.Check("A change", "What it does.\n\n" + pointer);

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
        repository.Commit("README.md", "# Jason\n");

        var run = repository.Check("A change", "What it does.");

        Assert.True(run.Exit == 0, $"{kind}: {run.Stdout}{run.Stderr}");
    }

    /// <summary>
    /// What the check passes: whatever the tree's own guard passes, and a body that is empty.
    /// </summary>
    [Theory]
    [MemberData(nameof(Resemblances))]
    [InlineData("Install from reply-team/jason-ai.", "")]
    public void What_only_looks_like_a_pointer_passes(string line, string body)
    {
        using var repository = new Repository();
        repository.Commit("docs/architecture.md", "# Architecture\n");
        repository.Commit("docs/architecture.md", "# Architecture\n\n" + line + "\n");

        var run = repository.Check("A change", body);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
    }

    /// <summary>
    /// A title and a body are read as text and never run: a body written to break out of a script, or to run a command
    /// inside one, is a string the patterns read like any other, and the pointer after it is still found.
    /// </summary>
    [Theory]
    [InlineData("$(throw 'the body ran')")]
    [InlineData("'; throw 'the body ran'; '")]
    [InlineData("\"; throw \"the body ran\"; \"")]
    [InlineData("`n}; throw 'the body ran'; {")]
    public void The_title_and_the_body_are_read_as_text_and_never_run(string escape)
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        repository.Commit("README.md", "# Jason, again\n");

        var run = repository.Check(escape, escape + "\n\nCloses " + "REPLY" + "-4242.");

        Assert.DoesNotContain("the body ran", run.Stdout + run.Stderr, StringComparison.Ordinal);
        Assert.NotEqual(0, run.Exit);
        Assert.Contains("the body: a tracker key", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("the title:", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The workflow and this file, added whole as one change, pass the check: every pattern is written so that it does not
    /// match itself, and every pointer here is spelled in pieces.
    /// </summary>
    [Fact]
    public void The_workflow_and_its_tests_added_whole_pass_their_own_check()
    {
        using var repository = new Repository();
        repository.Commit("README.md", "# Jason\n");
        repository.Write(".github/workflows/hygiene.yml", Read(Hygiene));
        repository.Commit(
            "dotnet/tests/Jason.Runtime.Tests/Documentation/HygieneWorkflowTests.cs",
            Read(["dotnet", "tests", "Jason.Runtime.Tests", "Documentation", "HygieneWorkflowTests.cs"]));

        var run = repository.Check("The hygiene check", "What it reads.");

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
    }

    /// <summary>The rows a theory of the tree's own guard is written with, in their order.</summary>
    private static List<object?[]> Rows(string theory) =>
        [.. typeof(RepositoryHygieneTests).GetMethod(theory)!.GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
            .Cast<InlineDataAttribute>()
            .Select(row => row.Data)];

    /// <summary>The triggers a workflow names, whichever of the three shapes its <c>on</c> takes.</summary>
    private static List<string> Triggers(YamlMappingNode workflow) =>
        Node(workflow, "on") switch
        {
            YamlScalarNode scalar => [scalar.Value ?? string.Empty],
            YamlSequenceNode sequence => [.. sequence.Children.Select(trigger => trigger.ToString())],
            YamlMappingNode mapping => [.. mapping.Children.Keys.Select(trigger => trigger.ToString())],
            _ => [],
        };

    /// <summary>A mapping as <c>key=value</c>, in order.</summary>
    private static List<string> Grants(YamlMappingNode mapping) =>
        [.. mapping.Children.Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal)];

    private static string Step()
    {
        var job = Assert.Single(Mapping(Parse(Read(Hygiene)), "jobs").Children.Values.Cast<YamlMappingNode>());
        var step = Assert.Single(Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>(), step => Scalar(step, "name") == Reading);
        return Scalar(step, "run")!;
    }

    /// <summary>The patterns the step's script reads with, as its table writes them, in its order.</summary>
    private static List<(string Kind, string Pattern)> Patterns()
    {
        var table = Regex.Match(Step(), @"\$patterns = \[ordered\]@\{\n(?<rows>.*?)\n\s*\}\n", RegexOptions.Singleline);
        Assert.True(table.Success, "the step holds no pattern table");
        return [.. table.Groups["rows"].Value.Split('\n').Select(row =>
        {
            var pair = Regex.Match(row, @"^\s*'(?<kind>[^']+)'\s*=\s*'(?<pattern>[^']*)'\s*$");
            Assert.True(pair.Success, $"the table's row '{row}' is not a pattern by its words");
            return (pair.Groups["kind"].Value, pair.Groups["pattern"].Value);
        })];
    }

    /// <summary>A git repository in a temporary tree, and the check's step run inside it as a runner runs it.</summary>
    private sealed class Repository : IDisposable
    {
        private readonly GitTrees tree = new();

        public Repository()
        {
            Root = tree.NewDirectory("change");
            Git(Root, "init", "--quiet");
        }

        public string Root { get; }

        public void Write(string path, string text) => GitTrees.Write(Root, path, text);

        public void Commit(string path, string text)
        {
            Write(path, text);
            Git(Root, "add", "-A");
            Git(Root, "commit", "--quiet", "-m", "A change");
        }

        /// <summary>The step over the last commit's change against its parent, with this title and body.</summary>
        public (int Exit, string Stdout, string Stderr) Check(string title, string body)
        {
            var step = Path.Combine(tree.Root, Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(step, Step());
            var harness = Path.Combine(tree.Root, Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(harness, $"$ErrorActionPreference = 'Stop'\nSet-Location -LiteralPath '{Root}'\ntry {{ & '{step}'; if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) {{ exit $LASTEXITCODE }} }} catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}\n");
            return Pwsh(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["TITLE"] = title, ["BODY"] = body, ["GIT_CONFIG_NOSYSTEM"] = "1" },
                "-File",
                harness);
        }

        public void Dispose() => tree.Dispose();
    }
}
