using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Routing;

/// <summary>
/// What the claim resolved, carried in memory from the claim to the command that runs it. Everything a run needs
/// was decided inside the claim transaction and is frozen here: a reload that happens while the item is in the
/// handler pool changes nothing about the run that was already planned, because the plan holds the package and
/// the snapshot ids it was decided from rather than a way to look them up again.
/// </summary>
/// <param name="Plugin">The package that will perform the operation, as the active load left it.</param>
/// <param name="PluginSnapshotId">The plugin snapshot the package was taken from.</param>
/// <param name="RoutingSnapshotId">The route snapshot the decision was made against.</param>
/// <param name="Scope">The level the route won at, which is what says why this plugin and not another.</param>
/// <param name="Binding">Which account of that plugin to work through; opaque to the runtime, never a credential.</param>
/// <param name="BindingIdentity">The binding's hash, which is what an attempt records instead of the value.</param>
/// <param name="Contract">The operation's published contract: the budget, the schemas and the repeat rule.</param>
/// <param name="Input">The document the plugin receives, composed and already validated against that contract.</param>
public sealed record ProviderOpPlan(
    LoadedPlugin Plugin,
    string PluginSnapshotId,
    string RoutingSnapshotId,
    RouteScope Scope,
    JsonObject? Binding,
    string? BindingIdentity,
    OperationContract Contract,
    JsonObject Input);
