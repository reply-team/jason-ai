using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;
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

        // The three kinds this version reacts to, named on the page and not only in the code.
        Assert.Contains(JournalKinds.WorkItemFailed, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.ApprovalRejected, page, StringComparison.Ordinal);
        Assert.Contains(JournalKinds.ExternalEffectReported, page, StringComparison.Ordinal);

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

        // And the three kinds the page publishes are the three the defaults hold, in the order it prints them.
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
        var shape = Flattened(ManagerCheckIn.ResultFormat.ToJsonString());

        foreach (var field in new[] { "outcome", "summary", "created_work_items", "cancelled_work_items" })
        {
            Assert.Contains(field, page, StringComparison.Ordinal);
            Assert.Contains(field, shape, StringComparison.Ordinal);
        }

        Assert.Contains("acted | escalated | nothing", page, StringComparison.Ordinal);
    }

    /// <summary>What this version cannot do, said on the page rather than discovered in a launch.</summary>
    [Fact]
    public void The_page_says_what_the_loop_does_not_do_yet()
    {
        var page = Flattened(Read());

        Assert.Contains("Escalation", page, StringComparison.Ordinal);
        Assert.Contains("nothing sets it in this version", page, StringComparison.Ordinal);
        Assert.Contains("A manager skill", page, StringComparison.Ordinal);
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
