using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Which plugin performs an operation, decided by one pure function over one immutable snapshot: no options
/// read, no database, no clock. Four levels, most specific first — and a campaign default hides every global
/// override, because a campaign that named its provider must not still be sending one operation elsewhere.
/// </summary>
public class RouteResolverTests
{
    private const string Campaign = "cmp_01K5AAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherCampaign = "cmp_01K5BBBBBBBBBBBBBBBBBBBBBB";
    private const string Operation = "campaign.get";

    [Theory]
    [InlineData("A", "B", "C", "D", "A", RouteScope.CampaignOperation)]
    [InlineData(null, "B", "C", "D", "B", RouteScope.CampaignDefault)]
    [InlineData(null, null, "C", "D", "C", RouteScope.GlobalOperation)]
    [InlineData(null, null, null, "D", "D", RouteScope.GlobalDefault)]
    [InlineData("A", null, "C", null, "A", RouteScope.CampaignOperation)]
    [InlineData(null, "B", null, "D", "B", RouteScope.CampaignDefault)]
    [InlineData(null, null, "C", null, "C", RouteScope.GlobalOperation)]
    public void The_winning_route_is_the_most_specific_one(
        string? campaignOperation,
        string? campaignDefault,
        string? globalOperation,
        string? globalDefault,
        string expected,
        RouteScope scope)
    {
        var snapshot = Snapshot(campaignOperation, campaignDefault, globalOperation, globalDefault);

        var resolution = RouteResolver.Resolve(snapshot, Campaign, Operation);

        Assert.NotNull(resolution);
        Assert.Equal(expected, resolution.Route.PluginId);
        Assert.Equal(scope, resolution.Scope);
    }

    [Fact]
    public void Nothing_configured_resolves_to_nothing() =>
        Assert.Null(RouteResolver.Resolve(Snapshot(null, null, null, null), Campaign, Operation));

    [Fact]
    public void A_campaign_default_hides_a_global_override_and_an_explicit_campaign_override_brings_it_back()
    {
        var masked = Snapshot(campaignOperation: null, campaignDefault: "house", globalOperation: "special", globalDefault: "fallback");

        var hidden = RouteResolver.Resolve(masked, Campaign, Operation);

        Assert.Equal("house", hidden!.Route.PluginId);
        Assert.Equal(RouteScope.CampaignDefault, hidden.Scope);

        // The same global override still wins everywhere the campaign default does not reach.
        var elsewhere = RouteResolver.Resolve(masked, OtherCampaign, Operation);
        Assert.Equal("special", elsewhere!.Route.PluginId);
        Assert.Equal(RouteScope.GlobalOperation, elsewhere.Scope);

        // Bringing it back inside the campaign takes saying so.
        var restored = Snapshot(campaignOperation: "special", campaignDefault: "house", globalOperation: "special", globalDefault: "fallback");
        var back = RouteResolver.Resolve(restored, Campaign, Operation);
        Assert.Equal("special", back!.Route.PluginId);
        Assert.Equal(RouteScope.CampaignOperation, back.Scope);
    }

    [Fact]
    public void An_operation_the_campaign_did_not_override_still_reaches_the_campaign_default()
    {
        var snapshot = Snapshot(campaignOperation: "A", campaignDefault: "B", globalOperation: "C", globalDefault: "D");

        var resolution = RouteResolver.Resolve(snapshot, Campaign, "list_membership.add");

        Assert.Equal("B", resolution!.Route.PluginId);
        Assert.Equal(RouteScope.CampaignDefault, resolution.Scope);
    }

    [Fact]
    public void A_campaign_with_no_routes_of_its_own_is_the_same_as_a_campaign_nobody_has_heard_of()
    {
        var snapshot = new RouteSnapshot(
            "rts_01K5CCCCCCCCCCCCCCCCCCCCCC",
            DateTimeOffset.UnixEpoch,
            "snp_01K5DDDDDDDDDDDDDDDDDDDDDD",
            new RouteSet(new Route("fallback", null, null), Operations()),
            new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [Campaign] = RouteSet.Empty });

        Assert.Equal(RouteScope.GlobalDefault, RouteResolver.Resolve(snapshot, Campaign, Operation)!.Scope);
        Assert.Equal(RouteScope.GlobalDefault, RouteResolver.Resolve(snapshot, OtherCampaign, Operation)!.Scope);
    }

    /// <summary>
    /// The level that wins brings its own binding and nothing else's. Merging bindings across levels would let a
    /// campaign work against an account it never named, which is the surprise routing exists to prevent.
    /// </summary>
    [Fact]
    public void The_winning_route_carries_its_own_binding_and_no_part_of_any_other()
    {
        var campaign = new Route("house", new JsonObject { ["workspace"] = "west" }, "sha256:west");
        var global = new Route("house", new JsonObject { ["workspace"] = "east", ["mailbox"] = "sales" }, "sha256:east");
        var snapshot = new RouteSnapshot(
            "rts_01K5CCCCCCCCCCCCCCCCCCCCCC",
            DateTimeOffset.UnixEpoch,
            "snp_01K5DDDDDDDDDDDDDDDDDDDDDD",
            new RouteSet(global, Operations()),
            new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [Campaign] = new(campaign, Operations()) });

        var resolution = RouteResolver.Resolve(snapshot, Campaign, Operation);

        Assert.Equal("sha256:west", resolution!.Route.BindingIdentity);
        Assert.Equal("west", (string?)resolution.Route.Binding!["workspace"]);
        Assert.False(resolution.Route.Binding.ContainsKey("mailbox"));
    }

    /// <summary>
    /// A7(b). Nothing is routed anywhere until somebody says so: an empty snapshot answers "no route" for every
    /// operation this build publishes, so a vendor-neutral claim is a test rather than an intention.
    /// </summary>
    [Fact]
    public void An_empty_snapshot_routes_no_operation_anywhere()
    {
        var snapshot = RouteSnapshot.Empty(DateTimeOffset.UnixEpoch, "snp_01K5DDDDDDDDDDDDDDDDDDDDDD");

        Assert.NotEmpty(OperationCatalog.All);
        foreach (var contract in OperationCatalog.All)
        {
            Assert.Null(RouteResolver.Resolve(snapshot, Campaign, contract.Id));
        }
    }

    private static RouteSnapshot Snapshot(string? campaignOperation, string? campaignDefault, string? globalOperation, string? globalDefault)
    {
        var campaigns = new Dictionary<string, RouteSet>(StringComparer.Ordinal);
        if (campaignOperation is not null || campaignDefault is not null)
        {
            campaigns[Campaign] = new RouteSet(Plugin(campaignDefault), Operations(campaignOperation));
        }

        return new RouteSnapshot(
            "rts_01K5CCCCCCCCCCCCCCCCCCCCCC",
            DateTimeOffset.UnixEpoch,
            "snp_01K5DDDDDDDDDDDDDDDDDDDDDD",
            new RouteSet(Plugin(globalDefault), Operations(globalOperation)),
            campaigns);
    }

    private static Route? Plugin(string? id) => id is null ? null : new Route(id, null, null);

    private static IReadOnlyDictionary<string, Route> Operations(string? id = null)
    {
        var operations = new Dictionary<string, Route>(StringComparer.Ordinal);
        if (id is not null)
        {
            operations[Operation] = new Route(id, null, null);
        }

        return operations;
    }
}
