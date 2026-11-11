using Jason.Contracts.Execution;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// <c>JASON_CONFINED</c> confines the CLI call it is set on, and <c>jason runtime start</c> with the variable set starts
/// a runtime as it does without it. The start drops the variable from the runtime it starts, so this row shows only that
/// the start is not broken by it; that the runtime itself is indifferent is held by the foreground row in
/// <see cref="EndToEndTests"/>, which hands the variable to the runtime's own environment.
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
