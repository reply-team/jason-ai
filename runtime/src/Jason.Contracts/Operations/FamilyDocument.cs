using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Operations;

/// <summary>
/// The eight fields a contract family of its own is published with — the delivery contract and the events contract —
/// beside the canonical operations and never among them. Neither has an operation's level-1 fields, because neither is
/// SDR work a work item names.
/// </summary>
/// <remarks>
/// Read as strictly as an operation contract: every field is required and a field the family does not know is refused.
/// Each family passes what it calls its document — "a delivery contract", "an events contract" — so a refusal names the
/// right thing, and whether its schemas are held to the dialect as the document loads. Checking the dialect compiles
/// every pattern a schema writes, which the command-line client, loading this assembly, must never pay for: a family
/// whose schemas carry patterns is held to the dialect by the tests instead, as the operation catalog is.
/// </remarks>
internal sealed record FamilyDocument(
    string Id,
    int Version,
    string Intent,
    JsonObject InputSchema,
    JsonObject OutputSchema,
    IReadOnlyList<FailureCode> FailureCodes,
    int TimeoutMs,
    IReadOnlyList<string> Conformance)
{
    private static readonly Dictionary<string, FailureClass> Classes = new(StringComparer.Ordinal)
    {
        ["transient"] = FailureClass.Transient,
        ["permanent"] = FailureClass.Permanent,
        ["validation"] = FailureClass.Validation,
        ["ambiguous"] = FailureClass.Ambiguous,
    };

    public static FamilyDocument Parse(string source, JsonNode? document, string contract, bool checkDialect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        if (document is not JsonObject fields)
        {
            throw new InvalidOperationException($"{source}: {contract} is a JSON object.");
        }

        var read = new HashSet<string>(StringComparer.Ordinal);
        JsonNode Required(string name)
        {
            read.Add(name);
            return fields.TryGetPropertyValue(name, out var node) && node is not null
                ? node
                : throw new InvalidOperationException($"{source}: `{name}` is missing, and absence is not a default.");
        }

        string Text(string name) => Required(name) is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text)
            ? text
            : throw new InvalidOperationException($"{source}: `{name}` is a string.");

        int Integer(string name) => Required(name) is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int number)
            ? number
            : throw new InvalidOperationException($"{source}: `{name}` is a whole number.");

        JsonObject Schema(string name)
        {
            var schema = Required(name) as JsonObject ?? throw new InvalidOperationException($"{source}: `{name}` is an object.");
            if (!checkDialect)
            {
                return schema;
            }

            var problems = SchemaValidator.CheckDialect(schema);
            return problems.Count == 0
                ? schema
                : throw new InvalidOperationException($"{source}: `{name}` is outside the published dialect: {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}.");
        }

        var parsed = new FamilyDocument(
            Text("id"),
            Integer("version"),
            Text("intent"),
            Schema("input_schema"),
            Schema("output_schema"),
            ReadFailureCodes(source, Required("failure_codes")),
            Integer("timeout_ms"),
            ReadTexts(source, "conformance", Required("conformance")));

        foreach (var (name, _) in fields)
        {
            if (!read.Contains(name))
            {
                throw new InvalidOperationException($"{source}: `{name}` is not part of {contract}.");
            }
        }

        return parsed;
    }

    /// <summary>The problems an answer carrying identifiers has, one per kind: a family that declares none pins nothing.</summary>
    public static IReadOnlyList<SchemaProblem> Unpinned(JsonObject? externalIds, string message)
    {
        var problems = new List<SchemaProblem>();
        foreach (var (kind, _) in externalIds ?? [])
        {
            var pointer = "/external_ids/" + kind.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            problems.Add(new SchemaProblem(pointer, "additional_properties", message));
        }

        return problems;
    }

    private static List<FailureCode> ReadFailureCodes(string source, JsonNode node)
    {
        var codes = new List<FailureCode>();
        var entries = node as JsonArray ?? throw new InvalidOperationException($"{source}: `failure_codes` is an array.");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] as JsonObject
                ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{source}: `failure_codes[{index}]` is an object."));
            if (entry.Count != 3
                || entry["code"] is not JsonValue code || !code.TryGetValue(out string? text)
                || entry["class"] is not JsonValue @class || !@class.TryGetValue(out string? word) || !Classes.TryGetValue(word, out var failure)
                || entry["when"] is not JsonValue when || !when.TryGetValue(out string? condition))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{source}: `failure_codes[{index}]` is a code, one of {string.Join(", ", Classes.Keys)}, and when it is answered."));
            }

            codes.Add(new FailureCode(text, failure, condition));
        }

        return codes;
    }

    private static List<string> ReadTexts(string source, string name, JsonNode node) =>
        [
            .. (node as JsonArray ?? throw new InvalidOperationException($"{source}: `{name}` is an array.")).Select(entry =>
                entry is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text)
                    ? text
                    : throw new InvalidOperationException($"{source}: `{name}` is an array of strings.")),
        ];
}
