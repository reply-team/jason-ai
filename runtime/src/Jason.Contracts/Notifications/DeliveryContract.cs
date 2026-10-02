using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Notifications;

/// <summary>
/// What a notification plugin is invoked for: one delivery contract, published beside the canonical operations and
/// never among them. It has none of an operation's level-1 fields — no reach, no approval, no preflight, no repeat
/// rule — because a delivery is not SDR work: it puts a pointer in front of a person and changes nothing anywhere.
/// </summary>
/// <remarks>
/// Parsed as strictly as an operation contract: every field is required, a field this build does not know is refused,
/// and both schemas are held to the dialect the validator publishes.
/// </remarks>
public sealed record DeliveryContract(
    string Id,
    int Version,
    string Intent,
    JsonObject InputSchema,
    JsonObject OutputSchema,
    IReadOnlyList<FailureCode> FailureCodes,
    int TimeoutMs,
    IReadOnlyList<string> Conformance)
{
    /// <summary>The version of the delivery contract this build invokes plugins for, and a delivery's envelope carries.</summary>
    public const int CurrentVersion = 1;

    private static readonly Dictionary<string, FailureClass> Classes = new(StringComparer.Ordinal)
    {
        ["transient"] = FailureClass.Transient,
        ["permanent"] = FailureClass.Permanent,
        ["validation"] = FailureClass.Validation,
        ["ambiguous"] = FailureClass.Ambiguous,
    };

    public static DeliveryContract Parse(string source, JsonNode? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (document is not JsonObject fields)
        {
            throw new InvalidOperationException($"{source}: a delivery contract is a JSON object.");
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
            var problems = SchemaValidator.CheckDialect(schema);
            return problems.Count == 0
                ? schema
                : throw new InvalidOperationException($"{source}: `{name}` is outside the published dialect: {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}.");
        }

        var contract = new DeliveryContract(
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
                throw new InvalidOperationException($"{source}: `{name}` is not part of a delivery contract.");
            }
        }

        return contract;
    }

    /// <summary>A delivery's answer against its output schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result) => SchemaValidator.Validate(result, OutputSchema);

    /// <summary>What is wrong with an input the runtime is about to hand a plugin, against the published input schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input) => SchemaValidator.Validate(input, InputSchema);

    /// <summary>A delivery pins nothing: any identifier beside its result is one nothing later knows how to read.</summary>
    public static IReadOnlyList<SchemaProblem> CheckExternalIds(JsonObject? externalIds)
    {
        var problems = new List<SchemaProblem>();
        foreach (var (kind, _) in externalIds ?? [])
        {
            var pointer = "/external_ids/" + kind.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            problems.Add(new SchemaProblem(pointer, "additional_properties", "A delivery declares no identifier: it puts a pointer in front of a person and pins nothing."));
        }

        return problems;
    }

    /// <summary>
    /// Whether a failed delivery is tried once more: only where it failed transiently. An ambiguous end is never
    /// repeated, because a pointer that may have arrived is not sent twice.
    /// </summary>
    public static bool Retriable(FailureClass failureClass) => failureClass == FailureClass.Transient;

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
