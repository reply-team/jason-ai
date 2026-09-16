using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Invocation;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The whole spine, driven by the dispatcher against the shipped executable and a real provider account: a
/// planner's work item is claimed, routed, run in a child process, and answered — and what the plugin said
/// decides what happens to the item next.
/// </summary>
/// <remarks>
/// Nothing here is a stand-in but the provider itself. The evidence is what the account records: the state the
/// operations left behind, and the ordered log of every call it was asked for — which is what makes an
/// obligation to read before writing testable at all, since a provider that would have answered the same either
/// way cannot tell a plugin that checked from one that guessed right.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ProviderSpineTests
{
    private const string ProviderList = "lst_7";
    private const string ProviderCampaign = "cmp_42";
    private const string Marta = "marta@example.test";
    private const string Blocked = "blocked@example.test";

    /// <summary>A dispatcher that never ticks on its own and hands work straight back: every scan is the test's.</summary>
    private const string Idle = """{"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0},"Roles":{"DefaultEntryCommand":["agent-host"]}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The conformance case the recovery read exists for, end to end. The first attempt writes the membership
    /// and dies before it can say so; it answers <c>ambiguous</c>, carrying the one thing it learned — what the
    /// provider calls this person — which the runtime pins even though the attempt failed. The second attempt
    /// is allowed only because the contract obliges a recovery read, and it performs <b>both</b> the reads that
    /// contract names before answering from them. One crash, one effect.
    /// </summary>
    [Fact]
    public async Task A_lost_answer_costs_a_recovery_read_rather_than_a_second_effect()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects");
        await using var api = await StartAsync(workspace);
        var campaign = await CampaignAsync(api);
        var contact = await ContactAsync(api, campaign, Marta);
        var item = await ItemAsync(api, campaign, "list_membership.add", ToTheList(), contact);

        // The provider dies after the effect, under this item's own key — which is the key every attempt of it
        // carries, and the reason the account can answer "this already happened".
        workspace.FailAfterEffect(item);

        await ScanAsync(api);
        await ScanAsync(api);

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, read.Status);
        var attempts = InOrder(read);
        Assert.Equal(2, attempts.Count);

        var lost = attempts[0];
        Assert.Equal(AttemptStatus.Failed, lost.Status);
        Assert.Equal("provider_answer_lost", lost.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Error.Class);
        Assert.True(lost.Error.Retriable, "list_membership.add declares after_recovery_read, so the answer may be sought again.");

        var recovered = attempts[1];
        Assert.Equal(AttemptStatus.Succeeded, recovered.Status);
        Assert.Equal("already_member", (string?)read.Result!["items"]![0]!["status"]);

        // One effect at the provider, whatever the runtime did afterwards.
        var member = Assert.Single(workspace.MembersOf(ProviderList));

        // The pin the failed attempt returned, written down by the attempt that failed: after a lost answer the
        // identifier is the only trace of what was done, and without it the recovery read has nothing to read.
        var pinned = await api.PostOkAsync<ContactDto>(Operations.ContactGet, new { contact_id = contact }, Ct);
        var pin = Assert.Single(pinned.ExternalIds);
        Assert.Equal(member, pin.Value);
        Assert.Equal(lost.Id, pin.RecordedByAttemptId);

        // And what the account was actually asked, in order: the write, then both declared reads before the
        // answer. The contract names the membership of the pinned contact and the ledger under the key; the
        // plugin performs both, so the evidence matches the obligation rather than half of it.
        Assert.Equal(["contact ensure", "list add", "list membership", "ledger get"], workspace.Calls);
    }

    /// <summary>
    /// PA3c. A transient refusal is a different thing from a lost answer: nothing happened, so the item comes
    /// straight back. What matters is what the provider sees on the second try — the same idempotency key, and
    /// a plugin that knows it is not the first attempt, which it can only know from the number the runtime sent.
    /// </summary>
    [Fact]
    public async Task A_transient_refusal_comes_back_under_the_same_key_and_the_provider_sees_the_second_try()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects");
        await using var api = await StartAsync(workspace);
        var campaign = await CampaignAsync(api);
        var contact = await ContactAsync(api, campaign, Marta);
        var item = await ItemAsync(api, campaign, "list_membership.add", ToTheList(), contact);
        workspace.RefuseOnce(item);

        await ScanAsync(api);
        await ScanAsync(api);

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, read.Status);
        var attempts = InOrder(read);
        Assert.Equal("rate_limited", attempts[0].Error!.Code);
        Assert.Equal(FailureClass.Transient, attempts[0].Error!.Class);
        Assert.Equal(AttemptStatus.Succeeded, attempts[1].Status);
        Assert.Equal("added", (string?)read.Result!["items"]![0]!["status"]);

        // Every call the account was asked for carries the work item's own key, on both attempts: the key is the
        // item's, not the attempt's, which is what makes "the prior run under this key" answerable at all.
        Assert.All(workspace.KeyedCalls, call => Assert.True(
            call.Key.Length == 0 || call.Key == item,
            $"the provider was called with key '{call.Key}' rather than the work item's own"));

        // And the second attempt arrived knowing it was the second: the ledger read happens on no other attempt,
        // so the account's own log is what says the attempt number reached the plugin.
        Assert.Equal(
            ["contact ensure", "list add", "list membership", "ledger get", "contact ensure", "list add"],
            workspace.Calls);
    }

    /// <summary>
    /// PA3d. A2 through a real child process: the provider answers something the operation's output schema
    /// refuses, and the attempt is final whatever the operation's repeat rule allows. The pointers say where,
    /// and the answer itself is kept on the attempt so its author can see what was actually sent.
    /// </summary>
    [Fact]
    public async Task An_answer_the_operation_refuses_ends_the_item_and_keeps_what_was_sent()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Q3 outbound", "Active").WithCampaignName(ProviderCampaign, JsonValue.Create(42));
        await using var api = await StartAsync(workspace);
        var campaign = await CampaignAsync(api);
        var item = await ItemAsync(api, campaign, "campaign.get", Named(ProviderCampaign), contact: null);

        await ScanAsync(api);

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.Failed, read.Status);
        var attempt = Assert.Single(read.Attempts!);
        Assert.Equal("result_invalid", attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.False(attempt.Error.Retriable, "campaign.get says a repeat is safe; a shape error is final anyway.");

        var detail = Assert.Single(attempt.Error.Details!);
        Assert.Equal("/campaign/name", detail.Field);
        Assert.Equal("type", detail.Code);

        // The answer survived the protocol and is on the attempt, which a unit test of the recorder cannot show.
        var rejected = attempt.Provenance!.RejectedResult!;
        Assert.Equal(42, (int)rejected["campaign"]!["name"]!);
        Assert.Equal(ProviderCampaign, (string?)rejected["campaign"]!["external_id"]);
    }

    /// <summary>
    /// PA3e (D25). The runtime asks its own suppression register at claim; the provider has one of its own and
    /// may know something Jason does not. The plugin's call is refused on the provider's word, as
    /// <c>permanent</c>, so the item ends rather than retrying into the same refusal.
    /// </summary>
    [Fact]
    public async Task A_suppression_only_the_provider_knows_about_ends_the_item_rather_than_retrying_into_it()
    {
        using var workspace = new TestWorkspace();
        workspace.WithList(ProviderList, "Q3 prospects").WithSuppressed(Blocked);
        await using var api = await StartAsync(workspace);
        var campaign = await CampaignAsync(api);
        var contact = await ContactAsync(api, campaign, Blocked);
        var item = await ItemAsync(api, campaign, "list_membership.add", ToTheList(), contact);

        await ScanAsync(api);

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.Failed, read.Status);
        var attempt = Assert.Single(read.Attempts!);
        Assert.Equal("suppressed", attempt.Error!.Code);
        Assert.Equal(FailureClass.Permanent, attempt.Error.Class);
        Assert.False(attempt.Error.Retriable);

        // Nothing was added, and the claim let it through: Jason's own register holds nothing about this address.
        Assert.Empty(workspace.MembersOf(ProviderList));
        Assert.Equal(["contact ensure"], workspace.Calls);
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
    private static async Task ScanAsync(RuntimeApiFixture api)
    {
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)));
    }

    /// <summary>The attempts of one item, oldest first: a read answers newest first, and a story reads forwards.</summary>
    private static IReadOnlyList<AttemptDto> InOrder(WorkItemDto item) => [.. item.Attempts!.OrderBy(attempt => attempt.Number)];

    private static JsonObject ToTheList() => new()
    {
        ["list"] = new JsonObject { ["external_id"] = ProviderList },
        ["channel"] = "email",
    };

    private static JsonObject Named(string externalId) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = externalId },
    };

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Flow" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        return campaign.Id;
    }

    private static async Task<string> ContactAsync(RuntimeApiFixture api, string campaign, string address)
    {
        var contact = await api.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Marta", channels = new[] { new { channel = "email", value = address } } },
            Ct);
        await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign, contacts = new[] { new { contact_id = contact.Id } } },
            Ct);
        return contact.Id;
    }

    private static async Task<string> ItemAsync(RuntimeApiFixture api, string campaign, string operation, JsonObject arguments, string? contact)
    {
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation,
                contact_id = contact,
                context = new JsonObject { ["input"] = arguments },
            },
            Ct);
        return item.Id;
    }
}
