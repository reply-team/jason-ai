using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

/// <summary>
/// Where a skill's body begins, which is the text a <c>verified</c> row records the digest of. Two readers used
/// to answer that question: the front-matter reader, line by line, and the digest, which took the first
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
}
