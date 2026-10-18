using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The page every read that pages returns. A page is bounded twice: by <c>limit</c>, which the caller chooses, and
/// by bytes, which nobody can choose from the schema alone — a page of the largest items the schema allows can pass
/// the runtime's cap on a result, so the plugin stops filling a page at a budget below that cap and says
/// <c>has_more</c>. These are the rules that make that short page safe to read: the next page is found by counting,
/// and one item always fits.
/// </summary>
public class PageTests
{
    /// <summary>
    /// Where a plugin stops filling a page. Below the runtime's cap on a result
    /// (<see cref="PluginProtocol.MaxResultBytes"/>) by a quarter, so a plugin's own count of the bytes can be
    /// coarser than the runtime's and still never be the reason an answer is refused.
    /// </summary>
    private const int PageBudget = 768 * 1024;

    public static TheoryData<string> Paged()
    {
        var data = new TheoryData<string>();
        foreach (var contract in OperationCatalog.All.Where(IsPaged))
        {
            data.Add(contract.Id);
        }

        return data;
    }

    private static bool IsPaged(OperationContract contract) =>
        contract.OutputSchema["properties"] is JsonObject properties && properties.ContainsKey("has_more");

    [Fact]
    public void The_budget_a_page_is_cut_at_is_below_the_cap_on_a_result()
    {
        Assert.True(PageBudget < PluginProtocol.MaxResultBytes);
        Assert.NotEmpty(Paged());
    }

    [Theory]
    [MemberData(nameof(Paged))]
    public void A_paged_read_takes_a_limit_and_an_offset_and_says_whether_more_follow(string id)
    {
        var contract = OperationCatalog.Find(id)!;
        var args = Assert.IsType<JsonObject>(contract.InputSchema["properties"]?["args"]);
        var arguments = Assert.IsType<JsonObject>(args["properties"]);

        Assert.Contains("limit", Required(args));
        Assert.Contains("offset", Required(args));
        Assert.Equal(1, (int)arguments["limit"]!["minimum"]!);
        Assert.Equal(0, (int)arguments["offset"]!["minimum"]!);
        Assert.Equal(1_000_000, (int)arguments["offset"]!["maximum"]!);

        Assert.Contains("has_more", Required(contract.OutputSchema));
        Assert.Equal("boolean", (string?)contract.OutputSchema["properties"]!["has_more"]!["type"]);

        // A page never holds more than the caller may ask for, so the schema's ceiling and the argument's are one.
        var (_, page) = Page(contract);
        Assert.Equal((int)arguments["limit"]!["maximum"]!, (int)page["maxItems"]!);
    }

    /// <summary>
    /// A short page has to hold at least one item, or a page could come back empty with more to follow and the next
    /// one would start where it did. So the largest result the schema allows with one item on the page — every
    /// string at its longest, every character the most expensive the runtime's measure knows — is held under the
    /// budget. An output that leaves anything unbounded fails here too, because nothing could then be promised.
    /// </summary>
    [Theory]
    [MemberData(nameof(Paged))]
    public void One_item_of_a_page_always_fits_under_the_budget_a_page_is_cut_at(string id)
    {
        var contract = OperationCatalog.Find(id)!;
        var output = (JsonObject)contract.OutputSchema.DeepClone();
        var (name, _) = Page(contract);
        output["properties"]![name]!["maxItems"] = 1;

        var worst = Worst(output, output, "");

        Assert.True(worst <= PageBudget, $"{id}: a result with one item can take {worst} bytes, past the {PageBudget} a page is cut at.");
    }

