using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Which plugins handle a journal kind, and in what order, as a load freezes it: every plugin granted the kind, the ids
/// the order lists first, the rest in plugin-id order. Built from the plugin set and the order section alone, so every
/// rule of the resolution is a row here; the load that calls it is held by <see cref="SubscriptionReloadTests"/>.
/// </summary>
public class SubscriptionChainsTests
{
    private const string Failed = "workitem_failed";
    private const string Created = "campaign_created";

    [Fact]
    public void Each_kind_is_handled_by_the_plugins_granted_it_in_plugin_id_order()
    {
        var built = SubscriptionChains.Build(
            Snapshot(
                Subscriber("zeta", [Failed], [Failed]),
                Subscriber("alpha", [Failed, Created], [Failed, Created]),
                Subscriber("mid", [Failed], [])),
            new SubscriptionOrderOptions());

        Assert.Empty(built.Problems);
        var chains = built.Chains!;
        Assert.Equal([Created, Failed], chains.Kinds);
        Assert.Equal(["alpha", "zeta"], chains.For(Failed));
        Assert.Equal(["alpha"], chains.For(Created));
        Assert.Empty(chains.For("contacts_added"));
    }

    [Fact]
    public void The_ids_an_order_lists_go_first_and_the_plugins_it_leaves_out_follow_in_plugin_id_order()
    {
        var built = SubscriptionChains.Build(
            Snapshot(
                Subscriber("a", [Failed], [Failed]),
                Subscriber("b", [Failed], [Failed]),
                Subscriber("c", [Failed], [Failed]),
                Subscriber("d", [Failed], [Failed])),
            Order((Failed, ["c", "a"])));

        Assert.Equal(["c", "a", "b", "d"], built.Chains!.For(Failed));
    }

    [Fact]
    public void An_empty_order_is_plugin_id_order()
    {
        var built = SubscriptionChains.Build(
            Snapshot(Subscriber("b", [Failed], [Failed]), Subscriber("a", [Failed], [Failed])),
            Order((Failed, [])));

        Assert.Equal(["a", "b"], built.Chains!.For(Failed));
    }

    /// <summary>
    /// Availability changes only at a load, and a plugin that cannot run today is still the one the person granted the
    /// kind to: leaving it out would empty a kind it alone handles, and what happens to a line it cannot take is the
    /// runner's to decide, not the load's.
    /// </summary>
    [Fact]
    public void A_plugin_that_is_unavailable_keeps_its_place_in_the_chain()
    {
        var built = SubscriptionChains.Build(
            Snapshot(
                Subscriber("a", [Failed], [Failed]),
                Subscriber("b", [Failed], [Failed], PluginStatus.Unavailable)),
            Order((Failed, ["b"])));

        Assert.Equal(["b", "a"], built.Chains!.For(Failed));
    }

    [Fact]
    public void A_kind_nobody_is_granted_has_no_chain_even_when_the_order_names_it_with_an_empty_list()
    {
        var built = SubscriptionChains.Build(
            Snapshot(Subscriber("a", [Failed], [Failed])),
            Order((Created, [])));

        Assert.Equal([Failed], built.Chains!.Kinds);
        Assert.Empty(built.Chains.For(Created));
    }

    /// <summary>
    /// A provider granted a kind handles it as any subscriber does: nothing about a plugin's kind enters the chain.
    /// </summary>
    [Fact]
    public void A_provider_and_a_notification_plugin_granted_a_kind_are_in_its_chain()
    {
        var built = SubscriptionChains.Build(
            Snapshot(
                Subscriber("provider", [Failed], [Failed], kind: PluginKind.Provider),
                Subscriber("notify", [Failed], [Failed], kind: PluginKind.Notification)),
            new SubscriptionOrderOptions());

        Assert.Equal(["notify", "provider"], built.Chains!.For(Failed));
    }

    [Fact]
    public void A_set_with_no_subscriber_has_no_chains()
    {
        var built = SubscriptionChains.Build(Snapshot(TestPlugins.Loaded("fake")), new SubscriptionOrderOptions());

        Assert.Empty(built.Chains!.Kinds);
    }

    /// <summary>
    /// An id the order lists for a kind that plugin is not granted: the person meant something the plugin set does not
    /// hold, and running the chain without it would carry out an order nobody wrote. The message says which of the three
    /// causes it is, since each has a different repair.
    /// </summary>
    [Theory]
    [InlineData("gone", "no plugin with id 'gone' is in the candidate set")]
    [InlineData("quiet", "plugin 'quiet' does not declare workitem_failed")]
    [InlineData("held", "plugin 'held' declares workitem_failed but Plugins:Grants:held:Subscriptions does not grant it")]
    public void An_id_the_order_lists_without_a_granted_subscription_to_the_kind_refuses_the_set(string id, string because)
    {
        var built = SubscriptionChains.Build(
            Snapshot(
                Subscriber("a", [Failed], [Failed]),
                Subscriber("quiet", [Created], [Created]),
                Subscriber("held", [Failed], [])),
            Order((Failed, ["a", id])));

        Assert.Null(built.Chains);
        var problem = Assert.Single(built.Problems);
        Assert.Equal("Subscriptions:Order:workitem_failed[1]", problem.Field);
        Assert.Equal(SubscriptionProblemCodes.OrderUngranted, problem.Code);
        Assert.StartsWith(because, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_id_listed_without_a_granted_subscription_is_named()
    {
        var built = SubscriptionChains.Build(
            Snapshot(Subscriber("a", [Failed], [Failed])),
            Order((Failed, ["gone", "a", "lost"]), (Created, ["a"])));

        Assert.Null(built.Chains);
        Assert.Equal(
            ["Subscriptions:Order:campaign_created[0]", "Subscriptions:Order:workitem_failed[0]", "Subscriptions:Order:workitem_failed[2]"],
            built.Problems.Select(problem => problem.Field));
        Assert.All(built.Problems, problem => Assert.Equal(SubscriptionProblemCodes.OrderUngranted, problem.Code));
    }

    private static PluginSnapshot Snapshot(params LoadedPlugin[] plugins) =>
        PluginSnapshot.Empty(DateTime.UtcNow, SnapshotSource.Reload) with { Plugins = plugins };

    private static SubscriptionOrderOptions Order(params (string Kind, string[] Ids)[] lists)
    {
        var options = new SubscriptionOrderOptions();
        foreach (var (kind, ids) in lists)
        {
            options.Order[kind] = [.. ids];
        }

        return options;
    }

    private static LoadedPlugin Subscriber(
        string id,
        string[] declared,
        string[] granted,
        PluginStatus status = PluginStatus.Valid,
        PluginKind kind = PluginKind.Subscriber)
    {
        var plugin = TestPlugins.Loaded(id, kind: kind, status: status);
        return plugin with
        {
            Manifest = plugin.Manifest with { Subscriptions = declared },
            Grants = plugin.Grants with { Subscriptions = granted },
        };
    }
}
