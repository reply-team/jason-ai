using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// Which of CI's checks run on which push. A draft pull request's pushes run the Linux set; marking it ready for review,
/// every push after that, and every push to <c>main</c> run all ten checks the main branch's ruleset requires. Read out of
/// ci.yml, and the step that decides is run rather than read, so what these facts hold is what a runner does.
/// </summary>
public class CiRunsTests
{
    private static readonly string[] Ci = [".github", "workflows", "ci.yml"];
    private static readonly string[] Release = [".github", "workflows", "release.yml"];
    private static readonly string[] Action = [".github", "actions", "package", "action.yml"];

    /// <summary>The condition a job, or a step, carries when only a full run needs it.</summary>
    private const string Full = "needs.version.outputs.full == 'true'";

    /// <summary>What a draft pull request's push runs.</summary>
    private static readonly string[] TheLinuxSet = ["The version this tree carries", "linux-x64", "install.sh on ubuntu", "an update on linux-x64"];

    /// <summary>What every other run runs: the ten checks the main branch's ruleset requires before a merge.</summary>
    private static readonly string[] AllTen =
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
    ];

    /// <summary>
    /// A pull request runs CI when it is opened, pushed to, reopened, and when it is marked ready for review — the last is
    /// what starts its first full run. <c>main</c> runs it on every push, as before.
    /// </summary>
    [Fact]
    public void A_pull_request_runs_again_when_it_is_marked_ready_and_main_runs_on_every_push()
    {
        var on = Mapping(Parse(Read(Ci)), "on");

        var types = Assert.IsType<YamlSequenceNode>(Node(Mapping(on, "pull_request"), "types"));
        Assert.Equal(["opened", "synchronize", "reopened", "ready_for_review"], types.Children.Select(type => type.ToString()));

        var branches = Assert.IsType<YamlSequenceNode>(Node(Mapping(on, "push"), "branches"));
        Assert.Equal(["main"], branches.Children.Select(branch => branch.ToString()));
    }

    /// <summary>
    /// A run is full on a push to <c>main</c> and on a pull request that is not a draft. The version job, which every other
    /// job needs, decides it once and hands it on with the legs each matrix runs.
    /// </summary>
    [Fact]
    public void A_run_is_full_on_main_and_on_a_pull_request_that_is_not_a_draft()
    {
        Assert.Equal(
            "${{ github.event_name == 'push' || github.event.pull_request.draft == false }}",
            Scalar(Mapping(Deciding(), "env"), "FULL"));

        var outputs = Mapping(Mapping(Jobs(), "version"), "outputs");
        foreach (var output in new[] { "full", "platforms", "updates", "rids" })
        {
            Assert.Equal($"${{{{ steps.legs.outputs.{output} }}}}", Scalar(outputs, output));
        }
    }

    /// <summary>The step that decides keeps every leg on a full run, and the Linux leg alone on a draft.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_step_that_decides_keeps_every_leg_on_a_full_run_and_the_linux_leg_alone_on_a_draft(bool full)
    {
        var decided = Decide(full);

        Assert.Equal(full ? "true" : "false", decided["full"]);
        Assert.Equal(full ? ["linux-x64", "win-x64", "osx-arm64"] : ["linux-x64"], Legs(decided["platforms"]).Select(leg => leg["rid"]));
        Assert.Equal(full ? ["linux-x64", "win-x64"] : ["linux-x64"], Legs(decided["updates"]).Select(leg => leg["rid"]));
        Assert.Equal(full ? "linux-x64,win-x64,osx-arm64" : "linux-x64", decided["rids"]);

        // Each leg keeps the runner it is built on.
        var runners = new Dictionary<string, string> { ["linux-x64"] = "ubuntu-latest", ["win-x64"] = "windows-latest", ["osx-arm64"] = "macos-latest" };
        Assert.All(Legs(decided["platforms"]).Concat(Legs(decided["updates"])), leg => Assert.Equal(runners[leg["rid"]], leg["os"]));
    }

    /// <summary>
    /// What each kind of run reports: all ten required checks on a full run, the Linux set on a draft. A job either runs on
    /// every run or carries the one condition of a full run; a matrix takes its legs from the version job. The ruleset
    /// requires the ten by name, so a renamed check is renamed there in the same change.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_full_run_reports_all_ten_checks_and_a_draft_run_the_linux_set(bool full)
    {
        var decided = Decide(full);
        var reported = new List<string>();
        foreach (var (key, value) in Jobs().Children)
        {
            var job = (YamlMappingNode)value;
            var name = Scalar(job, "name") ?? key.ToString();
            var condition = Scalar(job, "if");
            Assert.True(condition is null || condition == Full, $"The '{key}' job runs on '{condition}', which is neither every run nor a full one.");
            if (condition == Full && !full)
            {
                continue;
            }

            var matrix = Node(job, "strategy") is YamlMappingNode strategy ? Node(strategy, "matrix") : null;
            if (matrix is null)
            {
                reported.Add(name);
                continue;
            }

            var output = Regex.Match((matrix as YamlScalarNode)?.Value ?? string.Empty, @"^\$\{\{\s*fromJSON\(needs\.version\.outputs\.(\w+)\)\s*\}\}$");
            Assert.True(output.Success, $"The '{key}' job's matrix is not the one the version job decides.");
            foreach (var leg in Legs(decided[output.Groups[1].Value]))
            {
                reported.Add(Regex.Replace(name, @"\$\{\{\s*matrix\.(\w+)\s*\}\}", match => leg[match.Groups[1].Value]));
            }
        }

        Assert.Equal((full ? AllTen : TheLinuxSet).Order(StringComparer.Ordinal), reported.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// install.sh's job runs on every run and assembles its feed from the archives that run packaged, which on a draft is
    /// the Linux one alone. The feed in the layout 0.1.0 published names all three archives, so that step runs on a full
    /// run only, as install.ps1's whole job does, which still requires all three.
    /// </summary>
    [Fact]
    public void The_install_jobs_require_the_archives_their_run_packaged()
    {
        var installSh = Steps(Mapping(Jobs(), "install-sh"));
        var assembling = Assert.Single(installSh, step => Scalar(step, "name") == "Assemble the feed the way a release is assembled");
        Assert.Contains("-Require ${{ needs.version.outputs.rids }}", Scalar(assembling, "run"), StringComparison.Ordinal);

        var fixture = Assert.Single(installSh, step => Scalar(step, "name") == "Install from the schema-1 fixture, the feed layout 0.1.0 published");
        Assert.Equal(Full, Scalar(fixture, "if"));
        Assert.All(installSh.Where(step => step != fixture), step => Assert.Null(Scalar(step, "if")));

        Assert.Equal(Full, Scalar(Mapping(Jobs(), "install-ps1"), "if"));
        Assert.Contains("-Require win-x64,linux-x64,osx-arm64", Job(Read(Ci), "install-ps1"), StringComparison.Ordinal);
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

    private static YamlMappingNode Jobs() => Mapping(Parse(Read(Ci)), "jobs");

    private static List<YamlMappingNode> Steps(YamlMappingNode job) =>
        [.. Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>()];

    /// <summary>The version job's step that decides whether this run is full and which legs each matrix runs.</summary>
    private static YamlMappingNode Deciding() =>
        Assert.Single(Steps(Mapping(Jobs(), "version")), step => Scalar(step, "id") == "legs");

    /// <summary>Every step of a workflow that packages through the shared action.</summary>
    private static List<YamlMappingNode> PackagingCalls(string[] workflow) =>
        [.. Mapping(Parse(Read(workflow)), "jobs").Children.Values.Cast<YamlMappingNode>()
            .SelectMany(job => Node(job, "steps") is YamlSequenceNode steps ? steps.Children.Cast<YamlMappingNode>() : [])
            .Where(step => Scalar(step, "uses") == "./.github/actions/package")];

    /// <summary>
    /// The deciding step's script, run as a runner runs a <c>pwsh</c> step, with the event's answer in <c>FULL</c> and the
    /// legs the workflow gives it: what it wrote to <c>GITHUB_OUTPUT</c>, by name.
    /// </summary>
    private static Dictionary<string, string> Decide(bool full)
    {
        var step = Deciding();
        var env = Mapping(step, "env");

        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "legs.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var output = Path.Combine(tree.Root, "output");
        File.WriteAllText(output, string.Empty);

        var environment = new Dictionary<string, string>
        {
            ["FULL"] = full ? "true" : "false",
            ["PLATFORMS"] = Scalar(env, "PLATFORMS")!,
            ["UPDATES"] = Scalar(env, "UPDATES")!,
            ["GITHUB_OUTPUT"] = output,
        };
        var ran = Pwsh(environment, "-Command", $"$ErrorActionPreference = 'Stop'; & '{script}'");
        Assert.True(ran.Exit == 0, ran.Stderr);

        return File.ReadAllLines(output)
            .Where(line => line.Contains('=', StringComparison.Ordinal))
            .ToDictionary(line => line[..line.IndexOf('=', StringComparison.Ordinal)], line => line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
    }

    /// <summary>The legs of a matrix the version job decided: <c>{"include":[{"rid":…,"os":…}]}</c>.</summary>
    private static List<Dictionary<string, string>> Legs(string matrix)
    {
        using var document = JsonDocument.Parse(matrix);
        return [.. document.RootElement.GetProperty("include").EnumerateArray()
            .Select(leg => leg.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal))];
    }
}
