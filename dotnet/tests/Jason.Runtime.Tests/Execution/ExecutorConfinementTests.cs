using Jason.Contracts.Execution;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// <c>JASON_CONFINED</c> confines the CLI call it is set on and never the role work a runtime launches: an agent the
/// runtime starts does not carry it, even where the runtime's own environment holds it, as a runtime run in the
/// foreground under the variable does.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ExecutorConfinementTests
{
    private const string WorkItemId = "wi_one";
    private const string AttemptId = "att_one";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_the_runtime_launches_does_not_carry_the_variable()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        var original = Environment.GetEnvironmentVariable(Confinement.Variable);
        try
        {
            Environment.SetEnvironmentVariable(Confinement.Variable, "1");

            await AiRoleCommandTests.RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Confinement.Variable, original);
        }

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains("confined-in-env=unset", stderr, StringComparison.Ordinal);
    }
}
