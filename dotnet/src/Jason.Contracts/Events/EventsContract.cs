using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Events;

/// <summary>
/// What a provider plugin that declares the events contract is invoked for: a read of what happened in one account
/// after the point the plugin's own cursor names. A family of its own, beside the canonical operations and the delivery
/// contract and never among them: a read of an account belongs to no campaign, so it is no work a work item could name,
/// and it puts nothing in front of a person.
/// </summary>
/// <remarks>
/// Parsed as strictly as an operation contract: every field is required and a field this build does not know is
/// refused. Its schemas carry patterns — an identifier and a cursor are printable ASCII with no space — so, as the
/// operation catalog's, they are held to the dialect by the tests rather than as the document loads: checking the
/// dialect compiles every pattern, and the command-line client loads this assembly too.
/// </remarks>
public sealed record EventsContract(
    string Id,
    int Version,
    string Intent,
    JsonObject InputSchema,
    JsonObject OutputSchema,
    IReadOnlyList<FailureCode> FailureCodes,
    int TimeoutMs,
    IReadOnlyList<string> Conformance)
{
    /// <summary>The version of the events contract this build invokes plugins for, and a read's envelope carries.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The kinds of event the output publishes, in the order the document lists them: the <c>const</c> of each branch the
    /// event's <c>anyOf</c> refers to. Read from the document, so nothing else in the build lists them a second time.
    /// </summary>
    public IReadOnlyList<string> Kinds { get; } = ReadKinds(OutputSchema);

    public static EventsContract Parse(string source, JsonNode? document)
    {
        var fields = FamilyDocument.Parse(source, document, "an events contract", checkDialect: false);
        return new EventsContract(
            fields.Id,
            fields.Version,
            fields.Intent,
            fields.InputSchema,
            fields.OutputSchema,
            fields.FailureCodes,
            fields.TimeoutMs,
            fields.Conformance);
    }

    /// <summary>A read's answer against its output schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result) => CheckResult(result, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result, PatternTiming timing) => SchemaValidator.Validate(result, OutputSchema, timing);

    /// <summary>What is wrong with an input the runtime is about to hand a plugin, against the published input schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input) => CheckInput(input, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input, PatternTiming timing) => SchemaValidator.Validate(input, InputSchema, timing);

    /// <summary>A read pins nothing: it finds the pins operations wrote, and an identifier beside its result is refused.</summary>
    public static IReadOnlyList<SchemaProblem> CheckExternalIds(JsonObject? externalIds) =>
        FamilyDocument.Unpinned(externalIds, "An events read declares no identifier: it finds the pins operations wrote and writes none.");

    /// <summary>
    /// Whether a read that failed is read again at the doubled cadence rather than after a day. A read changes nothing
    /// at the provider, so whatever may pass by itself is tried again: a transient failure, and an ambiguous one, which an
    /// answer outside the contract is too. A permanent or a validation failure waits the day.
    /// </summary>
    public static bool Retriable(FailureClass failureClass) => failureClass is FailureClass.Transient or FailureClass.Ambiguous;

    private static List<string> ReadKinds(JsonObject output)
    {
        var definitions = output["$defs"] as JsonObject;
        var branches = definitions?["event"]?["anyOf"] as JsonArray ?? [];
        var kinds = new List<string>();
        foreach (var branch in branches)
        {
            var reference = (string?)branch?["$ref"];
            var name = reference is { } text && text.StartsWith("#/$defs/", StringComparison.Ordinal) ? text["#/$defs/".Length..] : null;
            if (name is not null && definitions?[name]?["properties"]?["kind"]?["const"] is JsonValue kind && kind.TryGetValue(out string? word))
            {
                kinds.Add(word);
            }
        }

        return kinds;
    }
}
