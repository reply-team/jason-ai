using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Nothing ties a provider's routed work to the journal kinds it handles: route resolution reads the route and the
/// operation, the claim checks read the route, the plugin's status and the operation, and availability is decided by
/// the plugin's programs and bindings alone. The same provider, with and without subscriptions, routes, claims and
/// runs the same work the same way — and with them, it is in the chain of the kind it was granted.
/// </summary>
public class SubscribingProviderTests
{
    private const string ProviderCampaign = "cmp_42";
    private const string Failed = "workitem_failed";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_provider_that_subscribes_routes_claims_and_runs_its_work_as_one_that_does_not(bool subscribes)
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Outbound", "Active");
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0}}""");
                var root = TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
                if (subscribes)
                {
                    File.AppendAllText(Path.Combine(root, "plugin.yaml"), $"subscriptions: [{Failed}]\n");
                    GrantSubscriptions(paths.UserSettingsFile, TestPlugins.FakeProviderId, "*");
                }

                TestRoutes.WriteGlobal(
                    paths,
                    TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = workspace.Root }));
            },
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

        var snapshot = api.Resolve<PluginRegistry>().Snapshot;
        var plugin = Assert.Single(snapshot.Plugins);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(subscribes ? [TestPlugins.FakeProviderId] : [], snapshot.Subscriptions.For(Failed));
        Assert.Equal(TestPlugins.FakeProviderId, api.Resolve<RouteRegistry>().Snapshot.Global.Default!.PluginId);

        // The loop scans the moment it starts; seeding after that scan keeps the next one the test's own.
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Flow" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var created = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign.Id,
                kind = "provider_op",
                operation = "campaign.get",
                context = new JsonObject
                {
                    ["input"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = ProviderCampaign } },
                },
            },
            Ct);

        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)));

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = created.Id }, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, read.Status);
        Assert.Single(read.Attempts!);
    }

    private static void GrantSubscriptions(string settingsFile, string id, params string[] kinds)
    {
        var settings = JsonNode.Parse(File.ReadAllText(settingsFile))!.AsObject();
        settings["Plugins"]!["Grants"]![id]!["Subscriptions"] = new JsonArray([.. kinds.Select(kind => (JsonNode)JsonValue.Create(kind))]);
        File.WriteAllText(settingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }
}
