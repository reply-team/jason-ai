using Jason.Cli.Autostart;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// Two guards in this project type what a page prints, for real, through the whole command line. One of the
/// nouns they type is <c>jason runtime autostart </c>, and the verb behind it registers something with the
/// operating system. So both of them are handed a registrar that records instead of one that registers, and
/// this says so in one place rather than leaving it to whoever next edits either file.
/// </summary>
/// <remarks>
/// A third guard, in <c>Jason.App.Tests</c>, types what the skills pack prints. It is not covered here: it
/// names no registrar at all, which the seam answers with a refusal, and it proves that about itself in its
/// own project.
/// </remarks>
/// <remarks>
/// The seam defaults to the refusal rather than to this machine, so forgetting costs an
/// <c>autostart_unsupported</c> and not a logon task. This test is the other half: with the refusal, the
/// documented line would be typed and answered by nothing, and a guard that proves a command is understood has
/// to run the command.
/// </remarks>
public class GuardsRegisterNothingTests
{
    [Fact]
    public void The_guard_that_types_page_lines_registers_nothing()
    {
        using var directory = new TempPaths();

        var machine = DocumentedPageCommandsTests.Machine(directory, new StringWriter());

        Assert.IsType<RecordingRegistrar>(machine.Autostart);
    }

    /// <summary>
    /// The walkthrough, not the skills: the pack moved to <c>Jason.App.Tests</c>, and a test named for lines
    /// this guard no longer types would be a guard over nothing.
    /// </summary>
    [Fact]
    public void The_guard_that_types_walkthrough_lines_registers_nothing()
    {
        using var directory = new TempPaths();

        var machine = DocumentedCommandsTests.Machine(directory, new StringWriter());

        Assert.IsType<RecordingRegistrar>(machine.Autostart);
    }

    /// <summary>
    /// The same thing for the two seams that arrived with the skills verbs. The day a page prints
    /// <c>jason skills install</c>, these guards type it for real: with this machine's locator behind it that
    /// is a write into the home directory of whoever ran the suite, and with a real program runner behind it a
    /// <c>git clone</c> over the network. Both are held inside the test's own tree here, and both seams default
    /// to a refusal so that forgetting costs a refusal rather than either of those.
    /// </summary>
    [Fact]
    public void Neither_guard_can_reach_a_real_harness_root_or_start_a_real_program()
    {
        using var directory = new TempPaths();
        var real = HarnessLocators.ForThisMachine();

        foreach (var machine in new[]
                 {
                     DocumentedPageCommandsTests.Machine(directory, new StringWriter()),
                     DocumentedCommandsTests.Machine(directory, new StringWriter()),
                 })
        {
            Assert.NotNull(machine.Harnesses);
            Assert.NotEqual(real.GetType(), machine.Harnesses!.GetType());
            Assert.All(
                machine.Harnesses.Detect(),
                root => Assert.StartsWith(directory.Paths.Root, root.Directory, StringComparison.Ordinal));

            Assert.IsType<FakeProgramRunner>(machine.Programs);

            // And the process table, because a skills source fetched with git would otherwise be started by
            // the one that starts real processes: a null seam there falls back to the real thing.
            Assert.NotNull(machine.Processes);
            Assert.IsType<FakeProcessControl>(machine.Processes);
        }
    }

    /// <summary>
    /// And neither of them is this machine's. Said as its own assertion because "not null" is the cheap half:
    /// what matters is that it is not the thing that would write a logon task.
    /// </summary>
    [Fact]
    public void Neither_guard_holds_the_registrar_this_machine_would_use()
    {
        using var directory = new TempPaths();
        var real = AutostartRegistrars.ForThisMachine();

        foreach (var machine in new[]
                 {
                     DocumentedPageCommandsTests.Machine(directory, new StringWriter()),
                     DocumentedCommandsTests.Machine(directory, new StringWriter()),
                 })
        {
            Assert.NotNull(machine.Autostart);
            Assert.NotSame(real, machine.Autostart);
            Assert.NotEqual(real.GetType(), machine.Autostart!.GetType());
        }
    }
}
