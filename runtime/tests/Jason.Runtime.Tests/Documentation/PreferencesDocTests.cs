using Jason.Contracts.Api;
using Jason.Contracts.Preferences;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The contract page for the named preferences, held to what the build publishes: every key the catalog knows has its row,
/// every operation and every chronicle kind is named, and the repository's own instructions count the knowledge concepts
/// the product has.
/// </summary>
public sealed class PreferencesDocTests
{
    [Fact]
    public void The_projects_contract_names_every_key_operation_and_line()
    {
        var page = File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "docs", "projects.md"));

        Assert.All(PreferenceCatalog.Keys, key => Assert.True(page.Contains($"| `{key}` |", StringComparison.Ordinal), $"docs/projects.md has no row for `{key}`."));
        Assert.All(
            [Operations.PreferenceSet, Operations.PreferenceUnset, Operations.PreferenceList, JournalKinds.PreferenceSet, JournalKinds.PreferenceUnset],
            name => Assert.True(page.Contains($"| `{name}` |", StringComparison.Ordinal), $"docs/projects.md has no row for `{name}`."));
    }

    [Fact]
    public void The_repository_s_instructions_count_six_knowledge_concepts_and_point_at_the_contract()
    {
        var instructions = string.Join(' ', File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "CLAUDE.md")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "Skills, memory files, named preferences, campaign context, role notes and learned practice are six distinct knowledge concepts; do not blur them.",
            instructions,
            StringComparison.Ordinal);
        Assert.Contains("`docs/projects.md` is the contract.", instructions, StringComparison.Ordinal);
    }
}
