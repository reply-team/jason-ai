using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The events page, held to what the contract and the store do: the kinds and their codes, the bounds of a cursor and a
/// page, the lines a stored event says and what a source of a route without a binding is called. A page that drifted from
/// any of them would teach a plugin's author or a manager's a store they do not have.
/// </summary>
public partial class EventsDocTests
{
    [Fact]
    public void The_kinds_and_the_codes_the_page_publishes_are_the_contract_s()
    {
        var page = Flattened(Read("events.md"));
        var contract = EventsCatalog.Find("events.read")!;

        Assert.All(contract.Kinds, kind => Assert.Contains($"| `{kind}` |", page, StringComparison.Ordinal));
        Assert.All(contract.FailureCodes, code => Assert.Contains($"`{code.Code}`", page, StringComparison.Ordinal));
        Assert.Contains($"at most {Max(contract.InputSchema, "limit", "maximum")} events", page, StringComparison.Ordinal);
        Assert.Contains($"1 to {Max(contract.OutputSchema, "cursor", "maxLength")} printable ASCII characters", page, StringComparison.Ordinal);
        Assert.Contains($"{contract.TimeoutMs / 1000} seconds", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_names_the_lines_a_stored_event_says_and_the_word_for_no_binding()
    {
        var page = Flattened(Read("events.md"));

        Assert.Contains($"`{JournalKinds.InboundReplyObserved}`", page, StringComparison.Ordinal);
        Assert.Contains($"`{JournalKinds.ProviderEventObserved}`", page, StringComparison.Ordinal);
        Assert.Contains($"`{EventSource.NoBinding}`", page, StringComparison.Ordinal);
        Assert.Contains($"`{EventAttributionRules.CampaignPin}`", page, StringComparison.Ordinal);
        Assert.Contains("`events_no_update`, `events_no_delete`, `event_attributions_no_update` and `event_attributions_no_delete`", page, StringComparison.Ordinal);
    }

    /// <summary>The contracts' package says there is a third family, and that the events read is the one read that pages by a cursor.</summary>
    [Fact]
    public void The_contracts_readme_names_the_third_family_and_the_cursor()
    {
        var readme = Flattened(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "contracts", "README.md")));

        Assert.Contains("events/<name>.json", readme, StringComparison.Ordinal);
        Assert.Contains("## The events contract", readme, StringComparison.Ordinal);
        Assert.Contains("[`events/events.read.json`](events/events.read.md)", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("There are no cursors in this version:", readme, StringComparison.Ordinal);
    }

    private static int Max(JsonObject schema, string property, string keyword) =>
        (int)schema["properties"]![property]![keyword]!;

    private static string Read(string page) => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", page));

    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
