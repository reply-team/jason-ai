namespace Jason.App.Tests.Skills;

/// <summary>
/// What the runtime pack's catalog says the skills do not teach yet is read from the skills themselves: a skill that
/// gains one of the verbs fails here until the catalog says so.
/// </summary>
public sealed class CatalogClaimsTests
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
        var catalog = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(SkillPack.Catalog()), @"\s+", " ");

        Assert.Empty(Mentioning("jason skills install"));
        Assert.Empty(Mentioning("jason skills update"));
        Assert.Contains("None of the five mentions `jason skills install` or `jason skills update`", catalog, StringComparison.Ordinal);

        Assert.Equal(["operating-the-installation"], Mentioning("jason status"));
        Assert.Contains("`jason status` is mentioned only by `operating-the-installation`", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("None of the five mentions `jason skills install`, `jason skills update` or `jason status`", catalog, StringComparison.Ordinal);
    }
}
