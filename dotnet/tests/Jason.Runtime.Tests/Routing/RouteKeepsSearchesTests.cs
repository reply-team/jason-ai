using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A route is where a binding is kept, and a search is read and stopped only at the plugin and account it ran under. So a
/// route change that would take the last active route naming that account away from a search still followed is refused,
/// naming the search, whichever door it comes through: <c>route.set</c>, <c>route.unset</c> or a reload. The same change
/// goes through while another route still names the account, and once the search has ended.
/// </summary>
public class RouteKeepsSearchesTests
{
    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject West => RouteActivationTests.Workspace("west");

    private static JsonObject East => RouteActivationTests.Workspace("east");

    [Fact]
    public async Task Moving_the_only_route_off_a_running_search_s_account_is_refused_naming_the_search()
    {
        await using var api = await RouteActivationTests.StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(campaign, West), Ct);
        var search = await UnfinishedSearchAsync(api, campaign, West);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        var moved = await api.RecordErrorAsync(Operations.RouteSet, "orphans-search", Set(campaign, East), HttpStatusCode.Conflict, Ct);
        var unset = await api.PostErrorAsync(Operations.RouteUnset, new { campaign_id = campaign }, HttpStatusCode.Conflict, Ct);

        foreach (var refused in new[] { moved, unset })
        {
            Assert.Equal((RouteProblemCodes.OrphansSearch, false), (refused.Code, refused.Retryable));
            var detail = Assert.Single(refused.Details!);
            Assert.Equal((RouteActivator.SearchField(campaign, search), RouteProblemCodes.OrphansSearch), (detail.Field, detail.Code));
            Assert.Contains(search, detail.Message, StringComparison.Ordinal);
            Assert.Contains(campaign, detail.Message, StringComparison.Ordinal);
            Assert.Contains("audience.search_cancel", detail.Message, StringComparison.Ordinal);
        }

