using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A read of a thread goes where the thread was seen: for each plugin and account the campaign's lines say, newest first,
/// the read's own route where it names them, then the campaign's route for the list read, then every route in the order of
/// precedence, channel routes included; the first sighting a route names wins, and where none is named the read's own
/// route answers, for the claim to refuse.
/// </summary>
public class ThreadRouteResolverTests
{
    private const string Campaign = "cmp_01K5AAAAAAAAAAAAAAAAAAAAAA";
    private const string Get = "conversation.get";
    private const string List = "conversation.list";

    private static readonly ThreadSeen AtSocial = new("social", "sha256:social", AccountKnown: true);

    [Fact]
    public void The_read_s_own_route_answers_where_it_names_the_account_the_thread_was_seen_at()
    {
        var snapshot = Snapshot(new RouteSet(
            Route("mailer"),
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("social"), [List] = Route("social") }));

        var resolution = RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial]);

        Assert.Equal(("social", RouteScope.CampaignOperation), (resolution!.Route.PluginId, resolution.Scope));
        Assert.Same(snapshot.Campaigns[Campaign].Operations[Get], resolution.Route);
    }

    [Fact]
    public void The_list_read_s_route_answers_next()
    {
        var snapshot = Snapshot(new RouteSet(
            Route("social"),
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer") }));

        var resolution = RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial]);

        Assert.Equal(("social", RouteScope.CampaignDefault), (resolution!.Route.PluginId, resolution.Scope));
    }

    /// <summary>A route of another operation, a channel route, still names the account: the read goes by it.</summary>
    [Fact]
    public void Then_every_route_channel_routes_included()
    {
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer"), [List] = Route("mailer"), ["message.send"] = Route("mailer") },
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
            {
                ["message.send"] = new Dictionary<string, Route>(StringComparer.Ordinal) { ["linkedin"] = Route("social") },
            }));

        var resolution = RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial]);

        Assert.Equal(("social", "sha256:social", RouteScope.CampaignChannel), (resolution!.Route.PluginId, resolution.Route.BindingIdentity, resolution.Scope));
    }

    /// <summary>Where no route names the plugin and account any more, the read's own route answers, and the claim refuses it.</summary>
    [Fact]
    public void Otherwise_the_read_s_own_route()
    {
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer"), ["message.send"] = Route("social", "sha256:another-seat") }));

        var resolution = RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial]);

        Assert.Equal(("mailer", RouteScope.CampaignOperation), (resolution!.Route.PluginId, resolution.Scope));
        Assert.False(AtSocial.Names(resolution.Route));
    }

    /// <summary>
    /// A thread seen at two accounts over time is read at the newer, even though the read's own route names the older: the
    /// sightings are asked in turn, and every step is asked of the newest before the older is asked at all.
    /// </summary>
    [Fact]
    public void A_thread_seen_at_two_accounts_over_time_is_read_at_the_newest_a_route_names()
    {
        var older = new ThreadSeen("mailer", "sha256:mailer", AccountKnown: true);
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer"), ["message.send"] = Route("social") }));

        Assert.Equal("social", RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial, older])!.Route.PluginId);
        Assert.Equal("mailer", RouteResolver.ForThread(snapshot, Campaign, Get, List, [older, AtSocial])!.Route.PluginId);

        // The newest no route names any more gives way to the next.
        var retired = new ThreadSeen("social", "sha256:retired", AccountKnown: true);
        Assert.Equal("mailer", RouteResolver.ForThread(snapshot, Campaign, Get, List, [retired, older])!.Route.PluginId);
    }

    /// <summary>A sighting whose account was never recorded names its plugin under any account.</summary>
    [Fact]
    public void A_sighting_with_no_recorded_account_names_its_plugin_alone()
    {
        var unknown = new ThreadSeen("social", null, AccountKnown: false);

        Assert.True(unknown.Names(Route("social", "sha256:any")));
        Assert.False(unknown.Names(Route("mailer")));
        Assert.False(new ThreadSeen("social", null, AccountKnown: true).Names(Route("social", "sha256:any")));
        Assert.True(new ThreadSeen("social", null, AccountKnown: true).Names(new Route("social", null, null)));
    }

    private static Route Route(string plugin, string? identity = null) =>
        new(plugin, new JsonObject { ["seat"] = plugin }, identity ?? "sha256:" + plugin);

    private static RouteSnapshot Snapshot(RouteSet campaign) =>
        new(
            "rts_01K5AAAAAAAAAAAAAAAAAAAAAA",
            DateTimeOffset.UnixEpoch,
            "pls_01K5AAAAAAAAAAAAAAAAAAAAAA",
            RouteSet.Empty,
            new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [Campaign] = campaign });
}
