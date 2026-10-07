using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A start whose package cannot finish its binding probe on either of its two loads. Nobody is there to send a reload,
/// so the machine's state is not taken for the package's fault: the package holds back only itself, as
/// <c>unavailable</c> with <c>probe_timed_out</c>, and the rest of the load stands — routes to the package included, frozen
/// with their bindings unmeasured, because every claim asks availability before it measures a binding and the next load
/// measures everything. Only the measurement is skipped, and only for that plugin.
/// </summary>
public sealed class ProbeTimedOutAtStartTests
{
    private const string Patterned = "patterned-provider";
    private const string Plain = "plain-provider";
    private const string NeedsATool = "needs-a-tool";
    private const string BindingPattern = "^[a-z]+$";

    private static readonly string ProbedPattern = TimingOut.AsCompiled(BindingPattern);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string BindingSchema =>
        $"binding:\n  type: object\n  additionalProperties: false\n  required: [workspace]\n  properties:\n    workspace:\n      type: string\n      pattern: \"{BindingPattern}\"\n";

    /// <summary>
    /// The package loads unavailable, the other package and every route stand, and the binding of the route to the held-back
    /// plugin was never measured: two loads, each probing once and once more warm, and nothing more.
    /// </summary>
    [Fact]
    public async Task A_package_whose_probe_never_finishes_at_the_start_holds_back_only_itself_and_its_routes_stand_unmeasured()
    {
        var probing = new TimingOut(int.MaxValue) { OnlyPattern = ProbedPattern };

        await using var api = await StartAsync(probing, Routes(new JsonObject { ["workspace"] = "east" }));

        var plugins = api.Resolve<PluginRegistry>();
        Assert.True(plugins.LastReload!.Activated);
        var held = plugins.Snapshot.Find(Patterned)!;
        Assert.Equal(PluginStatus.Unavailable, held.Status);
        var problem = Assert.Single(held.Problems);
        Assert.Equal((ProblemCodes.ProbeTimedOut, "binding#/properties/workspace/pattern"), (problem.Code, problem.Path));
        Assert.Equal(PluginStatus.Valid, plugins.Snapshot.Find(Plain)!.Status);

        var routes = await api.PostOkAsync<RoutesDto>(Operations.RouteList, new { }, Ct);
        Assert.Equal(Plain, routes.Global.Default!.PluginId);
        Assert.Equal(Patterned, routes.Global.Operations["campaign.get"].PluginId);
        Assert.Equal(4, probing.TimedOut);
    }

    /// <summary>
    /// What a claim on that route decides, as <c>route.resolve</c> answers it through the claim's own routable check: the
    /// plugin is unavailable, at check 4, before the binding is ever measured.
    /// </summary>
    [Fact]
    public async Task Work_on_a_route_to_the_held_back_plugin_ends_plugin_unavailable()
    {
        var probing = new TimingOut(int.MaxValue) { OnlyPattern = ProbedPattern };
        await using var api = await StartAsync(probing, Routes(new JsonObject { ["workspace"] = "east" }));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

        var resolved = await api.PostOkAsync<RouteResolutionDto>(Operations.RouteResolve, new { campaign_id = campaign, operation = "campaign.get" }, Ct);

        Assert.False(resolved.Usable);
        Assert.Equal("plugin_unavailable", Assert.Single(resolved.Problems).Code);
        Assert.Equal(4, probing.TimedOut);
    }

    /// <summary>
    /// A route write under such a snapshot neither times out nor refuses for the held-back plugin's binding: the write's own
    /// route and the active global half it rechecks are both left unmeasured, so a route write for any plugin still works.
    /// </summary>
    [Fact]
    public async Task A_route_write_under_that_snapshot_neither_times_out_nor_refuses()
    {
        var probing = new TimingOut(int.MaxValue) { OnlyPattern = ProbedPattern };
        await using var api = await StartAsync(probing, Routes(new JsonObject { ["workspace"] = "east" }));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;

        await api.PostOkAsync<RoutesDto>(
            Operations.RouteSet, new { campaign_id = campaign, operation = "campaign.get", plugin = Patterned, binding = new { workspace = "north" } }, Ct);
        await api.PostOkAsync<RoutesDto>(
            Operations.RouteSet, new { campaign_id = campaign, operation = "enrollment.list", plugin = Plain }, Ct);

        Assert.Equal(4, probing.TimedOut);
    }

    /// <summary>Only the measurement is skipped: a route to the held-back plugin whose binding names a credential is still refused.</summary>
    [Fact]
    public async Task A_route_to_the_held_back_plugin_whose_binding_names_a_credential_is_still_refused()
    {
        var probing = new TimingOut(int.MaxValue) { OnlyPattern = ProbedPattern };

        await using var api = await StartAsync(probing, Routes(new JsonObject { ["workspace"] = "east", ["api_key"] = "x" }));

        var plugins = api.Resolve<PluginRegistry>();
        Assert.False(plugins.LastReload!.Activated);
        Assert.Equal(RouteProblemCodes.BindingSecretLike, Assert.Single(plugins.LastReload.Routes).Code);
    }

    /// <summary>
    /// The exemption is exactly as wide as the impossibility: a plugin unavailable for a missing program has patterns that can
    /// answer, so a route to it is measured as ever, and a binding its schema refuses is refused at activation.
    /// </summary>
    [Fact]
    public async Task A_route_to_a_plugin_unavailable_for_another_reason_is_still_measured_and_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.Write(
                    paths,
                    NeedsATool,
                    TestPlugins.Manifest(NeedsATool, "[campaign.get]", BindingSchema + "capabilities:\n  exec:\n    executables:\n      - name: jason-no-such-program-anywhere\n"),
                    "export function invoke() { return { result: {} }; }");
                TestRoutes.WriteGlobal(
                    paths,
                    new JsonObject { ["Default"] = new JsonObject { ["Plugin"] = NeedsATool, ["Binding"] = new JsonObject { ["workspace"] = "WEST" } } }.ToJsonString());
            });

        var plugins = api.Resolve<PluginRegistry>();
        Assert.Equal(CandidateStatus.Unavailable, Assert.Single(plugins.LastReload!.Candidates).Status);
        Assert.False(plugins.LastReload.Activated);
        Assert.Equal(RouteProblemCodes.BindingInvalid, Assert.Single(plugins.LastReload.Routes).Code);
    }

    private static string Routes(JsonObject patternedBinding) =>
        new JsonObject
        {
            ["Default"] = new JsonObject { ["Plugin"] = Plain },
            ["Operations"] = new JsonObject { ["campaign.get"] = new JsonObject { ["Plugin"] = Patterned, ["Binding"] = patternedBinding } },
        }.ToJsonString();

    private static Task<RuntimeApiFixture> StartAsync(TimingOut probing, string routes) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.Write(paths, Patterned, TestPlugins.Manifest(Patterned, "[campaign.get]", BindingSchema), "export function invoke() { return { result: {} }; }");
                TestPlugins.Write(paths, Plain, TestPlugins.Manifest(Plain, "[campaign.get, enrollment.list]"), "export function invoke() { return { result: {} }; }");
                TestRoutes.WriteGlobal(paths, routes);
            },
            configureServices: services => services.AddSingleton<PatternTiming>(probing));
}
