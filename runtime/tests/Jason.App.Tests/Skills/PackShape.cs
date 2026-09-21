namespace Jason.App.Tests.Skills;

/// <summary>
/// The front-matter rule, in one place, because two packs obey it. Three readers parse this front matter —
/// the launcher's, the role guard's and the test's — and a second copy of the rule is how they begin to
/// disagree with each other while each one looks correct.
/// </summary>
internal static class PackShape
{
    public static void ReadsAsTheSkillItIs(PackSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var front = SkillFrontMatter.Read(skill.File);

        Assert.True(
            front.Problems.Count == 0,
            $"'{skill.File}' does not read as front matter: {string.Join("; ", front.Problems)}");

        // A skill is looked up by the directory it lives in and must name itself the same way; a host answers
        // a mismatch by loading nothing and saying nothing.
        Assert.Equal(skill.Name, front.Top.GetValueOrDefault("name"));

        // The description is what a host reads to decide whether to load the skill at all.
        var description = front.Top.GetValueOrDefault("description") ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(description), $"'{skill.File}' describes nothing.");
        Assert.True(
            description.Length <= 1536,
            $"'{skill.File}' has a description of {description.Length} characters, and a host truncates the "
            + "combined description and when_to_use in the skill listing at 1,536 - the listing it chooses "
            + "from, so everything past it is invisible where it matters.");

        // Everything a pack invents of its own lives under metadata, which is the documented map for exactly
        // that. An unknown top-level key is tolerated by one host and is a hard error outside it. Assert.Equal
        // carries no message, and a tree of twenty-three files needs one: the failure has to name the file
        // somebody would open and the key that does not belong.
        var top = front.Keys.Order().ToArray();
        Assert.True(
            top is ["description", "metadata", "name"],
            $"'{skill.File}' declares the top-level keys [{string.Join(", ", top)}]. The pack's front matter "
            + "is name, description and metadata, and nothing else: an unknown top-level key is tolerated by "
            + "one host and is a hard error outside it.");

        // And metadata holds exactly one entry in this version. A second custom key admitted silently is the
        // thing moving status under metadata was about: the map is the documented place for what a pack
        // invents, not a place where anything may accumulate unread.
        var custom = front.Metadata.Keys.Order().ToArray();
        Assert.True(
            custom is ["status"],
            $"'{skill.File}' puts [{string.Join(", ", custom)}] under metadata. This version has exactly one "
            + "entry there, status, and a second one admitted silently is a key nobody reads.");
        Assert.Contains(front.Metadata.GetValueOrDefault("status"), (string[])["draft", "verified"]);
    }
}
