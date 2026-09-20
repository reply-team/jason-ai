using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The pack as a whole, rather than three files somebody remembered to name. A skill added to
/// <c>skills/runtime/</c> is guarded on the day it is added, which is the only version of this that keeps
/// working after the person who wrote the guards has stopped thinking about them.
/// </summary>
public partial class SkillPackTests
{
    /// <summary>
    /// One front-matter shape, because three separate readers parse it: the launcher's, the role guard's and
    /// this one. A folded scalar or a key nobody expected is the construct that would make them disagree with
    /// each other while each looked correct.
    /// </summary>
    [Fact]
    public void Every_skill_declares_itself_in_the_shape_the_pack_agreed()
    {
        var skills = SkillPack.All();
        Assert.NotEmpty(skills);
        Assert.Contains(skills, skill => skill.IsRole);
        Assert.Contains(skills, skill => !skill.IsRole);

        foreach (var skill in skills)
        {
            var front = SkillFrontMatter.Read(skill.File);

            Assert.True(
                front.Problems.Count == 0,
                $"'{skill.File}' does not read as front matter: {string.Join("; ", front.Problems)}");

            // A skill is looked up by the directory it lives in and must name itself the same way; a host
            // answers a mismatch by loading nothing and saying nothing.
            Assert.Equal(skill.Name, front.Top.GetValueOrDefault("name"));

            // The description is what a host reads to decide whether to load the skill at all.
            var description = front.Top.GetValueOrDefault("description") ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(description), $"'{skill.File}' describes nothing.");
            Assert.True(
                description.Length <= 1536,
                $"'{skill.File}' has a description of {description.Length} characters, and a host truncates the "
                + "combined description and when_to_use in the skill listing at 1,536 - the listing it chooses "
                + "from, so everything past it is invisible where it matters.");

            // Everything this pack invents of its own lives under metadata, which is the documented map for
            // exactly that. An unknown top-level key is tolerated by one host and is a hard error outside it.
            Assert.Equal(["description", "metadata", "name"], front.Keys.Order());

            // And metadata holds exactly one entry in this version. A second custom key admitted silently is
            // the thing moving status under metadata was about: the map is the documented place for what this
            // pack invents, not a place where anything may accumulate unread.
            Assert.Equal(["status"], front.Metadata.Keys.Order());
            Assert.Contains(front.Metadata.GetValueOrDefault("status"), (string[])["draft", "verified"]);
        }
    }

    /// <summary>
    /// The catalog is how a person finds a skill to install, so a skill it does not list is a skill nobody
    /// installs, and an entry with no directory behind it is a broken link in the first file anybody opens.
    /// </summary>
    [Fact]
    public void Every_skill_is_in_the_catalog_and_every_catalog_entry_is_a_skill()
    {
        var catalog = File.ReadAllText(SkillPack.Catalog());

        foreach (var skill in SkillPack.All())
        {
            var link = Path.GetRelativePath(SkillPack.Root(), skill.File).Replace('\\', '/');
            Assert.True(
                catalog.Contains(link, StringComparison.Ordinal),
                $"'{skill.Name}' is a skill in this pack and the catalog does not link it as '{link}'.");
        }

        foreach (var link in Links(catalog))
        {
            Assert.True(
                File.Exists(Path.Combine(SkillPack.Root(), link)),
                $"The catalog links '{link}', and there is no skill there.");
        }
    }

    /// <summary>
    /// Every <c>SKILL.md</c> the catalog <em>links</em>. Markdown links only, and never any token that happens
    /// to end in the file's name: the catalog also explains where a role's skill ends up — at
    /// <c>.claude/skills/&lt;role&gt;/SKILL.md</c> inside the attempt's work directory — and a reader that took
    /// prose for a link would report a missing skill for a sentence that is telling the truth.
    /// </summary>
    private static IEnumerable<string> Links(string catalog)
    {
        foreach (var match in LinkTarget().Matches(catalog).Cast<Match>())
        {
            yield return match.Groups[1].Value;
        }
    }

    [GeneratedRegex(@"\]\(([^()\s]+/SKILL\.md)\)")]
    private static partial Regex LinkTarget();

    /// <summary>
    /// The detector, before the pack. Every phrase on the list is absent from the pack today, so a guard with
    /// only the scan below would be green on the day it was written and would have shown nothing about
    /// itself. Each phrase is given to it here on its own, in a sentence of the kind that would really have
    /// been written.
    /// </summary>
    [Theory]
    [InlineData("The orchestrator decides when the work runs.")]
    [InlineData("Keep a markdown workspace beside the campaign.")]
    [InlineData("Each task gets a work item file under the plan.")]
    [InlineData("Each task gets a work-item file under the plan.")]
    [InlineData("Write the plan file first and update it as you go.")]
    [InlineData("The state file is the source of truth between sessions.")]
    [InlineData("Standing preferences go in your user memory.")]
    [InlineData("Standing preferences go in user-memory.")]
    [InlineData("Keep TODO.md up to date.")]
    [InlineData("Move the item to backlog.md when it is done.")]
    public void The_detector_finds_the_model_this_pack_replaces(string sentence) =>
        Assert.NotEmpty(SupersededVocabulary.Find(sentence));

    /// <summary>
    /// And leaves alone the words this pack really uses. The list is of phrases and not of words for exactly
    /// this reason: an account may be a workspace, and a role's note is its own memory.
    /// </summary>
    [Fact]
    public void The_detector_leaves_the_words_this_pack_really_uses_alone()
    {
        Assert.Empty(SupersededVocabulary.Find("--account names an identity - a mailbox, a workspace, a login."));
        Assert.Empty(SupersededVocabulary.Find("Your note is your own working memory, and it is not authoritative."));
        Assert.Empty(SupersededVocabulary.Find("Nothing else wakes the work - the runtime does."));
    }

    /// <summary>
    /// And then the pack. The model this pack replaces kept operational state in files and had something
    /// other than the runtime decide when work ran; a skill that reintroduced any of it would be teaching the
    /// thing the architecture explicitly superseded.
    /// </summary>
    [Fact]
    public void No_skill_teaches_the_model_this_pack_replaces()
    {
        foreach (var skill in SkillPack.All())
        {
            var found = SupersededVocabulary.Find(File.ReadAllText(skill.File));

            Assert.True(
                found.Count == 0,
                $"'{skill.File}' uses the vocabulary of the superseded model: {string.Join(", ", found)}. Say "
                + "what is true here instead - the runtime owns scheduling and resumption, and nothing else "
                + "wakes the work.");
        }
    }
}
