using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The gate where it matters: a real runtime, a real plugin host, and a real provider account at the other end.
/// What is proven here is not that a function returned something — it is that an operation nobody approved never
/// reached the account, that approving it ran exactly what was approved, and that editing the work afterwards
/// stopped it again.
/// </summary>
/// <remarks>
/// The account's own call log is the evidence for "nothing was asked". An empty log is a strong claim, and it is
/// the one the story asks for at the dispatcher boundary: a provider that would have answered the same either
/// way cannot tell a runtime that asked from one that did not.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ApprovalGateTests
{
    private const string ProviderCampaign = "seq_8811";

    private const string Recipient = "ada@example.test";

    /// <summary>A real dispatcher that never ticks on its own: every scan here is one the test asked for.</summary>
    private const string Idle = """{"Dispatcher":{"TickSeconds":3600}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The guarantee, at the boundary that makes it. Scans pass, the item stays where it is, no attempt is made,
    /// and the account was never asked for anything — not even the read an enrollment starts with.
    /// </summary>
    [Fact]
    public async Task An_unapproved_act_operation_never_reaches_a_plugin_host()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Autumn outreach", "Active");
        await using var api = await StartAsync(workspace);
        var (campaign, contact) = await WorkAsync(api);
        var item = await ItemAsync(api, campaign, contact);

        for (var scan = 0; scan < 3; scan++)
        {
            Assert.Equal(0, (await ScanAsync(api)).Claimed);
        }

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, read.Status);
        Assert.Equal(0, read.AttemptCount);
        Assert.Empty(read.Attempts!);

        // Nothing was asked of the account: not the enrollment, and not the read that would have preceded it.
        Assert.Empty(workspace.Calls);

        var waiting = await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct);
        Assert.Equal(item, Assert.Single(waiting.Items).WorkItemId);
    }

    /// <summary>
    /// Approving runs exactly what was approved: the document the plugin receives is the subject a person read,
    /// and the attempt that ran names the decision that released it.
    /// </summary>
    [Fact]
    public async Task An_approved_item_runs_the_subject_that_was_approved_and_names_the_approval_that_released_it()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Autumn outreach", "Active");
        await using var api = await StartAsync(workspace);
        var (campaign, contact) = await WorkAsync(api);
        var item = await ItemAsync(api, campaign, contact);
        Assert.Equal(0, (await ScanAsync(api)).Claimed);

        var approval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        var subject = (await api.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = approval.Id }, Ct)).Subject;

        var decided = await api.PostOkAsync<ApprovalDto>(
            Operations.ApprovalApprove,
            new { approval_id = approval.Id, actor = new { type = "human", id = "ada" }, reason = "go ahead" },
            Ct);
        Assert.Equal(ApprovalStatus.Approved, decided.Status);

        Assert.Equal(1, (await ScanAsync(api)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(30)));

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item, include_snapshots = true }, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, read.Status);
        var attempt = Assert.Single(read.Attempts!);
        Assert.Equal(approval.Id, attempt.Provenance!.ApprovalId);

        // The account was asked for the enrollment, and the person it enrolled is the one the subject named.
        Assert.Contains("campaign enroll", workspace.Calls, StringComparer.Ordinal);
        Assert.Equal(Recipient, (string?)subject["input"]!["contacts"]![0]!["channels"]![0]!["value"]);
        Assert.Single(workspace.EnrollmentsIn(ProviderCampaign));
    }

    /// <summary>
    /// The decision is about a subject. Editing the work after a person approved it makes what would run
    /// something nobody agreed to, so it is parked again — and the account still has not been asked.
    /// </summary>
    [Fact]
    public async Task An_input_changed_after_the_decision_is_parked_again_and_says_which_hash_moved()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Autumn outreach", "Active");
        workspace.WithCampaign("seq_9922", "Another campaign entirely", "Active");
        await using var api = await StartAsync(workspace);
        var (campaign, contact) = await WorkAsync(api);
        var item = await ItemAsync(api, campaign, contact);
        Assert.Equal(0, (await ScanAsync(api)).Claimed);

        var first = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        await api.PostOkAsync<ApprovalDto>(
            Operations.ApprovalApprove,
            new { approval_id = first.Id, actor = new { type = "human", id = "ada" } },
            Ct);

        // A planner edits the enrollment after it was approved: a different campaign at the provider.
        var edited = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            new { work_item_id = item, set = new JsonObject { ["input"] = Enrolment("seq_9922") } },
            Ct);
        Assert.Equal("seq_9922", (string?)edited.Context["input"]!["campaign"]!["external_id"]);

        Assert.Equal(0, (await ScanAsync(api)).Claimed);

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, read.Status);
        Assert.Empty(read.Attempts!);
        Assert.Empty(workspace.Calls);

        var second = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("input_changed", second.Reason);
        Assert.NotEqual(first.SubjectHash, second.SubjectHash);

        var outgrown = await api.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = first.Id }, Ct);
        Assert.Equal(ApprovalStatus.Superseded, outgrown.Status);
    }

    /// <summary>
    /// A rejection is an accountable ending: the item is over, it carries the decision and the person, and the
    /// account was never asked for anything.
    /// </summary>
    [Fact]
    public async Task A_rejected_item_ends_failed_carrying_the_decision_and_the_person_who_made_it()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Autumn outreach", "Active");
        await using var api = await StartAsync(workspace);
        var (campaign, contact) = await WorkAsync(api);
        var item = await ItemAsync(api, campaign, contact);
        Assert.Equal(0, (await ScanAsync(api)).Claimed);
        var approval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);

        await api.PostOkAsync<ApprovalDto>(
            Operations.ApprovalReject,
            new { approval_id = approval.Id, actor = new { type = "human", id = "ada" }, reason = "not this quarter" },
            Ct);

        Assert.Equal(0, (await ScanAsync(api)).Claimed);

        var read = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.Failed, read.Status);
        Assert.Equal("approval_rejected", read.LastError!.Code);
        Assert.Equal("not this quarter", read.LastError.Message);
        Assert.Empty(read.Attempts!);
        Assert.Empty(workspace.Calls);

        // The chronicle reads: parked, decided, ended.
        var kinds = await KindsAsync(api, item);
        Assert.Equal(
            ["workitem_created", "workitem_awaiting_approval", "approval_requested", "approval_rejected", "workitem_failed"],
            kinds);
    }

    /// <summary>
    /// The preview names a real person and where they would be reached. That belongs in the answer a person asks
    /// for and in the row it was written on — and nowhere else. The logs are the place this rule has always been
    /// kept, and a chronicle is not a copy of somebody's contact details either.
    /// </summary>
    [Fact]
    public async Task Nothing_of_a_preview_reaches_the_journal_or_the_log_files()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Autumn outreach", "Active");
        await using var api = await StartAsync(workspace);
        var (campaign, contact) = await WorkAsync(api);
        var item = await ItemAsync(api, campaign, contact);
        Assert.Equal(0, (await ScanAsync(api)).Claimed);
        var approval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        await api.PostOkAsync<ApprovalDto>(
            Operations.ApprovalReject,
            new { approval_id = approval.Id, actor = new { type = "human", id = "ada" }, reason = "no" },
            Ct);

        // The person's address is in the answer a person asked for, which is the point of a preview.
        var read = await api.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = approval.Id }, Ct);
        Assert.Equal(Recipient, (string?)read.Preview["contact"]!["value"]);

        // And in neither the chronicle nor the logs, which carry identifiers and nothing else.
        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { campaign_id = campaign, limit = 1000 }, Ct);
        Assert.DoesNotContain(Recipient, entries.Items.Select(entry => entry.New?.ToJsonString() ?? string.Empty).Aggregate(string.Empty, string.Concat), StringComparison.Ordinal);
        Assert.Contains(approval.Id, entries.Items.Select(entry => entry.New?.ToJsonString() ?? string.Empty).Aggregate(string.Empty, string.Concat), StringComparison.Ordinal);

        await api.Runtime.StopAsync();
        var logs = string.Concat(Directory.GetFiles(api.Paths.LogsDirectory).Select(File.ReadAllText));
        Assert.DoesNotContain(Recipient, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Autumn outreach", logs, StringComparison.Ordinal);
        _ = item;
    }

    private static async Task<IReadOnlyList<string>> KindsAsync(RuntimeApiFixture api, string item)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        return await db.Journal.AsNoTracking()
            .Where(entry => entry.WorkItemId == item)
            .OrderBy(entry => entry.Id)
            .Select(entry => entry.Kind)
            .ToListAsync(Ct);
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
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

    private static async Task<ScanReport> ScanAsync(RuntimeApiFixture api)
    {
        Assert.True(await DispatchHarness.FirstScanDoneAsync(api.Resolve<DispatcherStatus>(), Ct));
        return await api.Resolve<ScanRunner>().ScanOnceAsync(Ct);
    }

    private static async Task<(string Campaign, string Contact)> WorkAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Autumn outreach" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var contact = await api.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Ada", channels = new[] { new { channel = "email", value = Recipient } } },
            Ct);
        await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign.Id, contacts = new[] { new { contact_id = contact.Id } } },
            Ct);
        return (campaign.Id, contact.Id);
    }

    private static async Task<string> ItemAsync(RuntimeApiFixture api, string campaign, string contact)
    {
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "campaign.enroll",
                contact_id = contact,
                context = new JsonObject { ["input"] = Enrolment(ProviderCampaign) },
            },
            Ct);
        return item.Id;
    }

    private static JsonObject Enrolment(string providerCampaign) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = providerCampaign },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "authored_delay",
    };
}
