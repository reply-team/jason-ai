using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A route may name a channel under an operation, and each operation level gains one level above it: campaign operation
/// and channel, campaign operation, campaign default, global operation and channel, global operation, global default.
/// Masking stays exact — any answer at campaign scope hides every global route, the channel routes included — and a call
/// that names no channel never takes a channel route.
/// </summary>
public class ChannelRouteResolverTests
{
    private const string Campaign = "cmp_01K5AAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherCampaign = "cmp_01K5BBBBBBBBBBBBBBBBBBBBBB";
    private const string Send = "message.send";
    private const string LinkedIn = "linkedin";
    private const string Email = "email";

    [Theory]
    [InlineData("CC", "CO", "CD", "GC", "GO", "GD", "CC", RouteScope.CampaignChannel)]
    [InlineData(null, "CO", "CD", "GC", "GO", "GD", "CO", RouteScope.CampaignOperation)]
    [InlineData(null, null, "CD", "GC", "GO", "GD", "CD", RouteScope.CampaignDefault)]
    [InlineData(null, null, null, "GC", "GO", "GD", "GC", RouteScope.GlobalChannel)]
    [InlineData(null, null, null, null, "GO", "GD", "GO", RouteScope.GlobalOperation)]
    [InlineData(null, null, null, null, null, "GD", "GD", RouteScope.GlobalDefault)]
    [InlineData("CC", null, null, null, null, "GD", "CC", RouteScope.CampaignChannel)]
    [InlineData(null, null, null, "GC", null, null, "GC", RouteScope.GlobalChannel)]
    public void Each_of_the_six_levels_wins_where_nothing_more_specific_answers(
        string? campaignChannel,
        string? campaignOperation,
        string? campaignDefault,
        string? globalChannel,
        string? globalOperation,
        string? globalDefault,
        string expected,
        RouteScope scope)
    {
        var snapshot = Snapshot(
            Campaign,
            campaign: Set(campaignDefault, campaignOperation, campaignChannel),
            global: Set(globalDefault, globalOperation, globalChannel));

        var resolution = RouteResolver.Resolve(snapshot, Campaign, Send, LinkedIn);

        Assert.NotNull(resolution);
        Assert.Equal((expected, scope), (resolution.Route.PluginId, resolution.Scope));
    }

    /// <summary>
    /// A campaign that answers at all — by its default or by its route for the operation — hides the global channel route,
    /// as it hides every other global route: a campaign that named its provider never has one channel quietly sent elsewhere.
    /// Bringing the global channel's plugin back inside the campaign takes a campaign channel route.
    /// </summary>
    [Theory]
    [InlineData("house", null, "house", RouteScope.CampaignDefault)]
    [InlineData(null, "house", "house", RouteScope.CampaignOperation)]
    public void A_campaign_answer_hides_the_global_channel_route(string? campaignDefault, string? campaignOperation, string expected, RouteScope scope)
    {
        var snapshot = Snapshot(Campaign, campaign: Set(campaignDefault, campaignOperation, null), global: Set("fallback", "special", "social"));

        var hidden = RouteResolver.Resolve(snapshot, Campaign, Send, LinkedIn);
        Assert.Equal((expected, scope), (hidden!.Route.PluginId, hidden.Scope));

        // The same global channel route still wins where the campaign does not answer.
        var elsewhere = RouteResolver.Resolve(snapshot, OtherCampaign, Send, LinkedIn);
        Assert.Equal(("social", RouteScope.GlobalChannel), (elsewhere!.Route.PluginId, elsewhere.Scope));

        var restored = Snapshot(Campaign, campaign: Set(campaignDefault, campaignOperation, "social"), global: Set("fallback", "special", "social"));
        var back = RouteResolver.Resolve(restored, Campaign, Send, LinkedIn);
        Assert.Equal(("social", RouteScope.CampaignChannel), (back!.Route.PluginId, back.Scope));
    }

    /// <summary>
    /// A campaign channel route answers its own channel only. A call on another channel is one the campaign has no answer
    /// for, so it reaches the global levels in their order, the global route for its own channel first.
    /// </summary>
    [Fact]
    public void A_campaign_channel_route_answers_only_its_own_channel()
    {
        var snapshot = Snapshot(
            Campaign,
            campaign: Set(null, null, Email, "mailer"),
            global: Set("fallback", "special", "social"));

        Assert.Equal(("mailer", RouteScope.CampaignChannel), Won(RouteResolver.Resolve(snapshot, Campaign, Send, Email)));
        Assert.Equal(("social", RouteScope.GlobalChannel), Won(RouteResolver.Resolve(snapshot, Campaign, Send, LinkedIn)));
    }

