using Jason.Cli.Process;
using Jason.Contracts.Execution;

namespace Jason.Cli.Tests.Process;

/// <summary>
/// <c>JASON_CONFINED</c> confines the call it is set on and nothing that call starts: neither start of a runtime the CLI
/// makes hands it on, even where this process holds it. The tests set it in this process's own environment, so they run
/// apart from every other test in the assembly.
/// </summary>
[Collection(ConfinementEnvironmentCollection.Name)]
public class ConfinedStartTests
{
    [Fact]
    public void A_runtime_the_CLI_launches_does_not_carry_the_variable()
    {
        using var dir = new TempPaths();
        var started = Confined(() => (
            RuntimeProcessControl.LaunchStart(dir.Paths, ["jason"], updateLaunch: null),
            RuntimeProcessControl.LaunchStart(dir.Paths, ["jason"], "started")));

        Assert.False(started.Item1.Environment.ContainsKey(Confinement.Variable), "A plain launch passed the variable on.");
        Assert.False(started.Item2.Environment.ContainsKey(Confinement.Variable), "The update's launch passed the variable on.");
    }

    [Fact]
    public void The_command_an_update_hands_the_rest_of_itself_to_does_not_carry_the_variable()
    {
        using var dir = new TempPaths();
        var started = Confined(() => RuntimeProcessControl.RunStart(dir.Paths, ["jason", "update", "apply"]));

        Assert.False(started.Environment.ContainsKey(Confinement.Variable), "The command that starts a runtime passed the variable on.");
    }

    private static T Confined<T>(Func<T> start)
    {
        var original = Environment.GetEnvironmentVariable(Confinement.Variable);
        try
        {
            Environment.SetEnvironmentVariable(Confinement.Variable, "1");
            return start();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Confinement.Variable, original);
        }
    }
}

/// <summary>The tests that set this process's own <c>JASON_CONFINED</c>, kept away from everything else while they run.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfinementEnvironmentCollection
{
    public const string Name = "confinement environment";
}
