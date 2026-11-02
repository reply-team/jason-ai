using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Context;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Context;

/// <summary>
/// Prose explains; the generated block governs. Each reserved context key's page carries a table of its fields rendered
/// from its own document, as a named preference's page does, and the package's README names every page.
/// </summary>
public sealed class ContextKeyPageTests
{
    private const string Begin = "<!-- BEGIN GENERATED properties -->";
    private const string End = "<!-- END GENERATED properties -->";

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var key in ContextKeyCatalog.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void The_page_s_generated_block_is_what_the_document_says(string key)
    {
        var contract = ContextKeyCatalog.Find(key)!;
        var path = Page(key);

        Assert.True(File.Exists(path), $"{ContractFiles.Relative(path)} explains {key} for a person and a skill author, and is missing.");

        var page = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = page.IndexOf(Begin, StringComparison.Ordinal);
        var finish = page.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, $"{ContractFiles.Relative(path)} carries no generated block.");

        var found = page[start..(finish + End.Length)];
        var expected = Render(contract);

        Assert.True(found == expected, $"{ContractFiles.Relative(path)} is out of step with its document. The block should read:\n\n{expected}\n");
    }

    /// <summary>The page opens by naming its key and saying in one line what it is, as a preference's page does.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void The_page_s_title_names_the_key(string key)
    {
        var first = File.ReadAllText(Page(key)).ReplaceLineEndings("\n").Split('\n')[0];
        var heading = $"# `{key}` — ";

        Assert.True(
            first.StartsWith(heading, StringComparison.Ordinal) && first.Length > heading.Length,
            $"{ContractFiles.Relative(Page(key))} opens with '{first}', where its title names the key and says in one line what it is.");
    }

    /// <summary>
    /// The package's README names the family and links every key's page, so a key added to the catalog is a key a reader
    /// of the package can find.
    /// </summary>
    [Fact]
    public void The_readme_links_every_reserved_key_s_page()
    {
        var readme = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md"));

        Assert.All(ContextKeyCatalog.Keys, key => Assert.True(
            readme.Contains($"(context/{key}.md)", StringComparison.Ordinal),
            $"docs/contracts/README.md does not link context/{key}.md."));
    }

    /// <summary>The page's example is a value the key accepts, kept as a fixture the tests run.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void The_page_s_example_is_one_of_the_key_s_valid_fixtures(string key)
    {
        var page = File.ReadAllText(Page(key)).ReplaceLineEndings("\n");
        var open = page.IndexOf("```json\n", StringComparison.Ordinal);
        Assert.True(open >= 0, $"{ContractFiles.Relative(Page(key))} shows no JSON example.");

        var start = open + "```json\n".Length;
        var example = JsonNode.Parse(page[start..page.IndexOf("\n```", start, StringComparison.Ordinal)])!;

        var root = Path.Combine(ContractFiles.Fixtures, "context", key);
        var valid = Directory.EnumerateFiles(root, "value-*.json")
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!)
            .Where(fixture => fixture["expect"] is JsonValue expect && expect.GetValue<string>() == "valid")
            .Select(fixture => fixture["value"]!)
            .ToList();

        Assert.True(
            valid.Any(value => JsonNode.DeepEquals(value, example)),
            $"The example on {ContractFiles.Relative(Page(key))} is no valid fixture of {key}: {example.ToJsonString()}");
    }

    private static string Page(string key) => Path.Combine(ContractFiles.Package, "context", key + ".md");

    private static string Render(ContextKeyContract contract)
    {
        var block = new StringBuilder();
        block.Append(Begin).Append('\n');
        block.Append("<!-- Rendered from ").Append(contract.Key).Append(".json. Edit the document; this block follows it. -->\n\n");
        block.Append("| What it is | Value |\n|---|---|\n");
        block.Append("| key | `").Append(contract.Key).Append("` |\n");
        block.Append("| version | ").Append(contract.Version.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        block.Append("| value | ").Append(Holds(contract.ValueSchema)).Append(" |\n");
        if (contract.DistinctBy is { } member)
        {
            block.Append("| items told apart by | `").Append(member).Append("`, which no two items share |\n");
        }

        block.Append('\n');

        var shape = contract.ValueSchema;
        var prefix = contract.Key + ".";
        if (Type(shape) == "array" && shape["items"] is JsonObject items && Type(items) == "object")
        {
            shape = items;
            prefix = contract.Key + "[].";
        }

        if (Type(shape) == "object")
        {
            block.Append("| Field | Required | What it holds |\n|---|---|---|\n");
            Fields(block, prefix, shape);
            block.Append('\n');
        }

        block.Append(End);
        return block.ToString();
    }

    /// <summary>One row per field, in the document's order; the fields of an object in a list follow it, as <c>list[].field</c>.</summary>
    private static void Fields(StringBuilder block, string prefix, JsonObject schema)
    {
        var required = (schema["required"] as JsonArray ?? []).Select(name => (string)name!).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, node) in (JsonObject?)schema["properties"] ?? [])
        {
            var property = (JsonObject)node!;
            block.Append("| `").Append(prefix).Append(name).Append("` | ").Append(required.Contains(name) ? "yes" : "no")
                .Append(" | ").Append(Holds(property)).Append(" |\n");

            if (Type(property) == "object")
            {
                Fields(block, prefix + name + ".", property);
            }
            else if (Type(property) == "array" && property["items"] is JsonObject items && Type(items) == "object")
            {
                Fields(block, prefix + name + "[].", items);
            }
        }
    }

    /// <summary>
    /// What a field accepts, in words, for the part of the dialect the documents use; a keyword this does not read fails
    /// here, so the block never leaves a bound out without saying so.
    /// </summary>
    private static string Holds(JsonObject schema)
    {
        var read = new HashSet<string>(StringComparer.Ordinal) { "type", "$schema" };
        string text;
        if (schema["enum"] is JsonArray values)
        {
            read.Add("enum");
            text = "one of " + string.Join(", ", values.Select(value => "`" + (string)value! + "`"));
        }
        else
        {
            switch (Type(schema))
            {
                case "string":
                    read.UnionWith(["minLength", "maxLength", "pattern"]);
                    text = "text" + Bounds((int?)schema["minLength"], (int?)schema["maxLength"], " of", "characters")
                        + (schema["pattern"] is { } pattern ? ", matching `" + (string)pattern! + "`" : string.Empty);
                    break;
                case "integer":
                    read.UnionWith(["minimum", "maximum"]);
                    text = "a whole number" + Bounds((int?)schema["minimum"], (int?)schema["maximum"], " from", null);
                    break;
                case "array":
                    read.UnionWith(["minItems", "maxItems", "uniqueItems", "items"]);
                    var items = (JsonObject)schema["items"]!;
                    text = "a list" + Bounds((int?)schema["minItems"], (int?)schema["maxItems"], " of", null)
                        + ((bool?)schema["uniqueItems"] == true ? ", each different" : string.Empty)
                        + (Type(items) == "object" ? ", each an object whose fields follow" : "; each " + Holds(items));
                    break;
                case "object":
                    read.UnionWith(["required", "additionalProperties", "properties"]);
                    text = "an object whose fields follow";
                    break;
                default:
                    throw new InvalidOperationException($"No words for a field of type `{Type(schema)}`: {schema.ToJsonString()}");
            }
        }

        var unread = schema.Select(member => member.Key).Where(name => !read.Contains(name)).ToList();
        return unread.Count == 0
            ? text
            : throw new InvalidOperationException($"No words for {string.Join(", ", unread)} in {schema.ToJsonString()}");
    }

    private static string Bounds(int? least, int? most, string lead, string? unit)
    {
        var after = unit is null ? string.Empty : " " + unit;
        return (least, most) switch
        {
            ({ } low, { } high) => string.Create(CultureInfo.InvariantCulture, $"{lead} {low} to {high}{after}"),
            (null, { } high) => string.Create(CultureInfo.InvariantCulture, $"{lead} at most {high}{after}"),
            ({ } low, null) => string.Create(CultureInfo.InvariantCulture, $"{lead} at least {low}{after}"),
            _ => string.Empty,
        };
    }

    private static string? Type(JsonObject schema) => (string?)schema["type"];
}
