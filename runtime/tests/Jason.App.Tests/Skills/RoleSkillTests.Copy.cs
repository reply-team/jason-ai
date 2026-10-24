using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    private const string CopySection = "## Write as the sender, in the company's tone";

    /// <summary>
    /// Both roles that write copy read the two preferences, and reading them changes nothing unless the copy is written by
    /// them: in the company's tone, as the sender named, and in a language that marks the speaker's gender, in the forms
    /// the person gave for that sender rather than ones guessed from a name.
    /// </summary>
    [Theory]
    [InlineData("copywriter")]
    [InlineData("personalizer")]
    public void A_role_that_writes_copy_writes_in_the_tone_as_the_sender_in_their_gender_forms(string role)
    {
        var skill = Flattened(role);

        Assert.Contains(
            "Write in the tone `tone_of_voice` describes: its `summary` and `formality`, its `greeting` and `sign_off` as guidance for how a message opens and closes, and its `examples` as what the person likes, never as text to copy.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Write as the sender `sender.identity` names — the first in its list, unless the campaign names another by `name` or by one of their `aliases` — and sign as their `name`.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "In a language that marks the speaker's grammatical gender, every form that refers to the sender takes their `gender_forms`; `neutral` means wording that marks none. Never read a gender from a name.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A tone nobody set leaves the copy plain and says so; a sender nobody set leaves nobody to write as, which only a
    /// person can change, so the item ends as a failure rather than with a sender somebody invented. The register is not
    /// called <c>neutral</c>: that word is one of the gender forms in the same section.
    /// </summary>
    [Theory]
    [InlineData("copywriter")]
    [InlineData("personalizer")]
    public void A_role_that_writes_copy_says_what_it_does_when_a_preference_has_no_value(string role)
    {
        var skill = Flattened(role);

        Assert.Contains(
            "When `tone_of_voice` has no value, write plainly, in an even register, and say that no tone was set where the shape you were asked for has room.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "When `sender.identity` has no value, nobody is named to write as: invent no sender and complete as failed with `sender_identity_unset`, since only a person can set it and a second attempt would find it unset too.",
            skill,
            StringComparison.Ordinal);
        var neutral = Regex.Matches(CopyRule(role), @"\bneutral\b").Count;
        Assert.True(
            neutral == 1,
            $"'{role}' says 'neutral' {neutral} times in its '{CopySection}' section; it is said once, as a value of `gender_forms`.");
    }

    /// <summary>The catalog says what each role now does, so a reader choosing a role learns it before opening the file.</summary>
    [Theory]
    [InlineData("copywriter", "the tone and the sender it writes in")]
    [InlineData("personalizer", "the tone and the sender's forms it writes in")]
    public void The_catalog_says_that_a_role_that_writes_copy_writes_in_the_tone_and_as_the_sender(string role, string phrase)
    {
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        var start = catalog.IndexOf($"- [`roles/{role}`]", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The catalog has no line for '{role}'.");
        var end = catalog.IndexOf(" - [`", start + 1, StringComparison.Ordinal);
        var line = end > start ? catalog[start..end] : catalog[start..];

        Assert.Contains(phrase, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// One rule for both roles, so the two copies are held to the same bytes: a correction that reached one of them and
    /// not the other would be two roles writing the same campaign by two rules.
    /// </summary>
    [Fact]
    public void The_two_roles_that_write_copy_say_it_in_the_same_words()
    {
        var copywriter = CopyRule("copywriter");
        var personalizer = CopyRule("personalizer");

        Assert.True(
            copywriter == personalizer,
            $"The copywriter's and the personalizer's '{CopySection}' sections differ.\ncopywriter:\n{copywriter}\npersonalizer:\n{personalizer}");
    }

    /// <summary>The section, from its heading to the launch contract that follows it, line endings normalised.</summary>
    private static string CopyRule(string role)
    {
        var text = File.ReadAllText(SkillPack.Find(role).File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf(CopySection, StringComparison.Ordinal);
        var end = text.IndexOf(LaunchContract.Begin, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"'{role}' has no '{CopySection}' section before its launch contract ({start}, {end}).");
        return text[start..end];
    }
}
