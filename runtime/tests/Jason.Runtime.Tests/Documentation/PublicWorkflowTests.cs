using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The public repository's own workflows, written here under <c>.github/publish/public/.github/workflows/</c> and
/// published to its root by every release. GitHub never runs them in this repository, so these tests are the only
/// place they run before they run there: parsed for what they are allowed, and their PowerShell executed with
/// <c>git</c> and <c>gh</c> standing in.
/// </summary>
public sealed class PublicWorkflowTests
{
    private static readonly string[] PublicRelease = [".github", "publish", "public", ".github", "workflows", "release.yml"];
    private static readonly string[] PrivateRelease = [".github", "workflows", "release.yml"];
    private const string Sha = "2222222222222222222222222222222222222222";

    /// <summary>
    /// A merge into <c>main</c> that changes <c>VERSION</c> starts it, and so does the button, which takes nothing: what
    /// it publishes is what <c>VERSION</c> on <c>main</c> names, so nobody can type a version into it. It writes with
    /// this repository's own token and holds no other.
    /// </summary>
    [Fact]
    public void The_public_release_runs_on_a_merge_that_changes_VERSION_and_from_a_button_that_takes_nothing()
    {
        var text = Read(PublicRelease);
        var workflow = Parse(text);
        var triggers = Mapping(workflow, "on");

        var push = Mapping(triggers, "push");
        Assert.Equal(["main"], Assert.IsType<YamlSequenceNode>(Node(push, "branches")).Children.Select(node => node.ToString()));
        Assert.Equal(["VERSION"], Assert.IsType<YamlSequenceNode>(Node(push, "paths")).Children.Select(node => node.ToString()));
        Assert.Equal(["push", "workflow_dispatch"], triggers.Children.Keys.Select(key => key.ToString()).Order(StringComparer.Ordinal));
        var button = Node(triggers, "workflow_dispatch");
        Assert.True(
            button is YamlScalarNode { Value: null or "" } || (button is YamlMappingNode mapping && Node(mapping, "inputs") is null),
            "The button takes an input; it must publish only what VERSION names.");

        var permissions = Mapping(workflow, "permissions");
        Assert.Equal(["contents"], permissions.Children.Keys.Select(key => key.ToString()));
        Assert.Equal("write", Scalar(permissions, "contents"));
        Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);

