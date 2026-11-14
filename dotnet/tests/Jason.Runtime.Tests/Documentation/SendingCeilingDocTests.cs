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
}