    /// <summary>A call that names no channel is routed by its operation alone: no channel route is ever its answer.</summary>
    [Fact]
    public void A_call_without_a_channel_never_takes_a_channel_route()
    {
        var onlyChannels = Snapshot(Campaign, campaign: Set(null, null, "CC"), global: Set(null, null, "GC"));
        Assert.Null(RouteResolver.Resolve(onlyChannels, Campaign, Send, channel: null));
        Assert.Null(RouteResolver.Resolve(onlyChannels, OtherCampaign, Send, channel: null));

        var withOperations = Snapshot(Campaign, campaign: Set(null, "CO", "CC"), global: Set(null, "GO", "GC"));
        Assert.Equal(("CO", RouteScope.CampaignOperation), Won(RouteResolver.Resolve(withOperations, Campaign, Send, channel: null)));
        Assert.Equal(("GO", RouteScope.GlobalOperation), Won(RouteResolver.Resolve(withOperations, OtherCampaign, Send, channel: null)));
    }

    /// <summary>A channel no route names falls to the operation level, at campaign scope and at global scope alike.</summary>
    [Fact]
    public void A_channel_no_route_names_falls_to_the_operation_level()
    {
        var snapshot = Snapshot(Campaign, campaign: Set("CD", "CO", "CC"), global: Set("GD", "GO", "GC"));

        Assert.Equal(("CO", RouteScope.CampaignOperation), Won(RouteResolver.Resolve(snapshot, Campaign, Send, "phone")));
        Assert.Equal(("GO", RouteScope.GlobalOperation), Won(RouteResolver.Resolve(snapshot, OtherCampaign, Send, "phone")));
    }

    /// <summary>A channel route is the route of its own operation: another operation on the same channel never takes it.</summary>
    [Fact]
    public void A_channel_route_is_never_the_answer_for_another_operation()
    {
        var snapshot = Snapshot(Campaign, campaign: Set("CD", null, "CC"), global: Set("GD", null, "GC"));

        Assert.Equal(("CD", RouteScope.CampaignDefault), Won(RouteResolver.Resolve(snapshot, Campaign, "conversation.reply", LinkedIn)));
        Assert.Equal(("GD", RouteScope.GlobalDefault), Won(RouteResolver.Resolve(snapshot, OtherCampaign, "conversation.reply", LinkedIn)));
    }

    /// <summary>The channel route that wins brings its own binding and no part of the operation route's or the default's.</summary>
    [Fact]
    public void A_winning_channel_route_carries_its_own_binding()
    {
        var channels = Channels((LinkedIn, new Route("house", new JsonObject { ["seat"] = "west" }, "sha256:west")));
        var campaign = new RouteSet(
            new Route("house", new JsonObject { ["workspace"] = "east", ["mailbox"] = "sales" }, "sha256:east"),
            new Dictionary<string, Route>(StringComparer.Ordinal) { [Send] = new("house", new JsonObject { ["workspace"] = "north" }, "sha256:north") },
            channels);
        var snapshot = Snapshot(Campaign, campaign, RouteSet.Empty);

        var resolution = RouteResolver.Resolve(snapshot, Campaign, Send, LinkedIn);

        Assert.Equal("sha256:west", resolution!.Route.BindingIdentity);
        Assert.Equal(new[] { "seat" }, resolution.Route.Binding!.Select(pair => pair.Key));
    }

    /// <summary>A set made without channel routes holds none, so every snapshot made before them keeps its answers.</summary>
    [Fact]
    public void A_route_set_made_without_channel_routes_holds_none()
    {
        var set = new RouteSet(new Route("house", null, null), new Dictionary<string, Route>(StringComparer.Ordinal));

        Assert.Null(set.Channels);
        Assert.Null(set.ChannelRoute(Send, LinkedIn));
        Assert.Empty(set.ChannelRoutes);
        Assert.Empty(RouteSet.Empty.ChannelRoutes);
    }

    /// <summary>A set's channel routes are walked in one order: operations ordinally, then channels ordinally.</summary>
    [Fact]
    public void A_set_walks_its_channel_routes_by_operation_then_channel_ordinally()
    {
        var byOperation = new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
        {
            [Send] = new Dictionary<string, Route>(StringComparer.Ordinal) { [LinkedIn] = new("b", null, null), [Email] = new("a", null, null) },
            ["conversation.reply"] = new Dictionary<string, Route>(StringComparer.Ordinal) { [LinkedIn] = new("d", null, null), [Email] = new("c", null, null) },
        };
        var set = new RouteSet(null, new Dictionary<string, Route>(StringComparer.Ordinal), byOperation);

        Assert.Equal(
            [("conversation.reply", Email, "c"), ("conversation.reply", LinkedIn, "d"), (Send, Email, "a"), (Send, LinkedIn, "b")],
            set.ChannelRoutes.Select(entry => (entry.Operation, entry.Channel, entry.Route.PluginId)));
    }

