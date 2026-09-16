using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;
using Jason.Runtime.Tests.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What one claimed provider operation becomes: a single invocation of the package the claim pinned, run under
/// the budget the operation's own contract states. The plan is not consulted again and nothing is looked up a
/// second time — everything that could have been decided differently was decided inside the claim.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ProviderOpCommandTests
{
    private const string ProviderCampaign = "cmp_42";

    /// <summary>A real dispatcher that never ticks on its own: every scan here is one the test asked for.</summary>
    private const string Idle = """{"Dispatcher":{"TickSeconds":3600},"Roles":{"DefaultEntryCommand":["agent-host"]}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A1, the whole point of this path: the child's budget is the operation's, and the same two invocations of
    /// the same operation ask for the same thing whether the lease has an hour left or three seconds. A budget
    /// taken from what remains of a lease would make one operation behave differently because the claim before
    /// it was slow, which is not something anyone could reason about from a failure.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(7200)]
    public void The_child_is_given_the_contract_s_budget_and_not_what_is_left_of_the_lease(int secondsLeft)
    {
        var plan = PlanFor("campaign.get");

        var request = ProviderOpCommand.RequestFor(ContextWith(plan, secondsLeft));

        Assert.Equal(60_000, plan.Contract.TimeoutMs);
        Assert.Equal(TimeSpan.FromMilliseconds(60_000), request.Timeout);
    }

    /// <summary>
    /// The three seams the claim reaches the provider through. The package is passed as the pin the claim
    /// decided on rather than as a name to look up again, so a reload between the claim and the start changes
    /// nothing about what runs; the correlation id is the attempt's own, which is what ties a child's
    /// diagnostics back to the work; and the attempt number is what a plugin reads to know it is not the first
    /// try. What a plugin then owes on a second try is its contract's recovery read, which this does not prove:
    /// it proves only that the number the obligation is read from arrives.
    /// </summary>
    [Fact]
    public void The_invocation_names_the_pinned_package_the_attempt_and_the_try()
    {
        var plan = PlanFor("campaign.get");
        var context = ContextWith(plan, secondsLeft: 600) with { AttemptNumber = 2 };

        var request = ProviderOpCommand.RequestFor(context);

        Assert.Same(plan.Plugin, request.Pinned!.Plugin);
        Assert.Equal(plan.PluginSnapshotId, request.Pinned.SnapshotId);

        // The id the provenance is written from and the package that runs are the same fact, taken from the
        // plan rather than from anywhere a second name could come from.
        Assert.Equal(plan.Plugin.Manifest.Id, request.PluginId);
        Assert.Equal("campaign.get", request.Operation);
        Assert.Same(plan.Input, request.Input);
        Assert.Same(plan.Binding, request.Binding);
        Assert.Equal(context.AttemptId, request.CorrelationId);
        Assert.Equal(context.AttemptId, request.AttemptId);
        Assert.Equal(2, request.AttemptNumber);
        Assert.Equal(context.WorkItemId, request.WorkItemId);
        Assert.Equal(context.CampaignId, request.CampaignId);
    }

    /// <summary>
    /// A provider attempt without a plan is a mistake in the claim, and the claim is the only place that builds
    /// one. It is loud here rather than quiet in production: there is no invocation this command could invent.
    /// </summary>
    [Fact]
    public void A_provider_attempt_that_arrives_without_a_plan_is_a_mistake_in_the_claim()
    {
        var context = ContextWith(PlanFor("campaign.get"), secondsLeft: 600) with { Plan = null };

        var error = Assert.Throws<InvalidOperationException>(() => ProviderOpCommand.RequestFor(context));

        Assert.Contains("claim", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole path, once, through the shipped executable against a real provider account: an item a planner
    /// wrote is claimed, routed, invoked, and the attempt afterwards says how it was run and which invocation
    /// ran it. Nothing here is a stand-in but the provider itself.
    /// </summary>
    [Fact]
    public async Task A_claimed_provider_item_runs_its_operation_and_the_attempt_says_how()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Active");
        await using var api = await StartAsync(workspace);
        var campaign = await CampaignAsync(api);
        var item = await ProviderItemAsync(api, campaign, "campaign.get");

        await RunOneAsync(api);

        var attempt = await AttemptAsync(api, item);
        var launch = attempt.Launch;
        Assert.NotNull(launch);
        Assert.True(launch.Pid > 0);
        Assert.Equal(0, launch.ExitCode);
        Assert.Equal(
            ["--protocol", "1", "--plugin", TestPlugins.FakeProviderId, "--operation", "campaign.get", "--correlation", attempt.Id],
            launch.EntryCommand.TakeLast(8));

        // What only the invocation could know, added to the record the claim wrote.
        var provenance = attempt.Provenance;
        Assert.NotNull(provenance);
        Assert.StartsWith(PluginProtocol.InvocationIdPrefix + "_", provenance.InvocationId!, StringComparison.Ordinal);
        Assert.NotNull(provenance.Diagnostics);
        Assert.Equal(TestPlugins.FakeProviderId, provenance.PluginId);
        Assert.Equal("campaign.get", provenance.Operation);

        // The vendor program was really called, which is what makes the answer evidence rather than a fixture.
        Assert.Contains(workspace.Calls, call => call.Contains("campaign get", StringComparison.Ordinal));

        // Nothing turns an answer into an outcome yet: one recorder does that, and it is the next increment.
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    private static Task<RuntimeApiFixture> StartAsync(TestWorkspace workspace) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, Idle);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
                TestRoutes.WriteGlobal(
                    paths,
                    TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = workspace.Root }));
            },
            configureServices: services =>
            {
                // The test host is not the shipped executable, so the child is started from the same jason.dll
                // the runtime copied next to the tests; the search path is what makes the vendor program findable.
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

    /// <summary>One scan the test asked for, with the handler pool emptied before anything is read back.</summary>
    private static async Task RunOneAsync(RuntimeApiFixture api)
    {
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)));
    }

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

    private static async Task<string> ProviderItemAsync(RuntimeApiFixture api, string campaign, string operation)
    {
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation,
                context = new JsonObject
                {
                    ["input"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = ProviderCampaign } },
                },
            },
            Ct);
        return item.Id;
    }

    /// <summary>A plan as a claim leaves it: a package, the snapshots it was decided from, and a composed input.</summary>
    private static ProviderOpPlan PlanFor(string operation) => new(
        TestPlugins.Loaded(TestPlugins.FakeProviderId, [operation]),
        PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
        RouteSnapshot.IdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNB",
        RouteScope.GlobalDefault,
        new JsonObject { ["workspace"] = "west" },
        "sha256:0",
        OperationCatalog.Find(operation)!,
        new JsonObject { ["args"] = new JsonObject() });

    private static CommandContext ContextWith(ProviderOpPlan plan, int secondsLeft) => new(
        "wi_01K5B7Q2WE5X3M9T0YH4C6RDNC",
        "att_01K5B7Q2WE5X3M9T0YH4C6RDND",
        1,
        "cmp_01K5B7Q2WE5X3M9T0YH4C6RDNE",
        null,
        WorkItemKind.ProviderOp,
        null,
        null,
        [],
        null,
        new EffectiveLimits(600, 0, 3),
        DateTimeOffset.UtcNow.AddSeconds(secondsLeft),
        [],
        string.Empty,
        CancellationToken.None,
        plan.Contract.Id,
        plan);
}
