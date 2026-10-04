using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Every act that activates a route set — a route write, a route removal, a reload, the start — and the read that
/// predicts the claim, each meeting a binding no pattern could finish measuring. Nothing was found wrong with such a
/// binding, so it is never listed among a set's refusals: alone it makes the act one to ask again (or, at the start,
/// where nobody is there to ask, one to build once more), and beside a real problem it is left out of the refusal that
/// real problem earns.
/// </summary>
public sealed class PatternTimeoutAtActivationTests
{
    private const string PluginId = "patterned-provider";

    /// <summary>The binding pattern of the package these tests install; nothing the runtime publishes uses it.</summary>
    private const string BindingPattern = "^[a-z]+$";

    /// <summary>The one binding value these tests let time out; the warm-up and the load's probe never meet it.</summary>
    private const string West = "west";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Binding(string workspace) => new() { ["workspace"] = workspace };

    /// <summary>
    /// <c>route.set</c> checks the route it writes, and then activates the whole set with it: another route's binding
    /// that went unmeasured in that build leaves nothing written and nothing activated, and the write is one to send again.
    /// </summary>
    [Fact]
    public async Task A_route_set_whose_activation_could_not_measure_another_route_is_answered_503_and_writes_nothing()
    {
        var timing = new TimingOut(0, value => value == West);
        await using var api = await StartWithGlobalRouteAsync(timing);
        var campaign = await CampaignAsync(api);
        timing.TimeOutNext(int.MaxValue);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = PluginId, binding = Binding("east") },
            HttpStatusCode.ServiceUnavailable,
            Ct);

        Assert.Equal(("validation_timed_out", true), (error.Code, error.Retryable));
        Assert.Equal((RouteActivator.GlobalDefaultField, SchemaValidator.PatternTimedOut), (Assert.Single(error.Details!).Field, error.Details![0].Code));
        Assert.Empty((await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { }, Ct)).Campaigns);
    }

    /// <summary>A removal activates the set too, and is answered the same way: the route it would have removed is still there.</summary>
    [Fact]
    public async Task A_route_unset_whose_activation_could_not_measure_another_route_is_answered_503_and_removes_nothing()
    {
        var timing = new TimingOut(0, value => value == West);
        await using var api = await StartWithGlobalRouteAsync(timing);
        var campaign = await CampaignAsync(api);
        await api.PostOkAsync<RoutesDto>(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = PluginId, binding = Binding("east") },
            Ct);
        timing.TimeOutNext(int.MaxValue);

        var error = await api.PostErrorAsync(
            Operations.RouteUnset,
            new { campaign_id = campaign, operation = "campaign.get" },
            HttpStatusCode.ServiceUnavailable,
            Ct);

        Assert.Equal(("validation_timed_out", true), (error.Code, error.Retryable));
        Assert.Single((await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { }, Ct)).Campaigns);
    }

    /// <summary>
    /// Beside a route that really is wrong, the unmeasured binding is no part of the refusal: the 409 names what is
    /// wrong, and only that, as every other place that measures a value does.
    /// </summary>
    [Fact]
    public async Task A_route_set_refused_for_another_route_s_real_problem_names_only_that_problem()
    {
        var timing = new TimingOut(0, value => value == West);
        await using var api = await StartWithGlobalRouteAsync(timing);
        var campaign = await CampaignAsync(api);
        var stranded = await CampaignAsync(api);
        await StrandAsync(api, stranded);
        timing.TimeOutNext(int.MaxValue);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = PluginId, binding = Binding("east") },
            HttpStatusCode.Conflict,
            Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal((RouteActivator.CampaignField(stranded, "campaign.get"), RouteProblemCodes.PluginUnknown), (detail.Field, detail.Code));
    }

    /// <summary>
    /// A real route problem is deterministic, so a start that meets one beside an unmeasured binding fails closed at once
    /// — building the set again could change only the report — and the report names the real problem alone. A reload of
    /// the same set is refused the same way.
    /// </summary>
    [Fact]
    public async Task A_start_with_a_real_route_problem_beside_an_unmeasured_binding_fails_closed_at_once_naming_only_the_problem()
    {
        var timing = TimingOut.Always(value => value == West);
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                Install(paths);
                TestRoutes.WriteGlobal(
                    paths,
                    new JsonObject
                    {
                        ["Default"] = new JsonObject { ["Plugin"] = PluginId, ["Binding"] = Binding(West) },
                        ["Operations"] = new JsonObject { ["campaign.get"] = new JsonObject { ["Plugin"] = "no-such-plugin" } },
                    }.ToJsonString());
            },
            configureServices: services => services.AddSingleton<PatternTiming>(timing));

        var plugins = api.Resolve<PluginRegistry>();
        Assert.Null(plugins.Snapshot.Find(PluginId));
        Assert.Equal(RouteProblemCodes.PluginUnknown, Assert.Single(plugins.LastReload!.Routes).Code);
        Assert.Equal(2, timing.TimedOut);

        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal(RouteProblemCodes.PluginUnknown, Assert.Single(error.Details!).Code);
    }

    /// <summary>
    /// The start builds the whole load once more when a package's binding probe is all that held it back: the probe is
    /// a pattern's cost on this machine at this moment, and nobody is there to send a reload again.
    /// </summary>
    [Fact]
    public async Task A_start_whose_first_load_could_not_finish_a_binding_probe_loads_the_package_on_the_second()
    {
        var timing = new TimingOut(2) { OnlyPattern = BindingPattern };

        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: Install, configureServices: services => services.AddSingleton<PatternTiming>(timing));

        Assert.NotNull(api.Resolve<PluginRegistry>().Snapshot.Find(PluginId));
        Assert.True(api.Resolve<PluginRegistry>().LastReload!.Activated);
        Assert.Equal(2, timing.TimedOut);
    }

    /// <summary>Once more and only once: a probe that still cannot finish fails the start closed, as any package problem does.</summary>
    [Fact]
    public async Task A_start_whose_binding_probe_cannot_finish_twice_over_fails_closed_after_one_more_load()
    {
        var probing = new TimingOut(int.MaxValue) { OnlyPattern = BindingPattern };

        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: Install, configureServices: services => services.AddSingleton<PatternTiming>(probing));

        var plugins = api.Resolve<PluginRegistry>();
        Assert.Null(plugins.Snapshot.Find(PluginId));
        Assert.False(plugins.LastReload!.Activated);
        var problem = Assert.Single(Assert.Single(plugins.LastReload.Candidates).Problems);
        Assert.Equal(("field_invalid", "binding#/properties/workspace/pattern"), (problem.Code, problem.Path));

        // Two loads, each probing once and once more warm.
        Assert.Equal(4, probing.TimedOut);
    }

    /// <summary>A probe timeout beside a package that is really broken is not all that held the load back: no second load.</summary>
    [Fact]
    public async Task A_start_whose_probe_timed_out_beside_a_broken_package_fails_closed_at_once()
    {
        var probing = new TimingOut(int.MaxValue) { OnlyPattern = BindingPattern };

        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                Install(paths);
                TestPlugins.Write(paths, "broken-provider", TestPlugins.Manifest("broken-provider", "[campaign.get]").Replace("version: 1.0.0", "version: one", StringComparison.Ordinal), "export function invoke() { return { result: {} }; }");
            },
            configureServices: services => services.AddSingleton<PatternTiming>(probing));

        Assert.Null(api.Resolve<PluginRegistry>().Snapshot.Find(PluginId));
        Assert.Equal(2, probing.TimedOut);
    }

    /// <summary>
    /// <c>route.resolve</c> answers what the claim would decide, and the claim would end such an item transient rather
    /// than refuse it: a binding nobody measured is not "cannot run", so the read is answered as one to ask again.
    /// </summary>
    [Fact]
    public async Task A_route_resolve_whose_binding_a_pattern_could_not_measure_is_answered_503()
    {
        var timing = new TimingOut(0, value => value == West);
        await using var api = await StartWithGlobalRouteAsync(timing);
        var campaign = await CampaignAsync(api);
        timing.TimeOutNext(int.MaxValue);

        var error = await api.PostErrorAsync(
            Operations.RouteResolve,
            new { campaign_id = campaign, operation = "campaign.get" },
            HttpStatusCode.ServiceUnavailable,
            Ct);

        Assert.Equal(("validation_timed_out", true), (error.Code, error.Retryable));
        Assert.Equal(RouteActivator.GlobalDefaultField, Assert.Single(error.Details!).Field);
    }

    /// <summary>A route row naming a plugin that is not installed, written beneath the API the way a stale row would be.</summary>
    private static async Task StrandAsync(RuntimeApiFixture api, string campaignId)
    {
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = await db.Campaigns.SingleAsync(row => row.PublicId == campaignId, Ct);
        db.CampaignRoutes.Add(new CampaignRoute { CampaignId = campaign.Id, Operation = "campaign.get", PluginId = "no-such-plugin", UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);
    }

    private static void Install(JasonPaths paths)
    {
        File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        TestPlugins.Write(
            paths,
            PluginId,
            TestPlugins.Manifest(
                PluginId,
                "[campaign.get]",
                $"binding:\n  type: object\n  additionalProperties: false\n  required: [workspace]\n  properties:\n    workspace:\n      type: string\n      pattern: \"{BindingPattern}\"\n"),
            "export function invoke() { return { result: {} }; }");
    }

    private static Task<RuntimeApiFixture> StartWithGlobalRouteAsync(PatternTiming timing) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                Install(paths);
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(PluginId, Binding(West)));
            },
            configureServices: services => services.AddSingleton(timing));

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;
}
