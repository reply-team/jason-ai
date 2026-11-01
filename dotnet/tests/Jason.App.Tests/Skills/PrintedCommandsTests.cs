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

    /// <summary>
    /// Three tildes open a fence as three backticks do, and each is closed only by its own marker: a tilde line inside
    /// a backtick fence is part of what that fence prints, not the end of it.
    /// </summary>
    [Fact]
    public void A_tilde_fence_prints_too_and_each_fence_closes_on_its_own_marker()
    {
        string[] page =
        [
            "~~~",
            "jason rolenote list cmp_1",
            "~~~",
            "jason workitem list",
            "```",
            "~~~",
            "jason campaign get cmp_1",
            "```",
            "jason decision list",
        ];

        Assert.Equal(["jason rolenote list cmp_1", "jason campaign get cmp_1"], SkillPack.PrintedCommands(page));
    }
}
