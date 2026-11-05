using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Subscriptions;

/// <summary>
/// What a plugin that subscribes to a journal kind is invoked for: one line of that kind, after the runtime wrote it,
/// with the envelope the previous handler of the chain returned, answered by the envelope for the next. A family of its
/// own, beside the canonical operations, the delivery contract and the events contract and never among them: the line
/// is already written and what it records already done, so a handler is no work a work item could name, and nothing it
/// answers changes what the runtime did.
/// </summary>
/// <remarks>
/// Parsed as strictly as an operation contract: every field is required, a field this build does not know is refused,
/// and both schemas are held to the dialect the validator publishes. The operation word a handler is invoked under is
/// the line's kind, not this document's id; the id names the contract, as <c>notification.send</c> names the delivery's.
/// </remarks>
public sealed record SubscriptionContract(
    string Id,
    int Version,
    string Intent,
    JsonObject InputSchema,
    JsonObject OutputSchema,
    IReadOnlyList<FailureCode> FailureCodes,
    int TimeoutMs,
    IReadOnlyList<string> Conformance)
{
    /// <summary>The version of the subscription contract this build invokes handlers for, and an invocation's envelope carries.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The code the runtime records against a handler whose answer the output schema refuses, or that pins an identifier.</summary>
    public const string AnswerInvalid = "subscription_answer_invalid";

    /// <summary>The code the runtime records against a handler whose envelope is over the bound <c>Subscriptions:EnvelopeBytes</c> sets.</summary>
    public const string EnvelopeTooLarge = "subscription_envelope_too_large";

    public static SubscriptionContract Parse(string source, JsonNode? document)
    {
        var fields = FamilyDocument.Parse(source, document, "a subscription contract", checkDialect: true);
        return new SubscriptionContract(
            fields.Id,
            fields.Version,
            fields.Intent,
            fields.InputSchema,
            fields.OutputSchema,
            fields.FailureCodes,
            fields.TimeoutMs,
            fields.Conformance);
    }

    /// <summary>A handler's answer against the output schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result) => CheckResult(result, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckResult(JsonNode? result, PatternTiming timing) => SchemaValidator.Validate(result, OutputSchema, timing);

    /// <summary>What is wrong with an input the runtime is about to hand a handler, against the published input schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input) => CheckInput(input, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input, PatternTiming timing) => SchemaValidator.Validate(input, InputSchema, timing);

    /// <summary>A handler pins nothing: any identifier beside its answer is refused, whatever its kind.</summary>
    public static IReadOnlyList<SchemaProblem> CheckExternalIds(JsonObject? externalIds) =>
        FamilyDocument.Unpinned(externalIds, "A subscription handler declares no identifier: it acts after the fact and pins nothing.");

    /// <summary>
    /// Whether a handler that failed is invoked again for the same line: only where it failed transiently, with the
    /// envelope as it stood before it. Every other class ends the chain at that handler.
    /// </summary>
    public static bool Retriable(FailureClass failureClass) => failureClass == FailureClass.Transient;
}
