using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Runtime.Events;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// The store restates two things the events contract already says: which identifiers each kind names, which make two
/// reads of one event the same, and which members each kind carries into its line. Both are held here to the document's
/// own branches, so a kind or a member added to the contract cannot be left out of the key or the line without a red test.
/// </summary>
public class EventContractListsTests
{
    private static readonly string[] IdentifierTypes = ["#/$defs/identifier", "#/$defs/maybe_identifier"];

    /// <summary>The members every line names in its own way, or that are no part of what a kind carries.</summary>
    private static readonly string[] EveryLine = ["kind", "id", "occurred_at", "contact", "campaign", "channel"];

    [Fact]
    public void The_identifiers_each_kind_names_are_the_identifier_members_of_its_branch()
    {
        var contract = EventsCatalog.Find("events.read")!;
        var definitions = (JsonObject)contract.OutputSchema["$defs"]!;

        Assert.Equal(contract.Kinds, [.. ReadEvent.Identifiers.Keys]);
        foreach (var kind in contract.Kinds)
        {
            var named = Members(definitions, kind)
                .Where(member => member.Key != "id" && member.Value?["$ref"] is JsonValue reference && IdentifierTypes.Contains((string?)reference))
                .Select(member => member.Key)
                .ToList();
            Assert.True(
                named.SequenceEqual(ReadEvent.Identifiers[kind], StringComparer.Ordinal),
                $"{kind}: the contract names the identifiers {string.Join(", ", named)}; the key covers {string.Join(", ", ReadEvent.Identifiers[kind])}.");
        }
    }

    [Fact]
    public void What_each_kind_carries_into_its_line_is_the_rest_of_its_branch()
    {
        var contract = EventsCatalog.Find("events.read")!;
        var definitions = (JsonObject)contract.OutputSchema["$defs"]!;

        Assert.Equal([.. contract.Kinds.Where(kind => kind != ReadEvent.ThreadMoved)], [.. EventStore.Carried.Keys]);
        foreach (var (kind, carried) in EventStore.Carried)
        {
            var rest = Members(definitions, kind).Select(member => member.Key).Where(name => !EveryLine.Contains(name)).ToList();
            Assert.True(
                rest.SequenceEqual(carried, StringComparer.Ordinal),
                $"{kind}: the contract's other members are {string.Join(", ", rest)}; the line carries {string.Join(", ", carried)}.");
        }
    }

    private static JsonObject Members(JsonObject definitions, string kind) => (JsonObject)definitions[kind]!["properties"]!;
}
