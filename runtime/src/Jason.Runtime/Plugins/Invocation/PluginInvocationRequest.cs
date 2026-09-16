using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// A package a caller has already chosen, with the snapshot the choice was made from. It is what lets one
/// decision be acted on later: a route decided when work was claimed runs the package it decided on even if a
/// reload swaps the registry in between, and the provenance names the snapshot that was reasoned about rather
/// than whichever one happens to be active when the child starts.
/// </summary>
/// <remarks>
/// Pinning says which package runs, never that it may do more: the plugin's status, its kind and the operation
/// it offers are checked exactly as they are for a package looked up now, and the child recomputes the package's
/// digest before it runs a line of it, so a package edited after the decision is still refused by the child. A
/// running attempt therefore keeps the package it was claimed with; what should happen to work still queued
/// against a package a reload has changed or removed is deliberately left open.
/// </remarks>
public sealed record PinnedPlugin(LoadedPlugin Plugin, string SnapshotId);

/// <summary>
/// One thing a caller wants a plugin to do. The input is the operation's own arguments; the binding is the
/// caller's opaque note to the plugin; the correlation id is what ties the invocation to whatever caused it, and
/// it is one of only four values a process listing will show. <see cref="Pinned"/> is the package the caller has
/// already chosen; absent, the invoker chooses it from the active snapshot itself.
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
    TimeSpan? Timeout = null,
    PinnedPlugin? Pinned = null);
