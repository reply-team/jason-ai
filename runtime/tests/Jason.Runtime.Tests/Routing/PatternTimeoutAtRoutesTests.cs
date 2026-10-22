using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A route's binding is held to the schema its plugin declares, and that schema is a stranger's, so its patterns run
/// under the dialect's timeout. A pattern that did not finish matching a binding, twice, is no verdict on the binding:
/// it is never <c>route_binding_invalid</c>. Nothing has been written or activated, so the request is answered as one
/// to send again — and a plugin whose patterns cannot finish on a warm engine is refused when it loads, which is
/// where its author can do something about it.
/// </summary>
public sealed class PatternTimeoutAtRoutesTests
{
    private const string PluginId = "patterned-provider";

    /// <summary>The one binding value these tests let time out; the warm-up and the load's probe never meet it.</summary>
    private const string West = "west";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Binding(string workspace = West) => new() { ["workspace"] = workspace };

    [Fact]
    public async Task A_route_set_whose_binding_a_pattern_could_not_measure_is_answered_503_and_writes_nothing()
    {
        await using var api = await StartAsync(TimingOut.Always(value => value == West));
        var campaign = await CampaignAsync(api);

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = PluginId, binding = Binding() },
            HttpStatusCode.ServiceUnavailable,
            Ct);

        Assert.Equal(("validation_timed_out", true), (error.Code, error.Retryable));
        var detail = Assert.Single(error.Details!);
        Assert.Equal((RouteActivator.CampaignField(campaign, "campaign.get"), "pattern_timed_out"), (detail.Field, detail.Code));
        Assert.Empty((await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { }, Ct)).Campaigns);
    }

    [Fact]
    public async Task A_route_set_whose_binding_times_out_once_is_measured_warm_and_written()
    {
        await using var api = await StartAsync(new TimingOut(1, value => value == West));
        var campaign = await CampaignAsync(api);

        var routes = await api.PostOkAsync<RoutesDto>(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = PluginId, binding = Binding() },
            Ct);

        Assert.Equal(PluginId, Assert.Single(routes.Campaigns).Operations["campaign.get"].PluginId);
    }

    /// <summary>A binding wrong somewhere else is wrong whatever the unanswered pattern would have said.</summary>
    [Fact]
    public async Task A_route_set_whose_binding_is_wrong_elsewhere_is_refused_as_route_binding_invalid()
    {
        await using var api = await StartAsync(TimingOut.Always(value => value == West));
        var campaign = await CampaignAsync(api);
        var binding = Binding();
        binding["region"] = "eu";

        var error = await api.PostErrorAsync(
            Operations.RouteSet,
            new { campaign_id = campaign, operation = "campaign.get", plugin = PluginId, binding },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(RouteProblemCodes.BindingInvalid, Assert.Single(error.Details!).Code);
    }

    /// <summary>
    /// A reload checks every route against the candidate plugin set, and a binding no pattern could finish measuring
    /// is the same unanswered question there: nothing is activated, the previous snapshots stay, and the reload is
    /// answered as one to send again rather than rejected for a fault nobody can name.
    /// </summary>
    [Fact]
    public async Task A_reload_whose_route_a_pattern_could_not_measure_is_answered_503_and_activates_nothing()
    {
        await using var api = await StartAsync(TimingOut.Always(value => value == West));
        var before = api.Resolve<PluginRegistry>().Snapshot.Id;
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(PluginId, Binding()), Ct);

        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.ServiceUnavailable, Ct);

        Assert.Equal(("validation_timed_out", true), (error.Code, error.Retryable));
        Assert.Equal(RouteActivator.GlobalDefaultField, Assert.Single(error.Details!).Field);
        Assert.Equal(before, api.Resolve<PluginRegistry>().Snapshot.Id);
    }

    /// <summary>
    /// The load-time half: a plugin's binding patterns are probed on a warm engine, with one retry, and a pattern that
    /// cannot finish is a package problem — the reload is refused and the previous snapshot stays, exactly as for any
    /// other fault in a package.
    /// </summary>
    [Fact]
    [Critical(Family.X1, "an unmeasured pattern never activates")]
    public async Task A_reload_of_a_package_whose_binding_pattern_cannot_finish_keeps_the_previous_snapshot()
    {
        var timing = TimingOut.Never();
        await using var api = await StartAsync(timing);
        var before = api.Resolve<PluginRegistry>().Snapshot;
        Assert.NotNull(before.Find(PluginId));

        // From here no pattern finishes, the probe's warm retry included.
        timing.TimeOutNext(int.MaxValue);
        var error = await api.PostErrorAsync(Operations.PluginReload, new { }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        var problem = Assert.Single(error.Details!, detail => detail.Field.EndsWith("binding#/properties/workspace/pattern", StringComparison.Ordinal));
        Assert.Contains("did not finish matching", problem.Message, StringComparison.Ordinal);
        Assert.Equal(before.Id, api.Resolve<PluginRegistry>().Snapshot.Id);
    }

    /// <summary>
    /// At startup nobody is there to send a reload again, and a start that failed closed over a binding nobody found
    /// wrong would leave every provider item to fail <c>no_route</c> until somebody noticed. So a candidate whose only
    /// problems are unmeasured bindings is built once more — the engine is warm by then — and activated when it holds.
    /// </summary>
    [Fact]
    public async Task A_start_whose_first_route_build_could_not_measure_a_binding_builds_it_once_more_and_activates()
    {
        var timing = new TimingOut(2, value => value == West);

        await using var api = await StartWithRouteAsync(timing);

        Assert.NotNull(api.Resolve<PluginRegistry>().Snapshot.Find(PluginId));
        Assert.Equal(PluginId, api.Resolve<RouteRegistry>().Snapshot.Global.Default?.PluginId);
        Assert.Equal(2, timing.TimedOut);
    }

    /// <summary>
    /// Once more, and only once: a binding that still cannot be measured fails the start closed exactly as any other
    /// route problem does, and the startup report says why in the dialect's own word.
    /// </summary>
    [Fact]
    public async Task A_start_whose_binding_cannot_be_measured_twice_over_fails_closed_after_one_more_build()
    {
        var timing = TimingOut.Always(value => value == West);

        await using var api = await StartWithRouteAsync(timing);

        var plugins = api.Resolve<PluginRegistry>();
        Assert.Null(plugins.Snapshot.Find(PluginId));
        Assert.Null(api.Resolve<RouteRegistry>().Snapshot.Global.Default);
        Assert.False(plugins.LastReload!.Activated);
        var problem = Assert.Single(plugins.LastReload.Routes!);
        Assert.Equal((RouteActivator.GlobalDefaultField, SchemaValidator.PatternTimedOut), (problem.Field, problem.Code));

        // Two builds, each matching once and once more warm: the start tried again exactly once.
        Assert.Equal(4, timing.TimedOut);
    }

    /// <summary>The question both gates ask, answered for a binding no pattern could finish: never the refusal code.</summary>
    [Fact]
    public void Activation_answers_an_unmeasured_binding_with_the_timeout_and_never_route_binding_invalid()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{"workspace":{"type":"string","pattern":"^[a-z]+$"}}}""")!.AsObject();
        var plugins = new PluginSnapshot(
            PublicId.New(PluginProtocol.SnapshotIdPrefix),
            DateTime.UnixEpoch,
            SnapshotSource.Startup,
            [TestPlugins.Loaded(PluginId, ["campaign.get"], binding: schema)]);

        var problem = RouteActivator.Check(plugins, "campaign.get", PluginId, Binding(), TimingOut.Always());

        Assert.Equal(SchemaValidator.PatternTimedOut, problem!.Code);
        Assert.Null(RouteActivator.Check(plugins, "campaign.get", PluginId, Binding(), new TimingOut(1)));
    }

    private static void Install(Jason.Contracts.Discovery.JasonPaths paths)
    {
        File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        TestPlugins.Write(
            paths,
            PluginId,
            TestPlugins.Manifest(
                PluginId,
                "[campaign.get]",
                "binding:\n  type: object\n  additionalProperties: false\n  required: [workspace]\n  properties:\n    workspace:\n      type: string\n      pattern: \"^[a-z]+$\"\n"),
            "export function invoke() { return { result: {} }; }");
    }

    /// <summary>The package installed and a global route to it, carrying the binding these tests let time out.</summary>
    private static Task<RuntimeApiFixture> StartWithRouteAsync(PatternTiming timing) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                Install(paths);
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(PluginId, Binding()));
            },
            configureServices: services => services.AddSingleton(timing));

    private static Task<RuntimeApiFixture> StartAsync(PatternTiming timing) =>
        RuntimeApiFixture.StartAsync(Ct, prepare: Install, configureServices: services => services.AddSingleton(timing));

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;
}
