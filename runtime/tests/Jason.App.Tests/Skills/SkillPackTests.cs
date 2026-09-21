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
            // Assert.Equal carries no message, and a pack of fourteen files needs one: the failure has to name
            // the file somebody would open and the key that does not belong.
            var top = front.Keys.Order().ToArray();
            Assert.True(
                top is ["description", "metadata", "name"],
                $"'{skill.File}' declares the top-level keys [{string.Join(", ", top)}]. The pack's front matter "
                + "is name, description and metadata, and nothing else: an unknown top-level key is tolerated by "
                + "one host and is a hard error outside it.");

            // And metadata holds exactly one entry in this version. A second custom key admitted silently is
            // the thing moving status under metadata was about: the map is the documented place for what this
            // pack invents, not a place where anything may accumulate unread.
            var custom = front.Metadata.Keys.Order().ToArray();
            Assert.True(
                custom is ["status"],
                $"'{skill.File}' puts [{string.Join(", ", custom)}] under metadata. This version has exactly one "
                + "entry there, status, and a second one admitted silently is a key nobody reads.");
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
        var linked = Links(catalog).ToList();

        // Both halves read markdown links, and neither reads prose. A forward half that searched the text for
        // the path would count a skill merely *named* in a sentence as catalogued — and this catalog does name
        // one in prose, where it explains that a role's skill ends up at .claude/skills/<role>/SKILL.md. What a
        // person needs from a catalog is something to click.
        foreach (var skill in SkillPack.All())
        {
            var link = Path.GetRelativePath(SkillPack.Root(), skill.File).Replace('\\', '/');
            Assert.True(
                linked.Contains(link, StringComparer.Ordinal),
                $"'{skill.Name}' is a skill in this pack and the catalog does not link it as '{link}'. "
                + $"The catalog links: {string.Join(", ", linked)}");
        }

        foreach (var link in linked)
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
    /// What a skill says about itself in its own body, held to what its front matter says. These are two
    /// claims about one thing, made in two places, and only one of them is machine-readable — so the other is
    /// the one that goes stale.
    /// </summary>
    /// <remarks>
    /// It went stale once already, and expensively: five skills were promoted to <c>verified</c> with a host
    /// reading and a digest, while every one of their bodies still opened with "Status: draft". The digest
    /// covers the body, so each recorded reading was a reading of a text calling itself a draft. Nothing caught
    /// it, because the one fact that used to hold the two together was deleted when the pack's guards were
    /// rewritten and was replaced with nothing.
    /// </remarks>
    [Fact]
    public void What_a_skill_says_about_itself_agrees_with_its_front_matter()
    {
        const string Draft = "**Status: draft.**";

        foreach (var skill in SkillPack.All())
        {
            var status = SkillFrontMatter.Read(skill.File).Metadata.GetValueOrDefault("status");
            var body = SkillPack.Body(skill.File);
            var callsItselfADraft = body.Contains(Draft, StringComparison.Ordinal);

            if (status == "draft")
            {
                Assert.True(
                    callsItselfADraft,
                    $"'{skill.File}' is a draft and its body does not say so. A reader is told what this is by "
                    + $"the text, not by the front matter: it needs the line '{Draft}'.");
                continue;
            }

            Assert.False(
                callsItselfADraft,
                $"'{skill.File}' says it is '{status}' and its body still opens by calling itself a draft. The "
                + "reading recorded for it was therefore a reading of a text that says it is unfinished.");
        }
    }

    /// <summary>
    /// The sentences elsewhere that this pack makes true or false. Each was written while the runtime guidance
    /// still lived somewhere else, and a page that says the revision is coming, after it has arrived, is the
    /// kind of wrong nobody notices because nobody re-reads it.
    /// </summary>
    [Fact]
    public void The_pages_that_pointed_at_a_revision_that_was_coming_point_at_it_now()
    {
        // 6.4 and 7.2 both said published runtime guidance "is being revised". It has been: this pack is it.
        var architecture = File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "architecture.md"));
        Assert.DoesNotContain("is being revised", architecture, StringComparison.Ordinal);
        Assert.DoesNotContain("are being revised", architecture, StringComparison.Ordinal);
        Assert.Contains("skills/runtime", architecture, StringComparison.Ordinal);

        // The walkthrough hands a reader on to the skills, and there are five of them now. A person who
        // finished the golden path and was pointed at one of five would never learn the other four existed.
        var golden = File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "golden-path.md"));
        foreach (var skill in SkillPack.All().Where(one => !one.IsRole))
        {
            Assert.Contains(skill.Name, golden, StringComparison.Ordinal);
        }

        // And the page that says what arrives in a work directory says who contributes what to it — written
        // down before the second pack exists, because that is the only time the rule can still be set.
        var profiles = File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "execution-profiles.md"));
        Assert.Contains("METHOD.md", profiles, StringComparison.Ordinal);
    }

    /// <summary>
    /// The build the pack ships with, named in the catalog. What is compared is the release prefix and not the
    /// stamp: the stamp is <c>0.1.0-dev+&lt;sha&gt;</c> on every commit, so a catalog quoting it verbatim would
    /// be red at every push or would have to carry a commit sha.
    /// </summary>
    [Fact]
    public void The_catalog_names_the_version_this_tree_carries()
    {
        var version = Jason.Contracts.Update.SemanticVersion.Current;
        var prefix = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{version.Patch}");

        Assert.Contains(
            $"ships with Jason **{prefix}**",
            File.ReadAllText(SkillPack.Catalog()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And what "verified" is worth. It is a claim that a real host read this text, which the next edit would
    /// quietly falsify — so the catalog records the digest of the text that was read, and this recomputes it.
    /// An edit without a new reading is red, and the way out is <c>draft</c> again or a new digest after a new
    /// reading, which a reviewer sees in the diff.
    /// </summary>
    [Fact]
    public void Every_verified_skill_is_the_text_a_host_actually_read()
    {
        var catalog = File.ReadAllText(SkillPack.Catalog());

        foreach (var skill in SkillPack.All())
        {
            var status = SkillFrontMatter.Read(skill.File).Metadata.GetValueOrDefault("status");
            var row = Row(catalog, skill.Name);

            if (status != "verified")
            {
                Assert.True(
                    row is null,
                    $"'{skill.Name}' is not verified and the catalog records a reading of it anyway.");
                continue;
            }

            Assert.True(row is not null, $"'{skill.Name}' says it is verified and the catalog records no reading.");
            Assert.Contains(SkillPack.BodyDigest(skill.File), row, StringComparison.Ordinal);
            Assert.Contains("Claude Code 2.1.", row, StringComparison.Ordinal);
        }
    }

    /// <summary>The catalog's row for one skill, if it has one: a table line whose first cell names it.</summary>
    private static string? Row(string catalog, string name) =>
        catalog.Split('\n').FirstOrDefault(line => line.StartsWith($"| `{name}`", StringComparison.Ordinal));

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
