using System.Text.Json.Nodes;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// One thing a caller wants a plugin to do. The input is the operation's own arguments; the binding is the
/// caller's opaque note to the plugin; the correlation id is what ties the invocation to whatever caused it, and
/// it is one of only four values a process listing will show.
/// </summary>
/// <remarks>
/// Nothing here carries a secret. A plugin reaches its credentials through the environment variables the user
/// granted it, which the runtime places in the child's environment by name and never writes into an envelope.
/// </remarks>
public sealed record PluginInvocationRequest(
    string PluginId,
    string Operation,
    JsonObject Input,
    JsonObject? Binding,
    string CorrelationId,
    string? AttemptId = null,
    int? AttemptNumber = null,
    string? WorkItemId = null,
    string? CampaignId = null,
    TimeSpan? Timeout = null);
