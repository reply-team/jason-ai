using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Plugins;

/// <summary>
/// Why an invocation failed, in the plugin's own vocabulary under one of the runtime's four classes. The class is
/// what the runtime reads; the code is what a person reads.
/// </summary>
public sealed record OutcomeError(
    FailureClass Class,
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? Details,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? ExternalIds);

/// <summary>What the invocation cost, counted by the host rather than claimed by the plugin.</summary>
public sealed record OutcomeDiagnostics(long DurationMs, int ExecCalls, int HttpCalls, int LogLines);

/// <summary>
/// The one JSON object the child writes to stdout. Stdout belongs to the host alone — a plugin has no
/// <c>console</c> — so plugin text can never corrupt the channel.
/// </summary>
public sealed record PluginOutcome(
    int ProtocolVersion,
    string InvocationId,
    OutcomeStatus Status,
    JsonNode? Result,
    IReadOnlyDictionary<string, string>? ExternalIds,
    OutcomeError? Error,
    OutcomeDiagnostics Diagnostics);
