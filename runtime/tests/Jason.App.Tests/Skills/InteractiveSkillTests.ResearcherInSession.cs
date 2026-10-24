using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// A host chooses a skill by its description, so the description is held whole: the moment the skill is for, what the
    /// seat decides, and the skill it uses as its method. One that kept its opening and lost the rest could promise
    /// anything after the first clause.
    /// </summary>
    [Fact]
    public void The_researcher_in_a_persons_session_is_described_by_the_moment_it_is_for()
    {
        var description = SkillFrontMatter.Read(SkillPack.Find("researcher-in-session").File).Top.GetValueOrDefault("description") ?? string.Empty;
        Assert.Equal(
            "Use when a person who has Jason installed asks to find out or check something about an account or a person, in any words - who decides at this company, is this person still in that role, or a list to look into - and wants the researcher's judgement in the conversation: what was established and what was not, what stays a quick look, and what becomes research work items. This is the skill that researches in a session with Jason, and it uses `sdr-operations` as its method.",
            description);
    }

    /// <summary>
    /// The researcher's judgement, which the two files share byte for byte. What counts as established, whose doing a
    /// source outside Jason is, what the answer carries, that nothing reaching a person is the researcher's, and what is
    /// never written down: a block that lost one of them would teach both seats without it at once.
    /// </summary>
    [Theory]
    [InlineData("researcher-in-session")]
    [InlineData("researcher")]
    public void The_researchers_judgement_says_what_is_established_and_what_is_never_written(string skill)
    {
        var block = Whitespace().Replace(CraftBlock.Extract(SkillPack.Find(skill).File) ?? string.Empty, " ");

        // An earlier run, its note or a list repeated as fact is how a stale title reaches a first message.
        Assert.Contains("**Established means seen now.** A fact is established when you have seen it yourself, now, where it can be seen.", block, StringComparison.Ordinal);
        Assert.Contains("are reports: they tell you where to look first and what was already ruled out", block, StringComparison.Ordinal);

        // A host's reach mistaken for the runtime's would make Jason the warrant for a page it never read.
        Assert.Contains("what it gives you is that host's to give and never Jason's promise", block, StringComparison.Ordinal);

        // An answer that keeps its gaps to itself is acted on as if it had none.
        Assert.Contains("**The answer says what was established and what was not, in plain words.**", block, StringComparison.Ordinal);

        // A researcher that sent what it found would reach a person nobody approved.
        Assert.Contains("**Nothing that reaches a person is yours.**", block, StringComparison.Ordinal);

        // What a person would object to reading about themselves does not become a record because it was found.
        Assert.Contains("**What a person would object to seeing written down is not written.**", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The profession's method is <c>sdr-operations</c>', named rather than copied: what each finding-and-verifying
    /// operation reaches, whether it can be undone, what it needs and costs. A session that never heard of it would
    /// improvise the method; one that copied it would carry a second copy that drifts.
    /// </summary>
    [Fact]
    public void The_researcher_in_a_persons_session_names_its_method_and_copies_no_rule()
    {
        var skill = Flattened("researcher-in-session");
        Assert.Contains("is `sdr-operations`'s to say, in its discovery and contacts families", skill, StringComparison.Ordinal);

        // The commit and the note have their owner; a session that never heard of it would improvise both.
        Assert.Contains("How those items are created, and the keys they carry, is `managed-campaign-work`'s.", skill, StringComparison.Ordinal);

        var own = Runs(Prose(SkillPack.Find("researcher-in-session").File));
        Assert.True(own.Count > 50, $"researcher-in-session's own prose gave {own.Count} runs of eight words, so this guard holds little.");

        foreach (var sibling in new[]
        {
            Path.Combine(SkillPack.BusinessRoot(), "sdr-operations", SkillPack.SkillFile),
            SkillPack.Find("managed-campaign-work").File,
            SkillPack.Find("approvals-and-questions").File,
        })
        {
            var shared = own.Intersect(Runs(Prose(sibling)), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            Assert.True(
                shared.Count == 0,
                $"researcher-in-session shares {shared.Count} runs of eight words with '{sibling}', the first '{shared.FirstOrDefault()}'. "
                + "Point to that skill for its rule rather than copying it.");
        }
    }

    /// <summary>
    /// The method paragraph says that this build performs none of <c>sdr-operations</c>' discovery family, so a step of
    /// that kind is the host's own doing. That is a fact about the operations this build publishes, and it goes false the
    /// day one of the family is published: the family is read from <c>sdr-operations</c>' own register and the build's
    /// operations from <c>docs/contracts/operations</c>, so that day turns this red rather than leaving the sentence wrong.
    /// </summary>
    [Fact]
    public void The_researcher_in_a_persons_session_says_this_build_performs_none_of_the_discovery_family()
    {
        const string sentence = "today it performs none of the discovery family, so a step of that kind is your host's own doing";
        Assert.Contains(sentence, Flattened("researcher-in-session"), StringComparison.Ordinal);

        var register = Path.Combine(SkillPack.BusinessRoot(), "sdr-operations", "operations");
        var fragment = Assert.Single(Directory.EnumerateFiles(register, "*-discovery.yaml"));
        var family = OperationName().Matches(File.ReadAllText(fragment)).Select(match => match.Groups[1].Value).ToList();
        Assert.True(family.Count > 0, $"'{fragment}' names no operation, so this guard holds nothing.");

        var contracts = Path.Combine(SkillPack.RepositoryRoot(), "docs", "contracts", "operations");
        var published = family.Where(name => File.Exists(Path.Combine(contracts, name + ".json"))).ToList();
        Assert.True(
            published.Count == 0,
            $"docs/contracts/operations now publishes [{string.Join(", ", published)}] of sdr-operations' discovery family, "
            + $"and researcher-in-session's method paragraph says \"{sentence}\". Rewrite that sentence to say which of the "
            + "family this build performs.");
    }

    [GeneratedRegex(@"^  - name: ([a-z_]+\.[a-z_]+)\r?$", RegexOptions.Multiline)]
    private static partial Regex OperationName();

    /// <summary>
    /// A quick look is the conversation's; a list is work Jason runs. Two hundred lookups in a conversation are lost when
    /// it ends and nobody resumes them, and a list researched in the conversation never becomes anything Jason can show.
    /// </summary>
    [Fact]
    public void The_researcher_in_a_persons_session_keeps_a_quick_look_and_sends_a_list_to_work_items()
    {
        var skill = Flattened("researcher-in-session");
        Assert.Contains("One account or one person, checked while the person waits, is yours to do here, and nothing is created for it.", skill, StringComparison.Ordinal);
        Assert.Contains("it becomes research work items, one for each account or person, which Jason runs and resumes after this conversation ends", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The launched researcher's note is that role's own memory: read here as a lead, never written from a conversation it
    /// was not in, at the three levels it is kept at, its general craft first. Why a note is not the truth is
    /// managed-campaign-work's to say, once.
    /// </summary>
    [Fact]
    public void The_researcher_in_a_persons_session_reads_the_note_and_never_writes_it()
    {
        var skill = Flattened("researcher-in-session");
        Assert.Contains("The note is a lead, not the truth, as `managed-campaign-work` says. You read it and you do not write it.", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("researcher-in-session").File).ToList();
        var levels = new[]
        {
            "jason rolenote get --role researcher",
            "jason rolenote get --role researcher --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            "jason rolenote get --role researcher --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        };
        var at = levels.Select(line => printed.IndexOf(line)).ToList();
        Assert.True(
            at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second),
            $"researcher-in-session prints the researcher's notes at [{string.Join(", ", at)}] for [{string.Join(", ", levels)}]: "
            + "each level must be read, the general craft first and the campaign's last.");
    }

    /// <summary>
    /// Everything the session file prints is a read. What changes campaign state is managed-campaign-work's to print, and
    /// one that printed <c>rolenote set</c> would write the launched researcher's memory from a conversation it was never
    /// in. Printed means inside any fence, and written whole in a code span in the prose as well.
    /// </summary>
    [Fact]
    public async Task The_researcher_in_a_persons_session_prints_only_reads()
    {
        var printed = SkillPack.PrintedCommands(SkillPack.Find("researcher-in-session").File);
        Assert.True(printed.Count > 0, "researcher-in-session prints nothing, so this guard holds nothing.");

        var inline = InlineCommand().Matches(Flattened("researcher-in-session")).Select(match => match.Groups[1].Value);
        foreach (var command in printed.Concat(inline))
        {
            var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(
                words.Length > 2 && words[0] == "jason" && await ProgramVerbs.IsReadAsync(words[1], words[2]),
                $"researcher-in-session prints '{command}', which is not a read of the Runtime API. What changes campaign "
                + "state is managed-campaign-work's to print.");
        }

        // And it approves nothing anywhere: an approval is a person's.
        var approves = ApprovesForSomebody().Match(Flattened("researcher-in-session"));
        Assert.False(approves.Success, $"researcher-in-session says '{approves.Value}'. An approval is a person's, and a session gives none.");
    }
}
