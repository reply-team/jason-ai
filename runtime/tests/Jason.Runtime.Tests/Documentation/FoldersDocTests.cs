using Jason.Contracts.Harness;
using Jason.Runtime.Configuration;
using Jason.Runtime.Harness;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The folder policy's part of the projects contract, held to what the build does: the setting has its row, every refusal
/// the folders answer has its row, the marker is shown as the runtime writes it, and the architecture names the invariant.
/// </summary>
public sealed partial class FoldersDocTests
{
    private static string Page(params string[] path) => File.ReadAllText(Path.Combine([Publication.RepositoryRoot(), .. path]));

    [Fact]
    public void The_projects_contract_names_the_setting_and_every_refusal_of_the_folders()
    {
        var page = Page("docs", "projects.md");

        Assert.All(
            [$"{HarnessOptions.Section}:{nameof(HarnessOptions.Root)}", "harness_folder_unavailable", "harness_root_taken", "harness_folder_taken", "harness_folder_missing", "harness_folder_present"],
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

    /// <summary>
    /// What a host composes of the three levels was measured, and the page says what was measured: a session started in a
    /// campaign's folder is given the campaign's file, and the parent levels' imports, from outside that folder, are
    /// dropped in print mode without a word. A page that said the host reads all three by itself would have a session
    /// trust a composition that does not happen and plan against the campaign's words alone.
    /// </summary>
    [Fact]
    public void The_projects_contract_says_what_a_host_composes_and_what_is_read_by_path()
    {
        var page = Whitespace().Replace(Page("docs", "projects.md"), " ");

        Assert.DoesNotContain("reads the three by itself", page, StringComparison.Ordinal);
        Assert.Contains("in print mode it drops, without asking, an import of a file outside the folder it started in", page, StringComparison.Ordinal);
        Assert.Contains("The session skills therefore read the root's and the project's files by path", page, StringComparison.Ordinal);
        Assert.Contains("a role the runtime launches reads all three by the paths its launch envelope gives", page, StringComparison.Ordinal);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex Whitespace();

    /// <summary>
    /// The root is the person's against the CLI as well as the runtime: the invariant, the projects contract and the
    /// release page's account of an uninstall each say that an uninstall removes nothing under it, the purge included,
    /// and the first two that a diagnostic bundle lists and opens nothing in it; and the release page that the purge
    /// refuses a data directory that is a folder of the root.
    /// </summary>
    [Fact]
    public void The_pages_say_an_uninstall_and_a_bundle_leave_the_root_alone()
    {
        static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        const string Cli = "`jason uninstall` removes nothing under";

        Assert.Contains(
            $"Nor does the CLI touch it: {Cli} the root, `--purge-data` included, and `jason doctor` lists and opens nothing in it.",
            Page("docs", "architecture.md"),
            StringComparison.Ordinal);
        Assert.Contains(
            $"Nor does the CLI touch the root: {Cli} it, `--purge-data` included, since the purge empties the data directory and never the root; and `jason doctor` lists and opens nothing in it, so no memory file's name or word reaches a bundle.",
            Flat(Page("docs", "projects.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "and the harness root with every folder and file in it, the person's memory files and every project's and campaign's folder ([projects](projects.md) §8), `--purge-data` included.",
            Flat(Page("docs", "release-and-update.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "is refused before anything is removed, and so is a folder of the harness root or one inside it, and one holding such a folder directly in it or anywhere in a folder the purge would delete, each known by its `.jason/marker.json`, or holding a folder there that cannot be looked into.",
            Flat(Page("docs", "release-and-update.md")),
            StringComparison.Ordinal);
    }
}
