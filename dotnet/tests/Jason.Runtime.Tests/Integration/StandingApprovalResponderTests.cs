using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Plugins.Reply;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// A standing approval where it is meant to work: a reply arrives, the runtime wakes the campaign's responder, a real
/// child records its verdict and drafts the answer as its own attempt, and the claim lets the draft go to the account
/// under the word a person gave in advance — or, with no such word, parks it for them. The official package performs the
/// answer against the stand-in account, so what reached the provider is read from the account's own call log.
/// </summary>
/// <remarks>
/// Only the answer is routed, for this campaign. The read of the thread follows the thread to the plugin that route
/// names, so the responder is woken waiting on that read, made at the answer's plugin and account, and is claimed once
/// the read has succeeded.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class StandingApprovalResponderTests
{
    private const string Thread = "501";

    private const string Category = "interested";

    /// <summary>The account the campaign's <c>conversation.reply</c> route binds, which also asks for the first name.</summary>
    private static JsonObject Binding() => new() { ["derive_first_name"] = true };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole way: the verdict is the responder's attempt's own, the draft names it, and the claim releases the draft
    /// under an approved row the person who set the standing approval decided. The account was read for the responder,
    /// then read again and answered by the answer itself.
    /// </summary>
    [Fact]
    public async Task A_responder_s_draft_goes_to_the_account_under_a_standing_approval()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(int.Parse(Thread, System.Globalization.CultureInfo.InvariantCulture), "2026-09-14T11:55:00Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var host = await StartAsync();
        var (campaign, contact) = await CampaignAsync(host);
        await host.Fixture.PostOkAsync<StandingApprovalDto>(
            Operations.ApprovalSetStanding,
            new { campaign_id = campaign, category = Category, actor = new { type = "human", id = "operator" } },
            Ct);

        var (read, responder, draft) = await DraftedAsync(host, campaign, contact);

        await AssertReadAtTheAnswersRouteAsync(host, read);
        await host.WaitForStatusAsync(draft.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal(["inbox get " + Thread, "inbox get " + Thread, "inbox reply " + Thread], Answered(account));

        await using var db = Open(host);
        Assert.Equal(1, await db.Attempts.CountAsync(row => row.WorkItem!.PublicId == draft.Id, Ct));
        var verdict = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.ReplyClassified, Ct);
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(row => row.WorkItem!.PublicId == responder.Id, Ct);
        Assert.Equal((ActorType.Attempt, attempt.PublicId), (verdict.ActorType, verdict.ActorId));
        Assert.Equal(verdict.PublicId, (string?)draft.Context!["reply_verdict"]);

        var approval = await db.Approvals.AsNoTracking().SingleAsync(row => row.WorkItem!.PublicId == draft.Id, Ct);
        Assert.Equal((ApprovalStatus.Approved, ApprovalGate.StandingApproval), (approval.Status, approval.Reason));
        Assert.Equal((ActorType.Human, "operator"), (approval.DecidedByType, approval.DecidedById));
        Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.ApprovalStandingApplied, Ct));
    }

    /// <summary>The same reply and the same draft, with no word given in advance: the draft waits for a person, and the account is read once for the responder and never answered.</summary>
    [Fact]
    public async Task Without_a_standing_approval_the_responder_s_draft_waits_for_a_person()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(int.Parse(Thread, System.Globalization.CultureInfo.InvariantCulture), "2026-09-14T11:55:00Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var host = await StartAsync();
        var (campaign, contact) = await CampaignAsync(host);

        var (read, _, draft) = await DraftedAsync(host, campaign, contact);

        await AssertReadAtTheAnswersRouteAsync(host, read);
        await host.WaitForStatusAsync(draft.Id, WorkItemStatus.AwaitingApproval, Ct);
        Assert.Equal(["inbox get " + Thread], Answered(account));
        await using (var db = Open(host))
        {
            Assert.False(await db.Attempts.AnyAsync(row => row.WorkItem!.PublicId == draft.Id, Ct));
        }

        var waiting = await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct);
        var summary = Assert.Single(waiting.Items);
        var asked = await host.Fixture.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = summary.Id }, Ct);
        Assert.Equal((draft.Id, StandingApprovals.Why.NoStandingApproval), (asked.WorkItemId, (string?)asked.Preview!["standing_approval"]!["why"]));
    }

    /// <summary>
    /// What the account was asked about the thread: the package's reads of the account's events, which the runtime makes
    /// on its own cadence for every account a route binds, are left out.
    /// </summary>
    private static IEnumerable<string> Answered(ReplyAccount account) =>
        account.Calls.Where(call => call.Command != "inbox changes").Select(call => call.Line);

    /// <summary>
    /// A reply said on the campaign's thread; the read of the thread it queues and the responder created waiting on it,
    /// both run to their end; and the draft the responder made handed to the claim in the scan after. Answers the read,
    /// the responder and the draft as they stand after that scan.
    /// </summary>
    private static async Task<(WorkItemDto Read, WorkItemDto Responder, WorkItemDto Draft)> DraftedAsync(FakeHostRuntime host, string campaign, string contact)
    {
        await InboundAsync(host, campaign, contact);

        await host.ScanAsync(Ct);
        string woken;
        string read;
        await using (var db = Open(host))
        {
            woken = (await db.WorkItems.AsNoTracking().SingleAsync(item => item.Role == Responder.Role, Ct)).PublicId;
            read = (await db.WorkItems.AsNoTracking().SingleAsync(item => item.Operation == Responder.ReadOperation, Ct)).PublicId;
        }

        host.Track(read);
        host.Track(woken);
        await SettleAsync(host, async () => (await host.GetAsync(woken, Ct)).Status == WorkItemStatus.Succeeded);
        var responder = await host.GetAsync(woken, Ct);
        var draftId = (string)responder.Result!["created_work_items"]![0]!;
        host.Track(draftId);

        await host.ScanAsync(Ct);
        return (await host.GetAsync(read, Ct), responder, await host.GetAsync(draftId, Ct));
    }

    /// <summary>
    /// The read of the thread succeeded in one attempt, at the plugin and the account of the campaign's
    /// <c>conversation.reply</c> route: the thread was seen there, and no route of the read's own names another.
    /// </summary>
    private static async Task AssertReadAtTheAnswersRouteAsync(FakeHostRuntime host, WorkItemDto read)
    {
        var attempt = Assert.Single((await host.GetAsync(read.Id, Ct)).Attempts!);
        Assert.Equal(
            (AttemptStatus.Succeeded, ReplyPlugins.PluginId, BindingIdentity.Of(Binding())),
            (attempt.Status, attempt.Provenance!.PluginId, attempt.Provenance.BindingIdentity));
    }

    /// <summary>
    /// Scans until the row's condition holds, each scan followed by a wait for what it started: the handler pool's
    /// children and the events reader's read. The claim takes one item of a campaign per scan, so the read and then the
    /// responder it wakes take a few scans.
    /// </summary>
    private static async Task SettleAsync(FakeHostRuntime host, Func<Task<bool>> done)
    {
        var pool = host.Fixture.Resolve<HandlerPool>();
        var reader = host.Fixture.Resolve<EventReader>();
        for (var scan = 0; scan < 12; scan++)
        {
            await host.ScanAsync(Ct);
            Assert.True(
                await DispatchHarness.EventuallyAsync(() => pool.InFlightAttemptIds.Count == 0 && reader.Idle, Ct, timeoutMilliseconds: 30_000),
                "A child the scan started never finished.");
            if (await done())
            {
                return;
            }
        }

        Assert.Fail("Twelve scans did not bring the row to the state it waits for.");
    }

    /// <summary>The line the inbound read writes for a thread that moved, as it writes it, about the campaign's person.</summary>
    private static async Task InboundAsync(FakeHostRuntime host, string campaignId, string contactId)
    {
        await using var db = Open(host);
        var campaign = await db.Campaigns.SingleAsync(row => row.PublicId == campaignId, Ct);
        var moment = InboundSync.Instant(new DateTimeOffset(FakeHostRuntime.Noon.AddMinutes(-5)));
        new JournalWriter(host.Clock).Append(
            db,
            Actors.Dispatcher,
            JournalKinds.InboundReplyObserved,
            campaign,
            key: InboundSync.ObservationKey(ReplyPlugins.PluginId, Thread, moment),
            updated: new JsonObject
            {
                ["plugin"] = ReplyPlugins.PluginId,
                ["thread"] = Thread,
                ["channel"] = "email",
                ["last_activity_at"] = moment,
                ["unread"] = true,
                ["has_meeting_intent"] = null,
                ["contact_id"] = contactId,
            });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<(string Campaign, string Contact)> CampaignAsync(FakeHostRuntime host)
    {
        var campaign = await host.CampaignAsync(Ct, "Autumn outreach");
        var contact = await host.ContactAsync(campaign, ReplyOperations.Address, Ct);
        await TestRoutes.SetCampaignAsync(host.Fixture, campaign, "conversation.reply", ReplyPlugins.PluginId, Binding(), Ct);
        return (campaign, contact);
    }

    /// <summary>
    /// The fake host's short time limits, and the fake host as the entry command of every role without one of its own: the
    /// responder is one of the runtime's own roles, so it is launched by the default rather than added. The manager is
    /// one too and would be launched the same way, so it is woken neither by the reply nor by the cadence here: the reply
    /// is the responder's, and a review in the same campaign would only take the claim the draft is waiting for.
    /// </summary>
    private static string Settings()
    {
        var settings = JsonNode.Parse(FakeHostRuntime.Settings)!.AsObject();
        settings["Roles"] = new JsonObject
        {
            ["DefaultEntryCommand"] = new JsonArray("dotnet", Execution.FakeAgentHost.Dll, "responder", "--category", Category),
        };
        settings["Manager"] = new JsonObject
        {
            ["Triggers"] = new JsonArray(JournalKinds.WorkItemFailed, JournalKinds.DecisionAnswered),
            ["ReviewSeconds"] = ManagerOptions.MaximumReviewSeconds,
        };
        return settings.ToJsonString();
    }

    private static Task<FakeHostRuntime> StartAsync() =>
        FakeHostRuntime.StartAsync(
            Ct,
            Settings(),
            prepare: paths =>
            {
                ReplyPlugins.Install(paths);
                TestPlugins.Grant(paths, ReplyPlugins.PluginId, exec: [ReplyPlugins.ExecutableName]);
            },
            configureServices: services =>
            {
                services.AddSingleton<ISearchPath>(ReplyPlugins.SearchPath);
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
            });

    private static JasonDbContext Open(FakeHostRuntime host) => new(JasonDbContext.CreateOptions(host.Paths.DatabaseFile));
}
