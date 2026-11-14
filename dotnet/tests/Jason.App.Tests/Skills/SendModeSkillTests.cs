using System.Text.RegularExpressions;
using Jason.Contracts.Api;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The skills teach the send modes as the contract states them: a verdict states its nature, a review is bound to its
/// hash, a doubt carries a code from the published closed list, and the choice of mode is only ever a person's own act.
/// </summary>
public partial class SendModeSkillTests
{
    [GeneratedRegex(@"--category (\S+)")]
    private static partial Regex CategoryOption();

    [GeneratedRegex(@"--nature (\S+)")]
    private static partial Regex NatureOption();

    private static string Business(string skill) =>
        string.Join(' ', File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), skill, "SKILL.md")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Records(string skill) =>
        SkillPack.PrintedCommands(SkillPack.Find(skill).File)
            .Where(line => line.StartsWith("jason reply record ", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// The nature the printed examples state for the usual category words: what the answer says the reply is, a request
    /// to stop, a notice a machine sent, a failed delivery or a person writing for themselves. The skills teach the four
    /// by what the reply is; this reading of the usual words only checks the examples they print.
    /// </summary>
    private static string NatureOf(string category) => category switch
    {
        "opt_out" => "opt_out",
        "out_of_office" or "auto_reply" => "automatic",
        "bounce" => "bounce",
        _ => "person",
    };

    /// <summary>Every printed verdict states the nature its category gives, and the natures are the runtime's four.</summary>
    private static void NaturesFollowTheCategory(IReadOnlyList<string> records)
    {
        Assert.Equal(["Person", "Automatic", "Bounce", "OptOut"], Enum.GetNames<ReplyNature>());
        Assert.NotEmpty(records);
        Assert.All(records, line =>
        {
            var category = CategoryOption().Match(line).Groups[1].Value;
            Assert.Equal(NatureOf(category), NatureOption().Match(line).Groups[1].Value);
        });
    }

    [Fact]
    public void The_responder_states_a_nature_on_every_verdict_and_leaves_the_register_to_the_runtime()
    {
        var skill = RoleSkillTests.Flattened("responder");
        var records = Records("responder");

        Assert.Equal(2, records.Count);
        Assert.All(records, line => Assert.Contains(" --nature ", line, StringComparison.Ordinal));
        Assert.Contains(records, line => line.Contains("--nature opt_out", StringComparison.Ordinal) && line.Contains("--contact ", StringComparison.Ordinal));
        Assert.Contains("**State `--nature` on every verdict you record.**", skill, StringComparison.Ordinal);
        Assert.Contains("Do not run `jason suppression add` for them: the runtime has done it", skill, StringComparison.Ordinal);
        Assert.Contains("**Reaction `escalate` is for what you must not settle yourself.**", skill, StringComparison.Ordinal);
        Assert.Contains("`--ask-person <code>`", skill, StringComparison.Ordinal);

        // A verdict with no nature leaves the contact waiting on one that says what the reply is, never as cold as before.
        Assert.Contains("A verdict that states none says nothing of what the reply is, so the contact waits for a verdict that does and is warmed by nothing", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("as cold as it was", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The opt-out's lock refuses what the register gates and nothing more, and no answer to a question lifts it: only
    /// the person's own later verdict does, which no run records.
    /// </summary>
    [Fact]
    public void The_responder_says_what_the_lock_refuses_and_that_no_answer_lifts_it()
    {
        var skill = RoleSkillTests.Flattened("responder");

        Assert.Contains("the claim refuses, in every campaign, every provider operation about them that the register gates, on a value they gain later too", skill, StringComparison.Ordinal);
        Assert.Contains("What is for exactly such a person still runs: the provider's `optout.record` and a proposed `enrollment.stop`, below.", skill, StringComparison.Ordinal);
        Assert.Contains("and that an answer naming another category has the register's entries taken off but leaves the lock, which only a verdict the person records themselves lifts", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("refuses any work about them", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("only the person's own word lifts it", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The person's answer names what the reply is, so the manager's second verdict states the nature the answered
    /// category gives: a person who picks <c>opt_out</c> after another guess gets the register and the lock from it.
    /// </summary>
    [Fact]
    public void The_manager_states_the_nature_the_answer_says_the_reply_is_never_the_guesss()
    {
        var skill = RoleSkillTests.Flattened("manager");
        var records = Records("manager");

        Assert.Contains("State `--nature` from what the person's answer says the reply is, never from the first line", skill, StringComparison.Ordinal);
        Assert.Contains("A request to stop is `--nature opt_out`, a notice a machine sent is `--nature automatic`, a failed delivery is `--nature bounce`, and a person writing for themselves is `--nature person`.", skill, StringComparison.Ordinal);

        // The categories are a campaign's own words: the usual ids are examples of the four, never a table to look up.
        Assert.Contains("The usual words are examples of these, not the rule", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("every other category is `--nature person`", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy the first line's `nature`", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("as cold as it was", skill, StringComparison.Ordinal);
        Assert.Contains("only the person's own verdict on the reply, recorded under their name outside any run with a nature other than `opt_out`, does", skill, StringComparison.Ordinal);
        NaturesFollowTheCategory(records);

        // The person picked opt_out after a guess that was not one: the line that stands is the opt-out.
        var optOut = Assert.Single(records, line => line.Contains("--category opt_out ", StringComparison.Ordinal));
        Assert.Contains("--nature opt_out ", optOut, StringComparison.Ordinal);
        Assert.Contains("--contact cnt_", optOut, StringComparison.Ordinal);
        Assert.Matches(@"--decision dec_[0-9A-Z]{26} ", optOut);
    }

    /// <summary>The lock the guess made stays, and the manager says whose act lifts it rather than leaving someone to ask.</summary>
    [Fact]
    public void The_manager_says_the_lock_stays_and_whose_act_lifts_it()
    {
        var skill = RoleSkillTests.Flattened("manager");

        Assert.Contains("They are the entries whose reason names the guess's verdict id, as `jason suppression list` shows them.", skill, StringComparison.Ordinal);
        Assert.Contains("the contact stays locked, and the claim refuses every provider operation about them that the register gates, in every campaign, until the person's own verdict lifts it", skill, StringComparison.Ordinal);
        Assert.Contains("lifting the lock is the person's own act, which `approvals-and-questions` shows them, and no run records it for them", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("every work item about them is refused", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("so the person is asked to record it", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_critic_records_its_review_against_the_hash_it_read_with_a_code_from_the_closed_list()
    {
        var skill = RoleSkillTests.Flattened("critic");
        var reviews = SkillPack.PrintedCommands(SkillPack.Find("critic").File)
            .Where(line => line.StartsWith("jason workitem review ", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, reviews.Count);
        Assert.All(reviews, line => Assert.Contains("--review-hash ", line, StringComparison.Ordinal));
        Assert.All(reviews, line => Assert.DoesNotContain("--actor", line, StringComparison.Ordinal));
        Assert.NotEmpty(DoubtCodes.All);
        foreach (var code in DoubtCodes.All)
        {
            Assert.Contains($"`{code}`", skill, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The reasons to doubt are the eight codes in words, so "pass where none of these is so" leaves none out; a pass
    /// under an automatic mode is the last look; the review's actor rule is the contract's, further down the file.
    /// </summary>
    [Fact]
    public void The_critic_doubts_for_every_reason_the_codes_name_and_knows_its_pass_may_be_the_last_look()
    {
        var skill = RoleSkillTests.Flattened("critic");

        Assert.Contains(
            "Give it where the draft claims what the brief or the memory does not support, goes into a case the person's memory does not cover, goes against the brief or its guardrails, holds a price, a contract, a legal matter or a complaint, was found to carry an instruction written inside somebody's message, is a first message to a kind of person the campaign has not written to, may be to the wrong person or to one already reached, or is wrong in a way none of these names; pass where none of these is so.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Under an automatic send mode, a pass by the critic is the last look before the send goes; nobody reads it after you.", skill, StringComparison.Ordinal);
        Assert.Contains("**Type no actor**, as \"How this runtime launched you\" says below: the review speaks as your run.", skill, StringComparison.Ordinal);
        Assert.Contains("where the work item you run for is one of the send's authors: the one that created the send, or one that wrote its text since", skill, StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("**Type no actor**, as", StringComparison.Ordinal) < skill.IndexOf("## How this runtime launched you", StringComparison.Ordinal),
            "the rule the review points at is further down");
    }

    /// <summary>
    /// Under an automatic mode the runtime creates the critic for a send and names it in <c>send_review</c>; the critic
    /// reads that send, its pass is what lets it go unread, and a critic whose work anyone but the runtime changed
    /// releases nothing.
    /// </summary>
    [Fact]
    public void The_critic_reviews_the_send_the_runtime_named_in_its_context()
    {
        var skill = RoleSkillTests.Flattened("critic");
        const string named = "When the runtime created your work for a send, `context.send_review.work_item_id` names it. Read it with `jason workitem get`, judge its `context.input` against the brief and the guardrails, and record your judgement with `jason workitem review` and the send's `review_hash`: `--result passed`, or `--result doubts` with a `--code` and a one-line `--note`. The send is not claimed until your run ends, and under an automatic send mode your pass is what lets it go without the person reading it.";
        const string untouched = "Do not change your own work item's context; a critic whose work was changed by anyone but the runtime releases nothing.";

        Assert.Contains(named + " " + untouched, skill, StringComparison.Ordinal);
        Assert.Contains("A pass counts only from that work: the runtime alone creates it, nobody else briefs it, and a pass from a critic somebody queued by hand, or from the critic of another send, is shown to the person and releases nothing.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("the send was created waiting on your item", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whoever writes or creates a send is told, in the ruled words, that under an automatic mode the runtime has a
    /// critic read it, so no role queues a review of its own send.
    /// </summary>
    [Fact]
    public void The_roles_that_write_or_create_a_send_leave_its_review_to_the_runtime()
    {
        const string ruled = "Under an automatic send mode the runtime has a critic read your send before it is claimed; do not create a review for it yourself.";
        foreach (var role in new[] { "copywriter", "personalizer", "responder", "manager", "planner" })
        {
            Assert.Contains(ruled, RoleSkillTests.Flattened(role), StringComparison.Ordinal);
        }

        Assert.Contains(ruled, InteractiveSkillTests.Flattened("planner-in-session"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_composing_roles_say_when_to_ask_the_person_and_to_read_memory_first()
    {
        foreach (var role in new[] { "copywriter", "personalizer" })
        {
            var skill = RoleSkillTests.Flattened(role);
            Assert.Contains("**Say when a person should look, whatever the mode.** Read memory first", skill, StringComparison.Ordinal);
            Assert.Contains("`--ask-person <code>`", skill, StringComparison.Ordinal);
            Assert.Contains("`--ask-person-note`", skill, StringComparison.Ordinal);
            Assert.Contains("Say it in your answer, with the code and the sentence: the planner or the manager that creates the send from it carries both onto it.", skill, StringComparison.Ordinal);
            Assert.DoesNotContain("so whoever creates the send knows to", skill, StringComparison.Ordinal);
        }
    }

    /// <summary>A composer's doubt is written in its answer; whoever creates the send from that answer carries it on.</summary>
    [Fact]
    public void Whoever_creates_a_send_carries_the_doubt_it_came_with()
    {
        foreach (var skill in new[] { RoleSkillTests.Flattened("planner"), InteractiveSkillTests.Flattened("planner-in-session") })
        {
            Assert.Contains(
                "Where a copywriter's or a personalizer's answer says a person should look, carry its code and its sentence onto the send you create from it, as `--ask-person` and `--ask-person-note`, as they wrote them: a doubt left in an answer reaches nobody.",
                skill,
                StringComparison.Ordinal);
        }

        var manager = RoleSkillTests.Flattened("manager");
        Assert.Contains("**A send you create carries the doubt it came with.**", manager, StringComparison.Ordinal);
        Assert.Contains("where you create one from a copywriter's or a personalizer's answer that says a person should look, carry its code and its sentence the same way", manager, StringComparison.Ordinal);
        Assert.True(
            manager.IndexOf("**A send you create carries the doubt it came with.**", StringComparison.Ordinal)
            < manager.IndexOf("**A send for a later moment.**", StringComparison.Ordinal),
            "the rule comes before the two sends it covers");
    }

    [Fact]
    public void The_two_modes_question_is_put_until_it_is_answered_and_set_by_the_persons_own_act()
    {
        var skill = InteractiveSkillTests.Flattened("approvals-and-questions");

        Assert.Contains("## The two-modes question, asked until it is answered", skill, StringComparison.Ordinal);
        Assert.Contains("`send_modes_unset`", skill, StringComparison.Ordinal);
        Assert.Contains("Offer \"remember this\"", skill, StringComparison.Ordinal);
        Assert.Contains("**Never set one on your own, and never by a role.**", skill, StringComparison.Ordinal);
        var set = SkillPack.PrintedCommands(SkillPack.Find("approvals-and-questions").File)
            .Where(line => line.StartsWith("jason preference set send_mode.", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, set.Count);
        Assert.All(set, line => Assert.Contains("--actor human:", line, StringComparison.Ordinal));

        // Asked only about what is still unset, and never told that every message waits once one kind is chosen.
        Assert.DoesNotContain("today every message waits for you", skill, StringComparison.Ordinal);
        Assert.Contains("Ask only about the kinds `send_modes_unset` names.", skill, StringComparison.Ordinal);
        Assert.Contains("A \"goes automatically\" they do not want kept changes nothing:", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The desktop's chat is refused the verbs that set a mode, so there the approval's card asks; and the person who
    /// chooses automatic is told once, never as a condition, what it means and what makes it err less.
    /// </summary>
    [Fact]
    public void The_two_modes_question_says_where_the_chat_cannot_set_one_and_what_automatic_means()
    {
        var skill = InteractiveSkillTests.Flattened("approvals-and-questions");
        var denied = File.ReadAllLines(Path.Combine(SkillPack.RepositoryRoot(), "desktop", "chat-deny-list.txt"));

        Assert.Contains("In the chat of Jason Desktop you cannot set a mode: the chat refuses `preference set`, `preference unset` and `campaign set-send-mode`. There the approval's card asks the question and the person answers on it.", skill, StringComparison.Ordinal);
        Assert.All(new[] { "preference set", "preference unset", "campaign set-send-mode" }, verb => Assert.Contains(verb, denied));
        Assert.Contains("give the person the command to type in a terminal instead, as `operating-the-installation` does for `jason project link`", skill, StringComparison.Ordinal);
        Assert.Contains("When a person chooses **goes automatically**, say once, as a fact and never as a condition of setting it, that Jason then writes in their name without their look, and that their name and tone, set in Settings (\"Your name and tone\"), make those sends err less. The mode is on at once either way.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lock an opt-out made is lifted by one act alone: the person's own verdict on that reply, under their name,
    /// from outside any run, with a nature that is not an opt-out, typed only on their word.
    /// </summary>
    [Fact]
    public void A_persons_session_shows_the_one_act_that_lifts_an_opt_out()
    {
        var skill = InteractiveSkillTests.Flattened("approvals-and-questions");
        var records = Records("approvals-and-questions");

        Assert.Contains("## An opt-out the person says was not one", skill, StringComparison.Ordinal);
        Assert.Contains("you record it only when they have said, in their own words, that the reply was not a request to stop and what it was", skill, StringComparison.Ordinal);
        Assert.Contains("and the nature from what they say the reply is: `person` for a person writing for themselves, `automatic` for a notice a machine sent, `bounce` for a failed delivery.", skill, StringComparison.Ordinal);
        var lift = Assert.Single(records);
        Assert.Contains("--actor human:", lift, StringComparison.Ordinal);
        Assert.DoesNotContain("--nature opt_out", lift, StringComparison.Ordinal);
        Assert.DoesNotContain("--decision", lift, StringComparison.Ordinal);
        NaturesFollowTheCategory(records);
    }

    [Fact]
    public void The_approval_boundaries_say_the_automatic_mode_plainly_beside_the_standing_approval()
    {
        var skill = Business("approval-boundaries");

        Assert.Contains("### The automatic send mode", skill, StringComparison.Ordinal);
        Assert.Contains("**It is a person's recorded decision in advance, and it lowers no class.**", skill, StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("### Standing approval", StringComparison.Ordinal) < skill.IndexOf("### The automatic send mode", StringComparison.Ordinal),
            "the mode follows the standing approval");

        // The campaign's verb sets either kind.
        Assert.Contains("and for one campaign by `jason campaign set-send-mode`, for its cold contacts, its warm ones or both", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("for one campaign's cold contacts", skill, StringComparison.Ordinal);
    }
}
