using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The chains are frozen by the act that freezes plugins and routes, on the plugin snapshot itself, so plugins, grants
/// and chains change together and nothing else changes them. An order that names a plugin not granted its kind is
/// refused as a route to a missing plugin is: the whole load, both snapshots kept, naming the setting.
/// </summary>
public class SubscriptionReloadTests
{
    private const string Failed = "workitem_failed";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_load_freezes_the_chains_on_the_plugin_snapshot_and_a_reload_swaps_them_with_it()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher");
            Settings(paths, settings => Grant(settings, "watcher", "*"));
        });
        var registry = api.Resolve<PluginRegistry>();
        var started = registry.Snapshot;
        Assert.Equal([Failed], started.Subscriptions.Kinds);
        Assert.Equal(["watcher"], started.Subscriptions.For(Failed));

        WriteSubscriber(api.Paths, "alpha");
        Settings(api.Paths, settings =>
        {
            Grant(settings, "alpha", Failed);
            Order(settings, Failed, "watcher");
        });
        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var after = registry.Snapshot;
        Assert.Equal(reloaded.Snapshot.Id, after.Id);
        Assert.Equal(["watcher", "alpha"], after.Subscriptions.For(Failed));
        Assert.Equal(["watcher"], started.Subscriptions.For(Failed));
    }

    /// <summary>
    /// A campaign's route is activated without a plugin load, so it leaves the plugin snapshot, and with it the
    /// chains, exactly as they were — even when the order section has been edited since.
    /// </summary>
    [Fact]
    public async Task A_route_set_leaves_the_chains_where_they_were()
    {
        await using var api = await StartAsync(paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            WriteSubscriber(paths, "watcher");
            Settings(paths, settings => Grant(settings, "watcher", "*"));
        });
        var registry = api.Resolve<PluginRegistry>();
        var before = registry.Snapshot;
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

        Settings(api.Paths, settings => Order(settings, Failed, "nobody"));
        await TestRoutes.SetCampaignAsync(api, campaign, operation: null, TestPlugins.FakeProviderId, RouteActivationTests.Workspace(), Ct);

        Assert.Same(before, registry.Snapshot);
        Assert.Same(before.Subscriptions, registry.Snapshot.Subscriptions);
    }

    [Theory]
    [InlineData("gone", "no plugin with id 'gone' is in the candidate set")]
    [InlineData("quiet", "plugin 'quiet' does not declare workitem_failed")]
    [InlineData("held", "plugin 'held' declares workitem_failed but Plugins:Grants:held:Subscriptions does not grant it")]
    public async Task An_order_naming_a_plugin_not_granted_the_kind_rejects_the_reload_and_keeps_both_snapshots(string id, string because)
    {
        await using var api = await StartAsync(paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
            WriteSubscriber(paths, "watcher");
            WriteSubscriber(paths, "quiet", "campaign_created");
            WriteSubscriber(paths, "held");
            Settings(paths, settings =>
            {
                Grant(settings, "watcher", "*");
                Grant(settings, "quiet", "*");
            });
        });
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        var chains = api.Resolve<PluginRegistry>().Snapshot.Subscriptions;

        Settings(api.Paths, settings => Order(settings, Failed, id));
        var details = await RefusedAsync(api);

        var detail = Assert.Single(details);
        Assert.Equal("Subscriptions:Order:workitem_failed[0]", detail.Field);
        Assert.Equal(SubscriptionProblemCodes.OrderUngranted, detail.Code);
        Assert.StartsWith(because, detail.Message, StringComparison.Ordinal);

        var after = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.Equal(before.Snapshot.Id, after.Snapshot.Id);
        Assert.Equal(before.RoutingSnapshotId, after.RoutingSnapshotId);
        Assert.Same(chains, api.Resolve<PluginRegistry>().Snapshot.Subscriptions);
        Assert.False(api.Resolve<PluginRegistry>().LastReload!.Activated);
    }

    /// <summary>
    /// The order section edited into something its validator will not hand over: the reload is rejected naming the
    /// setting, as a mistyped route is, and never answered as a fault of the runtime.
    /// </summary>
    [Fact]
    public async Task An_order_section_its_validator_refuses_rejects_the_reload_naming_the_setting()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher");
            Settings(paths, settings => Grant(settings, "watcher", "*"));
        });
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        Settings(api.Paths, settings => Order(settings, "not_a_kind_we_write", "watcher"));
        var details = await RefusedAsync(api);

        var detail = Assert.Single(details);
        Assert.Equal("Subscriptions:Order:<key>", detail.Field);
        Assert.Equal(SubscriptionProblemCodes.SettingsInvalid, detail.Code);
        Assert.DoesNotContain("not_a_kind_we_write", detail.Message, StringComparison.Ordinal);

        var after = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.Equal(before.Snapshot.Id, after.Snapshot.Id);
    }

    /// <summary>
    /// Everything that could refuse is asked before the answer: a reload whose routes and whose order are both wrong
    /// names both, so the person repairs the two in one pass rather than discovering the second after the first.
    /// </summary>
    [Fact]
    public async Task A_reload_with_a_bad_route_and_a_bad_order_names_both()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher");
            Settings(paths, settings => Grant(settings, "watcher", "*"));
        });

        TestRoutes.WriteGlobal(api.Paths, TestRoutes.GlobalDefault("absent"));
        Settings(api.Paths, settings => Order(settings, Failed, "gone"));
        var details = await RefusedAsync(api);

        Assert.Equal(
            [RouteProblemCodes.PluginUnknown, SubscriptionProblemCodes.OrderUngranted],
            details.Select(detail => detail.Code));
        Assert.Equal("Subscriptions:Order:workitem_failed[0]", details[1].Field);
    }

    /// <summary>
    /// The start follows the reload's rule, with the start's outcome for a set it cannot activate: nothing is active,
    /// the report says why, and the runtime runs on, as it does for a bad route.
    /// </summary>
    [Fact]
    public async Task A_start_whose_order_names_a_plugin_not_granted_the_kind_activates_nothing_and_says_why()
    {
        await using var api = await StartAsync(paths =>
        {
            WriteSubscriber(paths, "watcher");
            Settings(paths, settings => Order(settings, Failed, "watcher"));
        });

        var registry = api.Resolve<PluginRegistry>();
        Assert.Empty(registry.Snapshot.Plugins);
        Assert.Empty(registry.Snapshot.Subscriptions.Kinds);
        var report = registry.LastReload!;
        Assert.False(report.Activated);
        Assert.Equal(SnapshotSource.Startup, report.Source);
        var problem = Assert.Single(report.Subscriptions);
        Assert.Equal("Subscriptions:Order:workitem_failed[0]", problem.Field);
        Assert.Equal(SubscriptionProblemCodes.OrderUngranted, problem.Code);
    }

    private static Task<RuntimeApiFixture> StartAsync(Action<JasonPaths> prepare) =>
        RouteActivationTests.StartAsync(prepare: prepare);

    private static async Task<IReadOnlyList<ErrorDetail>> RefusedAsync(RuntimeApiFixture api)
    {
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        return error.Details!;
    }

    private static void WriteSubscriber(JasonPaths paths, string id, string kind = Failed) =>
        TestPlugins.Write(
            paths,
            id,
            $"""
            manifest_version: 1
            id: {id}
            version: 1.0.0
            kind: subscriber
            contracts:
              protocol: [1]
            subscriptions: [{kind}]

            """,
            "export function invoke() { return { result: {} }; }");

    /// <summary>The settings file merged rather than written over, so the dispatcher stays off and earlier lines stay.</summary>
    private static void Settings(JasonPaths paths, Action<JsonObject> edit)
    {
        Directory.CreateDirectory(paths.ConfigDirectory);
        var settings = File.Exists(paths.UserSettingsFile)
            ? JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject()
            : JsonNode.Parse(RuntimeApiFixture.DispatcherOff)!.AsObject();
        edit(settings);
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    private static void Grant(JsonObject settings, string id, params string[] kinds) =>
        Child(Child(Child(settings, "Plugins"), "Grants"), id)["Subscriptions"] = Array(kinds);

    private static void Order(JsonObject settings, string kind, params string[] ids) =>
        Child(Child(settings, "Subscriptions"), "Order")[kind] = Array(ids);

    private static JsonArray Array(string[] values) => [.. values.Select(value => (JsonNode)JsonValue.Create(value))];

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }
}
