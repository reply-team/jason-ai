using System.Text.RegularExpressions;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests.Execution;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The release page publishes the skill problem codes as a table: every code, what it means, and what a launch does with
/// it. The codes are held to the contracts' closed set both ways, and the launch column to the table the launcher's own
/// test is held to, so the page cannot name a code the runtime does not give or say a launch refuses what it runs.
/// </summary>
public sealed partial class SkillProblemCodesDocTests
{
    [Fact]
    public void The_release_page_lists_every_code_with_what_a_launch_does()
    {
        var rows = Rows();

        Assert.Equal(SkillProblemCodes.All.Order(StringComparer.Ordinal), rows.Keys.Order(StringComparer.Ordinal));
        foreach (var code in SkillProblemCodes.All)
        {
            Assert.True(
                rows[code] == SkillProblemLaunches.ByCode[code],
                $"The page says a launch with {code} is \"{rows[code]}\"; the launcher's table says \"{SkillProblemLaunches.ByCode[code]}\".");
        }
    }

    [Fact]
    public void The_release_page_names_every_word_for_a_refusal()
    {
        var section = Section();

        foreach (var word in SkillProblemCodes.Exceptions)
        {
            Assert.Contains($"`{word}`", section, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The pages that say why the launcher refuses a skill name every reason it refuses for and point at the table, so a
    /// reader is not told of two refusals where there are five.
    /// </summary>
    [Theory]
    [InlineData("docs/work-execution.md", "because its `SKILL.md` is a link, cannot be read, gives itself no name or names something other than the role")]
    [InlineData("docs/work-execution.md", "[release-and-update.md](release-and-update.md#and-what-it-says-about-the-skills-it-will-teach-from) lists each by its code")]
    [InlineData("skills/README.md", "a role whose skill file is a link, cannot be read, gives itself no name or names itself differently from its directory")]
    [InlineData("skills/README.md", "`jason status` reports each of these in a sentence that ends with its code")]
    public void The_pages_that_say_why_a_launch_is_refused_name_every_reason(string page, string sentence) =>
        Assert.Contains(sentence, System.Text.RegularExpressions.Regex.Replace(Source(page), @"\s+", " "), StringComparison.Ordinal);

    private static Dictionary<string, string> Rows() =>
        Row().Matches(Section()).ToDictionary(match => match.Groups["code"].Value, match => match.Groups["launch"].Value.Trim(), StringComparer.Ordinal);

    /// <summary>The skills subsection of the page, up to the next section.</summary>
    private static string Section()
    {
        var page = Source("docs/release-and-update.md").ReplaceLineEndings("\n");
        var start = page.IndexOf("### And what it says about the skills it will teach from", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/release-and-update.md no longer has its skills subsection.");
        var end = page.IndexOf("\n## ", start, StringComparison.Ordinal);
        return end < 0 ? page[start..] : page[start..end];
    }

    [GeneratedRegex(@"^\| `(?<code>[a-z_]+)` \| [^|\n]+ \| (?<launch>[^|\n]+) \|$", RegexOptions.Multiline)]
    private static partial Regex Row();

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
