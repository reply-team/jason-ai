using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The guard beside this one types, for real, every command the pack prints — and one of the things the pack
/// teaches a person to type registers something with the operating system. The source scan in this project
/// cannot see that: the line arrives from a skill's text rather than from a C# literal, so
/// <see cref="NothingHereRegistersAnythingTests"/> reads nothing and says nothing. The seam is proved here
/// instead, because safe-by-construction is exactly what a later edit breaks with nothing going red.
/// </summary>
/// <remarks>
/// <para>
/// The seam is left null rather than handed a recorder, which is the stronger of the two: the production code
/// answers a missing registrar with a refusal, so this guard cannot register anything even if somebody later
/// hands it a line nobody expected.
/// </para>
/// <para>
/// The sibling guard in <c>Jason.Cli.Tests</c> asserts in addition that what it holds is not the registrar
/// this machine would use. That assertion is not repeated here and is not missing: it is needed there because
/// that seam holds a recorder, which is not null and therefore has to be told apart from the real thing. Here
/// the seam is null, and the refusal below is the behaviour that assertion was standing in for anyway.
/// </para>
/// <para>
/// This file names the verb plainly. It may, because it reaches neither the router's own entry point nor the
/// real environment — which is the pair <see cref="NothingHereRegistersAnythingTests"/> forbids.
/// </para>
/// </remarks>
public class GuardRegistersNothingTests
{
    [Fact]
    public void The_guard_that_types_pack_lines_names_no_registrar()
    {
        using var tree = new TempTree();
        var machine = DocumentedSkillCommandsTests.Machine(tree, new StringWriter());

        Assert.Null(machine.Autostart);
    }

    /// <summary>
    /// And names neither of the two seams the skills verbs arrived with, for the same reason and with the same
    /// answer. A pack that printed <c>jason skills install</c> would have this guard type it: with a locator
    /// behind it that is a write into somebody's own skills directory, and with a program runner behind it a
    /// <c>git clone</c> over the network. Null refuses, and null is what this holds.
    /// </summary>
    [Fact]
    public void The_guard_that_types_pack_lines_can_reach_no_harness_root_and_start_no_program()
    {
        using var tree = new TempTree();
        var machine = DocumentedSkillCommandsTests.Machine(tree, new StringWriter());

        Assert.Null(machine.Harnesses);
        Assert.Null(machine.Programs);
    }

    /// <summary>
    /// And what that comes to when the line is really typed, through the guard's own path: the line a page
    /// teaches somebody to type is understood, answered with a business refusal, and nothing on this machine
    /// changes. Exit 1 rather than 2 is the whole point — the guard beside this one asserts the line is not a
    /// usage error, and this says what it is instead.
    /// </summary>
    [Fact]
    public async Task The_line_that_would_register_something_is_refused_rather_than_performed()
    {
        using var tree = new TempTree();
        var output = new StringWriter();
        var error = new StringWriter();
        var machine = DocumentedSkillCommandsTests.Machine(tree, new StringWriter()) with
        {
            Out = output,
            Error = error,
        };

        var exit = await CliApp.RunAsync(
            ["runtime", "autostart", "enable"], machine, TestContext.Current.CancellationToken);

        Assert.NotEqual(ExitCodes.Usage, exit);
        Assert.Equal(1, exit);
        Assert.Contains("autostart_unsupported", output.ToString() + error.ToString(), StringComparison.Ordinal);
    }
}
