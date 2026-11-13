using Jason.Contracts.Api;

namespace Jason.Runtime.Routing;

/// <summary>
/// The precedence, and the one rule that surprises people:
/// <code>
/// campaign operation and channel → campaign operation override → campaign default
///   → global operation and channel → global operation override → global default
/// </code>
/// <para>
/// A channel level answers only a call that names a channel: the caller reads it from the argument the operation's
/// document names (<c>route_channel_argument</c>), and a call that names none — an operation that names no such
/// argument, or a filter left out — is routed by its operation alone, never by a channel route.
/// </para>
/// <para>
/// <b>Masking is exact.</b> A campaign default hides <i>every</i> global route for that campaign, the channel routes
/// included; keeping one global exception inside such a campaign takes an explicit campaign route. The
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
    /// <summary>
    /// The route that wins for this campaign, operation and channel, and the level it won at; null when nothing routes it.
    /// <paramref name="channel"/> is the channel the call names for its route, and null where it names none. It is
    /// required, so every caller says which: a channel route is never taken by a call that forgot to name its channel.
    /// </summary>
    public static Resolution? Resolve(RouteSnapshot snapshot, string campaignId, string operation, string? channel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(campaignId);
        ArgumentNullException.ThrowIfNull(operation);

        if (snapshot.Campaigns.TryGetValue(campaignId, out var campaign))
        {
            if (campaign.ChannelRoute(operation, channel) is { } campaignChannel)
            {
                return new Resolution(campaignChannel, RouteScope.CampaignChannel);
            }

            if (campaign.Operations.TryGetValue(operation, out var campaignOperation))
            {
                return new Resolution(campaignOperation, RouteScope.CampaignOperation);
            }

            if (campaign.Default is { } campaignDefault)
            {
                return new Resolution(campaignDefault, RouteScope.CampaignDefault);
            }
        }

        if (snapshot.Global.ChannelRoute(operation, channel) is { } globalChannel)
        {
            return new Resolution(globalChannel, RouteScope.GlobalChannel);
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
    /// <para>
    /// A search names no channel, so its own two routes are resolved without one. The walk of every route includes the
    /// channel routes, since a route that names the account is one wherever it sits: at each scope its channel routes
    /// (operations ordinally, then channels ordinally, <see cref="RouteSet.ChannelRoutes"/>), its operation routes
    /// (ordinally), then its default — the campaign's scope before the global one.
    /// </para>
    /// </summary>
    public static Resolution? ForSearch(RouteSnapshot snapshot, string campaignId, string operation, string searchOperation, string pluginId, string? bindingIdentity) =>
        WhereItRan(snapshot, campaignId, operation, searchOperation, madeChannel: null, pluginId, bindingIdentity);

    /// <summary>
    /// The route of a read or a cancel of one send, by the rule a search has (<see cref="ForSearch"/>): the route active now
    /// that names the plugin and the account the send ran under, since its reference is that account's. The operation's own
    /// route where it does, then the campaign's route for the send's own operation by the channel the send was routed by
    /// (<paramref name="sendChannel"/>, null where it named none), then every route in the order of precedence, channel
    /// routes included; and the operation's own route where none names them any more, which the fourteenth check then
    /// refuses as made elsewhere. So a campaign that sends email through one plugin and LinkedIn messages through another
    /// cancels and reads each send where it went.
    /// </summary>
    public static Resolution? ForSend(RouteSnapshot snapshot, string campaignId, string operation, string sendOperation, string? sendChannel, string pluginId, string? bindingIdentity) =>
        WhereItRan(snapshot, campaignId, operation, sendOperation, sendChannel, pluginId, bindingIdentity);

    /// <summary>
    /// The one rule behind <see cref="ForSearch"/> and <see cref="ForSend"/>: the route that names the plugin and the
    /// account the work named by its reference ran under, by the work's own route first, then the route of the operation
    /// that made it, then every route in the order of precedence, else the work's own route.
    /// </summary>
    private static Resolution? WhereItRan(RouteSnapshot snapshot, string campaignId, string operation, string madeOperation, string? madeChannel, string pluginId, string? bindingIdentity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pluginId);

        var own = Resolve(snapshot, campaignId, operation, channel: null);
        if (Names(own))
        {
            return own;
        }

        if (Resolve(snapshot, campaignId, madeOperation, madeChannel) is { } made && Names(made))
        {
            return made;
        }

        var campaign = snapshot.Campaigns.TryGetValue(campaignId, out var held) ? held : RouteSet.Empty;
        IEnumerable<Resolution> every =
        [
            .. campaign.ChannelRoutes.Select(entry => new Resolution(entry.Route, RouteScope.CampaignChannel)),
            .. campaign.Operations.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new Resolution(entry.Value, RouteScope.CampaignOperation)),
            .. campaign.Default is { } campaignDefault ? [new Resolution(campaignDefault, RouteScope.CampaignDefault)] : Array.Empty<Resolution>(),
            .. snapshot.Global.ChannelRoutes.Select(entry => new Resolution(entry.Route, RouteScope.GlobalChannel)),
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
