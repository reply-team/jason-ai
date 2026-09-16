using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The four <c>route.*</c> operations. Reading a route is answering the question the claim will ask, from the
/// snapshot the claim will read; writing one is a small activation of its own, over a global set that stays
/// exactly as the last reload froze it.
/// </summary>
public class RouteApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolve_answers_with_the_winning_scope_and_the_snapshot_it_answered_from()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);

        var resolution = await api.PostOkAsync<RouteResolutionDto>(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = "campaign.get" },
            Ct);

        Assert.Equal(TestPlugins.FakeProviderId, resolution.PluginId);
        Assert.Equal(RouteScope.GlobalDefault, resolution.Scope);
        Assert.True(resolution.Usable);
        Assert.Empty(resolution.Problems);
        Assert.StartsWith("rts_", resolution.RoutingSnapshotId, StringComparison.Ordinal);
        Assert.Equal(1, resolution.OperationVersion);
    }

    /// <summary>
    /// What an operator is really asking when they ask about a route: which package, at which digest, would be
    /// handed this work — and which of the four levels decided it.
    /// </summary>
    [Fact]
    public async Task Resolve_names_the_package_a_run_would_reach_and_the_binding_it_would_carry()
    {
        await using var api = await StartAsync(
            new JsonObject
            {
                ["Default"] = new JsonObject { ["Plugin"] = TestPlugins.OtherProviderId },
                ["Operations"] = new JsonObject
                {
                    ["campaign.get"] = new JsonObject
                    {
                        ["Plugin"] = TestPlugins.FakeProviderId,
                        ["Binding"] = new JsonObject { ["workspace"] = "east" },
                    },
                },
            }.ToJsonString());
        var campaign = await CampaignAsync(api);

        var resolution = await api.PostOkAsync<RouteResolutionDto>(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = "campaign.get" },
            Ct);

        Assert.Equal(RouteScope.GlobalOperation, resolution.Scope);
        Assert.Equal("1.0.0", resolution.PluginVersion);
        Assert.Equal(PluginStatus.Valid, resolution.PluginStatus);
        Assert.StartsWith("sha256:", resolution.Digest!, StringComparison.Ordinal);
        Assert.Equal("east", (string?)resolution.Binding!["workspace"]);
        Assert.StartsWith("sha256:", resolution.BindingIdentity!, StringComparison.Ordinal);
        Assert.Equal(api.Resolve<PluginRegistry>().Snapshot.Id, resolution.PluginSnapshotId);
    }

    /// <summary>
    /// Nothing routed anywhere is a configuration answer, and it is answered as the business error it is: a
    /// campaign that has never been pointed at a provider is not a runtime that is broken.
    /// </summary>
    [Fact]
    public async Task Resolving_an_operation_nothing_routes_is_the_business_error_no_route()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = "campaign.get" },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal(AttemptErrors.NoRoute, error.Code);
        Assert.False(error.Retryable);
    }

    /// <summary>
    /// The distinction the whole verb exists for. A route that resolves and cannot run is <b>not</b> refused:
    /// it is answered in full, with the plugin it names and the reason the claim would give, because an
    /// operator needs the diagnosis and not only the verdict.
    /// </summary>
    [Fact]
    public async Task A_route_that_resolves_but_cannot_run_is_answered_in_full_rather_than_refused()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.OtherProviderId));
        var campaign = await CampaignAsync(api);

        var resolution = await api.PostOkAsync<RouteResolutionDto>(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = "campaign.enroll" },
            Ct);

        Assert.False(resolution.Usable);
        Assert.Equal(TestPlugins.OtherProviderId, resolution.PluginId);
        Assert.Equal(RouteScope.GlobalDefault, resolution.Scope);

        // The code is the one the attempt would carry, and the field is the route as the operator wrote it.
        var problem = Assert.Single(resolution.Problems);
        Assert.Equal(RouteActivator.GlobalDefaultField, problem.Field);
        Assert.Equal(AttemptErrors.PluginOperationUnsupported, problem.Code);
        Assert.Contains("campaign.enroll", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolving_an_operation_no_build_publishes_is_a_bad_request()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = "not.an.operation" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("operation", detail.Field);
        Assert.Equal("unknown", detail.Code);
    }

    [Fact]
    public async Task Resolving_for_a_campaign_nobody_can_name_says_so()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));

        var error = await api.PostErrorAsync(
            Operations.RouteResolve,
            new { campaign_id = "cmp_NOPE", operation = "campaign.get" },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("campaign_not_found", error.Code);
    }

    [Fact]
    public async Task Listing_answers_the_global_set_and_every_campaign_that_has_a_route()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var first = await CampaignAsync(api);
        var second = await CampaignAsync(api);
        await TestRoutes.SetCampaignAsync(api, first, "campaign.get", TestPlugins.OtherProviderId, binding: null, Ct);
        await TestRoutes.SetCampaignAsync(api, second, operation: null, TestPlugins.OtherProviderId, binding: null, Ct);

        var listed = await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { }, Ct);

        Assert.Equal(TestPlugins.FakeProviderId, listed.Global.Default!.PluginId);
        Assert.Equal("west", (string?)listed.Global.Default.Binding!["workspace"]);
        Assert.Equal(2, listed.Campaigns.Count);
        Assert.Equal(TestPlugins.OtherProviderId, listed.Campaigns.Single(set => set.CampaignId == first).Operations["campaign.get"].PluginId);
        Assert.Equal(TestPlugins.OtherProviderId, listed.Campaigns.Single(set => set.CampaignId == second).Default!.PluginId);
    }

    /// <summary>
    /// INV-CAMP-001, as the one place a reader of this API could see across the wall: a campaign asked about by
    /// name answers with its own routes and with nobody else's.
    /// </summary>
    [Fact]
    public async Task Listing_one_campaigns_routes_never_shows_another_campaigns()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var first = await CampaignAsync(api);
        var second = await CampaignAsync(api);
        await TestRoutes.SetCampaignAsync(api, first, "campaign.get", TestPlugins.FakeProviderId, RouteActivationTests.Workspace(), Ct);
        await TestRoutes.SetCampaignAsync(api, second, "campaign.get", TestPlugins.OtherProviderId, binding: null, Ct);

        var listed = await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { campaign_id = first }, Ct);

        var only = Assert.Single(listed.Campaigns);
        Assert.Equal(first, only.CampaignId);
        Assert.Equal(TestPlugins.FakeProviderId, only.Operations["campaign.get"].PluginId);

        // The global set is still answered: it is what this campaign falls back to for every other operation.
        Assert.Equal(TestPlugins.FakeProviderId, listed.Global.Default!.PluginId);
    }

    /// <summary>
    /// A5. Changing one campaign's route is not the act that activates an edit to the settings file. The file is
    /// edited while the runtime is live, a campaign route is written, and the global set is exactly what the
    /// last reload froze — until a reload says otherwise.
    /// </summary>
    [Fact]
    public async Task A_campaign_route_does_not_pick_up_an_edited_settings_file()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(TestPlugins.OtherProviderId), Ct);

        var afterSet = await api.PostOkAsync<RoutesDto>(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = TestPlugins.OtherProviderId },
            Ct);

        Assert.Equal(TestPlugins.FakeProviderId, afterSet.Global.Default!.PluginId);
        Assert.Equal("west", (string?)afterSet.Global.Default.Binding!["workspace"]);

        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        var afterReload = await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { }, Ct);

        Assert.Equal(TestPlugins.OtherProviderId, afterReload.Global.Default!.PluginId);
    }

    /// <summary>
    /// Setting a route is an activation: a new route snapshot over the same plugin snapshot, and a chronicle
    /// entry that says where this campaign's work used to go and where it goes now.
    /// </summary>
    [Fact]
    public async Task Setting_a_route_activates_a_new_snapshot_and_writes_down_what_changed()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        await TestRoutes.SetCampaignAsync(api, campaign, "campaign.get", TestPlugins.FakeProviderId, RouteActivationTests.Workspace("east"), Ct);
        var moved = await api.PostOkAsync<RoutesDto>(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = TestPlugins.OtherProviderId, reason = "the other one owns this list now" },
            Ct);

        Assert.NotEqual(before.Id, moved.RoutingSnapshotId);
        Assert.Equal(before.PluginSnapshotId, moved.PluginSnapshotId);
        Assert.Equal(moved.RoutingSnapshotId, api.Resolve<RouteRegistry>().Snapshot.Id);
        Assert.Equal(TestPlugins.OtherProviderId, Assert.Single(moved.Campaigns).Operations["campaign.get"].PluginId);

        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { CampaignId = campaign, Kind = JournalKinds.RoutesUpdated },
            Ct);
        Assert.Equal(2, entries.Items.Count);
        var latest = entries.Items.Single(entry => entry.Reason is not null);
        Assert.Equal("campaign.get", latest.Key);
        Assert.Equal(TestPlugins.FakeProviderId, (string?)latest.Old!["plugin"]);
        Assert.Equal("east", (string?)latest.Old["binding"]!["workspace"]);
        Assert.Equal(TestPlugins.OtherProviderId, (string?)latest.New!["plugin"]);
        Assert.Equal("the other one owns this list now", latest.Reason);
    }

    /// <summary>
    /// The route is checked against the active plugin set before the row exists, so what comes back is the route
    /// problem itself — not a row that would go on refusing its own activation until somebody deleted it.
    /// </summary>
    [Fact]
    public async Task A_route_that_could_never_be_activated_is_refused_before_the_row_is_written()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.enroll", plugin = TestPlugins.OtherProviderId },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(RouteActivator.CampaignField(campaign, "campaign.enroll"), detail.Field);
        Assert.Equal(RouteProblemCodes.OperationUnsupported, detail.Code);

        // Nothing was written, so a reload that reads the rows again still activates.
        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        Assert.True(reloaded.Activated);
        Assert.Empty(api.Resolve<RouteRegistry>().Snapshot.Campaigns);
    }

    /// <summary>
    /// A binding is bounded where it is written, not only where it is used. The cap belongs here because the
    /// write is the irreversible half: a binding the invocation would refuse has by then been journaled into an
    /// append-only table, listed by <c>route list</c> and answered as usable by <c>route resolve</c>, and every
    /// item through that route would then die at the invocation with nothing naming the route that carried it.
    /// </summary>
    [Fact]
    public async Task A_binding_larger_than_the_protocol_carries_is_refused_before_it_is_written()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);

        // Built rather than parsed: this is the one shape no fixture file should carry, and it is schema-valid
        // for fake-provider, so size is the only thing wrong with it.
        var binding = new JsonObject { ["workspace"] = new string('w', PluginProtocol.MaxBindingBytes) };

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = TestPlugins.FakeProviderId, binding },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(RouteActivator.CampaignField(campaign, "campaign.get"), detail.Field);
        Assert.Equal(RouteProblemCodes.BindingTooLarge, detail.Code);

        // The message says both numbers and never the value: an answer that quoted 64 KiB back would be the
        // same mistake in a different table.
        Assert.Contains(PluginProtocol.MaxBindingBytes.ToString(CultureInfo.InvariantCulture), detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('w', 64), detail.Message, StringComparison.Ordinal);

        // The journal is append-only, so a row written here could never be taken back. Nothing was written.
        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { CampaignId = campaign, Kind = JournalKinds.RoutesUpdated },
            Ct);
        Assert.Empty(entries.Items);
        Assert.Empty(api.Resolve<RouteRegistry>().Snapshot.Campaigns);
    }

    /// <summary>
    /// An archived campaign answers what it already answers everywhere else. A new code for "archived, but about
    /// routes" would be one more thing to learn for no new fact.
    /// </summary>
    [Fact]
    public async Task Setting_a_route_on_an_archived_campaign_is_refused_the_way_archiving_is_refused_everywhere()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { CampaignId = campaign }, Ct);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = TestPlugins.FakeProviderId, binding = new { workspace = "west" } },
            HttpStatusCode.Conflict,
            Ct);

        Assert.Equal("campaign_archived", error.Code);
    }

    /// <summary>Taking a route away gives back whatever it was hiding, which here is the global default.</summary>
    [Fact]
    public async Task Unsetting_a_route_gives_back_what_it_was_hiding()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);
        await TestRoutes.SetCampaignAsync(api, campaign, operation: null, TestPlugins.OtherProviderId, binding: null, Ct);
        Assert.Equal(RouteScope.CampaignDefault, (await ResolveAsync(api, campaign)).Scope);

        var after = await api.PostOkAsync<RoutesDto>(
            Operations.RouteUnset,
            new { campaign_id = campaign, reason = "back to the house account" },
            Ct);

        Assert.Empty(after.Campaigns);
        var resolution = await ResolveAsync(api, campaign);
        Assert.Equal(RouteScope.GlobalDefault, resolution.Scope);
        Assert.Equal(TestPlugins.FakeProviderId, resolution.PluginId);

        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { CampaignId = campaign, Kind = JournalKinds.RoutesUpdated },
            Ct);
        var removal = entries.Items.Single(entry => entry.New is null);
        Assert.Equal(RouteActivator.CampaignDefaultName, removal.Key);
        Assert.Equal(TestPlugins.OtherProviderId, (string?)removal.Old!["plugin"]);
    }

    /// <summary>
    /// Removing a route that is not there is the state the caller asked for, so it is not an error — and it
    /// activates nothing, because a snapshot that says the same thing is not a new snapshot.
    /// </summary>
    [Fact]
    public async Task Unsetting_a_route_that_is_not_there_changes_nothing()
    {
        await using var api = await StartAsync(TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        var campaign = await CampaignAsync(api);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        var after = await api.PostOkAsync<RoutesDto>(Operations.RouteUnset, new { campaign_id = campaign, operation = "campaign.get" }, Ct);

        Assert.Equal(before.Id, after.RoutingSnapshotId);
        Assert.Equal(before.Id, api.Resolve<RouteRegistry>().Snapshot.Id);
        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(
            Operations.JournalList,
            new { CampaignId = campaign, Kind = JournalKinds.RoutesUpdated },
            Ct);
        Assert.Empty(entries.Items);
    }

    /// <summary>
    /// The helper the routing fixtures are written with, held to what it claims. It posted to a verb that did
    /// not exist when it was written, so it is the one helper in that file that nothing proved: a row, an
    /// activation, and both kinds of campaign route readable from the snapshot the moment it returns.
    /// </summary>
    [Fact]
    public async Task The_campaign_route_helper_leaves_the_route_active_the_moment_it_returns()
    {
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        await TestRoutes.SetCampaignAsync(api, campaign, operation: null, TestPlugins.OtherProviderId, binding: null, Ct);
        await TestRoutes.SetCampaignAsync(api, campaign, "campaign.get", TestPlugins.FakeProviderId, RouteActivationTests.Workspace(), Ct);

        var set = api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign];
        Assert.Equal(TestPlugins.OtherProviderId, set.Default!.PluginId);
        Assert.Equal(TestPlugins.FakeProviderId, set.Operations["campaign.get"].PluginId);
        Assert.Equal("west", (string?)set.Operations["campaign.get"].Binding!["workspace"]);
    }

    private static Task<RouteResolutionDto> ResolveAsync(RuntimeApiFixture api, string campaign) =>
        api.PostOkAsync<RouteResolutionDto>(Operations.RouteResolve, new { campaign_id = campaign, operation = "campaign.get" }, Ct);

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

    /// <summary>
    /// Both checked-in providers installed, so a test can move work from one of them to the other — and the
    /// stand-in vendor program on the runtime's own search path, so the reference package loads as valid rather
    /// than as "this machine cannot run it", which is a true answer and the wrong one for a test about routes.
    /// </summary>
    private static Task<RuntimeApiFixture> StartAsync(string? routes = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.InstallOtherProvider(paths);
                if (routes is not null)
                {
                    TestRoutes.WriteGlobal(paths, routes);
                }
            },
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));
}
