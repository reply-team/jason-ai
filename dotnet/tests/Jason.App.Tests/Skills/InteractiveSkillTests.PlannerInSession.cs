using System.Text.RegularExpressions;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>The catalog says how the planner in a session opens and names the campaign, so a reader choosing a skill learns it before opening the file.</summary>
    [Fact]
    public void The_catalog_says_that_the_planner_in_a_session_asks_one_question_at_a_time_and_names_the_campaign_for_what_it_is_for()
    {
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        var start = catalog.IndexOf("- [`planner-in-session`]", StringComparison.Ordinal);
        Assert.True(start >= 0, "The catalog has no line for 'planner-in-session'.");
        var end = catalog.IndexOf(" - [`", start + 1, StringComparison.Ordinal);
        var line = end > start ? catalog[start..end] : catalog[start..];

        Assert.Contains("asking for what is missing one question at a time", line, StringComparison.Ordinal);
        Assert.Contains("a campaign named for what it is for", line, StringComparison.Ordinal);
    }

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
            "Use when a person who has Jason installed brings a brief or the goals of a campaign and asks to plan it, in any words - help me plan this, or a pasted paragraph - and wants the planner's judgement in the conversation: what is decided with them, what stays a quick look, what becomes work items for which role, and when it is committed. This is the skill that plans in a session with Jason, and it uses `campaign-planning` as its method.",
            description);
    }

    [Fact]
    public void The_planner_in_a_persons_session_agrees_the_direction_and_hands_the_rest_on()
    {
        var skill = Flattened("planner-in-session");

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
    /// Before the planner plans with a person, it has read what they have already written down, which a session's host
    /// may not have given it; how and in which order is the managed path's to say, once. And on a campaign that exists it
    /// reads the launched planner's notes at the three levels they are kept at, the client's first.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_reads_what_was_written_down_before_it_plans()
    {
        var skill = Flattened("planner-in-session");
        Assert.Contains("what the person has already written down", skill, StringComparison.Ordinal);
        Assert.Contains("in the order `managed-campaign-work` gives", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("planner-in-session").File);
        var levels = new[]
        {
            "jason rolenote get --role planner",
            "jason rolenote get --role planner --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            "jason rolenote get --role planner --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        };
        var at = levels.Select(line => printed.ToList().IndexOf(line)).ToList();
        Assert.True(
            at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second),
            $"planner-in-session prints the planner's notes at [{string.Join(", ", at)}] for [{string.Join(", ", levels)}]: each "
            + "level must be read, the client's first and the campaign's last.");
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
    /// in the prose as well: a command is as typeable from either. One write is the session's own: the person's answer
    /// about who writes and how the company sounds, set under their name. The opening sentence names a verb for the
    /// person to run, and is held whole by a test of its own, so it is the one place that verb may stand.
    /// </summary>
    [Fact]
    public async Task The_planner_in_a_persons_session_prints_only_reads_and_the_persons_own_preferences()
    {
        var file = SkillPack.Find("planner-in-session").File;
        var printed = SkillPack.PrintedCommands(file);
        Assert.True(printed.Count > 0, "planner-in-session prints nothing, so this guard holds nothing.");

        var prose = Flattened("planner-in-session").Replace(OpeningSentence, " ", StringComparison.Ordinal);
        var inline = InlineCommand().Matches(prose).Select(match => match.Groups[1].Value);
        foreach (var command in printed.Concat(inline))
        {
            Assert.True(
                await IsReadOrThePersonsPreferenceAsync(command),
                $"planner-in-session prints '{command}', which is neither a read of the Runtime API nor a preference set "
                + "under a person's name. What changes campaign state is managed-campaign-work's to print, and what is "
                + "about the installation is operating-the-installation's.");
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
    [InlineData("jason project link prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", false)]
    [InlineData("jason preference list", true)]
    [InlineData("jason preference set tone_of_voice --value '{\"summary\":\"Plain.\"}' --actor human:Marta", true)]
    [InlineData("jason preference set tone_of_voice --value '{\"summary\":\"Plain.\"}'", false)]
    [InlineData("jason preference set tone_of_voice --value '{\"summary\":\"Plain.\"}' --actor role:planner", false)]
    [InlineData("jason preference unset tone_of_voice --actor human:Marta", false)]
    public async Task What_counts_as_a_read_is_what_the_program_says_reads(string command, bool read) =>
        Assert.True(
            await IsReadOrThePersonsPreferenceAsync(command) == read,
            $"'{command}' is {(read ? "refused, and it is a read of the Runtime API or a preference set under a person's name" : "let through, and it is neither a read nor a preference set under a person's name")}.");

    private static async Task<bool> IsReadOrThePersonsPreferenceAsync(string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 2 && words[0] == "jason" && words[1] == "preference" && words[2] == "set")
        {
            return PersonsActor().IsMatch(command);
        }

        return words.Length > 2 && words[0] == "jason" && await ProgramVerbs.IsReadAsync(words[1], words[2]);
    }

    /// <summary>
    /// The planner's first words, before its first question: where the person's files live and how to change that.
    /// Install says the same; a session that opened without it would keep the one fact a person needs to read and edit
    /// what Jason learns about their business — the place — to itself. Held whole, and as the first section after the
    /// shared judgement, so a sentence moved down below the questions or reworded fails here.
    /// </summary>
    [Fact]
    public void The_planners_first_words_say_where_the_files_live_and_how_to_change_that()
    {
        var text = File.ReadAllText(SkillPack.Find("planner-in-session").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = text.IndexOf(CraftBlock.End, StringComparison.Ordinal);
        var first = end < 0 ? -1 : text.IndexOf("\n## ", end, StringComparison.Ordinal);
        var heading = first < 0 ? string.Empty : text[(first + 1)..text.IndexOf('\n', first + 1)];
        Assert.True(heading == "## Your first words", $"The first section after the shared judgement is '{heading}', and it must be '## Your first words'.");

        var next = text.IndexOf("\n## ", first + 1, StringComparison.Ordinal);
        var section = Whitespace().Replace(text[first..(next < 0 ? text.Length : next)], " ");
        Assert.True(
            section.Contains(OpeningSentence, StringComparison.Ordinal),
            $"The section 'Your first words' reads '{section.Trim()}', without the sentence '{OpeningSentence}'.");
        Assert.Contains("in place of the path", section, StringComparison.Ordinal);
        Assert.Contains("Ask nothing about the place", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The opening asks in the order a real start needs, and the shared judgement says that order once for both seats:
    /// who writes first, the first message on one real person last and confirmed before anything is sent. A plan made
    /// before the sender is known writes as nobody; a first message nobody saw is sent unseen to the first person on the
    /// list. Each step is said once in the paragraph, so a step moved or dropped fails here.
    /// </summary>
    [Theory]
    [InlineData("planner-in-session")]
    [InlineData("planner")]
    public void The_planners_judgement_opens_a_first_campaign_in_the_order_a_real_start_needs(string skill)
    {
        var block = CraftBlock.Extract(SkillPack.Find(skill).File) ?? string.Empty;
        const string Lead = "**A first campaign opens in the order a real start needs.**";
        var from = block.IndexOf(Lead, StringComparison.Ordinal);
        Assert.True(from >= 0, $"The planner's judgement in '{skill}' no longer says '{Lead}'.");
        var to = block.IndexOf("\n\n", from, StringComparison.Ordinal);
        var paragraph = Whitespace().Replace(block[from..(to < 0 ? block.Length : to)], " ");

        var steps = new[]
        {
            "Who the messages are written as comes first",
            "what is sold",
            "whom this campaign sells to",
            "a list the person brings or from a search, and by which principle",
            "the channels",
            "the clean-up of a brought list",
            "how the company sounds",
            "the first message, written for one real person",
        };
        var at = steps.Select(step => paragraph.IndexOf(step, StringComparison.Ordinal)).ToList();
        var once = steps.All(step => paragraph.IndexOf(step, StringComparison.Ordinal) == paragraph.LastIndexOf(step, StringComparison.Ordinal));
        Assert.True(
            once && at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second),
            $"The opening names its steps at [{string.Join(", ", at)}] for [{string.Join(" | ", steps)}]: each must be said "
            + $"once, in that order. It reads: '{paragraph.Trim()}'.");

        Assert.Contains("confirms it before anything is sent", paragraph, StringComparison.Ordinal);
        Assert.Contains("never as a form", paragraph, StringComparison.Ordinal);
        Assert.Contains("nothing that would write to anybody is settled while either is unknown", paragraph, StringComparison.Ordinal);
        Assert.Contains("is a rule for the messages after it, so it is kept at the level it belongs to", paragraph, StringComparison.Ordinal);
    }

    /// <summary>
    /// A campaign is shaped by who it is for and what it is trying to find out, never by a voice: the tone is the
    /// person's preference, and a person or a group that needs other words gets them in the copy. A planner that split a
    /// campaign to give some of its people another tone would multiply the campaigns a person has to approve, read and
    /// compare for a difference the copy carries. Said whole in the shared judgement, so both seats say the same rule.
    /// </summary>
    [Theory]
    [InlineData("planner-in-session")]
    [InlineData("planner")]
    public void The_planners_judgement_never_splits_a_campaign_only_to_give_some_of_its_people_another_tone(string skill)
    {
        var block = Whitespace().Replace(CraftBlock.Extract(SkillPack.Find(skill).File) ?? string.Empty, " ");
        const string Rule = "**A campaign is never split only to give some of its people another tone of voice.** "
            + "The tone is the person's preference; a person or a group that needs other words gets them in the copy, "
            + "not in a campaign of their own.";
        Assert.True(
            block.Contains(Rule, StringComparison.Ordinal),
            $"The planner's judgement in '{skill}' does not say '{Rule}'.");
        Assert.Equal(block.IndexOf(Rule, StringComparison.Ordinal), block.LastIndexOf(Rule, StringComparison.Ordinal));
    }

    /// <summary>The catalog says that the planner never splits a campaign only for a tone, in both seats, so a reader learns the rule before opening a file.</summary>
    [Theory]
    [InlineData("- [`planner-in-session`]")]
    [InlineData("- [`roles/planner`]")]
    public void The_catalog_says_that_the_planner_never_splits_a_campaign_only_for_a_tone(string entry)
    {
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        var start = catalog.IndexOf(entry, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The catalog has no line for '{entry}'.");
        var end = catalog.IndexOf(" - [`", start + 1, StringComparison.Ordinal);
        var line = end > start ? catalog[start..end] : catalog[start..];

        Assert.Contains("a campaign never split only to give some of its people another tone", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// Who writes and how the company sounds are the person's word: read before the first question, asked when the order
    /// reaches each, and set on the answer under the person's own name — a preference is refused from anything but a
    /// person, and one set under another name records a word nobody gave. Nothing is agreed or created while either has
    /// no value, so the section stands before the agreement, and the read before the two sets.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_has_both_preferences_before_it_plans_and_sets_them_as_the_persons()
    {
        var skill = Flattened("planner-in-session");
        Assert.Contains("Until both have a value, nothing is agreed and nothing is created.", skill, StringComparison.Ordinal);
        Assert.Contains("under their own name as the actor", skill, StringComparison.Ordinal);
        Assert.Contains("Ask for the forms; a name does not tell you them.", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("planner-in-session").File).ToList();
        var read = printed.IndexOf("jason preference list");
        var sender = printed.FindIndex(line => line.StartsWith("jason preference set sender.identity ", StringComparison.Ordinal) && PersonsActor().IsMatch(line));
        var tone = printed.FindIndex(line => line.StartsWith("jason preference set tone_of_voice ", StringComparison.Ordinal) && PersonsActor().IsMatch(line));
        Assert.True(
            read >= 0 && sender > read && tone > sender,
            $"planner-in-session prints the preferences' read at {read}, the sender's set at {sender} and the tone's at {tone}: "
            + "the read comes first, then each set under a person's name, the sender's before the tone's.");

        var text = File.ReadAllText(SkillPack.Find("planner-in-session").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var headings = new[]
        {
            "## Your first words",
            "## Who writes, and how the company sounds",
            "## Take the brief as it comes",
            "## Agree the direction before anything is created",
            "## Commit it",
            "## The first message, before anything is sent",
            "## When the campaign already exists",
        };
        var order = headings.Select(heading => text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal)).ToList();
        Assert.True(
            order.All(index => index >= 0) && order.Zip(order.Skip(1)).All(pair => pair.First < pair.Second),
            $"planner-in-session has its sections at [{string.Join(", ", order)}] for [{string.Join(" | ", headings)}]: each "
            + "must be there, in the order the opening runs.");
    }

    /// <summary>
    /// The first message is seen as it would go out and confirmed before anything is sent, and confirming its words
    /// approves no send. What the person says about it that holds beyond it is a standing rule, kept in their own memory
    /// file at the level it belongs to — never in a preference's value, and never in a role's note, which is that role's
    /// own and written from no conversation it was in.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_shows_the_first_message_and_keeps_a_standing_rule_at_its_level()
    {
        var skill = Flattened("planner-in-session");
        Assert.Contains("show it to the person as it would go out. Change it with them until they confirm it.", skill, StringComparison.Ordinal);
        Assert.Contains("Their confirmation is of the words; a send still waits for its own approval.", skill, StringComparison.Ordinal);
        Assert.Contains("into their memory file at the level it belongs to: the client's when it holds for all of their work, the project's when it holds for this product, the campaign's when it holds for this campaign alone", skill, StringComparison.Ordinal);
        Assert.Contains("nor a role's note, which is that role's own", skill, StringComparison.Ordinal);

        // A launched planner cannot write the person's file and raises the rule as a decision instead; a session that did
        // not know it is the one that writes it would leave that rule in a decision nobody carries to the file.
        Assert.Contains("A launched planner that was given such a rule raises it as a decision that names its file; once the person says yes, you write it there the same way.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The brief becomes a project with its first campaign: the project named from what is sold, never the default for a
    /// named product, and how is the managed path's to print. And the wiring check is no step of the opening: a person who
    /// skipped it plans the same way, so a session that waited on it would hold the real work behind a walk they declined.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_commits_a_first_campaign_under_the_project_of_what_is_sold()
    {
        var skill = Flattened("planner-in-session");
        Assert.Contains("The first campaign of what they sell goes under a project named from it, never under `Default`, and the campaign is made with the brief as its context before anyone is added to it", skill, StringComparison.Ordinal);
        Assert.Contains("The wiring check is no step of this opening.", skill, StringComparison.Ordinal);
        Assert.Contains("a person who skipped it plans exactly as one who took it", skill, StringComparison.Ordinal);
        Assert.Contains("Offered once and skipped, it stays skipped", skill, StringComparison.Ordinal);
        Assert.Contains("a provider is connected when the work first reaches for one, as `operating-the-installation` says", skill, StringComparison.Ordinal);

        // A second campaign of a product that has its project: that project's file is the person's guidance for it, and it
        // can be read only once the project is found, which comes after the reading order the session starts with.
        Assert.Contains("Where what they sell already has its project, read that project's memory file as soon as the project is found, before you plan.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The opening asks what is missing one question per message and waits for each answer before the next. A host once
    /// asked five questions as one list against a sentence that said "one at a time", so the method itself carries the
    /// wait, and the file names the failure it forbids, a numbered list of questions: once, where the opening is described.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_asks_the_opening_one_question_per_message()
    {
        var text = File.ReadAllText(SkillPack.Find("planner-in-session").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = Whitespace().Replace(SectionOf(text, "## Take the brief as it comes"), " ");

        // A brief comes in whatever form the person has. A session that handed over a form would make the person do the
        // reading the session is there to do.
        foreach (var held in new[]
        {
            "Ask for what is missing one question per message",
            "ask it, stop, and ask the next only once its answer is in",
            "A numbered list of questions is the form this opening never hands over, however many are missing.",
        })
        {
            Assert.True(section.Contains(held, StringComparison.Ordinal), $"'Take the brief as it comes' reads '{section.Trim()}', without '{held}'.");
        }

        var named = Regex.Matches(Flattened("planner-in-session"), "numbered list of questions", RegexOptions.IgnoreCase).Count;
        Assert.True(named == 1, $"planner-in-session names the numbered list of questions {named} times; it is said once, where the opening is described.");
    }

    /// <summary>
    /// A campaign has a name of its own, drawn from what it is for: a number or a placeholder tells the person nothing when
    /// they read their campaigns back. The name is the session's to propose and the person's to change, said once, where
    /// the campaign is created.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_names_a_campaign_for_what_it_is_for()
    {
        const string Sentence =
            "The campaign has a name of its own, drawn from what it is for, never a number or a placeholder; the name is "
            + "yours to propose before the campaign is made, and the person's to change.";
        var text = File.ReadAllText(SkillPack.Find("planner-in-session").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = Whitespace().Replace(SectionOf(text, "## Commit it"), " ");
        Assert.True(section.Contains(Sentence, StringComparison.Ordinal), $"'Commit it' reads '{section.Trim()}', without '{Sentence}'.");

        var said = Regex.Matches(Flattened("planner-in-session"), "a name of its own", RegexOptions.IgnoreCase).Count;
        Assert.True(said == 1, $"planner-in-session says 'a name of its own' {said} times; it is said once, where the campaign is created.");
    }

    /// <summary>The sentence the planner opens with, as the install says it.</summary>
    private const string OpeningSentence =
        "Jason keeps what it learns about your business in plain files you can read and edit, one folder per project and "
        + "campaign, in `C:\\Users\\…\\Jason`; to keep them somewhere else, set `Harness:Root` and run `jason project link`.";

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

    /// <summary>An actor that is a person with a name.</summary>
    [GeneratedRegex(@"(?<!\S)--actor human:[^\s']+")]
    private static partial Regex PersonsActor();

    /// <summary>A session giving an approval: on somebody's behalf, or "approve it" here, itself or for them.</summary>
    [GeneratedRegex(@"\bon (the person's|their|his|her|somebody's|someone's) behalf\b|\bapprove (it|them|this|that|the \w+) (here|now|yourself|for (them|the person|him|her))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovesForSomebody();

    [GeneratedRegex(@"<!-- craft:begin -->.*?<!-- craft:end -->", RegexOptions.Singleline)]
    private static partial Regex CraftBlockText();

    [GeneratedRegex(@"[a-z0-9']+")]
    private static partial Regex ProseWord();

    /// <summary>
    /// In a person's session the goals are asked as one question once the audience and the channels are settled, with
    /// the default said aloud where the person has not said, and they go into the campaign with the brief when it is
    /// made, not afterwards. Without the last the campaign is made with no goals and the person is never told.
    /// </summary>
    [Fact]
    public void The_planner_in_a_persons_session_asks_what_the_campaign_is_for_and_writes_it_with_the_campaign()
    {
        var skill = Flattened("planner-in-session");

        Assert.Contains("**Ask what the campaign is for.**", skill, StringComparison.Ordinal);
        Assert.Contains(
            "Ask it as one question, in your own words, once the audience and the channels are settled, and say the default aloud where they have not said: a demo.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("and its goals go in as the `goals` key of that context.", skill, StringComparison.Ordinal);
        Assert.Contains("`managed-campaign-work` prints how, including how a change of mind on an existing campaign replaces the list whole.", skill, StringComparison.Ordinal);
        Assert.Contains("is made with the brief as its context before anyone is added to it", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The quick start is what a person meets first, so the words the person sees are held exactly: the one question and
    /// its invitation, the plan message's last line, the line of fact about connected channels, the firm line when none is
    /// connected, and the wiring line in the message after "go". A paraphrase would change what the first minutes say.
    /// </summary>
    [Fact]
    public void The_quick_start_says_its_words_exactly()
    {
        var skill = Flattened("quick-start");

        Assert.Contains("What do you sell, and who should buy it? One sentence is enough, and a link to your site works too.", skill, StringComparison.Ordinal);
        Assert.Contains("If you like, add a line about yourself and how you write. You can also do that later.", skill, StringComparison.Ordinal);
        Assert.Contains("End it with exactly: \"Say what to change, or say go.\"", skill, StringComparison.Ordinal);
        Assert.Contains("\"Email goes out through {provider}.\" and \"LinkedIn goes out through {provider}.\"", skill, StringComparison.Ordinal);
        Assert.Contains("\"Which mailboxes and LinkedIn accounts are connected there I read as soon as you say go.\"", skill, StringComparison.Ordinal);
        Assert.Contains("\"{Channel} is not connected.\"", skill, StringComparison.Ordinal);
        Assert.Contains("read no accounts and count none before \"go\"", skill, StringComparison.Ordinal);
        Assert.Contains("\"{N} mailboxes and {M} LinkedIn accounts are connected; you can add more at any time.\"", skill, StringComparison.Ordinal);
        Assert.Contains("A routed channel that holds no account is said firmly: not one message leaves on it until an account is connected", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing is started before the person's go.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("The count never holds the person up", skill, StringComparison.Ordinal);
        Assert.Contains("no channel is connected, connecting one now is recommended and is one step, and until it is done not one message leaves", skill, StringComparison.Ordinal);
        Assert.Contains("`reply auth login`", skill, StringComparison.Ordinal);
        Assert.Contains("\"It can be done later.\"", skill, StringComparison.Ordinal);
        Assert.Contains("Never put the offer in the plan message.", skill, StringComparison.Ordinal);
        Assert.Contains("The message after \"go\" that hands over to the planning ends with one line: \"There is a guided check of the wiring; say so if you want it.\"", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("--idempotency-key first-campaign ", skill, StringComparison.Ordinal);

        // The sender.list read comes after "go" and never before it: a draft campaign cannot run it.
        var go = skill.IndexOf("## 5. After \"go\"", StringComparison.Ordinal);
        var read = skill.IndexOf("--operation sender.list", StringComparison.Ordinal);
        Assert.True(go >= 0 && read > go, $"The sender.list read is at offset {read}; it belongs after the heading of \"go\" (offset {go}).");
    }
}
