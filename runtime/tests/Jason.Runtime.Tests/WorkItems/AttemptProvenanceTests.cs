using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>What an invocation adds to the record the claim wrote, and what the claim could not know.</summary>
    private static readonly InvocationRecord Answered = new(
        "pin_01K5B7Q2WE5X3M9T0YH4C6RDNA",
        new OutcomeDiagnostics(41, 0, 1, 3),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "c-7714" });

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
        Assert.Equal(Answered.InvocationId, provenance.InvocationId);
        Assert.NotNull(provenance.Diagnostics);
    }

    /// <summary>An attempt of the other kind carries none, and its JSON says so by leaving the field out.</summary>
    [Fact]
    public async Task An_agent_attempt_carries_the_agent_half_and_none_of_the_provider_one()
    {
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), ToTheReferenceProvider);
        var campaign = await CampaignAsync(api);
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "ai_role", role = "researcher" },
            Ct);

        await RunOneAsync(api);

        // There is one provenance record per attempt whatever kind of work it is, so a reader never has to
        // know which kind it is looking at first. What differs is which half of it is filled in: an agent
        // attempt has no plugin, no route and no operation, and every one of those stays null.
        var attempt = await AttemptAsync(api, item.Id);
        Assert.NotNull(attempt.Provenance);
        var provenance = attempt.Provenance;
        Assert.Null(provenance.PluginId);
        Assert.Null(provenance.Operation);
        Assert.Null(provenance.RouteScope);
        Assert.Null(provenance.PluginSnapshotId);

        // No profile is configured in this test, so the role's own entry command ran it — and the record says
        // that rather than leaving it to be inferred from an absent name.
        Assert.NotNull(provenance.Agent);
        var agent = provenance.Agent;
        Assert.Equal(ProfileResolutionSource.RoleEntryCommand, agent.ResolutionSource);
        Assert.Null(agent.ProfileName);
    }

    /// <summary>
    /// The invariant the whole wave is for: an attempt is a record of what happened, and nothing that happens
    /// afterwards may edit it. The world moves under a finished attempt in exactly three ways — the routes are
    /// changed, the plugins are reloaded, and the package on disk is edited — and all three are here.
    /// </summary>
    [Fact]
    public async Task What_ran_stays_what_ran_after_the_routes_the_plugins_and_the_package_change()
    {
        var executor = new Executor();
        await using var api = await StartAsync(executor.Command, ToTheReferenceProvider);
        executor.Answers(api);
        var campaign = await CampaignAsync(api);
        var item = await ProviderItemAsync(api, campaign, "campaign.get", Named("c-7714"));
        await RunOneAsync(api);
        var before = (await AttemptAsync(api, item)).Provenance;
        Assert.NotNull(before);

        // A reload on its own: both snapshots are new, and the finished attempt still names the old ones.
        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        Assert.True(reloaded.Activated);
        Assert.NotEqual(before.PluginSnapshotId, reloaded.Snapshot.Id);
        Assert.NotEqual(before.RoutingSnapshotId, reloaded.RoutingSnapshotId);
        await AssertUnchangedAsync(api, item, before);

        // The route now names the other package, and the one that ran has been edited on disk since.
        File.AppendAllText(Path.Combine(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), "main.js"), "\n// edited after the attempt\n");
        await TestRoutes.WriteGlobalAsync(api, TestRoutes.GlobalDefault(TestPlugins.OtherProviderId), Ct);
        var moved = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);

        Assert.True(moved.Activated);
        Assert.Equal(TestPlugins.OtherProviderId, api.Resolve<RouteRegistry>().Snapshot.Global.Default!.PluginId);
        var edited = Assert.Single(moved.Plugins, plugin => plugin.Id == TestPlugins.FakeProviderId);
        Assert.NotEqual(before.PluginDigest, edited.Digest);

        await AssertUnchangedAsync(api, item, before);
    }

    /// <summary>
    /// Every field as it stands now against every field as it stood, compared as the row itself stores them so
    /// that a change to any one of them fails this rather than only the ones a test thought to name.
    /// </summary>
    private static async Task AssertUnchangedAsync(RuntimeApiFixture api, string item, AttemptProvenanceDto before)
    {
        var now = (await AttemptAsync(api, item)).Provenance;
        Assert.NotNull(now);
        Assert.Equal(JsonSerializer.Serialize(before, JasonJson.Options), JsonSerializer.Serialize(now, JasonJson.Options));
    }

    /// <summary>
    /// The completion adds what only the invocation could know, and it adds it without reading the record back:
    /// the attempt's id is the fencing token, and a writer that reads a row to write it again is how one writer
    /// silently undoes another. Here the other writer is a heartbeat landing in between.
    /// </summary>
    [Fact]
    public async Task A_completion_adds_what_the_invocation_learned_and_undoes_nothing()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.ProviderOp, _ => gate.Task);
        await using var api = await StartAsync(command, ToTheReferenceProvider);
        var campaign = await CampaignAsync(api);
        var item = await ProviderItemAsync(api, campaign, "campaign.get", Named("c-7714"));
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        var running = command.Contexts.Single();

        // A context that read the attempt before anybody else touched it: exactly what a read-then-write would
        // save back over the heartbeat that lands next.
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var stale = await db.Attempts.FirstAsync(a => a.PublicId == running.AttemptId, Ct);
        Assert.Null(stale.LastHeartbeatAt);
        await api.PostOkAsync<HeartbeatResponse>(
            Operations.WorkItemHeartbeat,
            new { work_item_id = running.WorkItemId, attempt_id = running.AttemptId },
            Ct);

        Assert.True(await AttemptProvenance.CompleteAsync(db, running.AttemptId, Answered, Ct));

        // A second writer adds what it knows and nothing else: whoever reads the answer and whoever runs it are
        // not the same code, and neither of them has to carry the other's half to write its own.
        Assert.True(await AttemptProvenance.CompleteAsync(
            db,
            running.AttemptId,
            new InvocationRecord(RejectedResult: new JsonObject { ["campaign"] = "not what the schema asked for" }),
            Ct));

        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(10)));

        // The rejected answer is the one unbounded thing a record can carry, so it is asked for by the same flag
        // as the context snapshot beside it rather than riding on every read of the item.
        Assert.Null((await AttemptAsync(api, item)).Provenance!.RejectedResult);

        var attempt = await AttemptAsync(api, item, snapshots: true);
        Assert.NotNull(attempt.LastHeartbeatAt);
        var provenance = attempt.Provenance;
        Assert.NotNull(provenance);
        Assert.Equal(Answered.InvocationId, provenance.InvocationId);
        Assert.Equal(Answered.Diagnostics, provenance.Diagnostics);
        var returned = Assert.Single(provenance.ExternalIdsReturned!);
        Assert.Equal("campaign", returned.Key);
        Assert.Equal("c-7714", returned.Value);
        Assert.Equal("not what the schema asked for", (string?)provenance.RejectedResult!["campaign"]);

        // And everything the claim recorded is still what it recorded.
        Assert.Equal(TestPlugins.FakeProviderId, provenance.PluginId);
        Assert.Equal(RouteScope.GlobalDefault, provenance.RouteScope);
        Assert.Equal(BindingIdentity.Of(Workspace), provenance.BindingIdentity);
        Assert.Equal(attempt.Id, provenance.CorrelationId);
    }

    /// <summary>
    /// Nothing to complete is not a failure of the work: an attempt the claim never wrote a record for keeps
    /// none, and an attempt that is not there is answered with "no" rather than with an exception.
    /// </summary>
    [Fact]
    public async Task A_completion_writes_nothing_where_the_claim_recorded_nothing()
    {
        await using var api = await StartAsync(FakeCommand.Returning(new CommandOutcome.Completed(null)), ToTheReferenceProvider);
        var campaign = await CampaignAsync(api);
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "ai_role", role = "researcher" },
            Ct);
        await RunOneAsync(api);
        var agent = await AttemptAsync(api, item.Id);

        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();

        // An attempt that does not exist: nothing to complete, and nothing written.
        Assert.False(await AttemptProvenance.CompleteAsync(db, "att_00000000000000000000000000", Answered, Ct));

        // And an attempt with no claim-time record — the shape every agent attempt written before execution
        // profiles existed still has on an upgraded database. The merge is one guarded UPDATE over a row that
        // holds a record, so a row that holds none is left alone rather than given one.
        var bare = await db.Attempts.SingleAsync(a => a.PublicId == agent.Id, Ct);
        bare.Provenance = null;
        await db.SaveChangesAsync(Ct);

        Assert.False(await AttemptProvenance.CompleteAsync(db, agent.Id, Answered, Ct));
        Assert.Null((await AttemptAsync(api, item.Id)).Provenance);
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
            configureServices: services =>
            {
                services.AddSingleton<ICommand>(command);

                // The reference package names the stand-in vendor program itself, so a runtime that cannot find
                // it on the search path loads the plugin unavailable — which is a different pre-flight answer
                // from the ones these tests are about.
                services.AddSingleton(TestPlugins.SearchPath);
            });

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

    private static async Task<AttemptDto> AttemptAsync(RuntimeApiFixture api, string item, bool snapshots = false)
    {
        var read = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemGet,
            new { work_item_id = item, include_snapshots = snapshots },
            Ct);
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
            await using (var scope = _api!.Resolve<IServiceScopeFactory>().CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
                Assert.True(await AttemptProvenance.CompleteAsync(db, context.AttemptId, Answered, Ct));
            }

            await _api.PostOkAsync<WorkItemDto>(
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
