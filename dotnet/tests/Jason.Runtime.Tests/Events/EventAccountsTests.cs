using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Events;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using static Jason.Runtime.Tests.Events.EventsBed;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// The accounts whose events are read are every account a route binds at a plugin that reads events, and a route that
/// names a channel under an operation binds an account like any other: a reply to a send that went out on that route is
/// read only if its account is.
/// </summary>
public class EventAccountsTests
{
    private const string Campaign = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string Send = "message.send";

    [Fact]
    public void An_account_a_channel_route_binds_is_read_at_global_and_at_campaign_scope()
    {
        var plugins = Plugins(EventsPlugin(), EventsPlugin("social"), TestPlugins.Loaded("silent", operations: [Send]));
        var global = new RouteSet(
            Bound(Reader, "a"),
            new Dictionary<string, Route>(StringComparer.Ordinal),
            OnLinkedIn(Bound("social", "g")));
        var campaign = new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
            {
                [Send] = new Dictionary<string, Route>(StringComparer.Ordinal)
                {
                    ["linkedin"] = Bound("social", "c"),
                    ["email"] = Bound("silent", "s"),
                },
            });
        var routes = new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Id,
            global,
            new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [Campaign] = campaign });

        var bound = EventAccounts.Bound(routes, plugins);

        // The global half first, its default, operations and then channel routes, then each campaign's; a plugin that reads
        // no events binds nothing to read.
        Assert.Equal(
            [(Reader, "sha256:a"), ("social", "sha256:g"), ("social", "sha256:c")],
            bound.Select(account => (account.PluginId, account.BindingIdentity)));
    }

    [Fact]
    public void An_account_bound_by_an_operation_route_and_a_channel_route_is_read_once()
    {
        var plugins = Plugins(EventsPlugin("social"));
        var global = new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Send] = Bound("social", "g") },
            OnLinkedIn(Bound("social", "g")));
        var routes = new RouteSnapshot(PublicId.New(RouteSnapshot.IdPrefix), Noon, plugins.Id, global, new Dictionary<string, RouteSet>(StringComparer.Ordinal));

        var account = Assert.Single(EventAccounts.Bound(routes, plugins));

        Assert.Equal(("social", "sha256:g"), (account.PluginId, account.BindingIdentity));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, Route>> OnLinkedIn(Route route) =>
        new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
        {
            [Send] = new Dictionary<string, Route>(StringComparer.Ordinal) { ["linkedin"] = route },
        };

    private static PluginSnapshot Plugins(params LoadedPlugin[] plugins) =>
        new(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, plugins);
}
