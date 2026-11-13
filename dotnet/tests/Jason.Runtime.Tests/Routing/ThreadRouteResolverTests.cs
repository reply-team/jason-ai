using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A read of a thread goes where the thread was seen. Where the sightings name one plugin: for each account they say,
/// newest first, the read's own route where it names the plugin and account, then the campaign's route for the list read,
/// then every route in the order of precedence, channel routes included; then, the same walk for a route that names the
/// plugin under any binding. Where they name more than one plugin, the read's own route. Where none is named, the read's
/// own route answers, for the claim to refuse.
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

    /// <summary>
    /// Where no route names the account any more, a route that names the plugin under another binding answers: the claim
    /// refuses on the plugin alone, and another account of it is the provider's to refuse.
    /// </summary>
    [Fact]
    public void Then_a_route_that_names_the_plugin_under_another_binding()
    {
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer"), ["message.send"] = Route("social", "sha256:another-seat") }));

        var resolution = RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial]);

        Assert.Equal(("social", "sha256:another-seat"), (resolution!.Route.PluginId, resolution.Route.BindingIdentity));
    }

    /// <summary>Where no route names the plugin any more, the read's own route answers, and the claim refuses it.</summary>
    [Fact]
    public void Otherwise_the_read_s_own_route()
    {
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer"), ["message.send"] = Route("archive") }));

        var resolution = RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial]);

        Assert.Equal(("mailer", RouteScope.CampaignOperation), (resolution!.Route.PluginId, resolution.Scope));
        Assert.False(AtSocial.NamesThePlugin(resolution.Route));
    }

    /// <summary>
    /// An id seen at two plugins is two threads, and only the read's own route says which is meant: it answers, never the
    /// newest sighting, whichever plugin it names.
    /// </summary>
    [Fact]
    public void An_id_seen_at_two_plugins_takes_the_read_s_own_route()
    {
        var atMailer = new ThreadSeen("mailer", "sha256:mailer", AccountKnown: true);
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("mailer"), ["message.send"] = Route("social") }));

        Assert.Equal("mailer", RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial, atMailer])!.Route.PluginId);
        Assert.Equal("mailer", RouteResolver.ForThread(snapshot, Campaign, Get, List, [atMailer, AtSocial])!.Route.PluginId);
    }

    /// <summary>
    /// A thread seen at two accounts of one plugin over time is read at the newer, even though the read's own route names
    /// the older: the sightings are asked in turn, and every step is asked of the newest before the older is asked at all.
    /// </summary>
    [Fact]
    public void A_thread_seen_at_two_accounts_over_time_is_read_at_the_newest_a_route_names()
    {
        var older = new ThreadSeen("social", "sha256:older", AccountKnown: true);
        var snapshot = Snapshot(new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Get] = Route("social", "sha256:older"), ["message.send"] = Route("social") }));

        Assert.Equal("sha256:social", RouteResolver.ForThread(snapshot, Campaign, Get, List, [AtSocial, older])!.Route.BindingIdentity);
        Assert.Equal("sha256:older", RouteResolver.ForThread(snapshot, Campaign, Get, List, [older, AtSocial])!.Route.BindingIdentity);

        // The newest no route names any more gives way to the next.
        var retired = new ThreadSeen("social", "sha256:retired", AccountKnown: true);
        Assert.Equal("sha256:older", RouteResolver.ForThread(snapshot, Campaign, Get, List, [retired, older])!.Route.BindingIdentity);
    }

    /// <summary>
    /// A sighting names a route exactly by its plugin and binding identity, and its plugin under any binding; one whose
    /// account was never recorded names no route exactly, and its plugin as any does.
    /// </summary>
    [Fact]
    public void A_sighting_names_its_account_exactly_and_its_plugin_under_any_binding()
    {
        var unknown = new ThreadSeen("social", null, AccountKnown: false);

        Assert.False(unknown.Names(Route("social", "sha256:any")));
        Assert.True(unknown.NamesThePlugin(Route("social", "sha256:any")));
        Assert.False(unknown.NamesThePlugin(Route("mailer")));
        Assert.True(AtSocial.Names(Route("social")));
        Assert.False(AtSocial.Names(Route("social", "sha256:another")));
        Assert.True(AtSocial.NamesThePlugin(Route("social", "sha256:another")));
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