        var checkout = Steps().Single(step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true);
        Assert.Equal("0", Scalar(Mapping(checkout, "with"), "fetch-depth"));
    }

    /// <summary>A version one repository builds is a version the other publishes: the two read it with one pattern.</summary>
    [Fact]
    public void The_public_release_reads_a_version_exactly_as_the_private_release_does()
    {
        Assert.Equal(VersionRule(Read(PrivateRelease)), VersionRule(Read(PublicRelease)));
    }

    /// <summary>
    /// What it does in each state the release and its tag can be in, run: the step's own PowerShell, with <c>git</c>
    /// and <c>gh</c> replaced by functions that record what they were asked and answer as the state says.
    /// </summary>
    [Theory]
    [InlineData("draft", false, 0, "tag,push,publish")]
    [InlineData("draft", true, 0, "publish")]
    [InlineData("published", true, 0, "")]
    [InlineData("published", false, 1, "")]
    [InlineData("none", false, 1, "")]
    [InlineData("none", true, 1, "")]
    public void The_public_release_tags_the_merge_and_publishes_its_draft_in_every_state_it_can_meet(string release, bool tagged, int exit, string acts)
    {
        var run = RunTheStep("0.6.0", release, tagged);

        Assert.True(run.Exit == exit, $"The step exited {run.Exit}: {run.Stderr}");
        Assert.Equal(acts, string.Join(',', Acts(run.Calls)));

        if (acts.Contains("tag", StringComparison.Ordinal))
        {
            // The commit that last changed VERSION, which is the merge of the release's pull request, and not
            // whatever main holds when the run happens; annotated, by the workflow's own identity.
            var tag = Assert.Single(run.Calls, call => call.StartsWith("git ", StringComparison.Ordinal) && call.Contains(" tag ", StringComparison.Ordinal));
            Assert.Contains($"--annotate v0.6.0 {Sha} -m Jason 0.6.0", tag, StringComparison.Ordinal);
            Assert.Contains("user.name=github-actions[bot]", tag, StringComparison.Ordinal);
            Assert.Contains(run.Calls, call => call == "git push origin refs/tags/v0.6.0");
        }

        if (acts.Contains("publish", StringComparison.Ordinal))
        {
            var publish = Assert.Single(run.Calls, call => call.StartsWith("gh release edit", StringComparison.Ordinal));
            Assert.Equal("gh release edit v0.6.0 --draft=false --latest", publish);
        }

        if (release == "none")
        {
            Assert.Contains("there is no draft of v0.6.0", run.Stderr, StringComparison.OrdinalIgnoreCase);
        }

        if (release == "published" && tagged)
        {
            Assert.Contains("v0.6.0 is already published", run.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>A candidate is published as a pre-release and never becomes the latest release.</summary>
    [Fact]
    public void A_candidate_is_published_as_a_pre_release_and_never_as_latest()
    {
        var run = RunTheStep("1.0.0-rc.1", "draft", tagged: false);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal("gh release edit v1.0.0-rc.1 --draft=false --prerelease", Assert.Single(run.Calls, call => call.StartsWith("gh release edit", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A release becomes latest unless a newer one already is: a fix to an older line, published after a newer
    /// release, must not take latest from it — that is what every install one-liner and the default feed download.
    /// </summary>
    [Theory]
    [InlineData("v0.7.0", "--latest=false")]
    [InlineData("v0.10.0", "--latest=false")]
    [InlineData("v0.5.3", "--latest")]
    [InlineData("", "--latest")]
    public void A_release_older_than_the_latest_is_published_beside_it(string latest, string flag)
    {
        var run = RunTheStep("0.6.0", "draft", tagged: true, latest);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal($"gh release edit v0.6.0 --draft=false {flag}", Assert.Single(run.Calls, call => call.StartsWith("gh release edit", StringComparison.Ordinal)));
        if (flag == "--latest=false")
        {
            Assert.Contains($"{latest} is newer and stays the latest release", run.Stdout, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("0.6")]
    [InlineData("v0.6.0")]
    [InlineData("0.6.0+build.1")]
    public void A_VERSION_that_is_not_a_version_is_refused_before_anything_is_asked(string version)
    {
        var run = RunTheStep(version, "draft", tagged: false);

        Assert.NotEqual(0, run.Exit);
        Assert.Empty(run.Calls);
    }

    private static IEnumerable<YamlMappingNode> Steps()
    {
        var job = (YamlMappingNode)Assert.Single(Mapping(Parse(Read(PublicRelease)), "jobs").Children).Value;
        return Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>();
    }

    /// <summary>The identifier and the whole pattern a workflow checks a version with.</summary>
    private static (string Identifier, string Pattern) VersionRule(string workflow) =>
        (Assert.Single(Regex.Matches(workflow, @"\$identifier = '(?<rule>[^']+)'")).Groups["rule"].Value,
         Assert.Single(Regex.Matches(workflow, @"-cnotmatch ""(?<rule>[^""]+)""")).Groups["rule"].Value);

    /// <summary>What the recorded calls did, in order: tagged, pushed the tag, published.</summary>
    private static IEnumerable<string> Acts(IReadOnlyList<string> calls) => calls
        .Select(call => call.StartsWith("git ", StringComparison.Ordinal) && call.Contains(" tag ", StringComparison.Ordinal) ? "tag"
            : call.StartsWith("git push", StringComparison.Ordinal) ? "push"
            : call.StartsWith("gh release edit", StringComparison.Ordinal) ? "publish"
            : null)
        .OfType<string>();

    private static (int Exit, string Stdout, string Stderr, IReadOnlyList<string> Calls) RunTheStep(string version, string release, bool tagged, string latest = "")
    {
        var step = Steps().Single(step => Scalar(step, "run") is not null);

        using var tree = new TempTree();
        var work = tree.NewDirectory("public");
        File.WriteAllText(Path.Combine(work, "VERSION"), version + "\n");
        var script = Path.Combine(tree.Root, "step.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, StandIns.Replace("@STEP@", script, StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["WORK"] = work,
                ["CALLS"] = calls,
                ["RELEASE"] = release,
                ["TAGGED"] = tagged ? "yes" : "no",
                ["LATEST"] = latest,
                ["SHA"] = Sha,
                ["GH_TOKEN"] = "not-a-token",
            },
            "-File", harness);

        return (exit, stdout, stderr, File.ReadAllLines(calls));
    }

    /// <summary>
    /// <c>git</c> and <c>gh</c> for the length of one step, every call recorded as one line. <c>git log</c> answers
    /// the commit, <c>git ls-remote</c> answers by whether the tag exists; <c>gh release view</c> answers the
    /// release's state, or the latest release's tag when it names none.
    /// </summary>
    private const string StandIns = """
        $ErrorActionPreference = 'Stop'
        Set-Location $env:WORK

        function git {
            Add-Content -LiteralPath $env:CALLS -Value ('git ' + ($args -join ' '))
            $global:LASTEXITCODE = 0
            if ($args -contains 'log') { $env:SHA; return }
            if ($args -contains 'ls-remote') { if ($env:TAGGED -ne 'yes') { $global:LASTEXITCODE = 2 }; return }
        }

        function gh {
            Add-Content -LiteralPath $env:CALLS -Value ('gh ' + ($args -join ' '))
            $global:LASTEXITCODE = 0
            if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
                if ($args[2].StartsWith('--')) {
                    if ($env:LATEST) { $env:LATEST } else { [Console]::Error.WriteLine('release not found'); $global:LASTEXITCODE = 1 }
                    return
                }

                switch ($env:RELEASE) {
                    'draft'     { 'true'; return }
                    'published' { 'false'; return }
                    default     { [Console]::Error.WriteLine('release not found'); $global:LASTEXITCODE = 1; return }
                }
            }
        }

        try { & '@STEP@'; if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;
}
