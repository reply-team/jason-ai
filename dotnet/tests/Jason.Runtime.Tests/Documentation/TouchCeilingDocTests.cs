using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The ceiling of touches a person may have in one campaign, as the work-execution contract publishes it: its setting's
/// row holds the default and the range the validator enforces, says there is no zero and no unlimited, and that only the
/// settings file writes it.
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
}
