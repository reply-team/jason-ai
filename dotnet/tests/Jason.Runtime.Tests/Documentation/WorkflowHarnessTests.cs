using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The harness the publication tests run scripts and workflow steps through ends as GitHub's own step wrapper ends: a
/// failure reported in its own words, and otherwise the exit code the script left. A harness that answered 0 where a
/// step fails on GitHub would turn a red step green here.
/// </summary>
public sealed class WorkflowHarnessTests
{
    [Theory]
    [InlineData("Write-Output 'done'", 0)]
    [InlineData("exit 3", 3)]
    [InlineData("$global:LASTEXITCODE = 5", 5)]
    [InlineData("throw 'refused'", 1)]
    public void A_script_run_through_the_harness_exits_as_a_github_step_would(string body, int exit)
    {
        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "step.ps1");
        File.WriteAllText(script, body + "\n");

        var run = Script(new Dictionary<string, string>(), script);

        Assert.True(run.Exit == exit, $"The harness exited {run.Exit}, and a GitHub step would exit {exit}: {run.Stderr}");
    }
}
