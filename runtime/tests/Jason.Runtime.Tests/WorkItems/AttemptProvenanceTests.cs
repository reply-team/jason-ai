using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// What an attempt says about the decision that led to it. Provenance is written at claim, as far as resolution
/// got, and nothing that happens afterwards may edit it: an attempt is a record of what happened, not a view of
/// what the world looks like now.
/// </summary>
public class AttemptProvenanceTests
{
    /// <summary>A real dispatcher that never ticks on its own: every scan here is one the test asked for.</summary>
    private const string Idle = """{"Dispatcher":{"TickSeconds":3600},"Roles":{"DefaultEntryCommand":["agent-host"]}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Workspace => new() { ["workspace"] = "west" };

    /// <summary>The <c>Routes</c> section of a runtime that sends every operation to the reference package.</summary>
    private static string ToTheReferenceProvider => TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, Workspace);

    /// <summary>A <c>Routes</c> section that sends nothing anywhere, which is what an installation starts with.</summary>
    private const string Nowhere = "{}";

    /// <summary>A package whose declared program is nowhere on this machine: installed, listed, and held back.</summary>
    private const string HeldBack = "held-back-provider";

    [Fact]
    public async Task A_provider_attempt_says_exactly_what_ran()
    {
        var executor = new Executor();
        await using var api = await StartAsync(executor.Command, ToTheReferenceProvider);
        executor.Answers(api);
        var campaign = await CampaignAsync(api);
        var item = await ProviderItemAsync(api, campaign, "campaign.get", Named("c-7714"));

        await RunOneAsync(api);

        var attempt = await AttemptAsync(api, item);
        Assert.Equal(AttemptStatus.Succeeded, attempt.Status);
        var provenance = attempt.Provenance;
        Assert.NotNull(provenance);
        Assert.Equal(TestPlugins.FakeProviderId, provenance.PluginId);
        Assert.Equal("1.0.0", provenance.PluginVersion);
        Assert.StartsWith("sha256:", provenance.PluginDigest!, StringComparison.Ordinal);
        Assert.Equal(PluginProtocol.CurrentVersion, provenance.ProtocolVersion);
        Assert.Equal(PluginProtocol.OperationContractVersion, provenance.OperationContractVersion);
        Assert.Equal("campaign.get", provenance.Operation);
        Assert.Equal(1, provenance.OperationVersion);
        Assert.StartsWith(PluginProtocol.SnapshotIdPrefix + "_", provenance.PluginSnapshotId!, StringComparison.Ordinal);
        Assert.StartsWith(RouteSnapshot.IdPrefix + "_", provenance.RoutingSnapshotId!, StringComparison.Ordinal);
        Assert.Equal(RouteScope.GlobalDefault, provenance.RouteScope);
        Assert.Equal(BindingIdentity.Of(Workspace), provenance.BindingIdentity);
        Assert.Equal(attempt.Id, provenance.CorrelationId);
    }

    /// <summary>An attempt of the other kind carries none, and its JSON says so by leaving the field out.</summary>
    [Fact]
    public async Task An_agent_attempt_carries_no_provenance_at_all()
    {
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), ToTheReferenceProvider);
        var campaign = await CampaignAsync(api);
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "ai_role", role = "researcher" },
            Ct);

        await RunOneAsync(api);

        var attempt = await AttemptAsync(api, item.Id);
        Assert.Null(attempt.Provenance);

        var (_, body) = await api.PostAsync(Operations.WorkItemGet, new { work_item_id = item.Id }, Ct);
        Assert.DoesNotContain("provenance", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The half of a refusal a manager can act on. Nothing was routed here, so there is no plugin to name and
    /// the record says so with nulls rather than by not existing: the operation, the two snapshots the decision
    /// was made against and the attempt it belongs to are facts either way.
    /// </summary>
    [Fact]
    public async Task An_item_nothing_routed_records_how_far_the_decision_got()
    {
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), Nowhere);
        var campaign = await CampaignAsync(api);
        var item = await ProviderItemAsync(api, campaign, "campaign.get", Named("c-7714"));

        await ScanAsync(api, claimed: 0);

        var attempt = await AttemptAsync(api, item);
        Assert.Equal(AttemptErrors.NoRoute, attempt.Error!.Code);
        var provenance = attempt.Provenance;
        Assert.NotNull(provenance);
        Assert.Equal("campaign.get", provenance.Operation);
        Assert.Equal(1, provenance.OperationVersion);
        Assert.StartsWith(PluginProtocol.SnapshotIdPrefix + "_", provenance.PluginSnapshotId!, StringComparison.Ordinal);
        Assert.StartsWith(RouteSnapshot.IdPrefix + "_", provenance.RoutingSnapshotId!, StringComparison.Ordinal);
        Assert.Equal(attempt.Id, provenance.CorrelationId);
        Assert.Null(provenance.PluginId);
        Assert.Null(provenance.PluginVersion);
        Assert.Null(provenance.PluginDigest);
        Assert.Null(provenance.ProtocolVersion);
        Assert.Null(provenance.OperationContractVersion);
        Assert.Null(provenance.RouteScope);
        Assert.Null(provenance.BindingIdentity);
        Assert.Null(provenance.InvocationId);
        Assert.Null(provenance.Diagnostics);
    }

    /// <summary>
    /// An item refused because its plugin cannot run on this machine names that plugin anyway — which package,
    /// which version, which digest, and which route chose it. Being told only that something was unavailable
    /// would leave a manager nothing to repair.
    /// </summary>
    [Fact]
    public async Task An_item_refused_by_its_plugin_still_names_the_plugin_it_would_have_used()
    {
        await using var api = await StartAsync(
            FakeCommand.Returning(new CommandOutcome.Completed(null)),
            TestRoutes.GlobalDefault(HeldBack),
            install: HoldOneBack);
        var campaign = await CampaignAsync(api);
        var item = await ProviderItemAsync(api, campaign, "campaign.get", Named("c-7714"));

        await ScanAsync(api, claimed: 0);

        var attempt = await AttemptAsync(api, item);
        Assert.Equal(AttemptErrors.PluginUnavailable, attempt.Error!.Code);
        var provenance = attempt.Provenance;
        Assert.NotNull(provenance);
        Assert.Equal(HeldBack, provenance.PluginId);
        Assert.Equal("1.0.0", provenance.PluginVersion);
        Assert.StartsWith("sha256:", provenance.PluginDigest!, StringComparison.Ordinal);
        Assert.Equal(RouteScope.GlobalDefault, provenance.RouteScope);
        Assert.Equal("campaign.get", provenance.Operation);
        Assert.Null(provenance.BindingIdentity);
        Assert.Null(provenance.InvocationId);
    }

    /// <summary>
    /// A runtime holding the two checked-in packages, sending work where the test says, and running whatever
    /// command the test hands it in place of the one this wave has not built yet.
    /// </summary>
    private static Task<RuntimeApiFixture> StartAsync(ICommand command, string routes, Action<JasonPaths>? install = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, Idle);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.InstallOtherProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
                install?.Invoke(paths);
                TestRoutes.WriteGlobal(paths, routes);
            },
            configureServices: services => services.AddSingleton<ICommand>(command));

    /// <summary>A package that asks for a program this machine does not have, which is what holds it back.</summary>
    private static void HoldOneBack(JasonPaths paths) => TestPlugins.Write(
        paths,
        HeldBack,
        TestPlugins.Manifest(HeldBack, "[campaign.get]", "capabilities:\n  exec:\n    executables:\n      - name: not-installed-anywhere\n"),
        "export function invoke() { return { result: {} }; }");

    /// <summary>One scan the test asked for, with the handler pool emptied before anything is read back.</summary>
    private static async Task ScanAsync(RuntimeApiFixture api, int claimed)
    {
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        Assert.Equal(claimed, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(10)));
    }

    private static Task RunOneAsync(RuntimeApiFixture api) => ScanAsync(api, claimed: 1);

    private static async Task<AttemptDto> AttemptAsync(RuntimeApiFixture api, string item)
    {
        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        return Assert.Single(read.Attempts!);
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Flow" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        return campaign.Id;
    }

    private static async Task<string> ProviderItemAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject arguments)
    {
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation,
                context = new JsonObject { ["input"] = arguments },
            },
            Ct);
        return item.Id;
    }

    private static JsonObject Named(string externalId) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = externalId },
    };

    /// <summary>
    /// A stand-in for the command the next task builds: it answers through the API exactly as an executor does,
    /// so what these tests read is a finished attempt rather than one nobody ever decided.
    /// </summary>
    private sealed class Executor
    {
        private RuntimeApiFixture? _api;

        public Executor() => Command = new FakeCommand(WorkItemKind.ProviderOp, RunAsync);

        public ICommand Command { get; }

        public void Answers(RuntimeApiFixture api) => _api = api;

        private async Task<CommandOutcome> RunAsync(CommandContext context)
        {
            await _api!.PostOkAsync<WorkItemDto>(
                Operations.WorkItemComplete,
                new
                {
                    work_item_id = context.WorkItemId,
                    attempt_id = context.AttemptId,
                    status = "succeeded",
                    result = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
                },
                Ct);
            return new CommandOutcome.Completed(null);
        }
    }
}
