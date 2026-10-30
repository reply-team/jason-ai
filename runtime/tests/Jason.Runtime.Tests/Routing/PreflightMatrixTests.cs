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
/// Sixteen of the twenty are here. <c>contract_incompatible</c> cannot be reached through an installed package — the
/// manifest rules refuse a plugin that does not speak the operation-contract version this build publishes — so it
/// is proven over a built snapshot in <see cref="ProviderOpPreflightTests"/> instead, and so is <c>provider_answer_lost</c>,
/// which needs an earlier attempt that lost its answer and is met through the real claim in <c>ClaimerTests</c>. <c>approval_required</c> and
/// <c>provider_blocked</c> are not in this matrix at all, because they are the two answers that do not fail an item: work
/// whose operation needs a person's approval is parked for one, and work a provider block holds waits on the block, which
/// <c>ClaimerTests</c> is where to read about.
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
    private const string Deleting = "deleting-provider";
    private const string SendingNow = "sending-now-provider";
    private const string ReadingSends = "reading-sends-provider";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> EveryWayAProviderItemCannotRun() => [.. Cases.Keys];

    // contact_not_linked is among them because without it a person is asked to approve a delete that can never run: the
    // refusal has to come before the gate, through the real claim.
    // Critical: no_route, suppressed, contact_not_linked, input_invalid.
    public static TheoryData<string> CriticalWaysAProviderItemCannotRun() =>
        CriticalRows.Mark(
            EveryWayAProviderItemCannotRun(),
            Family.W3,
            data => data is ["no_route"] or ["suppressed"] or ["contact_not_linked"] or ["input_invalid"]);

    [Theory]
    [MemberData(nameof(CriticalWaysAProviderItemCannotRun))]
    public async Task A_provider_item_that_cannot_run_says_why_and_keeps_its_attempt(string reason)
    {
        var scenario = Cases[reason];
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, Idle);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
                scenario.Install(paths);
            },
            // The reference package names the stand-in vendor program itself, so without its directory on the
            // search path the plugin loads as unavailable — which is one of the twenty answers this matrix is
            // about, and would silently stand in for the others.
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));

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

        // A delete works by the routed plugin's pin alone, and this person was never pinned by anybody: refused at the
        // claim, where a person would otherwise have been asked to approve it.
        [AttemptErrors.ContactNotLinked] = new(
            paths => TestPlugins.Write(
                paths,
                Deleting,
                TestPlugins.Manifest(Deleting, "[contact.delete]"),
                "export function invoke() { return { result: {} }; }"),
            _ => new Route(Deleting, null, null),
            async (api, campaign, ct) =>
            {
                var contact = await ContactAsync(api, campaign, "email", "ada@example.test", ct);
                return await ItemAsync(api, campaign, "contact.delete", new JsonObject { ["channel"] = "email" }, ct, contact);
            },
            AttemptErrors.ContactNotLinked,
            FailureClass.Permanent),

        // A read of a send names one this campaign does not hold: the send is real, and another campaign's, which this
        // campaign's work does not act on.
        [AttemptErrors.SendUnknown] = new(
            ReadsSends,
            _ => new Route(ReadingSends, null, null),
            async (api, campaign, ct) =>
            {
                var other = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Elsewhere" }, ct);
                return await ItemAsync(api, campaign, "message.status", Naming(await SendRowAsync(api, other.Id, WorkItemStatus.Succeeded, ReadingSends, ct)), ct);
            },
            AttemptErrors.SendUnknown,
            FailureClass.Permanent),

        // A read of a send that failed: the provider kept no reference to read it by.
        [AttemptErrors.SendReferenceAbsent] = new(
            ReadsSends,
            _ => new Route(ReadingSends, null, null),
            async (api, campaign, ct) =>
                await ItemAsync(api, campaign, "message.status", Naming(await SendRowAsync(api, campaign, WorkItemStatus.Failed, ReadingSends, ct)), ct),
            AttemptErrors.SendReferenceAbsent,
            FailureClass.Permanent),

        // A read of a send made through another plugin: its reference is that provider account's.
        [AttemptErrors.SendRouteMismatch] = new(
            ReadsSends,
            _ => new Route(ReadingSends, null, null),
            async (api, campaign, ct) =>
                await ItemAsync(api, campaign, "message.status", Naming(await SendRowAsync(api, campaign, WorkItemStatus.Succeeded, "another-provider", ct)), ct),
            AttemptErrors.SendRouteMismatch,
            FailureClass.Permanent),

        // Creation checks the caller's own arguments; that the campaign is named at all — by an argument or by a
        // pin — is a rule over the whole composed document, and only the claim composes it.
        [AttemptErrors.InputInvalid] = new(
            Nothing,
            ToTheReferenceProvider,
            (api, campaign, ct) => ItemAsync(api, campaign, "campaign.get", null, ct),
            AttemptErrors.InputInvalid,
            FailureClass.Validation),

        // A send for a later moment, routed to a package whose manifest lists no moment for it on any channel: refused at
        // the claim, before a person is asked to approve a send that would have gone out at once.
        [AttemptErrors.ArgumentUnsupported] = new(
            paths => TestPlugins.Write(
                paths,
                SendingNow,
                TestPlugins.Manifest(SendingNow, "[message.send]"),
                "export function invoke() { return { result: {} }; }"),
            _ => new Route(SendingNow, null, null),
            async (api, campaign, ct) =>
            {
                var contact = await ContactAsync(api, campaign, "email", "ada@example.test", ct);
                return await ItemAsync(api, campaign, "message.send", Later(), ct, contact);
            },
            AttemptErrors.ArgumentUnsupported,
            FailureClass.Permanent),

        // A message written as a template, routed to a package whose manifest lists no channel it writes one on: refused
        // at the claim, before a person is asked to approve a template the package would have sent as written.
        [AttemptErrors.TemplateNotOffered] = new(
            paths => TestPlugins.Write(
                paths,
                SendingNow,
                TestPlugins.Manifest(SendingNow, "[message.send]"),
                "export function invoke() { return { result: {} }; }"),
            _ => new Route(SendingNow, null, null),
            async (api, campaign, ct) =>
            {
                var contact = await ContactAsync(api, campaign, "email", "ada@example.test", ct);
                return await ItemAsync(api, campaign, "message.send", Template(), ct, contact);
            },
            AttemptErrors.TemplateNotOffered,
            FailureClass.Permanent),
    };

    private static void Nothing(JasonPaths paths)
    {
    }

    private static void ReadsSends(JasonPaths paths) => TestPlugins.Write(
        paths,
        ReadingSends,
        TestPlugins.Manifest(ReadingSends, "[message.status]"),
        "export function invoke() { return { result: {} }; }");

    private static JsonObject Naming(string send) => new() { ["send"] = send };

    /// <summary>
    /// A send a build made earlier, written where a caller cannot: a <c>message.send</c> work item of the campaign, ended
    /// <paramref name="status"/>, whose one attempt ran under <paramref name="pluginId"/> and, where it succeeded, returned
    /// its reference.
    /// </summary>
    private static async Task<string> SendRowAsync(RuntimeApiFixture api, string campaign, WorkItemStatus status, string pluginId, CancellationToken ct)
    {
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var now = api.Resolve<TimeProvider>().GetUtcNow().UtcDateTime;
        var owner = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, ct);
        var send = WorkItemFactory.NewProviderOp(owner, "message.send", now);
        send.Status = status;
        var succeeded = status == WorkItemStatus.Succeeded;
        var attempt = WorkItemFactory.NewAttempt(send, 1, succeeded ? AttemptStatus.Succeeded : AttemptStatus.Failed, now);
        attempt.Provenance = new AttemptProvenanceDto(
            pluginId, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion, "message.send", 1,
            null, null, RouteScope.GlobalDefault, BindingIdentity: null,
            ExternalIdsReturned: succeeded ? new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = "email-1001" } : null);
        db.WorkItems.Add(send);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return send.PublicId;
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

    private static JsonObject Later() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
        ["send_after"] = "2026-11-02T09:00:00+01:00",
    };

    private static JsonObject Template() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["compose"] = "template", ["subject"] = "Hello", ["body"] = "Hello, {{contact.first_name}}." },
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
