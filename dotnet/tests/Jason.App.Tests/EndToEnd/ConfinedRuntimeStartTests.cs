using Jason.Cli;
using Jason.Contracts.Execution;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// <c>runtime</c> is a whole noun in <see cref="ConfinedVerbs"/>, so <c>jason runtime start</c> under
/// <c>JASON_CONFINED</c> is refused with the sentence before any request: no runtime is started. What the old row
/// also protected, that a runtime started where the refusal is not in play does not carry the variable on, is held by
/// <c>ConfinedStartTests</c> in the CLI's tests, and the runtime's indifference to the variable by the foreground row
/// in <see cref="EndToEndTests"/>.
/// </summary>
public class ConfinedRuntimeStartTests
{
    [Fact]
    public async Task Under_the_variable_runtime_start_is_refused_with_the_sentence_and_no_runtime_is_started()
    {
        using var it = GoldenPath.Create("start-confined");
        var confined = new Dictionary<string, string> { [Confinement.Variable] = "1" };

        var started = await GoldenPath.JasonAsync(it, confined, "runtime", "start");
        try
        {
            Assert.Equal(1, started.ExitCode);
            Assert.Equal(ConfinedVerbs.Sentence, started.Error.Trim());
            Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
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
