using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A provider block end to end: a real runtime on a held clock, a real plugin host for every provider call, a stand-in
/// agent host for the manager, and a stand-in account at the other end. The account refuses an enrolment for a reason
/// outside the work — the account's billing — and what is proven is what that does to the rest of the campaign: the
/// work the block holds waits on it without reaching the account, a read still runs, the manager hears of it once, and
/// when the block's trial gets past it everything it held runs by itself.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ProviderBlockParkTests
{
    private const string ProviderCampaign = "seq_8811";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An idle loop, a trial every fifteen minutes, and every role — the manager among them — launched through the
    /// stand-in host, which answers a check-in in the shape it is held to.
    /// </summary>
    private static string Settings()
    {
        var command = string.Join(
            ",",
            new[] { "dotnet", Execution.FakeAgentHost.Dll, "manager" }.Select(part => JsonSerializer.Serialize(part)));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"Dispatcher\":{{\"TickSeconds\":3600,\"RetryDelaySeconds\":0,\"ExitGraceSeconds\":30,\"Blocks\":{{\"TrialSeconds\":900}},"
            + $"\"AiRole\":{{\"TimeoutSeconds\":60,\"HeartbeatSeconds\":10,\"MaxAttempts\":2}}}},"
            + $"\"Roles\":{{\"DefaultEntryCommand\":[{command}]}},"
            + $"\"Manager\":{{\"ReviewSeconds\":86400,\"TimeoutSeconds\":60}}}}");
    }

    [Fact]
    public async Task Work_an_account_block_holds_waits_on_it_a_read_runs_and_a_trial_that_gets_past_it_runs_the_rest()
    {
        using var workspace = new TestWorkspace();
        workspace.WithCampaign(ProviderCampaign, "Autumn outreach", "Active");
        await using var host = await FakeHostRuntime.StartAsync(
            Ct,
            Settings(),
            prepare: paths =>
            {
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
        var campaign = await host.CampaignAsync(Ct, "Autumn outreach");
        var marta = await host.ContactAsync(campaign, "marta@example.test", Ct);
        var ada = await host.ContactAsync(campaign, "ada@example.test", Ct);

        // The campaign is linked to the account's first, by a read, so what the account calls it is known before anyone
        // decides about work in it.
        var link = await ReadAsync(host, campaign);
        await SettleAsync(host, campaign, () => AllAsync(host, WorkItemStatus.Succeeded, link));

        // Two enrolments a person approves; the first the claim takes is the one the account refuses, for its billing.
        var first = await EnrolAsync(host, campaign, marta, priority: 9);
        var second = await EnrolAsync(host, campaign, ada, priority: 5);
        await SettleAsync(host, campaign, () => AllAsync(host, WorkItemStatus.AwaitingApproval, first, second));
        await ApproveAsync(host);

        workspace.RefuseOnce(first, "account_blocked");

        // 1. The refused attempt ends blocked, uncounted, and its item waits on the block; the other enrolment, which the
        //    account would have taken, waits on it too, with no attempt, and the account was never asked for it.
        await SettleAsync(host, campaign, () => AllAsync(host, WorkItemStatus.Waiting, first, second));
        var refused = await host.GetAsync(first, Ct);
        Assert.Equal(AttemptStatus.Blocked, Assert.Single(refused.Attempts!).Status);
        Assert.Equal(0, refused.AttemptCount);
        var held = await host.GetAsync(second, Ct);
        Assert.Empty(held.Attempts!);
        var blockedBy = held.Wait!.BlockedBy!;
        Assert.Equal(refused.Wait!.BlockedBy, blockedBy);
        Assert.Equal((ProviderBlockScope.Account, "billing"), (blockedBy.Scope, blockedBy.Reason));
        Assert.DoesNotContain("provider.example", JsonSerializer.Serialize(held.Wait, JasonJson.Options), StringComparison.Ordinal);
        Assert.Empty(workspace.EnrollmentsIn(ProviderCampaign));

        // 2. The campaign is told once, and that line summons its manager, whose check-in runs and ends.
        var told = Assert.Single(await LinesAsync(host, campaign, JournalKinds.ProviderBlocked));
        Assert.Equal(blockedBy.ProviderBlockId, told.Key);
        Assert.DoesNotContain("provider.example", told.New!.ToJsonString(), StringComparison.Ordinal);
        await SettleAsync(host, campaign, async () => (await CheckInsAsync(host, campaign)).Any(c => c.Status == WorkItemStatus.Succeeded));
        var checkIn = Assert.Single(await CheckInsAsync(host, campaign));
        Assert.Equal(JournalKinds.ProviderBlocked, (string?)checkIn.Context["trigger"]);

        // 3. A read runs through the open block, and the block still holds what it held.
        var read = await ReadAsync(host, campaign);
        await SettleAsync(host, campaign, () => AllAsync(host, WorkItemStatus.Succeeded, read));
        Assert.True(await AllAsync(host, WorkItemStatus.Waiting, first, second), "The read released what the block holds.");

        // 4. The trial comes due and lets the first enrolment through. The refused call had learned what the account
        //    calls Marta, so what would run is no longer the document the person approved, and they decide once more —
        //    the rule every repeat of approved work is held to. The account takes it, and the block clears; the next
        //    scan releases the other one, whose document did not change, and it runs once and is taken too.
        host.Clock.Advance(TimeSpan.FromSeconds(901));
        await SettleAsync(host, campaign, () => AllAsync(host, WorkItemStatus.AwaitingApproval, first));
        Assert.Equal(WorkItemStatus.Waiting, (await host.GetAsync(second, Ct)).Status);
        await ApproveAsync(host);
        await SettleAsync(host, campaign, () => AllAsync(host, WorkItemStatus.Succeeded, first, second));
        Assert.Equal([AttemptStatus.Blocked, AttemptStatus.Succeeded], [.. (await host.GetAsync(first, Ct)).Attempts!.OrderBy(a => a.Number).Select(a => a.Status)]);
        Assert.Equal(AttemptStatus.Succeeded, Assert.Single((await host.GetAsync(second, Ct)).Attempts!).Status);
        Assert.Equal(2, workspace.EnrollmentsIn(ProviderCampaign).Count);
        Assert.Equal(["block_trial"], await BecauseAsync(host, first));
        Assert.Equal(["block_cleared"], await BecauseAsync(host, second));
        Assert.Equal(blockedBy.ProviderBlockId, Assert.Single(await LinesAsync(host, campaign, JournalKinds.ProviderBlockCleared)).Key);

        // 5. The clear woke nobody: the manager was summoned once, by the block.
        await host.ScanAsync(Ct);
        Assert.Single(await CheckInsAsync(host, campaign));
    }

    private static async Task<string> ReadAsync(FakeHostRuntime host, string campaign) =>
        (await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "campaign.get",
                context = new JsonObject { ["input"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = ProviderCampaign } } },
            },
            Ct)).Id;

    /// <summary>Every decision waiting on a person, approved by one.</summary>
    private static async Task ApproveAsync(FakeHostRuntime host)
    {
        foreach (var approval in (await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items)
        {
            await host.Fixture.PostOkAsync<ApprovalDto>(
                Operations.ApprovalApprove,
                new { approval_id = approval.Id, actor = new { type = "human", id = "marta" }, reason = "checked" },
                Ct);
        }
    }

    private static async Task<string> EnrolAsync(FakeHostRuntime host, string campaign, string contact, int priority) =>
        (await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "campaign.enroll",
                contact_id = contact,
                priority,
                context = new JsonObject
                {
                    ["input"] = new JsonObject
                    {
                        ["campaign"] = new JsonObject { ["external_id"] = ProviderCampaign },
                        ["channel"] = "email",
                        ["collision"] = "skip",
                        ["start"] = new JsonObject { ["position"] = "first_step" },
                        ["first_touch"] = "authored_delay",
                    },
                },
            },
            Ct)).Id;

    private static async Task<bool> AllAsync(FakeHostRuntime host, WorkItemStatus status, params string[] items)
    {
        foreach (var item in items)
        {
            if ((await host.GetAsync(item, Ct)).Status != status)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Scans the test asks for, each followed by the wait for what it handed out to end — children run in real time —
    /// until <paramref name="done"/> answers, within the patience a real child is given.
    /// </summary>
    private static async Task SettleAsync(FakeHostRuntime host, string campaign, Func<Task<bool>> done)
    {
        var deadline = DateTime.UtcNow.Add(Patience);
        while (DateTime.UtcNow < deadline)
        {
            await host.ScanAsync(Ct);
            while (DateTime.UtcNow < deadline && await RunningAsync(host, campaign))
            {
                await Task.Delay(200, Ct);
            }

            if (await done())
            {
                return;
            }
        }

        var items = (await host.Fixture.PostOkAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, new { campaign_id = campaign }, Ct)).Items;
        var approvals = (await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(
            Operations.ApprovalList,
            new { statuses = Enum.GetValues<ApprovalStatus>(), limit = 100 },
            Ct)).Items;
        Assert.Fail(
            $"The campaign never settled as expected: {string.Join(", ", items.Select(i => $"{i.Id} {i.Operation ?? i.Role} {i.Status}"))}; "
            + $"its decisions: {string.Join(", ", approvals.Select(a => $"{a.WorkItemId} {a.Status} {a.Reason}"))}.");
    }

    private static async Task<bool> RunningAsync(FakeHostRuntime host, string campaign) =>
        (await host.Fixture.PostOkAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, new { campaign_id = campaign }, Ct)).Items
            .Any(i => i.Status is WorkItemStatus.Scheduled or WorkItemStatus.Processing);

    private static async Task<IReadOnlyList<JournalEntryDto>> LinesAsync(FakeHostRuntime host, string campaign, string kind) =>
        (await host.Fixture.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { campaign_id = campaign, kind, limit = 100 }, Ct)).Items;

    /// <summary>Why each of an item's waits was released, oldest first, as its <c>workitem_resumed</c> lines say.</summary>
    private static async Task<IReadOnlyList<string?>> BecauseAsync(FakeHostRuntime host, string item) =>
        [.. (await host.Fixture.PostOkAsync<Page<JournalEntryDto>>(
                Operations.JournalList,
                new { work_item_id = item, kind = JournalKinds.WorkItemResumed, limit = 100 },
                Ct)).Items
            .OrderBy(line => line.Ts)
            .Select(line => (string?)line.New!["because"])];

    private static async Task<IReadOnlyList<WorkItemDto>> CheckInsAsync(FakeHostRuntime host, string campaign)
    {
        var page = await host.Fixture.PostOkAsync<Page<WorkItemSummaryDto>>(
            Operations.WorkItemList,
            new { campaign_id = campaign, role = ManagerCheckIn.Role },
            Ct);
        var read = new List<WorkItemDto>();
        foreach (var item in page.Items)
        {
            host.Track(item.Id);
            read.Add(await host.GetAsync(item.Id, Ct));
        }

        return read;
    }
}