    /// <summary>
    /// A read or a stop of a search goes where the search ran, found among every route in force: a channel route that names
    /// the search's plugin and account is one of them, at campaign scope and at global scope.
    /// </summary>
    [Theory]
    [InlineData(true, RouteScope.CampaignChannel)]
    [InlineData(false, RouteScope.GlobalChannel)]
    public void A_search_is_followed_to_a_channel_route_that_names_its_account(bool atCampaign, RouteScope scope)
    {
        // Each scope's own default names another plugin, so the search's own operation resolves elsewhere.
        var other = new Route("other", null, null);
        var seat = new Route("searcher", new JsonObject { ["seat"] = "west" }, "sha256:seat");
        var withSeat = new RouteSet(other, new Dictionary<string, Route>(StringComparer.Ordinal), Channels((LinkedIn, seat)));
        var without = new RouteSet(other, new Dictionary<string, Route>(StringComparer.Ordinal));
        var snapshot = atCampaign ? Snapshot(Campaign, withSeat, without) : Snapshot(Campaign, without, withSeat);

        var followed = RouteResolver.ForSearch(snapshot, Campaign, "audience.search_status", "audience.search", "searcher", "sha256:seat");

        Assert.Equal(("searcher", "sha256:seat", scope), (followed!.Route.PluginId, followed.Route.BindingIdentity, followed.Scope));
    }

    /// <summary>
    /// Where several routes name the search's account, the first in a fixed order is taken: at each scope its channel routes
    /// before its operation routes and its default, the channel routes by operation ordinally and then by channel ordinally.
    /// </summary>
    [Fact]
    public void A_search_is_followed_to_the_first_channel_route_in_a_fixed_order()
    {
        const string Identity = "sha256:seat";
        var byOperation = new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
        {
            [Send] = new Dictionary<string, Route>(StringComparer.Ordinal) { [Email] = Seat(1) },
            ["conversation.reply"] = new Dictionary<string, Route>(StringComparer.Ordinal) { [LinkedIn] = Seat(2), [Email] = Seat(3) },
        };
        var campaign = new RouteSet(
            null,
            new Dictionary<string, Route>(StringComparer.Ordinal) { ["campaign.get"] = Seat(4), ["audience.search_status"] = new("other", null, null) },
            byOperation);
        var snapshot = Snapshot(Campaign, campaign, new RouteSet(null, new Dictionary<string, Route>(StringComparer.Ordinal), Channels((LinkedIn, Seat(6)))));

        var followed = RouteResolver.ForSearch(snapshot, Campaign, "audience.search_status", "audience.search", "searcher", Identity);

        Assert.Equal((3, RouteScope.CampaignChannel), ((int)followed!.Route.Binding!["n"]!, followed.Scope));

        static Route Seat(int n) => new("searcher", new JsonObject { ["n"] = n }, Identity);
    }

    private static (string, RouteScope) Won(Resolution? resolution) => (resolution!.Route.PluginId, resolution.Scope);

    private static RouteSet Set(string? fallback, string? operation, string? linkedIn) =>
        Set(fallback, operation, LinkedIn, linkedIn);

    private static RouteSet Set(string? fallback, string? operation, string channel, string? plugin)
    {
        var operations = new Dictionary<string, Route>(StringComparer.Ordinal);
        if (operation is not null)
        {
            operations[Send] = new Route(operation, null, null);
        }

        return new RouteSet(
            fallback is null ? null : new Route(fallback, null, null),
            operations,
            plugin is null ? null : Channels((channel, new Route(plugin, null, null))));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, Route>> Channels(params (string Channel, Route Route)[] routes) =>
        new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
        {
            [Send] = routes.ToDictionary(entry => entry.Channel, entry => entry.Route, StringComparer.Ordinal),
        };

    private static RouteSnapshot Snapshot(string campaignId, RouteSet campaign, RouteSet global) =>
        new(
            "rts_01K5CCCCCCCCCCCCCCCCCCCCCC",
            DateTimeOffset.UnixEpoch,
            "snp_01K5DDDDDDDDDDDDDDDDDDDDDD",
            global,
            new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [campaignId] = campaign });
}
