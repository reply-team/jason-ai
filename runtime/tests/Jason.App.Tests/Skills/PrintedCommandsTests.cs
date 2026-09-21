namespace Jason.App.Tests.Skills;

/// <summary>
/// The one reader of what a page prints. Two copies of this loop lived in this project, one of which had been
/// strengthened — whole lines, so a printed command cannot quietly grow an option a test never runs — while
/// the other had not, and neither had a test of its own. A rule about what a skill teaches is worth keeping in
/// one place, with something holding it.
/// </summary>
public class PrintedCommandsTests
{
    [Fact]
    public void Only_the_jason_lines_inside_a_fence_are_printed_commands()
    {
        string[] page =
        [
            "Prose about `jason campaign list`, which is not a printed command.",
            "```",
            "jason campaign list",
            "  jason contact list",
            "echo not-jason",
            "```",
            "jason workitem list",
            "```sh",
            "jason approval list",
            "```",
        ];

        Assert.Equal(
            ["jason campaign list", "jason contact list", "jason approval list"],
            SkillPack.PrintedCommands(page));
    }
}
