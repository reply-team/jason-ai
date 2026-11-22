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

    /// <summary>
    /// A line that hands its request to standard input in a here-document carries the fence's lines after it, up to the
    /// delimiter, as its body; and the body is never a command of its own, whatever its lines begin with.
    /// </summary>
    [Fact]
    public void A_here_document_is_the_body_of_the_line_that_opens_it_and_never_a_command()
    {
        string[] page =
        [
            "```",
            "jason campaign create --name \"Q3\" --file - <<'EOF'",
            "{\"context\":{\"brief\":\"two words\"}}",
            "jason campaign list",
            "EOF",
            "jason contact list",
            "```",
        ];

        var printed = SkillPack.Printed(page, "page.md");

        Assert.Equal(
            ["jason campaign create --name \"Q3\" --file - <<'EOF'", "jason contact list"],
            printed.Select(command => command.Line));
        Assert.Equal("{\"context\":{\"brief\":\"two words\"}}\njason campaign list", printed[0].Body);
        Assert.Null(printed[1].Body);
        Assert.Equal(printed.Select(command => command.Line), SkillPack.PrintedCommands(page));
    }

    /// <summary>A body inside an indented fence loses the indentation its command line has, and nothing more.</summary>
    [Fact]
    public void A_body_in_an_indented_fence_loses_the_fence_s_indentation()
    {
        string[] page =
        [
            "1. Create it:",
            "   ```",
            "   jason campaign create --file - <<'EOF'",
            "   {",
            "     \"context\": {}",
            "   }",
            "   EOF",
            "   ```",
        ];

        Assert.Equal("{\n  \"context\": {}\n}", Assert.Single(SkillPack.Printed(page, "page.md")).Body);
    }

    /// <summary>A here-document the fence closes before its delimiter is a page that prints half a command: the read fails, naming the file and the line.</summary>
    [Fact]
    public void A_here_document_that_never_reaches_its_delimiter_fails_the_read_naming_the_file_and_the_line()
    {
        string[] page =
        [
            "Some prose.",
            "```",
            "jason campaign create --file - <<'EOF'",
            "{\"context\":{}}",
            "```",
            "EOF",
        ];

        var refused = Assert.Throws<InvalidDataException>(() => SkillPack.Printed(page, "skills/runtime/x/SKILL.md"));

        Assert.Contains("skills/runtime/x/SKILL.md:3", refused.Message, StringComparison.Ordinal);
        Assert.Contains("'EOF'", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SkillPack.PrintedCommands(page));
    }

    /// <summary>
    /// A line that reads its request from a file names that file; the page has to say what the file is somewhere else,
    /// or a reader is told to type a file nobody described.
    /// </summary>
    [Fact]
    public void A_file_on_standard_input_names_a_file_the_page_also_prints()
    {
        string[] page =
        [
            "`brief.json` holds the request:",
            "```",
            "jason campaign create --file - < brief.json",
            "```",
        ];

        var command = Assert.Single(SkillPack.Printed(page, "page.md"));
        Assert.Equal("brief.json", command.InputFile);
        Assert.Null(command.Body);

        string[] undescribed = ["```", "jason campaign create --file - < brief.json", "```"];
        var refused = Assert.Throws<InvalidDataException>(() => SkillPack.Printed(undescribed, "page.md"));
        Assert.Contains("page.md:2", refused.Message, StringComparison.Ordinal);
        Assert.Contains("brief.json", refused.Message, StringComparison.Ordinal);
    }
}
