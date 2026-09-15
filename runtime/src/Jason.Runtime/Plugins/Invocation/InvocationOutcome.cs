using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// How an invocation ended, in the only three ways it can: the plugin answered, the plugin declared a failure,
/// or the protocol itself did not complete. The third is kept apart from the second on purpose — a protocol
/// failure is not the plugin's answer, it means the runtime never got one, and what may already have happened
/// at the provider is then a question of how far the invocation got.
/// </summary>
public abstract record InvocationOutcome
{
    /// <summary>The plugin ran and answered.</summary>
    public sealed record Succeeded(JsonNode? Result, JsonObject? ExternalIds, OutcomeDiagnostics Diagnostics) : InvocationOutcome;

    /// <summary>The plugin ran and said why it could not do the work. The class is what the runtime reads.</summary>
    public sealed record Failed(OutcomeError Error, OutcomeDiagnostics Diagnostics) : InvocationOutcome;

    /// <summary>
    /// The protocol did not complete: nothing the plugin may have said can be trusted. The code is one of
    /// <see cref="ProtocolCodes"/>, and its class comes from the same table every other failure is read through.
    /// </summary>
    public sealed record ProtocolFailure(string Code, string Message, int? ExitCode, string? StderrTail) : InvocationOutcome;
}
