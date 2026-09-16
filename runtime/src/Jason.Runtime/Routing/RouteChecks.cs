using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Routing;

/// <summary>
/// The questions both gates ask of a route, spelled once. Activation asks them of every route a load would
/// freeze; the claim asks them again of the one route an item resolved to, because the plan a run is handed has
/// to be true of the package in front of it rather than of the one the route was written against.
/// <para>
/// Each is a predicate rather than a code. The two callers answer in different vocabularies on purpose — a route
/// problem is read by an operator repairing an installation, an attempt error by a manager reading why one item
/// failed — and only the sentence they read should differ. What may never differ is the verdict.
/// </para>
/// <para>
/// The order these are composed in belongs to each caller, and neither order is arbitrary. Both ask about the
/// kind before the operation, because a plugin that only notifies lists no operation at all and would otherwise
/// be reported for what is missing rather than for what it is. Activation alone asks whether a binding reads
/// like a credential, and asks it before the schema, because a plugin's schema will usually refuse an unexpected
/// field too and "this looks like a credential" is the sentence whoever wrote it needs to read. The claim alone
/// asks whether the plugin is usable on this machine: a missing program is an environment's problem, and
/// refusing every route to that plugin would turn it into a rejected reload.
/// </para>
/// </summary>
public static class RouteChecks
{
    /// <summary>
    /// The plugin a route names, or null when the set holds none. Everything below rests on the answer, which is
    /// why it is the first thing either gate asks.
    /// </summary>
    public static LoadedPlugin? Loaded(PluginSnapshot plugins, string? pluginId)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        return string.IsNullOrWhiteSpace(pluginId) ? null : plugins.Find(pluginId);
    }

    /// <summary>Installed and listed, but not usable on this machine: a load's verdict about the environment.</summary>
    public static bool Unavailable(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return plugin.Status == PluginStatus.Unavailable;
    }

    /// <summary>A kind nothing hands a canonical operation to: a notification plugin performs none by construction.</summary>
    public static bool KindNotInvocable(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return plugin.Manifest.Kind != PluginKind.Provider;
    }

    /// <summary>The plugin never claimed the operation, and work is never handed to a plugin that did not claim it.</summary>
    public static bool OperationUnsupported(LoadedPlugin plugin, string operation)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return !plugin.Supports(operation);
    }

    /// <summary>The plugin speaks no version of the operation contract that this build publishes the operation at.</summary>
    public static bool ContractIncompatible(LoadedPlugin plugin, OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(contract);

        return !plugin.Manifest.Contracts.Operations.Contains(contract.Version);
    }

    /// <summary>
    /// What the plugin's own binding schema makes of this binding: empty when it objects to nothing, which
    /// includes a plugin that declares no schema and therefore asks for nothing. A route carrying no binding at
    /// all is measured too — a plugin whose schema requires one is unusable without it.
    /// </summary>
    public static IReadOnlyList<SchemaProblem> BindingProblems(LoadedPlugin plugin, JsonObject? binding)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return plugin.Manifest.Binding is { } schema ? SchemaValidator.Validate(binding, schema) : [];
    }
}
