using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Context;

/// <summary>
/// One reserved key of a campaign's context: a top-level key whose value the runtime holds to a published schema, where
/// every other key of the context is the caller's own and is never read. A family of its own, beside the canonical
/// operations, the delivery contract, the events contract and the named preferences and never among them: a key is no
/// work anybody performs and no fact about the client, but part of one campaign's context, written by
/// <c>campaign.create</c> and <c>campaign.update_context</c> and checked there.
/// </summary>
/// <remarks>
/// Parsed as strictly as the other families: a field this build does not know is refused, and every field is required
/// but <c>distinct_by</c>, which only a list of objects may carry. The value schema is held to the dialect by the tests
/// rather than as the document loads, as a preference's is, because checking the dialect compiles every pattern a schema
/// writes and the command-line client loads this assembly too. A key's schema changes only by gaining an optional field,
/// which raises the document's version; anything else is a new key.
/// </remarks>
public sealed record ContextKeyContract(string Key, int Version, string Summary, JsonObject ValueSchema, string? DistinctBy)
{
    /// <summary>The longest key.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>
    /// The reason a list whose items share the member <see cref="DistinctBy"/> names is refused with: the dialect's own
    /// code for a list whose items must differ, said at the pointer of the second item's member.
    /// </summary>
    public const string UniqueItems = "unique_items";

    private const string Contract = "a reserved context key";

    private static readonly string[] Fields = ["key", "version", "summary", "value_schema", "distinct_by"];

    /// <summary>
    /// Whether <paramref name="key"/> is spelled as a reserved key is: one lower-case word of ASCII letters, digits and
    /// underscores, starting with a letter, at most <see cref="MaxKeyLength"/> characters. A context key is a top-level
    /// member of a JSON object, so it has no dots.
    /// </summary>
    public static bool IsWellFormedKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || key[0] is < 'a' or > 'z')
        {
            return false;
        }

        return key.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
    }

    /// <summary>
    /// One document, as published under <c>docs/contracts/context/&lt;key&gt;.json</c>. <paramref name="source"/> is the
    /// file's name, which has to be the key's.
    /// </summary>
    public static ContextKeyContract Parse(string source, JsonNode? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (document is not JsonObject fields)
        {
            throw new InvalidOperationException($"{source}: {Contract} is a JSON object.");
        }

        foreach (var (name, _) in fields)
        {
            if (!Fields.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{source}: `{name}` is not part of {Contract}.");
            }
        }

        JsonNode Required(string name) =>
            fields.TryGetPropertyValue(name, out var node) && node is not null
                ? node
                : throw new InvalidOperationException($"{source}: `{name}` is missing, and absence is not a default.");

        string Text(JsonNode node, string name) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text.Trim().Length > 0
                ? text
                : throw new InvalidOperationException($"{source}: `{name}` is a string with something in it.");

        var key = Text(Required("key"), "key");
        if (!IsWellFormedKey(key))
        {
            throw new InvalidOperationException(
                $"{source}: `{key}` is not a context key: one lower-case word of letters, digits and underscores, starting with a letter, at most {MaxKeyLength} characters.");
        }

        if (!string.Equals(source, key + ".json", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{source}: `{key}` is published in {source}, and a key's document is named for it: {key}.json.");
        }

        var version = Required("version") is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue(out int whole) && whole >= 1
            ? whole
            : throw new InvalidOperationException($"{source}: `version` is a whole number from 1.");

        var summary = Text(Required("summary"), "summary");

        var schema = Required("value_schema") as JsonObject
            ?? throw new InvalidOperationException($"{source}: `value_schema` is a schema, a JSON object.");

        string? distinctBy = null;
        if (fields.TryGetPropertyValue("distinct_by", out var member))
        {
            distinctBy = Text(member!, "distinct_by");
            var items = schema["items"] as JsonObject;
            var declared = (string?)schema["type"] == "array"
                && items is not null
                && (string?)items["type"] == "object"
                && items["required"] is JsonArray required
                && required.Any(name => name is JsonValue value && value.TryGetValue(out string? text) && text == distinctBy);
            if (!declared)
            {
                throw new InvalidOperationException(
                    $"{source}: `distinct_by` names a member every item of a list must carry, and `value_schema` is no list of objects that requires `{distinctBy}`.");
            }
        }

        return new ContextKeyContract(key, version, summary, schema, distinctBy);
    }

    /// <summary>What is wrong with a value somebody is writing under the key, against the key's published document.</summary>
    public IReadOnlyList<SchemaProblem> CheckValue(JsonNode? value) => CheckValue(value, PatternTiming.Default);

    /// <summary>
    /// The same check, with the schema's patterns matched under <paramref name="timing"/>. The schema decides first; only
    /// a value it accepts is read for items that share their <see cref="DistinctBy"/> member, each later one refused at
    /// that member's pointer, as <see cref="UniqueItems"/>.
    /// </summary>
    public IReadOnlyList<SchemaProblem> CheckValue(JsonNode? value, PatternTiming timing)
    {
        var problems = SchemaValidator.Validate(value, ValueSchema, timing);
        if (problems.Count > 0 || DistinctBy is null || value is not JsonArray list)
        {
            return problems;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var repeated = new List<SchemaProblem>();
        for (var index = 0; index < list.Count; index++)
        {
            var identity = list[index]![DistinctBy]!.ToJsonString();
            if (!seen.Add(identity))
            {
                // The reason is written out rather than named, so the scan that holds the code's reasons to the published
                // ones reads it; it is UniqueItems.
                var pointer = $"/{index}/{DistinctBy}";
                repeated.Add(new SchemaProblem(
                    pointer,
                    "unique_items",
                    $"`{DistinctBy}` names one item of the list, and an earlier item has the same."));
            }
        }

        return repeated;
    }
}
