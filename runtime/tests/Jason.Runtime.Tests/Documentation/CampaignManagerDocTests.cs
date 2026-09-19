using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;
using Jason.Runtime.Decisions;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The manager loop's page, held to the few claims a reader will act on: what the word "check-in" means, which
/// kinds wake a manager, what the dispatcher does not read, and what a cadence costs. A page that drifted from
/// any of those would be teaching an operator to expect a loop they do not have.
/// </summary>
public partial class CampaignManagerDocTests
{
    [Fact]
    public void The_manager_contract_defines_the_check_in_and_prices_the_cadence()
    {
        var page = Flattened(Read());

        // One word for the work the runtime creates, defined where somebody meets it first.
        Assert.Contains("A check-in is the work item the runtime creates to have a campaign reviewed", page, StringComparison.Ordinal);

        // The kinds this version reacts to, named on the page and not only in the code.
        Assert.Contains(JournalKinds.WorkItemFailed, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.ApprovalRejected, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.ExternalEffectReported, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.DecisionAnswered, page, StringComparison.Ordinal);

        // The boundary the whole design rests on.
        Assert.Contains("The dispatcher reads no meaning", page, StringComparison.Ordinal);

        // What a number somebody will type actually costs them, said in launches rather than in seconds.
        Assert.Contains("One launch per active campaign per interval", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The settings table is the part an operator edits, so the numbers on the page have to be the numbers the
    /// runtime holds them to. A default that drifts here is a page that lies about what happens by itself.
    /// </summary>
    [Fact]
    public void The_settings_the_page_publishes_are_the_ones_the_runtime_enforces()
    {
        var page = Flattened(Read());
        var defaults = new ManagerOptions();

        var cadence = $"{ManagerOptions.MinimumReviewSeconds}..{ManagerOptions.MaximumReviewSeconds}";
        Assert.Contains($"`Manager:ReviewSeconds` | `{defaults.ReviewSeconds}` | {cadence}", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:TimeoutSeconds` | `{defaults.TimeoutSeconds}` | 30..86400", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:MaxAttempts` | `{defaults.MaxAttempts}` | 1..10", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:Priority` | `{defaults.Priority}` | -1000..1000", page, StringComparison.Ordinal);
        Assert.Contains($"`Manager:MaxEntriesPerScan` | `{defaults.MaxEntriesPerScan}` | 50..10000", page, StringComparison.Ordinal);

        // And every kind the defaults hold is one the page publishes.
        foreach (var kind in ManagerOptions.Default)
        {
            Assert.Contains(kind, page, StringComparison.Ordinal);
        }
        Assert.Contains("There is no `Manager:Enabled`", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the shape a review answers in, which is the one thing a role's own skill will be written around.
    /// </summary>
    [Fact]
    public void The_page_prints_the_shape_a_review_answers_in()
    {
        var page = Flattened(Read());

        // Read out of the shape itself rather than listed here, so a field added to what a review may answer
        // is a field the page has to print. A list written twice is a list that drifts.
        var fields = ManagerCheckIn.ResultFormat.AsObject()["properties"]!.AsObject().Select(property => property.Key).ToList();

        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            Assert.Contains(field, page, StringComparison.Ordinal);
        }

        Assert.Contains("acted | escalated | nothing", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a question and its answer may be, printed from the same constants the service refuses by. A bound
    /// that moved in code and not on the page would be a refusal nobody was warned about.
    /// </summary>
    [Fact]
    public void The_bounds_the_page_publishes_are_the_ones_a_question_is_held_to()
    {
        var page = Flattened(Read());

        Assert.Contains($"| {DecisionLimits.MaxQuestionLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxAnswerLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxOptions} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxOptionLabelLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxOptionDetailLength} |", page, StringComparison.Ordinal);
        Assert.Contains($"| {DecisionLimits.MaxReferences} |", page, StringComparison.Ordinal);

        // By its row and not by its number: 2000 is already on this page twice, so a bare bound would have
        // passed while the reason was undocumented — which is how a bound comes to live outside the type that
        // exists to hold them all.
        Assert.Contains(
            $"| the reason on either call, in characters | {DecisionLimits.MaxReasonLength} |",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every code escalation can answer with, one sentence each, the way the approvals section of the
    /// work-execution contract carries its own. A code a caller meets and cannot look up is a dead end.
    /// </summary>
    [Fact]
    public void The_page_names_every_code_a_question_can_be_refused_with()
    {
        var page = Flattened(Read());

        foreach (var code in new[]
        {
            "decision_not_found",
            "decision_not_pending",
            "decision_not_human",
            "decision_option_unknown",
            "decision_reference_unresolved",
        })
        {
            Assert.Contains($"`{code}`", page, StringComparison.Ordinal);
        }

        // And the fence, which is not escalation's own code but is the one a role meets most often.
        Assert.Contains("`stale_attempt`", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three claims about the loop that a reader would otherwise have to infer from code: why a person's
    /// line is exempt and what that trust is worth, what a narrowed trigger list costs, and how the summon
    /// names a decision without reading one.
    /// </summary>
    [Fact]
    public void The_page_states_the_exemption_its_limit_and_what_a_narrowed_list_costs()
    {
        var page = Flattened(Read());

        Assert.Contains("A line whose actor is a **person** is never passed over", page, StringComparison.Ordinal);
        Assert.Contains("tell a person from a process holding that person's own command line", page, StringComparison.Ordinal);
        Assert.Contains($"A narrowed list has to keep `{JournalKinds.DecisionAnswered}`", page, StringComparison.Ordinal);

        // How the cause names a decision: an identifier lookup, never a line's meaning.
        Assert.Contains("remembers the chronicle line its answer wrote", page, StringComparison.Ordinal);

        // And that raising is a fenced call, so nobody wonders why a lease moved.
        Assert.Contains("counts as a heartbeat", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// What this version cannot do, said on the page rather than discovered in a launch — and no longer
    /// claiming the two things that have since arrived. A "not here yet" list that outlives the work it
    /// described is worse than none: a reader believes it.
    /// </summary>
    [Fact]
    public void The_page_says_what_the_loop_does_not_do_yet()
    {
        var page = Flattened(Read());

        Assert.Contains("Notifications", page, StringComparison.Ordinal);
        Assert.Contains("Anomaly rules and reconciliation", page, StringComparison.Ordinal);

        Assert.DoesNotContain("nothing sets it in this version", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ships with escalation", page, StringComparison.Ordinal);
    }

    private static string Read() =>
        File.ReadAllText(Path.Combine(DocumentsDirectory(), "campaign-manager.md"));

    /// <summary>
    /// Read with its line breaks flattened. A published page wraps where the column runs out, so a guard that
    /// searched the raw text would be asserting where a paragraph happens to break rather than what it says.
    /// </summary>
    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    private static string DocumentsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
