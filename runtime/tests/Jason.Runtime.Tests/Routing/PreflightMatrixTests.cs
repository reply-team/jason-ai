using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Every way a provider work item can fail to run, through a real runtime and the real claim — because what is
/// worth proving is what a manager sees in <c>workitem.get</c>, not what a function returned. The item is failed
/// inside the claim: no child process is ever started, the attempt is kept with its context snapshot, and the
/// work item ends failed rather than sitting in the queue looking claimable.
/// </summary>
/// <remarks>
/// Eleven of the twelve are here. The twelfth, <c>contract_incompatible</c>, cannot be reached through an
/// installed package — the manifest rules refuse a plugin that does not speak the operation-contract version this
/// build publishes — so it is proven over a built snapshot in <see cref="ProviderOpPreflightTests"/> instead.
/// Routes are placed into the registry directly: which plugin an operation goes to is a decision this test takes
/// rather than the subject of it.
/// </remarks>
public class PreflightMatrixTests
{
    /// <summary>A real dispatcher that never ticks on its own: every scan in this test is one the test asked for.</summary>
    private const string Idle = """{"Dispatcher":{"TickSeconds":3600}}""";

    private const string Narrow = "narrow-provider";
    private const string HeldBack = "held-back-provider";
    private const string Ghost = "ghost-provider";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> EveryWayAProviderItemCannotRun() => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(EveryWayAProviderItemCannotRun))]
    public async Task A_provider_item_that_cannot_run_says_why_and_keeps_its_attempt(string reason)
    {
        var scenario = Cases[reason];
        await using var api = await RuntimeApiFixture.StartAsync(Ct, paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, Idle);
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
            scenario.Install(paths);
        });

        // The loop scans the moment it starts; everything this test seeds comes after that, so the only scan
        // that matters is the one below.
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));

        var campaign = await CampaignAsync(api);
        var item = await scenario.Item(api, campaign, Ct);
        Route(api, scenario.Route(api.Resolve<PluginRegistry>().Snapshot));

        Assert.Equal(0, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);

        var read = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemGet,
            new { work_item_id = item, include_snapshots = true },
            Ct);

        Assert.Equal(WorkItemStatus.Failed, read.Status);
        var attempt = Assert.Single(read.Attempts!);
        Assert.Equal(scenario.Code, attempt.Error!.Code);
        Assert.Equal(scenario.Class, attempt.Error.Class);
        Assert.False(attempt.Error.Retriable);
        Assert.NotNull(attempt.ContextSnapshot);
        Assert.Null(attempt.Launch);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(scenario.Code, read.LastError!.Code);
    }

    private static readonly IReadOnlyDictionary<string, PreflightCase> Cases = new Dictionary<string, PreflightCase>(StringComparer.Ordinal)
    {
        // A build that published this operation wrote the row; this one does not, and creation would refuse it
        // now — which is why the item is written where a caller cannot, rather than through the API.
        [AttemptErrors.OperationUnknown] = new(
            Nothing,
            ToTheReferenceProvider,
            (api, campaign, ct) => RowAsync(api, campaign, "archive.sweep", ct),
            AttemptErrors.OperationUnknown,
            FailureClass.Permanent),

        [AttemptErrors.NoRoute] = new(
            Nothing,
            _ => null,
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.get", Named("c-7714"), ct),
            AttemptErrors.NoRoute,
            FailureClass.Permanent),

        [AttemptErrors.PluginNotLoaded] = new(
            Nothing,
            _ => new Route(Ghost, null, null),
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.get", Named("c-7714"), ct),
            AttemptErrors.PluginNotLoaded,
            FailureClass.Permanent),

        // A package whose declared program is nowhere on this machine: installed, listed, and held back.
        [AttemptErrors.PluginUnavailable] = new(
            paths => TestPlugins.Write(
                paths,
                HeldBack,
                TestPlugins.Manifest(
                    HeldBack,
                    "[campaign.get]",
                    "capabilities:\n  exec:\n    executables:\n      - name: not-installed-anywhere\n"),
                "export function invoke() { return { result: {} }; }"),
            _ => new Route(HeldBack, null, null),
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.get", Named("c-7714"), ct),
            AttemptErrors.PluginUnavailable,
            FailureClass.Permanent),

        [AttemptErrors.PluginOperationUnsupported] = new(
            paths => TestPlugins.Write(
                paths,
                Narrow,
                TestPlugins.Manifest(Narrow, "[campaign.get]"),
                "export function invoke() { return { result: {} }; }"),
            _ => new Route(Narrow, null, null),
            (api, campaign, ct) => ItemAsync(api, campaign, "list_membership.add", ToAList(), ct),
            AttemptErrors.PluginOperationUnsupported,
            FailureClass.Permanent),

        // The reference package's manifest asks a route for a workspace, and this route names none.
        [AttemptErrors.BindingInvalid] = new(
            Nothing,
            _ => new Route(TestPlugins.FakeProviderId, null, null),
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.get", Named("c-7714"), ct),
            AttemptErrors.BindingInvalid,
            FailureClass.Permanent),

        [AttemptErrors.ApprovalRequired] = new(
            Nothing,
            ToTheReferenceProvider,
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.enroll", ToEnrol(), ct),
            AttemptErrors.ApprovalRequired,
            FailureClass.Permanent),

        [AttemptErrors.ContactRequired] = new(
            Nothing,
            ToTheReferenceProvider,
            (api, campaign, ct) => ItemAsync(api, campaign, "list_membership.add", ToAList(), ct),
            AttemptErrors.ContactRequired,
            FailureClass.Permanent),

        [AttemptErrors.NoChannelValue] = new(
            Nothing,
            ToTheReferenceProvider,
            async (api, campaign, ct) =>
            {
                var contact = await ContactAsync(api, campaign, "phone", "+15555550100", ct);
                return await ItemAsync(api, campaign, "list_membership.add", ToAList(), ct, contact);
            },
            AttemptErrors.NoChannelValue,
            FailureClass.Permanent),

        [AttemptErrors.Suppressed] = new(
            Nothing,
            ToTheReferenceProvider,
            async (api, campaign, ct) =>
            {
                var contact = await ContactAsync(api, campaign, "email", "ada@example.test", ct);
                await api.PostOkAsync<SuppressionDto>(
                    Operations.SuppressionAdd,
                    new { channel = "email", value = "ada@example.test", reason = "asked to be left alone" },
                    ct);
                return await ItemAsync(api, campaign, "list_membership.add", ToAList(), ct, contact);
            },
            AttemptErrors.Suppressed,
            FailureClass.Permanent),

        // Creation checks the caller's own arguments; that the campaign is named at all — by an argument or by a
        // pin — is a rule over the whole composed document, and only the claim composes it.
        [AttemptErrors.InputInvalid] = new(
            Nothing,
            ToTheReferenceProvider,
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.get", null, ct),
            AttemptErrors.InputInvalid,
            FailureClass.Validation),
    };

    private static void Nothing(JasonPaths paths)
    {
    }

    private static Route ToTheReferenceProvider(PluginSnapshot plugins) =>
        new(TestPlugins.FakeProviderId, new JsonObject { ["workspace"] = "west" }, "sha256:west");

    /// <summary>The route snapshot this claim will answer from, pinned to the plugin snapshot the load produced.</summary>
    private static void Route(RuntimeApiFixture api, Route? route)
    {
        var plugins = api.Resolve<PluginRegistry>().Snapshot;
        var now = api.Resolve<TimeProvider>().GetUtcNow();
        api.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            now,
            plugins.Id,
            new RouteSet(route, RouteSet.Empty.Operations),
            RouteSnapshot.Empty(now, plugins.Id).Campaigns));
    }

    private static JsonObject Named(string externalId) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = externalId },
    };

    private static JsonObject ToAList() => new()
    {
        ["list"] = new JsonObject { ["external_id"] = "L-1129" },
        ["channel"] = "email",
    };

    private static JsonObject ToEnrol() => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = "c-7714" },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "immediately",
    };

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Flow" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        return campaign.Id;
    }

    private static async Task<string> ContactAsync(RuntimeApiFixture api, string campaign, string channel, string value, CancellationToken ct)
    {
        var contact = await api.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Ada", channels = new[] { new { channel, value } } },
            ct);
        await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign, contacts = new[] { new { contact_id = contact.Id } } },
            ct);
        return contact.Id;
    }

    private static async Task<string> ItemAsync(
        RuntimeApiFixture api,
        string campaign,
        string operation,
        JsonObject? arguments,
        CancellationToken ct,
        string? contact = null)
    {
        var context = arguments is null ? null : new JsonObject { ["input"] = arguments };
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "provider_op", operation, contact_id = contact, context },
            ct);
        return item.Id;
    }

    /// <summary>
    /// A row a caller could not write today, written where the caller cannot: the work item of a build that
    /// published an operation this one does not.
    /// </summary>
    private static async Task<string> RowAsync(RuntimeApiFixture api, string campaign, string operation, CancellationToken ct)
    {
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var now = api.Resolve<TimeProvider>().GetUtcNow().UtcDateTime;
        var item = new WorkItem
        {
            PublicId = PublicId.New("wi"),
            CampaignId = await db.Campaigns.Where(c => c.PublicId == campaign).Select(c => c.Id).SingleAsync(ct),
            Kind = WorkItemKind.ProviderOp,
            Operation = operation,
            CreatedByType = ActorType.Human,
            Context = new JsonObject { ["note"] = "written by a build that knew this operation" },
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item.PublicId;
    }

    /// <summary>One way an item cannot run: what is installed, where the work is routed, and what is created.</summary>
    private sealed record PreflightCase(
        Action<JasonPaths> Install,
        Func<PluginSnapshot, Route?> Route,
        Func<RuntimeApiFixture, string, CancellationToken, Task<string>> Item,
        string Code,
        FailureClass Class);
}
