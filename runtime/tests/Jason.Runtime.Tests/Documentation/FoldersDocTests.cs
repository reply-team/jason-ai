using Jason.Runtime.Configuration;
using Jason.Runtime.Harness;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The folder policy's part of the projects contract, held to what the build does: the setting has its row, every refusal
/// the folders answer has its row, the marker is shown as the runtime writes it, and the architecture names the invariant.
/// </summary>
public sealed class FoldersDocTests
{
    private static string Page(params string[] path) => File.ReadAllText(Path.Combine([Publication.RepositoryRoot(), .. path]));

    [Fact]
    public void The_projects_contract_names_the_setting_and_every_refusal_of_the_folders()
    {
        var page = Page("docs", "projects.md");

        Assert.All(
            [$"{HarnessOptions.Section}:{nameof(HarnessOptions.Root)}", "harness_folder_unavailable", "harness_root_taken", "harness_folder_taken", "harness_folder_missing"],
            name => Assert.True(page.Contains($"| `{name}` |", StringComparison.Ordinal), $"docs/projects.md has no row for `{name}`."));
    }

    [Fact]
    public void The_projects_contract_shows_each_marker_and_the_import_as_they_are_written()
    {
        var page = Page("docs", "projects.md");

        Assert.All(
            [
                System.Text.Encoding.UTF8.GetString(new HarnessMarker(HarnessMarker.Root, null).Bytes()),
                System.Text.Encoding.UTF8.GetString(new HarnessMarker(HarnessMarker.Project, "prj_…").Bytes()),
                System.Text.Encoding.UTF8.GetString(new HarnessMarker(HarnessMarker.Campaign, "cmp_…").Bytes()),
                $"`{HarnessFolders.ClaudeImport}`",
            ],
            shown => Assert.True(page.Contains(shown, StringComparison.Ordinal), $"docs/projects.md does not show {shown}."));
    }

    [Fact]
    public void The_architecture_records_the_decision_and_the_invariant()
    {
        var architecture = Page("docs", "architecture.md");

        Assert.Contains("| DEC-HARNESS-001 |", architecture, StringComparison.Ordinal);
        Assert.Contains("| INV-FILE-002 |", architecture, StringComparison.Ordinal);
    }
}
