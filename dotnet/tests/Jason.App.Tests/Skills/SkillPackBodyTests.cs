using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// Where a skill's body begins, which with its description is the text a <c>verified</c> row records the digest
/// of. Two readers used to answer that question: the front-matter reader, line by line, and the digest, which took the first
/// <c>---</c> line anywhere in the file. Two answers to one question is one answer too many when the thing being
/// answered is what a host was shown.
/// </summary>
public class SkillPackBodyTests
{
    [Fact]
    public void A_file_whose_front_matter_never_closes_has_no_body_to_digest()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Root);
        var file = Path.Combine(tree.Root, "SKILL.md");

        // Front matter that runs straight into the body, and a body that has a rule in it. Taking the first
        // "---" line as the close reads everything above the rule as front matter and "more text" as the whole
        // body — so the digest recorded for such a file would be the digest of its last two lines, and the
        // front-matter guard would be failing at the same time for a reason that reads like a different bug.
        File.WriteAllText(
            file,
            "---\nname: unclosed\ndescription: it forgets to close\n\n# Unclosed\n\nA rule, and then:\n\n---\n\nmore text\n");

        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SkillPack.Body(file));
        Assert.Contains("has no body", failure.Message, StringComparison.Ordinal);
        Assert.Contains(file, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the ordinary case is unchanged, which is what makes the one above a repair rather than a new rule:
    /// the body is everything below the closing fence, whatever the text says further down.
    /// </summary>
    [Fact]
    public void A_body_is_everything_below_the_closing_fence()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.Root);
        var file = Path.Combine(tree.Root, "SKILL.md");
        File.WriteAllText(
            file,
            "---\nname: closed\ndescription: it closes\nmetadata:\n  status: draft\n---\n\n# Closed\n\nA line.\n\n---\n\nAnd another.\n");

        Assert.Equal("\n# Closed\n\nA line.\n\n---\n\nAnd another.", SkillPack.Body(file));
    }

    /// <summary>
    /// What a reading records is what a host read: the description it chose the skill by, then the body it followed. The
    /// description is the line a host reads before it decides to load the skill at all, so one body under two descriptions
    /// is two readings — and a digest of the body alone recorded them as one.
    /// </summary>
    [Fact]
    public void A_reading_covers_the_description_as_well_as_the_body()
    {
        using var tree = new TempTree();
        var read = Skill(tree, "read", "description: use it when the work begins\nmetadata:\n  status: draft");
        var reworded = Skill(tree, "reworded", "description: use it when the work begins or what comes first\nmetadata:\n  status: draft");

        Assert.Equal("use it when the work begins\n\n# One\n\nA line.", SkillPack.Reading(read));
        Assert.Equal(SkillPack.Body(read), SkillPack.Body(reworded));
        Assert.NotEqual(SkillPack.ReadingDigest(read), SkillPack.ReadingDigest(reworded));
    }

    /// <summary>
    /// And the rest of the front matter stays out of it. Promoting a skill edits its status, so a digest that covered the
    /// status could never be recorded: recording it would change it.
    /// </summary>
    [Fact]
    public void A_reading_leaves_the_status_out()
    {
        using var tree = new TempTree();
        var draft = Skill(tree, "draft", "description: use it when the work begins\nmetadata:\n  status: draft");
        var verified = Skill(tree, "verified", "description: use it when the work begins\nmetadata:\n  status: verified");

        Assert.Equal(SkillPack.ReadingDigest(draft), SkillPack.ReadingDigest(verified));
    }

    private static string Skill(TempTree tree, string name, string front)
    {
        var directory = Path.Combine(tree.Root, name);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, SkillPack.SkillFile);
        File.WriteAllText(file, $"---\nname: {name}\n{front}\n---\n\n# One\n\nA line.\n");
        return file;
    }
}
