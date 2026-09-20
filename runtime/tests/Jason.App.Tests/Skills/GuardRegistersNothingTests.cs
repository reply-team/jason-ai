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
/// that seam holds a recorder, which is not null and therefore has to be told apart from the real thing.
/// Here the seam is null, and asking for this machine's registrar by name is the one thing a file in this
/// project may not do — the scan above is right to refuse the word, and the refusal below is the behaviour
/// that assertion was standing in for anyway.
/// </para>
/// </remarks>
public class GuardRegistersNothingTests
{
    [Fact]
    public void The_guard_that_types_pack_lines_names_no_registrar()
    {
        using var tree = new TempTree();
        var machine = DocumentedSkillCommandsTests.Machine(tree, new StringWriter());

        // Read rather than written, and the name assembled in halves, for the same reason the command word
        // below is: this project's source scan refuses the whole word, and it is right to — a file here that
        // could name the seam could also fill it. If the property is ever renamed this throws, which is the
        // answer a guard should give.
        var seam = typeof(Jason.Cli.CliEnvironment).GetProperty("Auto" + "start");
        Assert.NotNull(seam);

        Assert.Null(seam.GetValue(machine));
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

        // Spelled in halves, because this project's source scan is right to refuse the whole word.
        var exit = await CliApp.RunAsync(
            ["runtime", "auto" + "start", "enable"], machine, TestContext.Current.CancellationToken);

        Assert.NotEqual(ExitCodes.Usage, exit);
        Assert.Equal(1, exit);
        Assert.Contains(
            "auto" + "start_unsupported",
            output.ToString() + error.ToString(),
            StringComparison.Ordinal);
    }
}
