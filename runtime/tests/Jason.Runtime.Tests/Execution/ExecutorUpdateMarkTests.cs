using Jason.Contracts.Update;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The update's mark is for the runtime an update starts, and stops there: an agent the runtime launches never
/// carries it, even where the runtime's own environment still holds one.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ExecutorUpdateMarkTests
{
    private const string WorkItemId = "wi_one";
    private const string AttemptId = "att_one";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_the_runtime_launches_does_not_carry_the_updates_mark()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        var original = Environment.GetEnvironmentVariable(UpdateLaunch.Variable);
        try
        {
            Environment.SetEnvironmentVariable(UpdateLaunch.Variable, UpdateLaunch.At(UpdateStep.Started));

            await AiRoleCommandTests.RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateLaunch.Variable, original);
        }

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains("update-mark-in-env=unset", stderr, StringComparison.Ordinal);
    }
}