        Assert.Same(before, api.Resolve<RouteRegistry>().Snapshot);
        await using var db = Open(api);
        var row = await db.CampaignRoutes.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(BindingIdentity.Of(West), BindingIdentity.Of(row.Binding));
    }

    [Fact]
    public async Task The_same_move_goes_through_once_the_search_has_ended()
    {
        await using var api = await RouteActivationTests.StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(campaign, West), Ct);
        await UnfinishedSearchAsync(api, campaign, West);
        Assert.Equal(
            RouteProblemCodes.OrphansSearch,
            (await api.PostErrorAsync(Operations.RouteSet, Set(campaign, East), HttpStatusCode.Conflict, Ct)).Code);

        await using (var db = Open(api))
        {
            await db.AudienceSearches.ExecuteUpdateAsync(set => set.SetProperty(row => row.EndedAs, "cancelled").SetProperty(row => row.EndedAt, Noon), Ct);
        }

        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(campaign, East), Ct);

        Assert.Equal(BindingIdentity.Of(East), api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].Default!.BindingIdentity);
    }

    [Fact]
    public async Task A_reload_carrying_the_move_is_refused_and_the_routes_in_force_stay_active()
    {
        await using var api = await RouteActivationTests.StartAsync(
            TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, West),
            paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        var search = await UnfinishedSearchAsync(api, campaign, West);
        var before = api.Resolve<RouteRegistry>().Snapshot;

        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, East), Ct);
        var refused = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", refused.Code);
        var detail = Assert.Single(refused.Details!);
        Assert.Equal((RouteActivator.SearchField(campaign, search), RouteProblemCodes.OrphansSearch), (detail.Field, detail.Code));
        Assert.Contains(search, detail.Message, StringComparison.Ordinal);

        Assert.Same(before, api.Resolve<RouteRegistry>().Snapshot);
        Assert.Equal(BindingIdentity.Of(West), before.Global.Default!.BindingIdentity);
        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, new { }, Ct);
        Assert.False(registry.Activated);
        var recorded = Assert.Single(registry.LastReload!.Routes);
        Assert.Equal((RouteActivator.SearchField(campaign, search), RouteProblemCodes.OrphansSearch), (recorded.Route, recorded.Code));
    }

    [Fact]
    public async Task A_move_that_leaves_another_route_naming_the_account_goes_through()
    {
        await using var api = await RouteActivationTests.StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var campaign = await CampaignAsync(api);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(campaign, West), Ct);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(campaign, West, "campaign.get"), Ct);
        await UnfinishedSearchAsync(api, campaign, West);

        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(campaign, East), Ct);

        Assert.Equal(BindingIdentity.Of(East), api.Resolve<RouteRegistry>().Snapshot.Campaigns[campaign].Default!.BindingIdentity);
        Assert.Equal(
            RouteProblemCodes.OrphansSearch,
            (await api.PostErrorAsync(Operations.RouteUnset, new { campaign_id = campaign, operation = "campaign.get" }, HttpStatusCode.Conflict, Ct)).Code);
    }

    /// <summary>
    /// An archived campaign's routes are history, but while one of its searches is still followed they stay in the set: the
    /// stop the archive made goes where the search ran. So archiving such a campaign locks nothing — a reload that changes
    /// nothing and another campaign's route change go through — while an uninstall of the plugin the search ran at, which
    /// really takes its account away, is still refused, naming the search and the stop it waits for.
    /// </summary>
    [Fact]
    public async Task Archiving_a_campaign_with_a_followed_search_locks_no_other_change_and_still_keeps_its_account()
    {
        await using var api = await RouteActivationTests.StartAsync(
            prepare: paths =>
            {
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.InstallOtherProvider(paths);
            });
        var archived = await CampaignAsync(api);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(archived, West), Ct);
        var search = await UnfinishedSearchAsync(api, archived, West);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = archived }, Ct);

        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, new { campaign_id = await CampaignAsync(api), plugin = TestPlugins.OtherProviderId }, Ct);

        Directory.Delete(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), recursive: true);
        var refused = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);
        var detail = Assert.Single(refused.Details!);
        Assert.Equal((RouteActivator.SearchField(archived, search), RouteProblemCodes.OrphansSearch), (detail.Field, detail.Code));
        Assert.Contains("archived", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stop an archive makes is claimed after the archive, and any route change in between rebuilds the set: the
    /// archived campaign's route still names the account the search ran under, so the stop is routed where the search ran.
    /// Once the search has ended, the next rebuild leaves the archived campaign's routes out again.
    /// </summary>
    [Fact]
    public async Task An_archived_campaign_s_stop_still_finds_its_account_after_a_rebuild_before_it_runs()
    {
        await using var api = await RouteActivationTests.StartAsync(prepare: paths => TestPlugins.InstallFakeProvider(paths));
        var archived = await CampaignAsync(api);
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(archived, West), Ct);
        await UnfinishedSearchAsync(api, archived, West);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = archived }, Ct);

        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Set(await CampaignAsync(api), East), Ct);

        var stop = RouteResolver.ForSearch(
            api.Resolve<RouteRegistry>().Snapshot, archived, "audience.search_cancel", "audience.search", TestPlugins.FakeProviderId, BindingIdentity.Of(West));
        Assert.Equal((TestPlugins.FakeProviderId, BindingIdentity.Of(West)), (stop?.Route.PluginId, stop?.Route.BindingIdentity));

        await using (var db = Open(api))
        {
            await db.AudienceSearches.ExecuteUpdateAsync(set => set.SetProperty(row => row.EndedAs, "cancelled").SetProperty(row => row.EndedAt, Noon), Ct);
        }

        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        Assert.False(api.Resolve<RouteRegistry>().Snapshot.Campaigns.ContainsKey(archived));
    }

    /// <summary>
    /// A start is not one of the doors a change is refused at: it has no routes in force to keep, and a start whose routes
    /// are refused leaves no plugin active at all. So the settings edited while the runtime was stopped, moving the only
    /// route off a search's account, are activated, and the search they leave without a route is said in the log, with
    /// its campaign and the way out, at the start and at each later activation while it lasts. A search already left
    /// without a route refuses no later change: only a change that would itself take the account away is refused.
    /// </summary>
    [Fact]
    public async Task A_start_with_the_move_already_in_the_settings_activates_it_and_says_the_search_in_the_log()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        TestPlugins.InstallFakeProvider(dir.Paths);
        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, West));
        string campaign;
        string search;
        await using (var first = await RuntimeHost.StartAsync(dir.Paths, Options, Ct))
        {
            await using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
            var owner = WorkItemFactory.NewCampaign("Searched", now: Noon);
            db.Campaigns.Add(owner);
            await db.SaveChangesAsync(Ct);
            campaign = owner.PublicId;
            search = await UnfinishedSearchAsync(dir.Paths, campaign, West);
        }

        TestRoutes.WriteGlobal(dir.Paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, East));
        await using (var second = await RuntimeHost.StartAsync(dir.Paths, Options, Ct))
        {
            Assert.Equal(BindingIdentity.Of(East), second.Services.GetRequiredService<RouteRegistry>().Snapshot.Global.Default!.BindingIdentity);
            using var http = new HttpClient { BaseAddress = second.BaseUrl };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Token);
            using var moved = await http.PostAsJsonAsync(Operations.Route(Operations.RouteSet), Set(campaign, East), JasonJson.Options, Ct);
            Assert.True(moved.StatusCode == HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        }

        var said = Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Contains(search, StringComparison.Ordinal))
            .Select(line => JsonNode.Parse(line)!)
            .ToList();
        Assert.Equal(2, said.Count);
        Assert.All(said, line =>
        {
            Assert.Equal("Warning", (string?)line["@l"]);
            Assert.Equal(campaign, (string?)line["CampaignId"]);
            Assert.Contains("Put such a route back", (string?)line["@mt"], StringComparison.Ordinal);
        });
    }

    private static RuntimeHostOptions Options => TestRuntimeOptions.Quiet with
    {
        ConfigureServices = services =>
        {
            TestRuntimeOptions.TurnTheOutsideOff(services);
            services.AddSingleton(TestPlugins.SearchPath);
        },
    };

    private static object Set(string campaign, JsonObject binding, string? operation = null) =>
        new { campaign_id = campaign, operation, plugin = TestPlugins.FakeProviderId, binding };

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Searched" }, Ct)).Id;

    private static JasonDbContext Open(RuntimeApiFixture api) => new(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));

    /// <summary>
    /// A search the provider took and the dispatcher still follows: its work item succeeded under
    /// <paramref name="binding"/>, as its attempt's provenance keeps it, and its row has not ended. Answers its work item.
    /// </summary>
    private static Task<string> UnfinishedSearchAsync(RuntimeApiFixture api, string campaign, JsonObject binding) =>
        UnfinishedSearchAsync(api.Paths, campaign, binding);

    private static async Task<string> UnfinishedSearchAsync(JasonPaths paths, string campaign, JsonObject binding)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var owner = await db.Campaigns.SingleAsync(row => row.PublicId == campaign, Ct);
        var audience = new Audience
        {
            PublicId = PublicIdOf("aud"),
            Campaign = owner,
            Number = 1,
            Definition = new JsonObject { ["industries"] = new JsonArray("Software") },
            DefinitionHash = "sha256:" + new string('1', 64),
            CreatedAt = Noon,
        };
        var item = WorkItemFactory.NewProviderOp(owner, "audience.search", Noon);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Noon;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            TestPlugins.FakeProviderId, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            "audience.search", 1, null, null, RouteScope.CampaignDefault, BindingIdentity.Of(binding));
        db.Audiences.Add(audience);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        db.AudienceSearches.Add(new AudienceSearch
        {
            Campaign = owner,
            Audience = audience,
            SearchWorkItem = item,
            PluginId = TestPlugins.FakeProviderId,
            Reference = "srch-1",
            State = "in_progress",
            Asked = 50,
            QuietSince = Noon,
            CreatedAt = Noon,
        });
        await db.SaveChangesAsync(Ct);
        return item.PublicId;
    }

    private static string PublicIdOf(string prefix) => Jason.Contracts.Ids.PublicId.New(prefix);
}