    /// <summary>
    /// The events read pages by its own cursor rather than by an offset, and its page is held to the same two bounds: it
    /// never holds more events than the caller may ask for, and the largest page the schema allows with one event on it
    /// fits under the budget, whichever of the four kinds that event is.
    /// </summary>
    [Fact]
    public void One_event_of_an_events_page_always_fits_under_the_budget_a_page_is_cut_at()
    {
        var contract = EventsCatalog.Find("events.read")!;
        var output = (JsonObject)contract.OutputSchema.DeepClone();
        var events = (JsonObject)output["properties"]!["events"]!;

        Assert.Equal((int)contract.InputSchema["properties"]!["limit"]!["maximum"]!, (int)events["maxItems"]!);
        Assert.Contains("has_more", Required(contract.OutputSchema));
        events["maxItems"] = 1;

        var worst = Worst(output, output, "");

        Assert.True(worst <= PageBudget, $"events.read: a page with one event can take {worst} bytes, past the {PageBudget} a page is cut at.");
    }

    /// <summary>The one array of a paged result: the page itself.</summary>
    private static (string Name, JsonObject Schema) Page(OperationContract contract)
    {
        var arrays = ((JsonObject)contract.OutputSchema["properties"]!)
            .Where(property => property.Value?["type"] is JsonValue type && (string?)type == "array")
            .ToList();

        var page = Assert.Single(arrays);
        return (page.Key, (JsonObject)page.Value!);
    }

    /// <summary>
    /// The most bytes a value the schema accepts can take in the runtime's measure: System.Text.Json's default
    /// encoder, which writes a character outside the Basic Multilingual Plane as twelve bytes — the most any one
    /// character costs — so a string is counted at twelve bytes a character whatever <c>maxLength</c> counts in.
    /// </summary>
    private static long Worst(JsonObject schema, JsonObject root, string pointer)
    {
        if (schema["$ref"] is JsonValue reference)
        {
            var name = ((string)reference!)["#/$defs/".Length..];
            return Worst((JsonObject)root["$defs"]![name]!, root, pointer);
        }

        if (schema["const"] is { } constant)
        {
            return Measure(constant);
        }

        if (schema["anyOf"] is JsonArray branches)
        {
            return branches.Max(branch => Worst((JsonObject)branch!, root, pointer));
        }

        if (schema["enum"] is JsonArray values)
        {
            return values.Max(value => value is null ? 4 : Measure(value));
        }

        var types = schema["type"] switch
        {
            JsonValue single => [(string)single!],
            JsonArray several => several.Select(type => (string)type!).ToList(),
            _ => throw Unbounded(pointer, "states no type"),
        };

        return types.Max(type => type switch
        {
            "null" => 4L,
            "boolean" => 5L,
            "integer" => 20L,
            "string" => schema["maxLength"] is JsonValue most ? 2 + (12L * (int)most!) : throw Unbounded(pointer, "is a string with no maxLength"),
            "array" => schema["maxItems"] is JsonValue count && schema["items"] is JsonObject items
                ? 2 + ((int)count! * (Worst(items, root, pointer + "/items") + 1))
                : throw Unbounded(pointer, "is an array with no maxItems"),
            "object" => Object(schema, root, pointer),
            _ => throw Unbounded(pointer, $"is a {type}, which this measure does not bound"),
        });
    }

    private static long Object(JsonObject schema, JsonObject root, string pointer)
    {
        if (schema["additionalProperties"] is not JsonValue closed || (bool)closed! != false)
        {
            throw Unbounded(pointer, "is an object that admits members it does not list");
        }

        var total = 2L;
        foreach (var (name, property) in (JsonObject?)schema["properties"] ?? [])
        {
            total += Measure(JsonValue.Create(name)) + 1 + Worst((JsonObject)property!, root, pointer + "/" + name) + 1;
        }

        return total;
    }

    private static long Measure(JsonNode node) => Encoding.UTF8.GetByteCount(node.ToJsonString());

    private static InvalidOperationException Unbounded(string pointer, string why) =>
        new($"The output at `{(pointer.Length == 0 ? "/" : pointer)}` {why}, so no page size can be promised.");

    private static IEnumerable<string> Required(JsonNode schema) =>
        schema["required"] is JsonArray names ? names.Select(name => (string)name!) : [];
}
