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

    public static DeliveryContract Parse(string source, JsonNode? document)
    {
        var fields = FamilyDocument.Parse(source, document, "a delivery contract", checkDialect: true);
        return new DeliveryContract(
            fields.Id,
            fields.Version,
            fields.Intent,
            fields.InputSchema,
            fields.OutputSchema,
            fields.FailureCodes,
            fields.TimeoutMs,
            fields.Conformance);
    }

    /// <summary>A delivery's answer against its output schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result) => CheckResult(result, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result, PatternTiming timing) => SchemaValidator.Validate(result, OutputSchema, timing);

    /// <summary>What is wrong with an input the runtime is about to hand a plugin, against the published input schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input) => CheckInput(input, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input, PatternTiming timing) => SchemaValidator.Validate(input, InputSchema, timing);

    /// <summary>A delivery pins nothing: any identifier beside its result is one nothing later knows how to read.</summary>
    public static IReadOnlyList<SchemaProblem> CheckExternalIds(JsonObject? externalIds) =>
        FamilyDocument.Unpinned(externalIds, "A delivery declares no identifier: it puts a pointer in front of a person and pins nothing.");

    /// <summary>
    /// Whether a failed delivery is tried once more: only where it failed transiently. An ambiguous end is never
    /// repeated, because a pointer that may have arrived is not sent twice.
    /// </summary>
    public static bool Retriable(FailureClass failureClass) => failureClass == FailureClass.Transient;
}
