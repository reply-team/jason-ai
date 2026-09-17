using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>What only the invocation itself can know, and what the claim therefore left blank.</summary>
/// <param name="InvocationId">The id the invoker gave the child, which is how its diagnostics are found again.</param>
/// <param name="Diagnostics">What the run cost: how long it took and how much of each capability it used.</param>
/// <param name="ExternalIdsReturned">The identifiers the plugin answered with, as it returned them.</param>
/// <param name="RejectedResult">An answer the operation's schema refused, kept so its author can see it.</param>
public sealed record InvocationRecord(
    string? InvocationId = null,
    OutcomeDiagnostics? Diagnostics = null,
    IReadOnlyDictionary<string, string>? ExternalIdsReturned = null,
    JsonNode? RejectedResult = null);

/// <summary>
/// The provenance of one attempt: written at claim from the decision that was just made, and completed once the
/// invocation has ended. Nothing else ever writes it, and nothing rewrites what the claim recorded — an attempt
/// is a record of what happened, so a route changed, a plugin reloaded or a package edited afterwards leaves it
/// exactly as it stands.
/// </summary>
public static class AttemptProvenance
{
    /// <summary>The path a rejected answer is written at, spelled as SQLite's JSON functions address it.</summary>
    private const string RejectedResultPath = "$.rejected_result";

    /// <summary>
    /// The fields a completion merges, and nothing else: serialised without its nulls so that what is merged
    /// into the stored record names only what the invocation learned. The names come from the DTO itself, so the
    /// patch and the record it is merged into cannot come to spell a field differently.
    /// </summary>
    private static readonly JsonSerializerOptions OnlyWhatIsKnown =
        new(JasonJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

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
    /// <param name="approvalId">The decision a person made about this subject, where the operation needed one.</param>
    public static AttemptProvenanceDto AtClaim(
        string? operation,
        PreflightVerdict verdict,
        PluginSnapshot plugins,
        RouteSnapshot routes,
        string correlationId,
        string? approvalId = null)
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
            ApprovalId = approvalId,
        };
    }

    /// <summary>
    /// Adds what the invocation learned to the record the claim already wrote, through one guarded UPDATE: the
    /// attempt's public id is the fencing token, the merge touches only the fields the completion names, and
    /// nothing is read back first — a lease enforcer may be writing the same row, and reading a row to write it
    /// again is how one writer silently undoes another.
    /// </summary>
    /// <returns>
    /// False where there was nothing to complete: the attempt is gone, or it never had a claim-time record,
    /// which is not a failure of the work but the reason its provenance stays as it is.
    /// </returns>
    public static async Task<bool> CompleteAsync(
        JasonDbContext db,
        string attemptId,
        InvocationRecord invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(invocation);

        var patch = JsonSerializer.Serialize(
            Unknown with
            {
                InvocationId = invocation.InvocationId,
                Diagnostics = invocation.Diagnostics,
                ExternalIdsReturned = invocation.ExternalIdsReturned,
            },
            OnlyWhatIsKnown);

        // A merge patch reads a null as "remove this key", which is the right reading for the fields above —
        // none of them can be null and still mean something — and the wrong one for a document a plugin wrote,
        // where a null is a value it sent. The rejected answer is therefore set whole at its own path, so what
        // is kept as evidence is what actually arrived rather than what survived a merge.
        var statement = invocation.RejectedResult is { } rejected
            ? (FormattableString)$"""
               UPDATE attempts
                  SET provenance_json = json_set(json_patch(provenance_json, {patch}), {RejectedResultPath}, json({rejected.ToJsonString()}))
                WHERE public_id = {attemptId} AND provenance_json IS NOT NULL
               """
            : $"""
               UPDATE attempts
                  SET provenance_json = json_patch(provenance_json, {patch})
                WHERE public_id = {attemptId} AND provenance_json IS NOT NULL
               """;

        var touched = await db.Database.ExecuteSqlInterpolatedAsync(statement, cancellationToken).ConfigureAwait(false);
        return touched == 1;
    }
}
