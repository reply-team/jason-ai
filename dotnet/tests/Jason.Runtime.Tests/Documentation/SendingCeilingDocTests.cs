using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The daily ceiling of automatic sends, as the work-execution contract publishes it: its setting's row holds the default
/// and the range the validator enforces, and says what a person who wants no automatic sends does instead of a zero.
/// </summary>
public sealed class SendingCeilingDocTests
{
    [Fact]
    public void The_setting_s_row_is_the_one_the_runtime_enforces()
    {
        var defaults = new SendingOptions();
        var row = Assert.Single(
            File.ReadAllLines(Path.Combine(RepositoryTree.Root(), "docs", "work-execution.md")),
            line => line.StartsWith("| `Sending:AutomaticMaxPerDay` |", StringComparison.Ordinal));

        Assert.StartsWith(
            $"| `Sending:AutomaticMaxPerDay` | `{defaults.AutomaticMaxPerDay}` | {SendingOptions.MinimumPerDay}..{SendingOptions.MaximumPerDay} |",
            row,
            StringComparison.Ordinal);
        Assert.Contains("since 00:00Z", row, StringComparison.Ordinal);
        Assert.Contains("a person who wants no automatic sends sets the mode to `every_message`", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// The claim rule says what the number means while a mode is automatic, what the count is of, what the park carries, and
    /// that a standing approval does not let a send past it.
    /// </summary>
    [Fact]
    public void The_claim_rule_says_what_the_ceiling_is_and_what_its_park_carries()
    {
        var page = string.Join(' ', File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "work-execution.md"))
            .Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "While a send mode is automatic, `Sending:AutomaticMaxPerDay` is the most sends that leave a campaign in a day without the person's look.",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            $"its row's reason is `{SendModeReasons.DailyCeiling}`, and its preview carries `{DailyCeiling.PreviewKey}` with three numbers only, `limit`, `released` and `since`",
            page,
            StringComparison.Ordinal);
        Assert.Contains("a standing approval that covers the send does not let it go either", page, StringComparison.Ordinal);
    }
}
