using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Execution;

/// <summary>
/// The provenance of one attempt: written at claim from the decision that was just made. Nothing else ever
/// writes it, and nothing rewrites what the claim recorded — an attempt is a record of what happened, so a route
/// changed, a plugin reloaded or a package edited afterwards leaves it exactly as it stands.
/// </summary>
public static class AttemptProvenance
{
    /// <summary>Everything a claim-time record can say before the invocation exists.</summary>
    private static readonly AttemptProvenanceDto Unknown =
        new(null, null, null, null, null, null, null, null, null, null, null);

    /// <summary>
    /// What the claim resolved, as far as it got. A verdict that refused the item still names the plugin it
    /// would have used, because "the plugin was unavailable" is only actionable next to the plugin's name; where
    /// the refusal came before anything was routed, the nulls say exactly that.
    /// </summary>
    /// <param name="operation">The operation the item names, which is known even where no contract publishes it.</param>
    /// <param name="verdict">The pre-flight's decision, carrying the plan or the resolution it reached.</param>
    /// <param name="plugins">The plugin snapshot the decision was made against.</param>
    /// <param name="routes">The route snapshot the decision was made against.</param>
    /// <param name="correlationId">The attempt's own public id: what ties an invocation back to the work.</param>
    public static AttemptProvenanceDto AtClaim(
        string? operation,
        PreflightVerdict verdict,
        PluginSnapshot plugins,
        RouteSnapshot routes,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(routes);

        var contract = operation is null ? null : OperationCatalog.Find(operation);
        var route = verdict.Resolution?.Route;

        // The package as the same frozen snapshot holds it: a lookup by the id the resolution already chose,
        // never a second decision about which plugin this is.
        var plugin = route is null ? null : plugins.Find(route.PluginId);
        return Unknown with
        {
            PluginId = route?.PluginId,
            PluginVersion = plugin?.Manifest.Version,
            PluginDigest = plugin?.Digest,

            // The protocol a child would have been started under, named only where there is a package to start.
            ProtocolVersion = plugin is null ? null : PluginProtocol.CurrentVersion,
            OperationContractVersion = plugin is null ? null : PluginProtocol.OperationContractVersion,
            Operation = operation,
            OperationVersion = contract?.Version,
            PluginSnapshotId = plugins.Id,
            RoutingSnapshotId = routes.Id,
            RouteScope = verdict.Resolution?.Scope,
            BindingIdentity = route?.BindingIdentity,
            CorrelationId = correlationId,
        };
    }
}
