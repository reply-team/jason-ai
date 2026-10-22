using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// Which of CI's checks run on which push, decided by the branch a change lands on, and which report as skipped by rule. A
/// draft pull request's pushes run the Linux set, and so does a pull request into a release branch once it is ready, which
/// is all a release branch's ruleset requires. The release's pull request, from a release branch into <c>main</c>, runs
/// every check, macOS included, once it is ready; a push to it that changes nothing but documentation since the tree its
/// last run of every check tested runs the Linux set. Any other pull request into <c>main</c> runs the Linux set. Both
/// report every other check <c>main</c>'s ruleset requires as skipped by rule, under its own name. A push to <c>main</c> or
/// to a release branch whose tree the merged pull request's last ready run tested runs the version alone; any other push to
/// <c>main</c> runs every check, and any other push to a release branch the Linux set. A run started by hand runs every
/// check, and nothing runs nightly. Read out of ci.yml, and the steps that decide are run rather than read, so what these
/// facts hold is what a runner does.
/// </summary>
public class CiRunsTests
{
    private static readonly string[] Ci = [".github", "workflows", "ci.yml"];
    private static readonly string[] Release = [".github", "workflows", "release.yml"];
    private static readonly string[] Action = [".github", "actions", "package", "action.yml"];

    /// <summary>The condition a job carries when only a run of every check needs it.</summary>
    private const string Full = "needs.version.outputs.full == 'true'";

    /// <summary>The condition a job carries when the Linux set needs it, as every set that builds anything does.</summary>
    private const string Linux = "needs.version.outputs.linux == 'true'";

    /// <summary>The condition a step carries when it needs the macOS archive, which only a run that builds macOS has.</summary>
    private const string Macos = "needs.version.outputs.macos == 'true'";

    /// <summary>The condition of the job that reports the checks a rule skipped.</summary>
    private const string Skipping = "needs.version.outputs.skipping == 'true'";

    /// <summary>The job that reports a check a rule skipped, as a success under that check's own name.</summary>
    private const string StandIn = "skipped-by-rule";

    /// <summary>The eleven checks the main branch's ruleset requires before a merge.</summary>
    private static readonly string[] Required =
    [
        "The version this tree carries",
        "The suite under a release's version",
        "win-x64",
        "linux-x64",
        "osx-arm64",
        "jason.exe on a Windows older than 24H2",
        "an update on win-x64",
        "an update on linux-x64",
        "install.sh on ubuntu",
        "install.ps1 on windows",
        "The UI on ubuntu",
    ];

    /// <summary>What the Linux set runs: a draft's push, and all a release branch's ruleset requires.</summary>
    private static readonly string[] TheLinuxSet =
    [
        "The version this tree carries", "linux-x64", "install.sh on ubuntu", "an update on linux-x64", "The UI on ubuntu",
    ];

    /// <summary>The commit, the tree and the pull request a run is about. Made up.</summary>
    private const string Sha = "c0ffee00000000000000000000000000000000a1";

    private const string AnotherSha = "c0ffee00000000000000000000000000000000b2";

    private const string Tree = "7ee0000000000000000000000000000000000a01";

    private const string AnotherTree = "7ee0000000000000000000000000000000000b02";

    private const string PullRequest = "7";

    private const int PullRequestNumber = 7;

    /// <summary>A release branch, and the branch any other pull request comes from. Made up.</summary>
    private const string ReleaseBranch = "release/0.15";

    private const string FeatureBranch = "feature/the-change";

    /// <summary>A tree as <c>git ls-tree -r</c> lists it: what the last ready run tested. Made up.</summary>
    private static readonly string[] Tested =
    [
        "100644 blob 1111111111111111111111111111111111111111\tREADME.md",
        "100644 blob 2222222222222222222222222222222222222222\tdocs/plugins.md",
        "100644 blob 3333333333333333333333333333333333333333\truntime/src/Jason.App/Program.cs",
        "100755 blob 4444444444444444444444444444444444444444\tinstall/install.sh",
    ];

    /// <summary>
    /// A run as the deciding step is told about it: the event, what the step before it found — the tree an earlier ready run
    /// tested (none when nothing was found) — this run's own listing, the branch the change lands on and, for a pull request,
    /// the branch it comes from.
    /// </summary>
    private sealed record Run(
        string Event, string Action = "", string Draft = "", string? TestedTree = null, string[]? Listing = null, string Target = "main", string Branch = FeatureBranch);

    /// <summary>Every kind of run, and the set it takes.</summary>
    private static readonly Dictionary<string, (Run Run, string Set)> Kinds = new(StringComparer.Ordinal)
    {
        ["a draft's push"] = (new Run("pull_request", "synchronize", "true", Target: ReleaseBranch), "linux"),
        ["a draft's push into main"] = (new Run("pull_request", "synchronize", "true"), "linux"),
        ["a push to the release's pull request while it is a draft"] = (new Run("pull_request", "synchronize", "true", Branch: ReleaseBranch), "linux"),
        ["marking a pull request into a release branch ready"] = (new Run("pull_request", "ready_for_review", "false", Target: ReleaseBranch), "linux"),
        ["a push after ready into a release branch"] = (new Run("pull_request", "synchronize", "false", AnotherTree, Changed("runtime/src/Jason.App/Program.cs"), Target: ReleaseBranch), "linux"),
        ["marking a pull request into another pull request's branch ready"] = (new Run("pull_request", "ready_for_review", "false", Target: "feature/the-first"), "linux"),
        ["marking the release's pull request ready"] = (new Run("pull_request", "ready_for_review", "false", Branch: ReleaseBranch), "everything"),
        ["opening the release's pull request ready"] = (new Run("pull_request", "opened", "false", Branch: ReleaseBranch), "everything"),
        ["reopening the release's pull request"] = (new Run("pull_request", "reopened", "false", Branch: ReleaseBranch), "everything"),
        ["a push to the release's pull request that changes code"] = (new Run("pull_request", "synchronize", "false", AnotherTree, Changed("runtime/src/Jason.App/Program.cs"), Branch: ReleaseBranch), "everything"),
        ["a push to the release's pull request that changes documentation only"] = (new Run("pull_request", "synchronize", "false", AnotherTree, Changed("docs/plugins.md"), Branch: ReleaseBranch), "documentation"),
        ["a push to the release's pull request that changes nothing"] = (new Run("pull_request", "synchronize", "false", Tree, Branch: ReleaseBranch), "documentation"),
        ["a push to the release's pull request with no record found"] = (new Run("pull_request", "synchronize", "false", Branch: ReleaseBranch), "everything"),
        ["marking another pull request into main ready"] = (new Run("pull_request", "ready_for_review", "false"), "not-the-release"),
        ["a push after ready to another pull request into main"] = (new Run("pull_request", "synchronize", "false", AnotherTree, Changed("runtime/src/Jason.App/Program.cs")), "not-the-release"),
        ["a push to main"] = (new Run("push", TestedTree: AnotherTree), "everything"),
        ["a push to main of the tree its pull request's last ready run tested"] = (new Run("push", TestedTree: Tree), "tested"),
        ["a push to main with no record found"] = (new Run("push"), "everything"),
        ["a push to a release branch"] = (new Run("push", TestedTree: AnotherTree, Target: ReleaseBranch), "linux"),
        ["a push to a release branch of the tree its pull request's last ready run tested"] = (new Run("push", TestedTree: Tree, Target: ReleaseBranch), "none"),
        ["a push to a release branch with no record found"] = (new Run("push", Target: ReleaseBranch), "linux"),
        ["a run started by hand"] = (new Run("workflow_dispatch"), "everything"),
    };

