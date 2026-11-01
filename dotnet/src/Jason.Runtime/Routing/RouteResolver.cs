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
}
