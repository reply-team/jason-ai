using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// What the runtime pack's catalog says the skills do not teach yet is read from the skills themselves: a skill that
/// gains one of the verbs fails here until the catalog says so.
/// </summary>
public sealed partial class CatalogClaimsTests
{
    /// <summary>The skills a person's own session reads, by the verbs each mentions.</summary>
    private static IReadOnlyList<string> Mentioning(string verb) =>
        [.. SkillPack.All()
            .Where(skill => !skill.IsRole && File.ReadAllText(skill.File).Contains(verb, StringComparison.Ordinal))
            .Select(skill => skill.Name)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void The_catalog_says_which_skills_mention_the_newer_verbs()
    {
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");

        Assert.Empty(Mentioning("jason skills install"));
        Assert.Equal(["operating-the-installation"], Mentioning("jason skills update"));
        Assert.Equal(["operating-the-installation"], Mentioning("jason status"));
        Assert.Contains(
            $"None of the {InteractiveCount()} mentions `jason skills install`; `jason skills update` and `jason status` are mentioned only by `operating-the-installation`",
            catalog,
            StringComparison.Ordinal);
        Assert.DoesNotContain("mentions `jason skills install` or `jason skills update`", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("None of the five mentions `jason skills install`, `jason skills update` or `jason status`", catalog, StringComparison.Ordinal);
    }

    /// <summary>
    /// The catalog and the walkthrough count the skills a person's session reads, and a count is the claim that goes
    /// stale when a skill is added: the sixth arrived while three sentences still said five, and a reader told five
    /// never looks for the sixth.
    /// </summary>
    [Fact]
    public void The_pages_count_the_skills_a_person_s_session_reads_as_the_pack_holds_them()
    {
        var word = InteractiveCount();
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        var golden = Whitespace().Replace(File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "golden-path.md")), " ");

        foreach (var (page, text, pattern, expected) in new[]
        {
            ("skills/runtime/README.md", catalog, @"None of the (\w+) mentions", word),
            ("skills/runtime/README.md", catalog, @"That deploys the (\w+) interactive skills", word),
            ("docs/golden-path.md", golden, @"(\w+) skills, one per moment", char.ToUpperInvariant(word[0]) + word[1..]),
        })
        {
            var said = Regex.Match(text, pattern);
            Assert.True(
                said.Success && said.Groups[1].Value == expected,
                $"'{page}' counts the skills a person's session reads as '{(said.Success ? said.Groups[1].Value : "nothing")}', "
                + $"and the pack holds {expected.ToLowerInvariant()} of them (the pattern is '{pattern}').");
        }
    }

    /// <summary>How many skills a person's session reads, as the pages spell it.</summary>
    private static string InteractiveCount()
    {
        string[] words = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];
        var count = SkillPack.All().Count(skill => !skill.IsRole);
        Assert.True(count < words.Length, $"The pack holds {count} skills a person's session reads, past the words this guard spells.");
        return words[count];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