    /// <summary>What each set runs, and what it reports as skipped by rule.</summary>
    private static readonly Dictionary<string, (string[] Ran, string[] SkippedByRule)> Sets = new(StringComparer.Ordinal)
    {
        ["linux"] = (TheLinuxSet, []),
        ["documentation"] = (TheLinuxSet, ["win-x64", "osx-arm64", "an update on win-x64"]),
        ["not-the-release"] = (TheLinuxSet, ["win-x64", "osx-arm64", "an update on win-x64"]),
        ["tested"] = (["The version this tree carries"], ["linux-x64", "win-x64", "osx-arm64", "an update on linux-x64", "an update on win-x64"]),
        ["everything"] = (Required, []),
        ["none"] = (["The version this tree carries"], []),
    };

    public static TheoryData<string> KindsOfRun() => [.. Kinds.Keys];

    // Critical: the pipeline reports what the ruleset waits for.
    public static TheoryData<string> CriticalKindsOfRun() =>
        CriticalRows.Mark(KindsOfRun(), Family.B7, data => data is ["a draft's push"]);

    /// <summary>
    /// A pull request runs CI when it is opened, pushed to, reopened, and when it is marked ready for review; <c>main</c> and
    /// every release branch run it on every push, and somebody may start it by hand. Nothing runs it on a schedule.
    /// </summary>
    [Fact]
    public void Ci_runs_on_a_pull_request_on_pushes_to_main_and_to_a_release_branch_and_by_hand()
    {
        var on = Mapping(Parse(Read(Ci)), "on");

        var types = Assert.IsType<YamlSequenceNode>(Node(Mapping(on, "pull_request"), "types"));
        Assert.Equal(["opened", "synchronize", "reopened", "ready_for_review"], types.Children.Select(type => type.ToString()));

        var branches = Assert.IsType<YamlSequenceNode>(Node(Mapping(on, "push"), "branches"));
        Assert.Equal(["main", "release/**"], branches.Children.Select(branch => branch.ToString()));

        Assert.False(on.Children.ContainsKey(new YamlScalarNode("schedule")), "CI still runs on a schedule");

        Assert.True(on.Children.ContainsKey(new YamlScalarNode("workflow_dispatch")), "CI cannot be started by hand");
    }

    /// <summary>
    /// A run cancels the run of the same kind on the same ref, and no other: a run started by hand on <c>main</c> must not
    /// cancel the run a merge started, nor be cancelled by it.
    /// </summary>
    [Fact]
    public void A_run_cancels_only_a_run_of_its_own_kind_on_its_own_ref()
    {
        var concurrency = Mapping(Parse(Read(Ci)), "concurrency");
        Assert.Equal("ci-${{ github.event_name }}-${{ github.ref }}", Scalar(concurrency, "group"));
        Assert.Equal("true", Scalar(concurrency, "cancel-in-progress"));
    }

