using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Routes are frozen by the same act that freezes plugins, and validated before anything is swapped. The rule
/// the whole file is about: nothing becomes active until everything that could refuse has refused — so a route
/// naming a plugin the new set does not have keeps <b>both</b> snapshots, rather than leaving a fresh plugin
/// set active beside routes that no longer point anywhere.
/// </summary>
public class RouteActivationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_route_to_a_plugin_that_is_not_in_the_new_set_rejects_the_whole_reload()
    {
        await using var api = await StartAsync(
            TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, Workspace()),
            paths =>
            {
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.InstallOtherProvider(paths);
            });
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        // The startup load froze the route, so the ids below are a real activation rather than the empty
        // snapshot a runtime holds before its first load.
        Assert.Equal(TestPlugins.FakeProviderId, api.Resolve<RouteRegistry>().Snapshot.Global.Default!.PluginId);

        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault("no-such-plugin"), Ct);
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("Routes:Default", detail.Field);
        Assert.Equal(RouteProblemCodes.PluginUnknown, detail.Code);

        var after = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.Equal(before.Snapshot.Id, after.Snapshot.Id);
        Assert.Equal(before.RoutingSnapshotId, after.RoutingSnapshotId);
        Assert.False(after.Activated);
        var problem = Assert.Single(after.LastReload!.Routes);
        Assert.Equal("Routes:Default", problem.Route);
        Assert.Equal(RouteProblemCodes.PluginUnknown, problem.Code);

        // A route is not a package, and never appears among them.
        Assert.All(after.LastReload.Candidates, candidate => Assert.NotEqual("routes", candidate.Directory));
    }

    [Fact]
    public async Task A_campaign_route_for_an_operation_no_build_publishes_refuses_the_reload()
    {
        await using var api = await StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await WriteRouteAsync(api, campaign, "not.an.operation", TestPlugins.FakeProviderId, Workspace());

        var detail = await RefusedAsync(api);

        Assert.Equal($"campaign:{campaign}/routes/not.an.operation", detail.Field);
        Assert.Equal(RouteProblemCodes.OperationUnknown, detail.Code);
    }

    /// <summary>
    /// The same mistake in the settings file never reaches the route checks: the section is validated before it
    /// is handed over at all, so it is refused as the setting it is. One rejected reload either way, naming the
    /// same route — an operator sees where the mistake is, not which layer noticed it.
    /// </summary>
    [Fact]
    public async Task A_global_route_for_an_operation_no_build_publishes_is_refused_as_the_setting_it_is()
    {
        await using var api = await StartAsync(
            TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, Workspace()),
            paths => TestPlugins.InstallFakeProvider(paths));

        await TestRoutes.WriteGlobalAsync(
            api,
            new JsonObject
            {
                ["Operations"] = new JsonObject { ["not.an.operation"] = new JsonObject { ["Plugin"] = TestPlugins.FakeProviderId } },
            }.ToJsonString(),
            Ct);
        var detail = await RefusedAsync(api);

        Assert.Equal("Routes:Operations:not.an.operation", detail.Field);
        Assert.Equal(RouteProblemCodes.SettingsInvalid, detail.Code);
        Assert.Contains("must name a published operation", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_route_to_a_plugin_that_never_claimed_the_operation_refuses_the_reload()
    {
        await using var api = await StartAsync(prepare: paths => TestPlugins.InstallOtherProvider(paths));
        var campaign = await CampaignAsync(api);
        await WriteRouteAsync(api, campaign, "campaign.enroll", TestPlugins.OtherProviderId, binding: null);

        var detail = await RefusedAsync(api);

        Assert.Equal($"campaign:{campaign}/routes/campaign.enroll", detail.Field);
        Assert.Equal(RouteProblemCodes.OperationUnsupported, detail.Code);
        Assert.Contains("campaign.enroll", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one code no installed package can reach today, because a manifest declaring an operation-contract
    /// version this runtime does not speak is refused by the manifest reader long before a route names it. The
    /// check exists for the day a second contract version is published, and the plugin set it is checked against
    /// is therefore built here rather than installed.
    /// </summary>
    [Fact]
    public void A_plugin_that_speaks_another_operation_contract_cannot_be_routed_to()
    {
        var snapshot = SnapshotOf(BuiltPlugin("ahead-of-us", ["campaign.get"], operationContracts: [2]));

        var byOperation = RouteActivator.Check(snapshot, "campaign.get", "ahead-of-us", binding: null);
        var byDefault = RouteActivator.Check(snapshot, operation: null, "ahead-of-us", binding: null);

        Assert.Equal(RouteProblemCodes.ContractIncompatible, byOperation!.Code);
        Assert.Contains("campaign.get", byOperation.Message, StringComparison.Ordinal);

        // A default route carries every operation, so it is refused by the first one the plugin could not be handed.
        Assert.Equal(RouteProblemCodes.ContractIncompatible, byDefault!.Code);
    }

    /// <summary>
    /// A binding no parser could have produced — deeper than any writer's limit, with a credential-shaped name
    /// at the bottom of it — is answered rather than thrown over: the walk carries its own stack, so depth costs
    /// heap instead of the thread that a reload runs on.
    /// </summary>
    [Fact]
    public void A_binding_deeper_than_any_parser_allows_is_still_read_to_the_bottom()
    {
        var snapshot = SnapshotOf(BuiltPlugin("anything", ["campaign.get"], operationContracts: [1]));
        var binding = new JsonObject();
        var leaf = binding;
        for (var depth = 0; depth < 20_000; depth++)
        {
            var next = new JsonObject();
            leaf["under"] = next;
            leaf = next;
        }

        leaf["api_token"] = "…";

        var problem = RouteActivator.Check(snapshot, "campaign.get", "anything", binding);

        Assert.Equal(RouteProblemCodes.BindingSecretLike, problem!.Code);
        Assert.Contains("api_token", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other thing only code can build: a number JSON has no spelling for. It is refused by the plugin's own
    /// schema, like any other value that is not what the schema asks for, and nothing about it throws.
    /// </summary>
    [Fact]
    public void A_binding_holding_a_number_json_cannot_spell_is_refused_by_the_schema()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["workspace"] = new JsonObject { ["type"] = "string" } },
        };
        var snapshot = SnapshotOf(BuiltPlugin("strict", ["campaign.get"], operationContracts: [1], binding: schema));

        var problem = RouteActivator.Check(
            snapshot,
            "campaign.get",
            "strict",
            new JsonObject { ["workspace"] = JsonValue.Create(double.NaN) });

        Assert.Equal(RouteProblemCodes.BindingInvalid, problem!.Code);
    }

    [Fact]
    public async Task A_binding_the_plugin_would_not_accept_refuses_the_reload()
    {
        await using var api = await StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await WriteRouteAsync(api, campaign, operation: null, TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = 5 });

        var detail = await RefusedAsync(api);

        Assert.Equal($"campaign:{campaign}/routes/default", detail.Field);
        Assert.Equal(RouteProblemCodes.BindingInvalid, detail.Code);
    }

    /// <summary>
    /// A binding is journaled and listed, so a field named like a credential is refused rather than redacted —
    /// and refused wherever it sits, not only where a plugin's schema happens to look. The binding is built here
    /// rather than written as text, and it is deliberately one fake-provider's schema would refuse too: the
    /// credential-shaped name is the sentence whoever wrote it has to read, so that check comes first.
    /// </summary>
    [Fact]
    public async Task A_binding_that_carries_something_credential_shaped_is_refused_however_deep_it_sits()
    {
        var binding = new JsonObject
        {
            ["workspace"] = "west",
            ["nested"] = new JsonObject { ["deeper"] = new JsonObject { ["api_key"] = "not a value anybody should paste here" } },
        };
        await using var api = await StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, binding), Ct);

        var detail = await RefusedAsync(api);

        Assert.Equal("Routes:Default", detail.Field);
        Assert.Equal(RouteProblemCodes.BindingSecretLike, detail.Code);
        Assert.Contains("api_key", detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not a value anybody should paste here", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_route_to_a_plugin_that_performs_no_operation_refuses_the_reload()
    {
        await using var api = await StartAsync(
            new JsonObject { ["Operations"] = new JsonObject { ["campaign.get"] = new JsonObject { ["Plugin"] = "quiet" } } }.ToJsonString(),
            paths => TestPlugins.Write(
                paths,
                "quiet",
                "manifest_version: 1\nid: quiet\nversion: 1.0.0\nkind: notification\ncontracts:\n  protocol: [1]\n",
                "export function invoke() { return { result: {} }; }"));

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        // The startup load is the same path, so this one never got as far as a first activation.
        Assert.False(registry.Activated);
        var problem = Assert.Single(registry.LastReload!.Routes);
        Assert.Equal("Routes:Operations:campaign.get", problem.Route);
        Assert.Equal(RouteProblemCodes.PluginKindNotInvocable, problem.Code);
    }

    /// <summary>
    /// History must never stand between an operator and an uninstall. A campaign that will never dispatch again
    /// is not a reason to keep a package on the machine.
    /// </summary>
    [Fact]
    public async Task An_archived_campaigns_route_never_blocks_an_uninstall()
    {
        await using var api = await StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await WriteRouteAsync(api, campaign, operation: null, TestPlugins.FakeProviderId, Workspace());
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { CampaignId = campaign }, Ct);

        Directory.Delete(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), recursive: true);
        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        Assert.True(reloaded.Activated);
        Assert.Empty(reloaded.Plugins);
        Assert.Empty(api.Resolve<RouteRegistry>().Snapshot.Campaigns);
    }

    /// <summary>
    /// The <c>Routes</c> section edited into something the validator will not hand over at all. The options
    /// monitor throws inside the reload path, and the answer is the one every other bad route gets: a rejected
    /// reload naming the setting. What it must never be is a 500, which says the runtime broke rather than the edit.
    /// </summary>
    [Fact]
    public async Task A_routes_section_edited_into_nonsense_refuses_the_reload_rather_than_failing()
    {
        await using var api = await StartAsync(
            TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, Workspace()),
            paths => TestPlugins.InstallFakeProvider(paths));
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        await TestRoutes.WriteGlobalAsync(
            api,
            new JsonObject { ["Default"] = new JsonObject { ["Plugin"] = TestPlugins.FakeProviderId, ["Binding"] = "not an object" } }.ToJsonString(),
            Ct);
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("Routes:Default:Binding", detail.Field);
        Assert.Equal(RouteProblemCodes.SettingsInvalid, detail.Code);

        var after = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.Equal(before.Snapshot.Id, after.Snapshot.Id);
        Assert.Equal(before.RoutingSnapshotId, after.RoutingSnapshotId);
    }

    /// <summary>
    /// The startup load takes the same path, and the same all-or-nothing rule: a runtime that started with a bad
    /// route holds no plugins and no routes, and says why.
    /// </summary>
    [Fact]
    public async Task A_bad_route_at_startup_leaves_both_registries_empty_and_says_why()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault("no-such-plugin"), paths => TestPlugins.InstallFakeProvider(paths));

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);

        Assert.False(registry.Activated);
        Assert.Empty(registry.Plugins);
        Assert.Equal(0, registry.Snapshot.PluginCount);
        Assert.Null(api.Resolve<RouteRegistry>().Snapshot.Global.Default);
        var problem = Assert.Single(registry.LastReload!.Routes);
        Assert.Equal("Routes:Default", problem.Route);
        Assert.Equal(RouteProblemCodes.PluginUnknown, problem.Code);

        // The package itself was fine: it is the route that kept it out, and the candidate report still says so.
        Assert.Equal(CandidateStatus.Valid, Assert.Single(registry.LastReload.Candidates).Status);
    }

    /// <summary>
    /// A reload freezes the campaign rows too, under a new route snapshot id pinned to the plugin snapshot it was
    /// checked against — and writes down where work was being sent, beside which packages were active.
    /// </summary>
    [Fact]
    public async Task An_activated_reload_freezes_every_route_and_writes_down_where_work_goes()
    {
        await using var api = await StartAsync(
            TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, Workspace()),
            paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await WriteRouteAsync(api, campaign, "campaign.get", TestPlugins.FakeProviderId, Workspace("east"));
        var before = api.Resolve<RouteRegistry>().Snapshot;

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        var snapshot = api.Resolve<RouteRegistry>().Snapshot;
        Assert.NotEqual(before.Id, snapshot.Id);
        Assert.Equal(reloaded.Snapshot.Id, snapshot.PluginSnapshotId);
        Assert.Equal(snapshot.Id, reloaded.RoutingSnapshotId);
        var campaignRoute = snapshot.Campaigns[campaign].Operations["campaign.get"];
        Assert.Equal("east", (string?)campaignRoute.Binding!["workspace"]);

        // Two bindings that differ only in the account they select have different identities, which is what makes
        // "was this retried against a different account?" answerable from an attempt alone.
        Assert.NotEqual(snapshot.Global.Default!.BindingIdentity, campaignRoute.BindingIdentity);

        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { Kind = JournalKinds.PluginsReloaded },
            Ct);
        var routes = entries.Items[0].New!["routes"]!;
        Assert.Equal(snapshot.Id, (string?)routes["snapshot_id"]);
        Assert.Equal(TestPlugins.FakeProviderId, (string?)routes["global_default"]);
        Assert.Equal(0, (int?)routes["override_count"]);
        Assert.Equal(1, (int?)routes["campaign_route_count"]);
    }

    /// <summary>
    /// Changing one campaign's route is not a reload, and must not quietly activate a global edit nobody asked
    /// for. Where the global half comes from is the one thing that says so, which is why it is a parameter rather
    /// than a rule two call sites have to remember.
    /// </summary>
    [Fact]
    public async Task A_candidate_built_from_the_active_snapshot_never_reads_the_settings_file()
    {
        await using var api = await StartAsync(
            TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, Workspace()),
            paths =>
            {
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.InstallOtherProvider(paths);
            });
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(TestPlugins.OtherProviderId), Ct);

        using var scope = api.Runtime.Services.CreateScope();
        var activator = scope.ServiceProvider.GetRequiredService<RouteActivator>();
        var plugins = api.Resolve<PluginRegistry>().Snapshot;

        var frozen = await activator.BuildAsync(plugins, GlobalRouteSource.FromActiveSnapshot, Ct);
        var reread = await activator.BuildAsync(plugins, GlobalRouteSource.FromSettings, Ct);

        Assert.Equal(TestPlugins.FakeProviderId, frozen.Snapshot!.Global.Default!.PluginId);
        Assert.Equal("west", (string?)frozen.Snapshot.Global.Default.Binding!["workspace"]);
        Assert.Equal(TestPlugins.OtherProviderId, reread.Snapshot!.Global.Default!.PluginId);
    }

    /// <summary>The binding fake-provider's manifest requires: a route to it without one carries nothing it asked for.</summary>
    internal static JsonObject Workspace(string name = "west") => new() { ["workspace"] = name };

    private static PluginSnapshot SnapshotOf(LoadedPlugin plugin) =>
        new("snp_built", DateTime.UnixEpoch, SnapshotSource.Reload, [plugin]);

    /// <summary>A plugin built rather than installed, for inputs no installable package could carry.</summary>
    private static LoadedPlugin BuiltPlugin(string id, string[] operations, int[] operationContracts, JsonObject? binding = null) =>
        new(
            new PluginManifest(
                id,
                "1.0.0",
                PluginKind.Provider,
                null,
                null,
                null,
                new ManifestContracts([1], operationContracts),
                operations,
                new PluginEntry("main.js", "invoke"),
                new CapabilityRequests(null, null, null),
                new ManifestLimits(null, null),
                binding),
            Root: "/built",
            Digest: "sha256:00",
            Executables: [],
            ResolvedGrants.None,
            new EffectivePluginLimits(60_000, 64),
            PluginStatus.Valid,
            Problems: []);

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

    /// <summary>
    /// A campaign route written straight into the row the activator reads. <c>route.set</c> does not exist yet;
    /// what this task owes is that the rows are checked, whoever wrote them.
    /// </summary>
    private static async Task WriteRouteAsync(RuntimeApiFixture api, string campaign, string? operation, string plugin, JsonObject? binding)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var id = await db.Campaigns.Where(c => c.PublicId == campaign).Select(c => c.Id).SingleAsync(Ct);
        db.CampaignRoutes.Add(new CampaignRoute
        {
            CampaignId = id,
            Operation = operation,
            PluginId = plugin,
            Binding = binding,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The single route problem a reload was refused for, asserted the same way every case asserts it.</summary>
    private static async Task<ErrorDetail> RefusedAsync(RuntimeApiFixture api)
    {
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        return Assert.Single(error.Details!);
    }

    internal static Task<RuntimeApiFixture> StartAsync(string? routes = null, Action<JasonPaths>? prepare = null) =>
        RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            prepare?.Invoke(paths);
            if (routes is not null)
            {
                TestRoutes.WriteGlobal(paths, routes);
            }
        });
}
