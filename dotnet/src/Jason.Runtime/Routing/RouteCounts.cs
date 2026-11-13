namespace Jason.Runtime.Routing;

/// <summary>
/// How many routes a snapshot holds, counted one way for every place that says it: the runtime's report of itself and the
/// journal line of a reload. A channel route is a route like the others, so it is counted with the scope it sits in — a
/// global one among the global overrides, a campaign's among that campaign's routes — and counted again, on its own, as a
/// channel route.
/// </summary>
public static class RouteCounts
{
    /// <summary>The global routes under an operation: the operation's own overrides and the channel routes beneath them.</summary>
    public static int GlobalOverrides(RouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Global.Operations.Count + snapshot.Global.ChannelRoutes.Count();
    }

    /// <summary>Every campaign's routes: each default, each operation's own route and each channel route.</summary>
    public static int CampaignRoutes(RouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Campaigns.Values.Sum(set => set.Operations.Count + set.ChannelRoutes.Count() + (set.Default is null ? 0 : 1));
    }

    /// <summary>The routes that name a channel, global and campaign together.</summary>
    public static int ChannelRoutes(RouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Global.ChannelRoutes.Count() + snapshot.Campaigns.Values.Sum(set => set.ChannelRoutes.Count());
    }
}
