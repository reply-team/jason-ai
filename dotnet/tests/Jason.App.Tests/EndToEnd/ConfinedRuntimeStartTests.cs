using Jason.Contracts.Execution;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// <c>JASON_CONFINED</c> confines the CLI call it is set on, and the runtime is indifferent to it: the shipped program,
/// asked to start a runtime with the variable set, starts one as it does without it.
/// </summary>
public class ConfinedRuntimeStartTests
{
    [Fact]
    public async Task A_runtime_started_with_the_variable_set_starts_as_it_does_without_it()
    {
        using var it = GoldenPath.Create("start-confined");
        var confined = new Dictionary<string, string> { [Confinement.Variable] = "1" };

        var started = await GoldenPath.JasonAsync(it, confined, "runtime", "start");
        try
        {
            GoldenPath.AssertSuccess(started);
            Assert.NotNull(GoldenPath.ReadDescriptor(it.Paths));
            GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, confined, "runtime", "status"));
        }
        finally
        {
            if (GoldenPath.ReadDescriptor(it.Paths) is not null)
            {
                await GoldenPath.StopAsync(it);
            }
        }
    }
}
