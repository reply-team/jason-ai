using Jason.Contracts.Api;

namespace Jason.Runtime.Routing;

/// <summary>
/// The precedence, and the one rule that surprises people:
/// <code>
/// campaign operation override → campaign default → global operation override → global default
/// </code>
/// <para>
/// <b>Masking is exact.</b> A campaign default hides <i>every</i> global operation override for that campaign;
/// keeping one global exception inside such a campaign takes an explicit campaign operation override. The
/// alternative — merging the levels — would mean a campaign that named its provider could still have one
/// operation quietly sent somewhere else, which is precisely the surprise routing exists to prevent. Bindings
/// are never merged either: the level that wins brings its own, or none.
/// </para>
/// <para>
/// This is a pure function over an immutable snapshot: no options read, no database, no clock. That is what
/// makes the precedence a unit test with no fixture, and what lets <c>route.resolve</c> answer exactly what the
/// claim will decide rather than something close to it.
/// </para>
/// </summary>
public static class RouteResolver
{
    /// <summary>The route that wins for this campaign and operation, and the level it won at; null when nothing routes it.</summary>
    public static Resolution? Resolve(RouteSnapshot snapshot, string campaignId, string operation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(campaignId);
        ArgumentNullException.ThrowIfNull(operation);

        if (snapshot.Campaigns.TryGetValue(campaignId, out var campaign))
        {
            if (campaign.Operations.TryGetValue(operation, out var campaignOperation))
            {
                return new Resolution(campaignOperation, RouteScope.CampaignOperation);
            }

            if (campaign.Default is { } campaignDefault)
            {
                return new Resolution(campaignDefault, RouteScope.CampaignDefault);
            }
        }

        if (snapshot.Global.Operations.TryGetValue(operation, out var globalOperation))
        {
            return new Resolution(globalOperation, RouteScope.GlobalOperation);
        }

        return snapshot.Global.Default is { } globalDefault
            ? new Resolution(globalDefault, RouteScope.GlobalDefault)
            : null;
    }

    /// <summary>
    /// The route of a read or a stop of one search: the route active now that names the plugin and the account the search
    /// ran under, since its reference means something only there. The operation's own route where it does, then the
    /// campaign's route for the search's own operation, then each of the campaign's routes and each global one, in the
    /// order of precedence; and the operation's own route where none names them any more, which the claim then refuses as
    /// made elsewhere. A binding is found by its identity, so the one found is the very binding the search ran under.
    /// </summary>
    public static Resolution? ForSearch(RouteSnapshot snapshot, string campaignId, string operation, string searchOperation, string pluginId, string? bindingIdentity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pluginId);

        var own = Resolve(snapshot, campaignId, operation);
        if (Names(own))
        {
            return own;
        }

        if (Resolve(snapshot, campaignId, searchOperation) is { } searched && Names(searched))
        {
            return searched;
        }

        var campaign = snapshot.Campaigns.TryGetValue(campaignId, out var held) ? held : RouteSet.Empty;
        IEnumerable<Resolution> every =
        [
            .. campaign.Operations.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new Resolution(entry.Value, RouteScope.CampaignOperation)),
            .. campaign.Default is { } campaignDefault ? [new Resolution(campaignDefault, RouteScope.CampaignDefault)] : Array.Empty<Resolution>(),
            .. snapshot.Global.Operations.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new Resolution(entry.Value, RouteScope.GlobalOperation)),
            .. snapshot.Global.Default is { } globalDefault ? [new Resolution(globalDefault, RouteScope.GlobalDefault)] : Array.Empty<Resolution>(),
        ];
        return every.FirstOrDefault(Names) ?? own;

        bool Names(Resolution? candidate) =>
            candidate is not null
            && string.Equals(candidate.Route.PluginId, pluginId, StringComparison.Ordinal)
            && string.Equals(candidate.Route.BindingIdentity, bindingIdentity, StringComparison.Ordinal);
    }
}
