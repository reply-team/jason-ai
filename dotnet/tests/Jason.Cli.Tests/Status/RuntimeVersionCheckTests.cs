using Jason.Cli.Status;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The required check that the running runtime is the build this CLI is. A binary can change under a running runtime —
/// a package manager's upgrade, a file copied over by hand — and the runtime keeps running the old one, starting plugin
/// hosts from an image that may be gone. Nothing said so: <c>ready</c> was true while provider work failed at launch.
/// </summary>
public sealed class RuntimeVersionCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_runtime_that_is_this_build_passes()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, check => check.Name == "runtime_version");
        Assert.True(check.Required);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Null(check.Fix);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>
    /// Another build is a required failure, with the fact and the one command that brings the runtime up as this build —
    /// and <c>ready</c> is false, which is the whole point: everything else here passes.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "a runtime of another build is not ready")]
    public async Task A_runtime_that_is_another_build_is_a_required_failure_with_the_restart()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir, version: "0.8.1");

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, build: "0.9.0"), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready, "ready said yes to a runtime that is not this build");
        Assert.Equal(ExitCodes.ApiError, exit);
        var check = Assert.Single(report.Checks, check => check.Name == "runtime_version");
        Assert.True(check.Required);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal("The runtime running is 0.8.1; this CLI is 0.9.0.", check.Fact);
        Assert.Equal("jason runtime restart", check.Fix);
        Assert.All(report.Checks.Where(other => other.Name != "runtime_version" && other.Required), other => Assert.Equal(CheckState.Ok, other.State));

        // And the person reading it is given the same line to type.
        var human = new StringWriter();
        await CliApp.RunAsync(["status", "--human"], StatusCommandTests.Machine(dir, human, build: "0.9.0"), Ct);
        var text = human.ToString();
        Assert.Contains("The runtime running is 0.8.1; this CLI is 0.9.0.", text, StringComparison.Ordinal);
        Assert.Contains("jason runtime restart", text[text.IndexOf("To repair:", StringComparison.Ordinal)..].Split(Environment.NewLine).Select(line => line.Trim()));
    }

    /// <summary>
    /// The whole string, the pre-release part included: a development build and the release it is on its way to are two
    /// builds. So are two builds of one version, which the build metadata tells apart.
    /// </summary>
    [Theory]
    [InlineData("0.9.0-dev", "0.9.0")]
    [InlineData("0.9.0", "0.9.0-dev")]
    [InlineData("0.9.0-dev+1ec19a3", "0.9.0-dev+4910ebb")]
    public async Task Two_builds_that_differ_only_after_the_version_are_two_builds(string runtime, string cli)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir, version: runtime);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, build: cli), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "runtime_version");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal($"The runtime running is {runtime}; this CLI is {cli}.", check.Fact);
    }

    /// <summary>
    /// A runtime that did not answer is not asked which build it is. The <c>runtime</c> check already says it did not
    /// answer, and saying the same thing twice as two failures would be one cause counted twice.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_did_not_answer_is_not_asked_which_build_it_is()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, check => check.Name == "runtime_version");
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.True(check.Required);
        Assert.Equal("jason runtime start", check.Fix);

        // Not ready, and through the runtime check alone: it is the one required check that failed.
        Assert.False(report.Ready);
        Assert.Equal(["runtime"], report.Checks.Where(other => other.Required && other.State == CheckState.Failed).Select(other => other.Name));
    }

    /// <summary>The real environment compares with this build's own stamp; only a test names another.</summary>
    [Fact]
    public void The_real_environment_names_no_build_of_its_own()
    {
        Assert.Null(CliEnvironment.Default().BuildVersion);
    }

    /// <summary>The help lists it among the required checks, where the pages copy the list from.</summary>
    [Fact]
    public async Task Help_lists_it_among_the_required_checks()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var help = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " ");
        var required = help[help.IndexOf("Required — a failure exits 1:", StringComparison.Ordinal)..help.IndexOf("Optional — absent never fails:", StringComparison.Ordinal)];
        Assert.Contains("the running runtime is the build this CLI is", required, StringComparison.Ordinal);
    }
}
