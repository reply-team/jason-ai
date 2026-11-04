using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Plugins;

/// <summary>
/// Why an invocation failed, in the plugin's own vocabulary under one of the runtime's five classes. The class is
/// what the runtime reads; the code is what a person reads.
/// </summary>
/// <remarks>
/// <c>ExternalIds</c> is a JSON object rather than a dictionary on purpose: its keys are the provider's names for
/// its own identifiers and must travel exactly as the plugin wrote them, while a dictionary's keys would be
/// rewritten by the snake_case policy that governs the runtime's own members. <c>Block</c> is present exactly when
/// the class is <see cref="FailureClass.Blocked"/>, and says what is blocked in the words of
/// <see cref="ProviderBlockContract"/>.
/// </remarks>
public sealed record OutcomeError(
    FailureClass Class,
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? Details = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? ExternalIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? Block = null);

/// <summary>
/// What the invocation cost, counted by the host rather than claimed by the plugin. <c>ApiCalls</c> counts the Runtime
/// API calls a handler made through its door, and is left out where there were none.
/// </summary>
public sealed record OutcomeDiagnostics(
    long DurationMs,
    int ExecCalls,
    int HttpCalls,
    int LogLines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ApiCalls = 0);

/// <summary>
/// The one JSON object the child writes to stdout. Stdout belongs to the host alone — a plugin has no
/// <c>console</c> — so plugin text can never corrupt the channel.
/// </summary>
/// <remarks>
/// <c>ExternalIdOrigins</c> says, for some of the identifiers in <c>ExternalIds</c>, whether the provider found the entity
/// or created it (<see cref="Plugins.ExternalIdOrigins"/>). It travels on a succeeded outcome only and is left out where
/// the plugin said nothing, so an outcome without it reads exactly as it did before the key existed.
/// </remarks>
public sealed record PluginOutcome(
    int ProtocolVersion,
    string InvocationId,
    OutcomeStatus Status,
    JsonNode? Result,
    JsonObject? ExternalIds,
    OutcomeError? Error,
    OutcomeDiagnostics Diagnostics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? ExternalIdOrigins = null);
