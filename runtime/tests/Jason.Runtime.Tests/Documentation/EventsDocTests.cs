using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Events;
using Jason.Runtime.Configuration;
using Jason.Runtime.Events;
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

    /// <summary>
    /// The reading section, held to the constants and the setting that decide it: how far back a read reaches, how many
    /// pages one takes, the cadence's default and range, the lines a read writes, and the inbound read's fourth skip.
    /// </summary>
    [Fact]
    public void The_reading_section_states_what_the_reader_does_by_its_own_numbers()
    {
        var page = Flattened(Read("events.md"));
        var defaults = new DispatcherOptions();

        Assert.Contains($"| no cursor yet | {EventSchedule.DepthDays} days back |", page, StringComparison.Ordinal);
        Assert.Contains($"At most {EventSchedule.PagesPerRead} pages", page, StringComparison.Ordinal);
        Assert.Contains($"each one child given the contract's {EventsCatalog.Find("events.read")!.TimeoutMs / 1000} seconds", page, StringComparison.Ordinal);
        Assert.Contains(
            $"| `Dispatcher:EventsCadenceSeconds` | `{defaults.EventsCadenceSeconds}` | {DispatcherOptions.MinimumEventsCadenceSeconds}..{DispatcherOptions.MaximumEventsCadenceSeconds} |",
            page,
            StringComparison.Ordinal);
        Assert.Contains($"`{JournalKinds.EventsGapObserved}`", page, StringComparison.Ordinal);
        Assert.Contains($"`{JournalKinds.EventsRead}`", page, StringComparison.Ordinal);
        Assert.Contains($"`{EventReader.ResultInvalid}`", page, StringComparison.Ordinal);
        Assert.Contains("within the shorter of two cadences and `Inbound:LookbackSeconds`, and never before that first success", page, StringComparison.Ordinal);
        Assert.Contains($"| `{Jason.Runtime.Dispatch.InboundSkipCodes.PluginReadsEvents}` |", Flattened(Read("inbound-sync.md")), StringComparison.Ordinal);
        var inbound = Flattened(Read("inbound-sync.md"));
        Assert.Contains("started within the shorter of two `Dispatcher:EventsCadenceSeconds` and `Inbound:LookbackSeconds`", inbound, StringComparison.Ordinal);
        Assert.Contains("a look-back of 0 never leaves one at all", inbound, StringComparison.Ordinal);
        Assert.DoesNotContain("two cadences are well inside the look-back", inbound, StringComparison.Ordinal);
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
