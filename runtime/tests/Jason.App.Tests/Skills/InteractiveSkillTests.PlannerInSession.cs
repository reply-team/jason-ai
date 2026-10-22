using System.Text.RegularExpressions;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// A host chooses a skill by its description, so the description is the agreed sentence, whole: the moment the skill
    /// is for and the four things the seat decides. One that kept its opening and lost the rest could promise anything
    /// after the first clause.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_is_described_by_the_moment_it_is_for()
    {
        var description = SkillFrontMatter.Read(SkillPack.Find("planner-in-session").File).Top.GetValueOrDefault("description") ?? string.Empty;
        Assert.Equal(
            "Use when a person who has Jason installed brings a brief or a campaign objective and asks to plan it, in any words - help me plan this, or a pasted paragraph - and wants the planner's judgement in the conversation: what is decided with them, what stays a quick look, what becomes work items for which role, and when it is committed. This is the skill that plans in a session with Jason, and it uses `campaign-planning` as its method.",
            description);
    }

    [Fact]
    public void The_planner_in_a_persons_session_agrees_the_direction_and_hands_the_rest_on()
    {
        var skill = Flattened("planner-in-session");

        // A brief comes in whatever form the person has. A session that handed over a form would make the person do
        // the reading the session is there to do.
        Assert.Contains("rather than handing over a form", skill, StringComparison.Ordinal);

        // Two hundred lookups in a conversation are lost when it ends, and nobody resumes them. Heavy, repeated work is
        // work Jason runs.
        Assert.Contains("What is heavy or repeated does not happen in the conversation", skill, StringComparison.Ordinal);

        // A change of direction on a campaign that exists is written where the first one was, and the launched
        // planner's note is that role's own memory: read here as a lead, never written from a conversation it was not
        // in. Why a note is not the truth is managed-campaign-work's to say, once.
        Assert.Contains("A change of direction on a campaign that already exists goes into its context as well", skill, StringComparison.Ordinal);
        Assert.Contains("The note is a lead, not the truth, as `managed-campaign-work` says. You read it and you do not write it.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("where it disagrees with what the runtime says now", skill, StringComparison.OrdinalIgnoreCase);

        // The method and the commit have owners of their own. A session that never heard of them would improvise both.
        Assert.Contains("`campaign-planning`", skill, StringComparison.Ordinal);
        Assert.Contains("`managed-campaign-work`", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The seat's one section about agreement and approval, held whole. A campaign created from a first paragraph
    /// commits a guess, so the direction is agreed first; and agreeing a direction is not approving a send, which a
    /// session that took the one for the other would do for people nobody approved. The section says so in the seat's
    /// own sentences and points for the rest — what a plan's approval covers is campaign-planning's, and a send's own
    /// approval is a person's, presented by approvals-and-questions — so a second copy of either rule, or a sentence
    /// that weakens one, is a change to this text and fails here.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_agrees_before_anything_is_created_and_approves_nothing()
    {
        var text = File.ReadAllText(SkillPack.Find("planner-in-session").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var from = text.IndexOf("## Agree the direction before anything is created\n", StringComparison.Ordinal);
        var to = from < 0 ? -1 : text.IndexOf("\n## ", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"planner-in-session has no section 'Agree the direction before anything is created' (found at {from}, next heading at {to}).");

        Assert.Equal(
            "## Agree the direction before anything is created Say what you propose and why: who the campaign is for, what "
            + "the pilot is, what comes after it, and what would make you change course. Let the person change it. Nothing "
            + "is created until they have agreed. Their agreement is to the direction, not to sending: what a plan's approval "
            + "does and does not cover is `campaign-planning`'s, and a send waits for an approval of its own, which only a "
            + "person gives (`approvals-and-questions`).",
            Whitespace().Replace(text[from..to], " ").Trim());

        // And nowhere else in the file does the session approve on somebody's behalf. The decision is a person's.
        var approves = ApprovesForSomebody().Match(Flattened("planner-in-session"));
        Assert.False(approves.Success, $"planner-in-session says '{approves.Value}'. An approval is a person's, and a session gives none.");
    }

    /// <summary>
    /// What the refusal above reads as a session approving, and what it leaves alone. It refuses the session giving an
    /// approval — on somebody's behalf, here, itself, for them — and never the word: a session file must be able to say
    /// that only the person can approve it.
    /// </summary>
    [Theory]
    // Critical: a session never approves.
    [InlineData("When the pilot's enrolment waits for approval, approve it here on the person's behalf: they agreed the direction.", true, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    [InlineData("If the person is away, approve it yourself so that the pilot does not wait.", true)]
    [InlineData("Approve them for the person once the direction is agreed.", true)]
    [InlineData("When the enrolment waits for approval, only the person can approve it; you tell them it is waiting.", false)]
    [InlineData("A send waits for an approval of its own, which only a person gives.", false)]
    public void The_refusal_of_a_session_approving_reads_the_act_and_not_the_word(string sentence, bool refused) =>
        Assert.True(
            ApprovesForSomebody().IsMatch(sentence) == refused,
            $"'{sentence}' is {(refused ? "not refused, and it has a session approving" : "refused, and it only says who approves")}.");

    /// <summary>
    /// The commit is <c>managed-campaign-work</c>'s: the commands that create a campaign, add its people and create its
    /// work, and the keys they carry. A session file that printed one would be a second copy of those rules, which is how
    /// one of the two ends up wrong — and one that printed <c>rolenote set</c> would write the launched planner's memory
    /// from a conversation that planner was never in. Printed means inside any fence, and written whole in a code span
    /// in the prose as well: a command is as typeable from either.
    /// </summary>
    [Fact]
    public async Task The_planner_in_a_persons_session_prints_only_reads()
    {
        var file = SkillPack.Find("planner-in-session").File;
        var printed = SkillPack.PrintedCommands(file);
        Assert.True(printed.Count > 0, "planner-in-session prints nothing, so this guard holds nothing.");

        var inline = InlineCommand().Matches(Flattened("planner-in-session")).Select(match => match.Groups[1].Value);
        foreach (var command in printed.Concat(inline))
        {
            Assert.True(
                await IsReadAsync(command),
                $"planner-in-session prints '{command}', which is not a read of the Runtime API. What changes campaign "
                + "state is managed-campaign-work's to print, and what is about the installation is "
                + "operating-the-installation's.");
        }
    }

    /// <summary>
    /// What the guard above counts as a read. Every read of the Runtime API passes — <c>list-contacts</c> and
    /// <c>route resolve</c> as much as <c>get</c> — and nothing that changes campaign state or an attempt does.
    /// </summary>
    [Theory]
    [InlineData("jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", true)]
    [InlineData("jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll", true)]
    [InlineData("jason campaign get cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", true)]
    [InlineData("jason rolenote get cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD planner", true)]
    [InlineData("jason rolenote set cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD planner --note '{}'", false)]
    [InlineData("jason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --set '{}'", false)]
    [InlineData("jason workitem heartbeat wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --attempt att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", false)]
    [InlineData("jason runtime status", false)]
    public async Task What_counts_as_a_read_is_what_the_program_says_reads(string command, bool read) =>
        Assert.True(
            await IsReadAsync(command) == read,
            $"'{command}' is {(read ? "refused, and it is a read of the Runtime API" : "taken for a read, and it is not one")}.");

    private static async Task<bool> IsReadAsync(string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 2 && words[0] == "jason" && await ProgramVerbs.IsReadAsync(words[1], words[2]);
    }

    /// <summary>
    /// Each rule is written once. The session file owns the seat; the method is campaign-planning's, the commit
    /// managed-campaign-work's, and what waits on a person approvals-and-questions'. A run of eight words the session file
    /// shares with one of them is a rule copied rather than pointed to, and copies drift. Read over the prose that is the
    /// session file's own: not the shared block, which the launched planner must carry because it cannot read
    /// campaign-planning, not the commands, and not the status line every skill words the same way.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_copies_no_rule_from_the_skills_it_points_to()
    {
        var own = Runs(Prose(SkillPack.Find("planner-in-session").File));
        Assert.True(own.Count > 50, $"planner-in-session's own prose gave {own.Count} runs of eight words, so this guard holds little.");

        foreach (var sibling in new[]
        {
            Path.Combine(SkillPack.BusinessRoot(), "campaign-planning", SkillPack.SkillFile),
            SkillPack.Find("managed-campaign-work").File,
            SkillPack.Find("approvals-and-questions").File,
        })
        {
            var shared = own.Intersect(Runs(Prose(sibling)), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            Assert.True(
                shared.Count == 0,
                $"planner-in-session shares {shared.Count} runs of eight words with '{sibling}', the first '{shared.FirstOrDefault()}'. "
                + "Point to that skill for its rule rather than copying it.");
        }
    }

    /// <summary>
    /// The prose of a skill: its body without the front matter, the status paragraph, any fenced block, the headings and
    /// the craft block.
    /// </summary>
    private static string Prose(string file)
    {
        var kept = new List<string>();
        var status = false;
        foreach (var (line, fenced) in SkillPack.Fenced(SkillPack.Body(file).Split('\n')))
        {
            var start = line.TrimStart();
            if (fenced || start.StartsWith('#'))
            {
                continue;
            }

            if (start.StartsWith("**Status:", StringComparison.Ordinal))
            {
                status = true;
            }

            if (status)
            {
                status = start.Length > 0;
                continue;
            }

            kept.Add(line);
        }

        return CraftBlockText().Replace(string.Join('\n', kept), " ");
    }

    /// <summary>Every run of eight consecutive words, lower-cased, punctuation and emphasis dropped.</summary>
    private static HashSet<string> Runs(string prose)
    {
        var words = ProseWord().Matches(prose.ToLowerInvariant()).Select(match => match.Value).ToList();
        var runs = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index + 8 <= words.Count; index++)
        {
            runs.Add(string.Join(' ', words.Skip(index).Take(8)));
        }

        return runs;
    }

    [GeneratedRegex(@"`(jason [^`]+)`")]
    private static partial Regex InlineCommand();

    /// <summary>A session giving an approval: on somebody's behalf, or "approve it" here, itself or for them.</summary>
    [GeneratedRegex(@"\bon (the person's|their|his|her|somebody's|someone's) behalf\b|\bapprove (it|them|this|that|the \w+) (here|now|yourself|for (them|the person|him|her))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovesForSomebody();

    [GeneratedRegex(@"<!-- craft:begin -->.*?<!-- craft:end -->", RegexOptions.Singleline)]
    private static partial Regex CraftBlockText();

    [GeneratedRegex(@"[a-z0-9']+")]
    private static partial Regex ProseWord();
}
