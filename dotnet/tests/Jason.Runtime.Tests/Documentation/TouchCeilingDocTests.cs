using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The ceiling of touches a person may have in one campaign, as the work-execution contract publishes it: its setting's
/// row holds the default and the range the validator enforces, says there is no zero and no unlimited, and that only the
/// settings file writes it. The claim's row for it says what counts, a send a cancel took back left out, and that the
/// repair is the manager's end of the line and never a raised ceiling; a send a read says failed or was cancelled, and no
/// read says went or may have gone, is left out by the ceiling's row, the check and the people list; the contracts page names who reads the touch flag; and the
/// reopen's paragraph says one answer reopens the end it is about.
/// </summary>
public sealed class TouchCeilingDocTests
{
    [Fact]
    public void The_setting_s_row_is_the_one_the_runtime_enforces()
    {
        var defaults = new LinesOptions();
        var row = Assert.Single(
            File.ReadAllLines(Path.Combine(RepositoryTree.Root(), "docs", "work-execution.md")),
            line => line.StartsWith("| `Lines:MaxTouches` |", StringComparison.Ordinal));

        Assert.StartsWith(
            $"| `Lines:MaxTouches` | `{defaults.MaxTouches}` | {LinesOptions.MinimumTouches}..{LinesOptions.MaximumTouches} |",
            row,
            StringComparison.Ordinal);
        Assert.Contains("`touch_ceiling_reached`", row, StringComparison.Ordinal);
        Assert.Contains("there is no 0 and no unlimited, and no verb writes it, only the settings file", row, StringComparison.Ordinal);
    }

    [Fact]
    public void The_claim_s_row_leaves_out_a_send_taken_back_and_never_asks_to_raise_the_ceiling()
    {
        var row = Assert.Single(
            File.ReadAllLines(Path.Combine(RepositoryTree.Root(), "docs", "routing.md")),
            line => line.StartsWith("| 30 | `touch_ceiling_reached` |", StringComparison.Ordinal));

        Assert.Contains("a send that a succeeded `message.cancel` of the campaign took back never went, and counts for nothing", row, StringComparison.Ordinal);
        Assert.EndsWith(
            "Nothing to change: the manager ends the line. `Lines:MaxTouches` is the installation's, for every campaign, and is not raised for one person's line |",
            row,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Raise the setting", row, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ceiling_s_row_the_check_and_the_people_list_leave_out_a_send_a_read_says_never_went()
    {
        var row = Assert.Single(
            File.ReadAllLines(Path.Combine(RepositoryTree.Root(), "docs", "routing.md")),
            line => line.StartsWith("| 30 | `touch_ceiling_reached` |", StringComparison.Ordinal));
        var page = Regex.Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "work-execution.md")), @"\s+", " ");

        Assert.Contains(
            "took back never went, and counts for nothing; a send a succeeded `message.status` of the campaign says failed or was cancelled is not counted either, whoever asked for the read, unless a read of it says it went or may have gone (`sent` or `unknown`); a `queued` read, a read that failed and a letter the provider still holds as `queued`, however long past its moment, leave it counted.",
            row,
            StringComparison.Ordinal);
        Assert.Contains(
            "since the letter may still go; a send a succeeded `message.status` of the campaign says failed or was cancelled is not counted either, whoever asked for the read, unless a read of it says it went or may have gone (`sent` or `unknown`); a `queued` read, a read that failed and a letter the provider still holds as `queued`, however long past its moment, leave it counted — one definition, which the people list counts by too.",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "it is no touch, and no row's last touch either. A send a succeeded `message.status` of the campaign says failed or was cancelled is left out the same way, whoever asked for the read, unless a read of it says it went or may have gone (`sent` or `unknown`); a `queued` read, a read that failed and a letter the provider still holds as `queued`, however long past its moment, leave it counted.",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_touch_flag_s_row_names_its_readers()
    {
        var row = Assert.Single(
            File.ReadAllLines(Path.Combine(RepositoryTree.Root(), "docs", "contracts", "README.md")),
            line => line.StartsWith("| `touch` |", StringComparison.Ordinal));

        Assert.Contains(
            "so that every check that counts or holds a touch reads one published fact and not a list a plugin author cannot see: the claim's checks 29 and 30, eligibility rule 3, the people list's `touches`, the cancel at `campaign.finish_contacts`, and the hold's reading of Jason's own move after a reply",
            row,
            StringComparison.Ordinal);
        Assert.DoesNotContain("later count", row, StringComparison.Ordinal);
    }

    [Fact]
    public void The_people_list_s_touches_leave_out_a_send_taken_back_and_take_a_held_send_s_moment()
    {
        var page = Regex.Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "work-execution.md")), @"\s+", " ");

        Assert.Contains("A send that a succeeded `message.cancel` of the campaign took back never went: it is no touch, and no row's last touch either.", page, StringComparison.Ordinal);
        Assert.Contains("A touch's moment, where it succeeded, is the one the provider took it for, its result's `accepted_for`, read in UTC as the hold reads it, else the item's finish", page, StringComparison.Ordinal);
        Assert.Contains("a send that a succeeded `message.cancel` of the campaign took back counts for nothing, while a take-back refused, failed or ended `ambiguous` leaves it counted", page, StringComparison.Ordinal);
        Assert.Contains("A person counts only where that line is the latest `contacts_finished` of the campaign naming them", page, StringComparison.Ordinal);
        Assert.DoesNotContain("its moment is the item's finish where it succeeded", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reopen_s_paragraph_says_one_answer_reopens_the_end_it_is_about()
    {
        var page = Regex.Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "runtime-api.md")), @"\s+", " ");

        Assert.Contains(
            "a person the line the question is about does not name, or whose line a later `contacts_finished` line ended again, since one answer reopens the end it is about and never a later one",
            page,
            StringComparison.Ordinal);
    }
}
