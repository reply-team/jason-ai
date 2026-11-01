namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The answer to a question a launched role raised releases the campaign manager's next check-in, which creates the work
    /// the answer implies: the role that asked has already ended and has no review of its own. A session that waits for that
    /// role, or that records the answer and then also creates the work, leaves the campaign holding the work twice, so both
    /// session skills that touch such an answer say who acts on it and that the session stops at the recording.
    /// </summary>
    [Theory]
    [InlineData("managed-campaign-work", "you record it with `jason decision answer` and the person as `--actor`, and you stop there")]
    [InlineData("managed-campaign-work", "the answer releases the campaign manager's next check-in, and the manager creates the work the answer implies")]
    [InlineData("managed-campaign-work", "**A question a role raised is answered by recording the answer, and the campaign's manager acts on it.**")]
    [InlineData("planner-in-session", "Record it as the person's (`approvals-and-questions`); the campaign manager's next check-in creates the work it implies")]
    public void A_session_records_a_roles_answer_and_leaves_the_work_it_implies_to_the_campaigns_manager(string name, string sentence)
    {
        var skill = Flattened(name);
        Assert.Contains(sentence, skill, StringComparison.Ordinal);
        Assert.Contains("twice", skill, StringComparison.Ordinal);
        Assert.Contains("approvals-and-questions", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("that role's review", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("the role's review", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check-in runs only for an active campaign and at the next scan, and one already open does not consume the answer.
    /// A session that read the work a minute after recording found nothing and made the work itself; so it reads the status,
    /// tells the person nothing acts on a campaign that is not active, and says the check-in has not run yet where it is.
    /// </summary>
    [Theory]
    [InlineData("managed-campaign-work", "runs only for an active campaign and at the next scan")]
    [InlineData("managed-campaign-work", "If the campaign is not active, tell the person that nothing acts on the answer until it is.")]
    [InlineData("managed-campaign-work", "say the check-in has not run yet and create nothing in its place")]
    [InlineData("planner-in-session", "It runs only for an active campaign, at the next scan")]
    [InlineData("planner-in-session", "say that it has not run yet and make nothing in its place")]
    public void The_answer_waits_for_an_active_campaigns_next_check_in(string name, string sentence)
    {
        Assert.Contains(sentence, Flattened(name), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>campaign list</c> follows the working directory's project. A session inside one project's folder that took its
    /// list for every project's told the person that another project's campaign did not exist, so the managed path says
    /// what the list follows, what lists every project, and which reads answer for every project regardless.
    /// </summary>
    [Fact]
    public void The_managed_path_says_that_campaign_list_follows_the_folder()
    {
        var skill = Flattened("managed-campaign-work");
        Assert.Contains("`jason campaign list` follows the folder you are in.", skill, StringComparison.Ordinal);
        Assert.Contains("list across projects with `jason campaign list --all-projects`", skill, StringComparison.Ordinal);
        Assert.Contains(
            "The four reads of where people stand, `campaign summary`, `campaign activity`, `campaign activity-by-period` and `campaign list-contacts`, are different: given no campaign they answer for every campaign not archived, in every project",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>enrolled</c> in <c>campaign list-contacts</c> is a person's membership of the campaign in Jason. A session that read
    /// it as enrolment at the provider told a person that the people of a campaign with no provider sequence were enrolled
    /// there, so the skills that read the list say what the word does not mean.
    /// </summary>
    [Theory]
    [InlineData("managed-campaign-work", "`enrolled` here is Jason's own membership and nothing more.")]
    [InlineData("troubleshooting-jason", "its `enrolled` is Jason's own membership, never enrolment at the provider")]
    [InlineData("planner-in-session", "it is Jason's own membership, and `managed-campaign-work` says what it does not mean")]
    public void The_skills_that_read_the_campaigns_people_say_enrolled_is_membership(string name, string sentence)
    {
        Assert.Contains(sentence, Flattened(name), StringComparison.Ordinal);
        Assert.Contains("A campaign with no provider sequence has members, never enrolments", Flattened("managed-campaign-work"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A failure is a fact about a moment. An analysis that named a send which failed before its route existed, and
    /// that a later send had superseded, as what is wrong now, sent the person to repair something that worked, so the
    /// skill reads the current route and the latest outcomes before it names a failure, and lets no summary contradict a
    /// figure the reads gave.
    /// </summary>
    [Fact]
    public void Performance_analysis_reads_the_current_route_and_latest_outcomes_before_naming_a_failure()
    {
        var skill = BusinessFlattened("performance-analysis");
        Assert.Contains("**Read what is current before you name anything that failed.**", skill, StringComparison.Ordinal);
        Assert.Contains("are history: say so as history, with its date, and leave it out of what is wrong", skill, StringComparison.Ordinal);
        Assert.Contains("A figure the reads give you stands as given.", skill, StringComparison.Ordinal);
        Assert.Contains("**Reporting an old failure as current.**", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>workitem list</c> orders oldest first and stops at a hundred, so a long campaign's plain listing shows its oldest
    /// items and misses the late success that supersedes an old failure. The two lines step 5 prints take the newest
    /// finished items, and the one for sends names the operation's kind, so that the latest send can be read from it.
    /// </summary>
    [Fact]
    public void Performance_analysis_lists_the_newest_finished_work_not_the_oldest()
    {
        var file = Path.Combine(SkillPack.BusinessRoot(), "performance-analysis", SkillPack.SkillFile);
        var lists = SkillPack.PrintedCommands(file).Where(line => line.Contains(" workitem list ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, line => Assert.Contains("--order finished --limit 20", line, StringComparison.Ordinal));
        Assert.Single(lists, line => line.Contains("--status succeeded --kind provider_op", StringComparison.Ordinal));
        Assert.Contains("the latest finished send is the newest `message.send` among them", BusinessFlattened("performance-analysis"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The business pack teaches provider-neutral jobs, and the runtime's own reads sit in a block opened as
    /// the runtime's, the way the planning skill opens its one note, so a user of another provider reads the prose as the
    /// job; the failure a reader meets when an agent program was not installed is named by its code.
    /// </summary>
    [Fact]
    public void Performance_analysis_frames_the_runtime_reads_as_the_runtimes_and_names_the_launch_code()
    {
        var skill = BusinessFlattened("performance-analysis");
        Assert.Contains("With Jason, the same reads are:", skill, StringComparison.Ordinal);
        Assert.Contains("a role whose agent program was not installed yet (`host_not_available`)", skill, StringComparison.Ordinal);
        Assert.Contains("where the campaign's sends are routed now", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The catalog counted the skills that carry the boundary note and was wrong the day it was written; a count that
    /// has to be kept true by hand is not kept, so it names the skills by what they do.
    /// </summary>
    [Fact]
    public void The_business_catalog_gives_no_count_of_the_skills_that_carry_the_boundary_note()
    {
        var catalog = File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), "README.md"));
        Assert.DoesNotContain("the six skills", catalog, StringComparison.Ordinal);
        Assert.Contains("**The boundary above was written into the skills** that name an operation this runtime enforces.", catalog, StringComparison.Ordinal);
    }
    /// <summary>
    /// A provider that already held a contact for an address keeps its own name for it, so a thread can name the person
    /// differently from Jason's record. A draft greets by Jason's person and says when the two differ, instead of choosing
    /// silently, and the seat that drafts for a person and the role that drafts for an approval both say so.
    /// </summary>
    [Theory]
    [InlineData("inbox-triage", "**Greet the person by the name you hold for them**")]
    [InlineData("responder", "**Greet the person by the name Jason holds, and say when the thread names them otherwise.**")]
    public void A_draft_greets_by_Jasons_person_and_says_when_the_thread_differs(string name, string sentence)
    {
        var skill = name == "responder" ? Flattened(name) : BusinessFlattened(name);
        Assert.Contains(sentence, skill, StringComparison.Ordinal);
        Assert.Contains("differ", skill, StringComparison.Ordinal);
    }

    /// <summary>A business-pack skill, flattened as <see cref="Flattened"/> does for the runtime pack's.</summary>
    private static string BusinessFlattened(string name) =>
        Whitespace().Replace(File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), name, SkillPack.SkillFile)), " ");
}