    /// <summary>
    /// The version job, which every other job needs, decides once, and hands on a word for each kind of job, the legs each
    /// matrix runs and the checks reported as skipped by rule. It may read runs, their artifacts and the pull request a commit
    /// merged, which is what deciding reads, and nothing more.
    /// </summary>
    [Fact]
    public void The_version_job_hands_on_what_it_decided_and_may_read_what_deciding_reads()
    {
        var version = Mapping(Jobs(), "version");

        var outputs = Mapping(version, "outputs");
        foreach (var output in new[] { "full", "linux", "macos", "platforms", "updates", "rids", "skipping", "skipped" })
        {
            Assert.Equal($"${{{{ steps.legs.outputs.{output} }}}}", Scalar(outputs, output));
        }

        var permissions = Mapping(version, "permissions");
        Assert.Equal(
            ["actions=read", "contents=read", "pull-requests=read"],
            permissions.Children.Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal));
        Assert.Equal("read", Scalar(Mapping(Parse(Read(Ci)), "permissions"), "contents"));
    }

    /// <summary>The steps that decide are told the event, and what the steps before them found, by these names.</summary>
    [Fact]
    public void The_steps_that_decide_are_told_the_event_and_what_was_found()
    {
        var looking = Mapping(Step("earlier"), "env");
        var said = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GH_TOKEN"] = "${{ github.token }}",
            ["REPOSITORY"] = "${{ github.repository }}",
            ["EVENT"] = "${{ github.event_name }}",
            ["ACTION"] = "${{ github.event.action }}",
            ["DRAFT"] = "${{ github.event.pull_request.draft }}",
            ["SHA"] = "${{ github.sha }}",
            ["PULL_REQUEST"] = "${{ github.event.pull_request.number }}",
            ["BRANCH"] = "${{ github.head_ref }}",
            ["TARGET"] = "${{ github.base_ref || github.ref_name }}",
        };
        Assert.Equal(said.OrderBy(pair => pair.Key, StringComparer.Ordinal), Variables(looking).OrderBy(pair => pair.Key, StringComparer.Ordinal));

        var deciding = Mapping(Step("legs"), "env");
        var told = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EVENT"] = "${{ github.event_name }}",
            ["ACTION"] = "${{ github.event.action }}",
            ["DRAFT"] = "${{ github.event.pull_request.draft }}",
            ["PULL_REQUEST"] = "${{ github.event.pull_request.number }}",
            ["SHA"] = "${{ github.sha }}",
            ["BRANCH"] = "${{ github.head_ref }}",
            ["TARGET"] = "${{ github.base_ref || github.ref_name }}",
            ["TREE"] = "${{ steps.tree.outputs.tree }}",
            ["LISTING"] = "${{ steps.tree.outputs.listing }}",
            ["RECORD"] = "${{ steps.earlier.outputs.record }}",
            ["FOUND"] = "${{ steps.earlier.outputs.found }}",
        };
        Assert.Equal(
            told.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            Variables(deciding).Where(pair => pair.Key is not ("PLATFORMS" or "UPDATES")).OrderBy(pair => pair.Key, StringComparer.Ordinal));

        // In the order a runner needs them: the tree, then what an earlier run tested, then the decision.
        var ids = Steps(Mapping(Jobs(), "version")).Select(step => Scalar(step, "id")).OfType<string>().ToList();
        Assert.Equal(["version", "tree", "earlier", "legs"], ids);
    }

    /// <summary>
    /// The tree this run tests, as the step names it: <c>git rev-parse HEAD^{tree}</c> of the checkout — a pull request's
    /// merge with its base, which is the tree a squash of it puts on <c>main</c> — and the listing a later run compares with.
    /// Run in a repository of this test's own.
    /// </summary>
    [Fact]
    public void The_tree_step_names_the_checkout_s_tree_and_lists_it()
    {
        using var tree = new TempTree();
        var repository = Path.Combine(tree.Root, "repository");
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        Git(repository, "init", "--quiet");
        File.WriteAllText(Path.Combine(repository, "README.md"), "# Jason\n");
        File.WriteAllText(Path.Combine(repository, "docs", "plugins.md"), "# Plugins\n");
        Git(repository, "add", "-A");
        Git(repository, "commit", "--quiet", "-m", "A tree");

        var step = Step("tree");
        var script = Path.Combine(tree.Root, "tree.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var output = Path.Combine(tree.Root, "output");
        File.WriteAllText(output, string.Empty);
        var ran = Pwsh(
            new Dictionary<string, string> { ["GITHUB_OUTPUT"] = output, ["RUNNER_TEMP"] = tree.Root, ["GIT_CONFIG_NOSYSTEM"] = "1" },
            "-Command",
            $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{repository}'; & '{script}'");
        Assert.True(ran.Exit == 0, ran.Stderr);

        var said = Outputs(output);
        Assert.Equal(Git(repository, "rev-parse", "HEAD^{tree}").Trim(), said["tree"]);
        Assert.Equal(
            Git(repository, "ls-tree", "-r", "--full-tree", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries),
            File.ReadAllLines(said["listing"]));
    }

    /// <summary>
    /// Each kind of run takes its set: what its jobs run, and what the job standing in reports as skipped by rule. On every
    /// run a merge into <c>main</c> or <c>main</c> itself relies on — all but a draft's push and a run into or on a release
    /// branch, whose ruleset requires the Linux set alone — every check <c>main</c>'s ruleset requires reports: by running, by a condition that skips it, which reports success, or by the stand-in. A
    /// matrix row that is absent never reports at all, and a matrix job a condition skips reports no leg's name. No check
    /// reports twice.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalKindsOfRun))]
    public void Each_kind_of_run_runs_its_set_and_reports_every_check_a_merge_waits_for(string kind)
    {
        var (run, set) = Kinds[kind];
        var decided = Decide(run);
        Assert.True(decided.Outputs["set"] == set, $"{kind} took the set '{decided.Outputs["set"]}', not '{set}': {decided.Summary}");

        var (ran, byRule, byCondition) = Report(decided.Outputs);
        var (expectedRan, expectedByRule) = Sets[set];
        Assert.Equal(expectedRan.Order(StringComparer.Ordinal), ran.Order(StringComparer.Ordinal));
        Assert.Equal(expectedByRule.Order(StringComparer.Ordinal), byRule.Order(StringComparer.Ordinal));

        var reported = ran.Concat(byRule).Concat(byCondition).ToList();
        var twice = reported.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        Assert.True(twice.Count == 0, $"{kind} reports {string.Join(", ", twice)} twice");

        if (set is not ("linux" or "none"))
        {
            var missing = Required.Except(reported, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, $"{kind} never reports {string.Join(", ", missing)}, and the ruleset waits for it");
        }

        // A check reported as skipped says why, in the stand-in's own log.
        Assert.All(Legs(decided.Outputs["skipped"]), leg => Assert.False(string.IsNullOrWhiteSpace(leg["why"]), $"{leg["check"]} is skipped without a reason"));
    }

    /// <summary>
    /// The deciding step's words for each set — the jobs only a run of every check needs, the Linux set's jobs, and the steps
    /// that need the macOS archive — and the legs each matrix runs: every platform when every check runs, Linux alone on the
    /// Linux set, none where the tree was tested already. And it says which case it took and why, in the run's summary.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindsOfRun))]
    public void The_deciding_step_hands_on_its_words_and_the_legs_each_matrix_runs(string kind)
    {
        var (run, set) = Kinds[kind];
        var decided = Decide(run);
        var said = decided.Outputs;

        Assert.Equal(set is "everything" ? "true" : "false", said["full"]);
        Assert.Equal(set is "tested" or "none" ? "false" : "true", said["linux"]);
        Assert.Equal(set is "everything" ? "true" : "false", said["macos"]);
        Assert.Equal(Sets[set].SkippedByRule.Length > 0 ? "true" : "false", said["skipping"]);

        string[] platforms = set switch
        {
            "everything" => ["linux-x64", "win-x64", "osx-arm64"],
            "linux" or "documentation" or "not-the-release" => ["linux-x64"],
            _ => [],
        };
        Assert.Equal(platforms, Legs(said["platforms"]).Select(leg => leg["rid"]));
        Assert.Equal(platforms.Where(rid => rid != "osx-arm64"), Legs(said["updates"]).Select(leg => leg["rid"]));
        Assert.Equal(string.Join(',', platforms), said["rids"]);

        // Each leg keeps the runner it is built on.
        var runners = new Dictionary<string, string> { ["linux-x64"] = "ubuntu-latest", ["win-x64"] = "windows-latest", ["osx-arm64"] = "macos-latest" };
        Assert.All(Legs(said["platforms"]).Concat(Legs(said["updates"])), leg => Assert.Equal(runners[leg["rid"]], leg["os"]));

        Assert.Contains($"**{set}**", decided.Summary, StringComparison.Ordinal);
        Assert.Contains($"set -> {set}", decided.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// A push to the release's pull request after ready runs the Linux set only when every path it changes since the tree its
    /// last run of every check tested is documentation: Markdown outside <c>runtime/</c>, <c>.github/</c> and <c>plugins/</c>, a plain file before and after,
    /// and neither the wiring check's page, whose step 3 a test runs in sh and both PowerShells, nor the runtime's licence,
    /// which every archive carries. Anything else runs every check.
    /// </summary>
    [Theory]
    [InlineData("docs/plugins.md", "100644", false, true)]
    [InlineData("README.md", "100644", false, true)]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md", "100644", false, true)]
    [InlineData("ui/README.md", "100644", false, true)]
    [InlineData("docs/a-new-page.md", "100644", false, true)]
    [InlineData("docs/plugins.md", "100644", true, true)]
    [InlineData("docs/golden-path.md", "100644", false, false)]
    [InlineData("LICENSE-RUNTIME.md", "100644", false, false)]
    [InlineData("runtime/README.md", "100644", false, false)]
    [InlineData(".github/release-notes-template.md", "100644", false, false)]
    [InlineData("plugins/reply/README.md", "100644", false, false)]
    [InlineData("docs/contracts/operations/campaign.create.json", "100644", false, false)]
    [InlineData("docs/PLUGINS.MD", "100644", false, false)]
    [InlineData("docs/plugins.md", "100755", false, false)]
    [InlineData("docs/plugins.md", "120000", false, false)]
    [InlineData("install/install.sh", "100755", false, false)]
    public void A_push_to_the_release_s_pull_request_runs_the_linux_set_only_when_every_path_it_changes_is_documentation(string path, string mode, bool removed, bool documentation)
    {
        var decided = Decide(new Run("pull_request", "synchronize", "false", AnotherTree, Changed(path, mode, removed), Branch: ReleaseBranch));

        Assert.True(
            decided.Outputs["set"] == (documentation ? "documentation" : "everything"),
            $"a push that {(removed ? "removes" : "changes")} {path} ({mode}) took the set '{decided.Outputs["set"]}': {decided.Summary}");
    }

    /// <summary>One path that is not documentation among several that are runs every check.</summary>
    [Fact]
    public void A_push_to_the_release_s_pull_request_that_changes_documentation_and_code_runs_every_check()
    {
        var listing = Changed("docs/plugins.md").Where(line => !line.EndsWith("\truntime/src/Jason.App/Program.cs", StringComparison.Ordinal))
            .Append("100644 blob 6666666666666666666666666666666666666666\truntime/src/Jason.App/Program.cs")
            .ToArray();

        var decided = Decide(new Run("pull_request", "synchronize", "false", AnotherTree, listing, Branch: ReleaseBranch));

        Assert.True(decided.Outputs["set"] == "everything", $"a push changing documentation and code took the set '{decided.Outputs["set"]}': {decided.Summary}");
        Assert.Contains("runtime/src/Jason.App/Program.cs", decided.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record that cannot be read — not JSON, no listing, a listing that is not one, no tree, a file that is not there — is
    /// doubt, and doubt skips nothing the branch requires: every check on the release's pull request and on <c>main</c>, the
    /// Linux set on a release branch. A leg is skipped only on what was found.
    /// </summary>
    [Theory]
    [InlineData("pull_request", "main", "not json")]
    [InlineData("pull_request", "main", "{\"tree\":\"7ee0000000000000000000000000000000000b02\",\"pull_request\":7}")]
    [InlineData("pull_request", "main", "{\"tree\":\"7ee0000000000000000000000000000000000b02\",\"pull_request\":7,\"listing\":[]}")]
    [InlineData("pull_request", "main", "{\"tree\":\"7ee0000000000000000000000000000000000b02\",\"pull_request\":7,\"listing\":[\"not a line of git ls-tree\"]}")]
    [InlineData("pull_request", "main", null)]
    [InlineData("push", "main", "not json")]
    [InlineData("push", "main", "{\"pull_request\":7,\"listing\":[]}")]
    [InlineData("push", "main", null)]
    [InlineData("push", ReleaseBranch, "not json")]
    [InlineData("push", ReleaseBranch, "{\"pull_request\":7,\"listing\":[]}")]
    [InlineData("push", ReleaseBranch, null)]
    public void A_record_that_cannot_be_read_skips_nothing_the_branch_requires(string @event, string target, string? record)
    {
        var run = @event == "push" ? new Run("push", Target: target) : new Run("pull_request", "synchronize", "false", Branch: ReleaseBranch);

        var decided = Decide(run, record ?? string.Empty, recordIsThere: record is not null);

        var set = target == ReleaseBranch ? "linux" : "everything";
        Assert.True(
            decided.Outputs["set"] == set,
            $"a {@event} run into {target} given the record '{record}' took the set '{decided.Outputs["set"]}', not '{set}': {decided.Summary}");
    }

    /// <summary>
    /// A ready pull request's run keeps a record of the tree it tested, for the runs after it: the release's run of every
    /// check, for a push to it that changes documentation only and for the push to <c>main</c> its merge makes, and a run of
    /// the Linux set into a release branch, for the push to the release branch its merge makes. No other run keeps one, so
    /// a record always means the checks its target branch requires ran on that tree. The record is trusted only from a run
    /// that passed, which the step that looks asks for.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindsOfRun))]
    public void Only_a_ready_pull_request_s_run_of_what_its_branch_requires_keeps_a_record(string kind)
    {
        var (run, set) = Kinds[kind];
        var decided = Decide(run);

        var keeps = run.Event == "pull_request" && run.Draft == "false" &&
            (set == "everything" || (set == "linux" && run.Target.StartsWith("release/", StringComparison.Ordinal)));
        Assert.True(keeps == decided.Record is not null, $"{kind} {(keeps ? "kept no record" : $"kept a record: {decided.Record}")}");
        if (decided.Record is { } record)
        {
            Assert.Equal(Tree, (string?)record["tree"]);
            Assert.Equal(Sha, (string?)record["commit"]);
            Assert.Equal(PullRequestNumber, (int)record["pull_request"]!);
            Assert.Equal(run.Listing ?? Tested, record["listing"]!.AsArray().Select(line => (string)line!));
        }
    }

    /// <summary>
    /// The record is uploaded under one name, when the deciding step wrote one, and kept fourteen days, as long as a pull
    /// request may wait for its merge: a record that has gone skips nothing. The step that looks reads that name, and no
    /// other upload of this workflow takes it.
    /// </summary>
    [Fact]
    public void The_record_is_kept_under_one_name_long_enough_for_a_merge()
    {
        var keeping = Assert.Single(Steps(Mapping(Jobs(), "version")), step => Scalar(step, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true);
        Assert.Equal("steps.legs.outputs.record != ''", Scalar(keeping, "if"));
        var with = Mapping(keeping, "with");
        Assert.Equal("tested-tree", Scalar(with, "name"));
        Assert.Equal("${{ steps.legs.outputs.record }}", Scalar(with, "path"));
        Assert.Equal("14", Scalar(with, "retention-days"));
        Assert.Equal("error", Scalar(with, "if-no-files-found"));

        var looking = Scalar(Step("earlier"), "run")!;
        Assert.Contains("artifacts?name=tested-tree", looking, StringComparison.Ordinal);
        Assert.Contains("--name tested-tree", looking, StringComparison.Ordinal);

        var named = Jobs().Children.Values.Cast<YamlMappingNode>()
            .SelectMany(job => Node(job, "steps") is YamlSequenceNode steps ? steps.Children.Cast<YamlMappingNode>() : [])
            .Where(step => Node(step, "with") is YamlMappingNode stepWith && Scalar(stepWith, "name") == "tested-tree")
            .ToList();
        Assert.Single(named);
    }

    /// <summary>
    /// A push to <c>main</c> or to a release branch finds the pull request whose merge is this commit, and the newest of that
    /// pull request's runs at its last head that passed and kept a record; a run that kept none is passed over.
    /// </summary>
    [Theory]
    [InlineData("main")]
    [InlineData(ReleaseBranch)]
    public void A_push_finds_the_record_its_merged_pull_request_s_newest_passing_run_kept(string target)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"api repos/acme/jason/commits/{Sha}/pulls"] = $"[{Pull(9, merged: false, AnotherSha)},{Pull(7, merged: true, Sha)}]",
            [$"api repos/acme/jason/actions/workflows/ci.yml/runs?event=pull_request&head_sha={Head}&status=success&per_page=20"] = RunsAnswer(302, 301),
            ["api repos/acme/jason/actions/runs/302/artifacts?name=tested-tree"] = ArtifactsAnswer(),
            ["api repos/acme/jason/actions/runs/301/artifacts?name=tested-tree"] = ArtifactsAnswer(kept: true),
        };

        var looked = Look(Facts("push", target: target), answers, new Dictionary<long, string> { [301] = RecordFor(7, Tree) });

        Assert.True(looked.Record is not null, $"nothing was found: {looked.Outputs["found"]}");
        Assert.Equal(Tree, (string?)JsonNode.Parse(looked.Record!)!["tree"]);
        Assert.Contains("301", looked.Outputs["found"], StringComparison.Ordinal);
        Assert.Contains(looked.Calls, call => call.StartsWith("run download 301 --repo acme/jason --name tested-tree --dir ", StringComparison.Ordinal));
        Assert.DoesNotContain(looked.Calls, call => call.StartsWith("run download 302", StringComparison.Ordinal));
    }

    /// <summary>A push to <c>main</c> that no merged pull request names as its merge finds nothing, and asks for no runs.</summary>
    [Fact]
    public void A_push_no_merged_pull_request_names_finds_nothing()
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"api repos/acme/jason/commits/{Sha}/pulls"] = $"[{Pull(7, merged: true, AnotherSha)}]",
        };

        var looked = Look(Facts("push"), answers);

        Assert.True(looked.Record is null, $"a record was found: {looked.Outputs["found"]}");
        Assert.DoesNotContain(looked.Calls, call => call.Contains("/runs", StringComparison.Ordinal));
    }

    /// <summary>
    /// A push to the release's pull request once it is ready finds the newest record its own runs on its branch kept: one
    /// another pull request kept on a branch of the same name is passed over, and so is an artifact that has expired.
    /// </summary>
    [Fact]
    public void A_push_to_the_release_s_pull_request_finds_its_own_newest_record()
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api repos/acme/jason/actions/workflows/ci.yml/runs?event=pull_request&branch=release%2F0.15&status=success&per_page=20"] = RunsAnswer(403, 402, 401),
            ["api repos/acme/jason/actions/runs/403/artifacts?name=tested-tree"] = ArtifactsAnswer(kept: true),
            ["api repos/acme/jason/actions/runs/402/artifacts?name=tested-tree"] = ArtifactsAnswer(kept: true, expired: true),
            ["api repos/acme/jason/actions/runs/401/artifacts?name=tested-tree"] = ArtifactsAnswer(kept: true),
        };
        var records = new Dictionary<long, string> { [403] = RecordFor(8, AnotherTree), [401] = RecordFor(7, Tree) };

        var looked = Look(Facts("pull_request", "synchronize", "false", branch: ReleaseBranch), answers, records);

        Assert.True(looked.Record is not null, $"nothing was found: {looked.Outputs["found"]}");
        Assert.Equal(7, (int?)JsonNode.Parse(looked.Record!)!["pull_request"]);
        Assert.DoesNotContain(looked.Calls, call => call.StartsWith("run download 402", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whatever goes wrong while looking — no answer, a download that fails — is an answer of nothing found, and the step still
    /// succeeds: the step after it skips nothing on nothing found, which is the failure that costs only minutes.
    /// </summary>
    [Theory]
    [InlineData("push", false)]
    [InlineData("push", true)]
    [InlineData("pull_request", false)]
    public void Any_failure_while_looking_is_an_answer_of_nothing_found(string @event, bool downloadFails)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (downloadFails)
        {
            answers[$"api repos/acme/jason/commits/{Sha}/pulls"] = $"[{Pull(7, merged: true, Sha)}]";
            answers[$"api repos/acme/jason/actions/workflows/ci.yml/runs?event=pull_request&head_sha={Head}&status=success&per_page=20"] = RunsAnswer(301);
            answers["api repos/acme/jason/actions/runs/301/artifacts?name=tested-tree"] = ArtifactsAnswer(kept: true);
        }

        var looked = Look(@event == "push" ? Facts("push") : Facts("pull_request", "synchronize", "false", branch: ReleaseBranch), answers);

        Assert.True(looked.Record is null, $"a record was found: {looked.Outputs["found"]}");
        Assert.False(string.IsNullOrWhiteSpace(looked.Outputs["found"]), "the step does not say what it found");
    }

    /// <summary>
    /// A draft's push, the ready flip, an opened or reopened pull request, a push to a ready pull request that is not the
    /// release's into <c>main</c>, and a run started by hand look for nothing: their set is the same whatever was found.
    /// </summary>
    [Theory]
    [InlineData("pull_request", "synchronize", "true", "main", ReleaseBranch)]
    [InlineData("pull_request", "ready_for_review", "false", "main", ReleaseBranch)]
    [InlineData("pull_request", "opened", "false", "main", ReleaseBranch)]
    [InlineData("pull_request", "reopened", "false", "main", ReleaseBranch)]
    [InlineData("pull_request", "synchronize", "false", "main", FeatureBranch)]
    [InlineData("pull_request", "synchronize", "false", ReleaseBranch, FeatureBranch)]
    [InlineData("workflow_dispatch", "", "", "main", "")]
    public void A_run_whose_set_no_earlier_run_can_change_looks_for_nothing(string @event, string action, string draft, string target, string branch)
    {
        var looked = Look(Facts(@event, action, draft, target, branch), new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.True(looked.Calls.Count == 0, $"it asked: {string.Join(" | ", looked.Calls)}");
        Assert.Null(looked.Record);
    }

    /// <summary>
    /// The job standing in for a skipped check reports it under the check's own name, as a success whose one step says why. It
    /// checks out nothing and builds nothing, and it runs only when the version job hands it a check.
    /// </summary>
    [Fact]
    public void The_stand_in_reports_a_skipped_check_under_its_own_name_and_says_why()
    {
        var job = Mapping(Jobs(), StandIn);

        Assert.Equal("${{ matrix.check }}", Scalar(job, "name"));
        Assert.Equal("version", Scalar(job, "needs"));
        Assert.Equal(Skipping, Scalar(job, "if"));
        Assert.Equal("ubuntu-latest", Scalar(job, "runs-on"));
        Assert.Equal("${{ fromJSON(needs.version.outputs.skipped) }}", Scalar(Mapping(job, "strategy"), "matrix"));

        var step = Assert.Single(Steps(job));
        Assert.Null(Scalar(step, "uses"));
        Assert.Equal("${{ matrix.why }}", Scalar(Mapping(step, "env"), "WHY"));
        Assert.Contains("$WHY", Scalar(step, "run"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every job but the version job's runs on one of its words: the Linux set's jobs on <c>linux</c>, the jobs only a run
    /// of every check needs on <c>full</c>, and the stand-in on <c>skipping</c>.
    /// </summary>
    [Fact]
    public void Every_job_runs_on_one_of_the_version_job_s_words()
    {
        var conditions = Jobs().Children.ToDictionary(pair => pair.Key.ToString(), pair => Scalar((YamlMappingNode)pair.Value, "if"), StringComparer.Ordinal);

        var expected = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["version"] = null,
            ["release-stamp"] = Full,
            ["build-test-publish"] = Linux,
            ["older-windows"] = Full,
            ["update-end-to-end"] = Linux,
            ["install-sh"] = Linux,
            ["install-ps1"] = Full,
            ["ui"] = Linux,
            [StandIn] = Skipping,
        };
        Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), conditions.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// install.sh's job and install.ps1's assemble their feed from the archives their run packaged, which is all three only on
    /// a run that builds macOS. The feed in the layout 0.1.0 published names all three archives, so that step runs there
    /// only, in both jobs.
    /// </summary>
    [Fact]
    public void The_install_jobs_require_the_archives_their_run_packaged()
    {
        foreach (var name in new[] { "install-sh", "install-ps1" })
        {
            var steps = Steps(Mapping(Jobs(), name));
            var assembling = Assert.Single(steps, step => Scalar(step, "name") == "Assemble the feed the way a release is assembled");
            Assert.Contains("-Require ${{ needs.version.outputs.rids }}", Scalar(assembling, "run"), StringComparison.Ordinal);

            var fixture = Assert.Single(steps, step => Scalar(step, "name") == "Install from the schema-1 fixture, the feed layout 0.1.0 published");
            Assert.Equal(Macos, Scalar(fixture, "if"));
            Assert.All(steps.Where(step => step != fixture), step => Assert.Null(Scalar(step, "if")));
        }

        Assert.DoesNotContain("-Require win-x64,linux-x64,osx-arm64", Read(Ci), StringComparison.Ordinal);
    }

    /// <summary>
    /// What CI uploads is kept three days: it exists for the jobs of the same run and for a person looking at a failure.
    /// The packaging action is shared with the release, whose call names no retention, and the action's default, 0, is
    /// upload-artifact's own word for the repository's retention — so a release keeps what it always kept.
    /// </summary>
    [Fact]
    public void Ci_keeps_what_it_uploads_three_days_and_a_release_what_it_always_kept()
    {
        var action = Parse(Read(Action));
        var retention = Mapping(Mapping(action, "inputs"), "retention-days");
        Assert.Equal("0", Scalar(retention, "default"));
        var upload = Assert.Single(
            Assert.IsType<YamlSequenceNode>(Node(Mapping(action, "runs"), "steps")).Children.Cast<YamlMappingNode>(),
            step => Scalar(step, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true);
        Assert.Equal("${{ inputs.retention-days }}", Scalar(Mapping(upload, "with"), "retention-days"));

        var ci = PackagingCalls(Ci);
        Assert.NotEmpty(ci);
        Assert.All(ci, call => Assert.Equal("3", Scalar(Mapping(call, "with"), "retention-days")));

        var release = PackagingCalls(Release);
        Assert.NotEmpty(release);
        Assert.All(release, call => Assert.Null(Scalar(Mapping(call, "with"), "retention-days")));
    }

    // --- what a run decides and reports --------------------------------------------------------------------------

    /// <summary>What the deciding step wrote: its outputs by name, the run's summary, what it printed, and the record it kept.</summary>
    private sealed record Decided(Dictionary<string, string> Outputs, string Summary, string Stdout, JsonNode? Record);

    /// <summary>
    /// The deciding step's script, run as a runner runs a <c>pwsh</c> step, told about <paramref name="run"/> by the names the
    /// workflow tells it, with the legs the workflow gives it. A run whose earlier ready run is named gets that run's record —
    /// <see cref="Tested"/> under its tree — unless <paramref name="record"/> says what the record holds instead.
    /// </summary>
    private static Decided Decide(Run run, string? record = null, bool recordIsThere = true)
    {
        var step = Step("legs");
        var env = Mapping(step, "env");

        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "legs.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var output = Path.Combine(tree.Root, "output");
        File.WriteAllText(output, string.Empty);
        var summary = Path.Combine(tree.Root, "summary");
        File.WriteAllText(summary, string.Empty);
        var listing = Path.Combine(tree.Root, "listing.txt");
        File.WriteAllLines(listing, run.Listing ?? Tested);

        var recorded = string.Empty;
        if (record is not null || run.TestedTree is not null)
        {
            recorded = Path.Combine(tree.Root, "earlier", "record.json");
            Directory.CreateDirectory(Path.GetDirectoryName(recorded)!);
            if (recordIsThere)
            {
                File.WriteAllText(recorded, record ?? RecordFor(PullRequestNumber, run.TestedTree!));
            }
        }

        var environment = new Dictionary<string, string>
        {
            ["EVENT"] = run.Event,
            ["ACTION"] = run.Action,
            ["DRAFT"] = run.Draft,
            ["PULL_REQUEST"] = run.Event == "pull_request" ? PullRequest : string.Empty,
            ["SHA"] = Sha,
            ["BRANCH"] = run.Event == "pull_request" ? run.Branch : string.Empty,
            ["TARGET"] = run.Target,
            ["TREE"] = Tree,
            ["LISTING"] = listing,
            ["RECORD"] = recorded,
            ["FOUND"] = recorded.Length > 0 ? "run 301 of pull request #7 tested tree " + run.TestedTree : "nothing was found",
            ["PLATFORMS"] = Scalar(env, "PLATFORMS")!,
            ["UPDATES"] = Scalar(env, "UPDATES")!,
            ["GITHUB_OUTPUT"] = output,
            ["GITHUB_STEP_SUMMARY"] = summary,
            ["RUNNER_TEMP"] = tree.Root,
        };
        var ran = Pwsh(environment, "-Command", $"$ErrorActionPreference = 'Stop'; & '{script}'");
        Assert.True(ran.Exit == 0, ran.Stderr);

        var said = Outputs(output);
        JsonNode? kept = null;
        if (said.TryGetValue("record", out var directory) && directory.Length > 0)
        {
            kept = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "record.json")));
        }

        return new Decided(said, File.ReadAllText(summary), ran.Stdout, kept);
    }

    /// <summary>
    /// What a run reports, from what its version job decided: the checks whose jobs run, the checks the stand-in reports as
    /// skipped by rule, and the checks of jobs a condition skips, which report success under their own names. A matrix job a
    /// condition skips reports no leg's name.
    /// </summary>
    private static (List<string> Ran, List<string> ByRule, List<string> ByCondition) Report(Dictionary<string, string> decided)
    {
        var ran = new List<string>();
        var byRule = new List<string>();
        var byCondition = new List<string>();
        foreach (var (key, value) in Jobs().Children)
        {
            var job = (YamlMappingNode)value;
            var name = Scalar(job, "name") ?? key.ToString();
            var condition = Scalar(job, "if");
            Assert.True(condition is null or Full or Linux or Skipping, $"The '{key}' job runs on '{condition}', which is none of the version job's words.");
            var on = condition switch
            {
                Full => decided["full"] == "true",
                Linux => decided["linux"] == "true",
                Skipping => decided["skipping"] == "true",
                _ => true,
            };

            var matrix = Node(job, "strategy") is YamlMappingNode strategy ? Node(strategy, "matrix") : null;
            if (matrix is null)
            {
                (on ? ran : byCondition).Add(name);
                continue;
            }

            if (!on)
            {
                continue;
            }

            var output = Regex.Match((matrix as YamlScalarNode)?.Value ?? string.Empty, @"^\$\{\{\s*fromJSON\(needs\.version\.outputs\.(\w+)\)\s*\}\}$");
            Assert.True(output.Success, $"The '{key}' job's matrix is not one the version job decides.");
            var names = Legs(decided[output.Groups[1].Value])
                .Select(leg => Regex.Replace(name, @"\$\{\{\s*matrix\.(\w+)\s*\}\}", match => leg[match.Groups[1].Value]));
            (key.ToString() == StandIn ? byRule : ran).AddRange(names);
        }

        return (ran, byRule, byCondition);
    }

    // --- what the step that looks finds ---------------------------------------------------------------------------

    /// <summary>The head of the pull request whose merge the push to main is. Made up.</summary>
    private const string Head = "feed0000000000000000000000000000000000c3";

    private static Dictionary<string, string> Facts(string @event, string action = "", string draft = "", string target = "main", string branch = FeatureBranch) =>
        new(StringComparer.Ordinal)
    {
        ["EVENT"] = @event,
        ["ACTION"] = action,
        ["DRAFT"] = draft,
        ["SHA"] = Sha,
        ["PULL_REQUEST"] = @event == "pull_request" ? PullRequest : string.Empty,
        ["BRANCH"] = @event == "pull_request" ? branch : string.Empty,
        ["TARGET"] = target,
        ["REPOSITORY"] = "acme/jason",
        ["GH_TOKEN"] = "not-a-token",
    };

    private static string Pull(int number, bool merged, string mergeCommit) =>
        $"{{\"number\":{number},\"merged_at\":{(merged ? "\"2026-10-20T10:00:00Z\"" : "null")},\"merge_commit_sha\":\"{mergeCommit}\",\"head\":{{\"sha\":\"{Head}\"}}}}";

    private static string RunsAnswer(params long[] ids) =>
        $"{{\"workflow_runs\":[{string.Join(',', ids.Select(id => $"{{\"id\":{id},\"head_sha\":\"{Head}\"}}"))}]}}";

    private static string ArtifactsAnswer(bool kept = false, bool expired = false) =>
        kept ? $"{{\"artifacts\":[{{\"name\":\"tested-tree\",\"expired\":{(expired ? "true" : "false")}}}]}}" : "{\"artifacts\":[]}";

    private static string RecordFor(int pullRequest, string tree) =>
        new JsonObject
        {
            ["tree"] = tree,
            ["commit"] = AnotherSha,
            ["pull_request"] = pullRequest,
            ["listing"] = JsonSerializer.SerializeToNode(Tested),
        }.ToJsonString();

    /// <summary>
    /// The step that looks for what an earlier run tested, run as a runner runs it, with a stand-in for <c>gh</c> defined
    /// before it: a PowerShell function, which a command name finds before any program, answers each call named in
    /// <paramref name="answers"/> and fails every other, and <c>gh run download</c> hands over the record kept for that run in
    /// <paramref name="records"/>. What came back: the step's outputs, the calls it made, and the record it found.
    /// </summary>
    private static (Dictionary<string, string> Outputs, List<string> Calls, string? Record) Look(
        Dictionary<string, string> facts, Dictionary<string, string> answers, Dictionary<long, string>? records = null)
    {
        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "earlier.ps1");
        File.WriteAllText(script, Scalar(Step("earlier"), "run"));
        var output = Path.Combine(tree.Root, "output");
        File.WriteAllText(output, string.Empty);
        var calls = Path.Combine(tree.Root, "calls");
        File.WriteAllText(calls, string.Empty);
        var answered = Path.Combine(tree.Root, "answers.json");
        File.WriteAllText(answered, JsonSerializer.Serialize(answers));
        var kept = Path.Combine(tree.Root, "kept");
        foreach (var (id, record) in records ?? new Dictionary<long, string>())
        {
            Directory.CreateDirectory(Path.Combine(kept, id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            File.WriteAllText(Path.Combine(kept, id.ToString(System.Globalization.CultureInfo.InvariantCulture), "record.json"), record);
        }

        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, $$"""
            $ErrorActionPreference = 'Stop'
            function gh {
                $call = $args -join ' '
                Add-Content -LiteralPath '{{calls}}' -Value $call
                if ($args[0] -eq 'run' -and $args[1] -eq 'download') {
                    $from = Join-Path '{{kept}}' $args[2]
                    if (-not (Test-Path -LiteralPath $from)) { $global:LASTEXITCODE = 1; return }
                    $into = $args[[array]::IndexOf($args, '--dir') + 1]
                    New-Item -ItemType Directory -Force -Path $into | Out-Null
                    Copy-Item -LiteralPath (Join-Path $from 'record.json') -Destination $into
                    $global:LASTEXITCODE = 0
                    return
                }
                $answers = Get-Content -Raw -LiteralPath '{{answered}}' | ConvertFrom-Json -AsHashtable
                if ($answers.ContainsKey($call)) { $global:LASTEXITCODE = 0; $answers[$call] } else { $global:LASTEXITCODE = 1 }
            }
            & '{{script}}'
            """);

        var environment = new Dictionary<string, string>(facts) { ["GITHUB_OUTPUT"] = output, ["RUNNER_TEMP"] = tree.Root };
        var ran = Pwsh(environment, "-File", harness);
        Assert.True(ran.Exit == 0, $"the step failed rather than finding nothing: {ran.Stderr}");

        var said = Outputs(output);
        var found = said.TryGetValue("record", out var path) && path.Length > 0 ? File.ReadAllText(path) : null;
        return (said, [.. File.ReadAllLines(calls).Where(line => line.Length > 0)], found);
    }

    // --- reading the workflow -------------------------------------------------------------------------------------

    /// <summary>The tree <see cref="Tested"/> with one path's entry changed or added, of the given mode, or taken out.</summary>
    private static string[] Changed(string path, string mode = "100644", bool removed = false)
    {
        var kept = Tested.Where(line => line[(line.IndexOf('\t') + 1)..] != path);
        return removed ? [.. kept] : [.. kept, $"{mode} blob 5555555555555555555555555555555555555555\t{path}"];
    }

    private static YamlMappingNode Jobs() => Mapping(Parse(Read(Ci)), "jobs");

    private static List<YamlMappingNode> Steps(YamlMappingNode job) =>
        [.. Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>()];

    /// <summary>One of the version job's steps, by its id.</summary>
    private static YamlMappingNode Step(string id) =>
        Assert.Single(Steps(Mapping(Jobs(), "version")), step => Scalar(step, "id") == id);

    private static Dictionary<string, string> Variables(YamlMappingNode env) =>
        env.Children.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString(), StringComparer.Ordinal);

    /// <summary>What a step wrote to <c>GITHUB_OUTPUT</c>, by name.</summary>
    private static Dictionary<string, string> Outputs(string output) =>
        File.ReadAllLines(output)
            .Where(line => line.Contains('=', StringComparison.Ordinal))
            .ToDictionary(line => line[..line.IndexOf('=', StringComparison.Ordinal)], line => line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);

    /// <summary>Every step of a workflow that packages through the shared action.</summary>
    private static List<YamlMappingNode> PackagingCalls(string[] workflow) =>
        [.. Mapping(Parse(Read(workflow)), "jobs").Children.Values.Cast<YamlMappingNode>()
            .SelectMany(job => Node(job, "steps") is YamlSequenceNode steps ? steps.Children.Cast<YamlMappingNode>() : [])
            .Where(step => Scalar(step, "uses") == "./.github/actions/package")];

    /// <summary>The legs of a matrix the version job decided: <c>{"include":[{"rid":…,"os":…}]}</c>, or the checks it skips.</summary>
    private static List<Dictionary<string, string>> Legs(string matrix)
    {
        using var document = JsonDocument.Parse(matrix);
        return [.. document.RootElement.GetProperty("include").EnumerateArray()
            .Select(leg => leg.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal))];
    }
}
