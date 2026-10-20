using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Contacts;
using Jason.Runtime.Routing;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Journal;
using Jason.Runtime.Domain;
using Jason.Runtime.Contacts;
using Jason.Contracts.Ids;
using Jason.Runtime.Tests.Ui;
using Jason.Runtime.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The exchanges a consumer renders, recorded from this runtime: each is also an ordinary test of the operation. Ids are
/// chosen so the files are the same on every machine (the scrubber renumbers them in order of appearance, so the order
/// a listing answers in must be fixed by the ids themselves).
/// </summary>
public class RecordedExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Noon = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    // Fixed ULID-shaped ids: the listing orders by id, so A sorts before B on every machine.
    private const string ApprovalA = "apr_01K7QN0000000000000000000A";
    private const string ApprovalB = "apr_01K7QN0000000000000000000B";
    private const string ApprovalC = "apr_01K7QN0000000000000000000C";
    private const string ApprovalD = "apr_01K7QN0000000000000000000D";
    private const string DecisionA = "dec_01K7QN0000000000000000000A";
    private const string DecisionB = "dec_01K7QN0000000000000000000B";

    [Fact]
    public async Task Approval_list_pending_and_empty_and_decided()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var empty = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "empty", null, Ct);
        Assert.Empty(empty.Items);

        await Seed.ApprovalAsync(api, ApprovalA, "Ada", "Lovelace", decided: false);
        await Seed.ApprovalAsync(api, ApprovalB, "Grace", "Hopper", decided: false, reason: ApprovalGate.InputChanged);
        await Seed.ApprovalAsync(api, ApprovalC, "Edsger", "Dijkstra", decided: true);
        await Seed.ApprovalAsync(api, ApprovalD, "Barbara", "Liskov", decided: true, status: ApprovalStatus.Superseded);

        var pending = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "pending", null, Ct);
        Assert.Equal(["Ada Lovelace", "Grace Hopper"], pending.Items.Select(a => a.ContactName));
        Assert.All(pending.Items, a => Assert.Equal("Autumn outreach", a.CampaignName));

        // Everything that no longer needs a person: decided by one, or ended without one.
        var decided = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "decided", new { statuses = new[] { "approved", "rejected", "superseded", "cancelled" } }, Ct);
        Assert.Equal([ApprovalStatus.Approved, ApprovalStatus.Superseded], decided.Items.Select(a => a.Status));
        Assert.Equal(new ActorRef(ActorType.Human, "ada"), decided.Items[0].DecidedBy);
        Assert.Equal(new ActorRef(ActorType.System, "dispatcher"), decided.Items[1].DecidedBy);
    }

    [Fact]
    public async Task Approval_get_approve_reject_and_the_already_decided_refusal()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        await Seed.ApprovalAsync(api, ApprovalA, "Ada", "Lovelace", decided: false);
        await Seed.ApprovalAsync(api, ApprovalB, "Grace", "Hopper", decided: false);

        var whole = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "pending-with-preview", new { approval_id = ApprovalA }, Ct);
        Assert.Equal("Put Ada Lovelace into the campaign at the provider.", (string?)whole.Preview["intent"]);

        var approved = await api.RecordAsync<ApprovalDto>(Operations.ApprovalApprove, "approved", new { approval_id = ApprovalA, actor = new { type = "human", id = "ada" } }, Ct);
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(new ActorRef(ActorType.Human, "ada"), approved.DecidedBy);

        var journal = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind = "approval_approved" }, Ct);
        var entry = Assert.Single(journal.Items);
        Assert.Equal(new ActorRef(ActorType.Human, "ada"), entry.Actor);
        Assert.Null(entry.Reason);

        var again = await api.RecordErrorAsync(Operations.ApprovalApprove, "already-decided", new { approval_id = ApprovalA, actor = new { type = "human", id = "ada" } }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("approval_not_pending", again.Code);

        var rejected = await api.RecordAsync<ApprovalDto>(Operations.ApprovalReject, "rejected", new { approval_id = ApprovalB, actor = new { type = "human", id = "ada" }, reason = "not this quarter" }, Ct);
        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
    }

    [Fact]
    public async Task Approval_list_with_a_token_nobody_minted_is_the_401_a_page_reads()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        await api.RecordUnauthorizedAsync(Operations.ApprovalList, "unauthorized", Ct);
    }

    [Fact]
    public async Task Decision_list_get_and_answer()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var empty = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "empty", null, Ct);
        Assert.Empty(empty.Items);

        await Seed.DecisionAsync(api, DecisionA, "Pause outreach to Vireo Labs until their funding news?", options: true);
        await Seed.DecisionAsync(api, DecisionB, "Which subject line for the SMB segment?", options: false);

        var pending = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "pending", null, Ct);
        Assert.Equal(2, pending.Items.Count);
        Assert.All(pending.Items, d => Assert.Equal("Autumn outreach", d.CampaignName));

        var whole = await api.RecordAsync<DecisionDto>(Operations.DecisionGet, "pending-with-options", new { decision_id = DecisionA }, Ct);
        Assert.Equal(["Pause until the news", "Keep going"], whole.Options!.Select(o => o.Label));

        var answered = await api.RecordAsync<DecisionDto>(Operations.DecisionAnswer, "answered", new { decision_id = DecisionA, answer = "Pause until the news", option = "Pause until the news", actor = new { type = "human", id = "ada" }, reason = "their CFO is leaving" }, Ct);
        Assert.Equal(DecisionStatus.Answered, answered.Status);
    }

    [Fact]
    public async Task System_info_with_and_without_a_ui_and_the_two_read_only_lists()
    {
        await using var without = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var bare = await without.RecordAsync<SystemInfoResponse>(Operations.SystemInfo, "without-ui", null, Ct);
        Assert.Null(bare.Ui);

        await using var with = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine), prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory, version: "0.10.0");
        });
        var info = await with.RecordAsync<SystemInfoResponse>(Operations.SystemInfo, "with-ui", null, Ct);
        Assert.Equal("default", info.Ui!.Source);

        var plugins = await with.RecordAsync<JsonObject>(Operations.PluginList, "none", null, Ct);
        Assert.NotEmpty(plugins);
        var routes = await with.RecordAsync<RoutesDto>(Operations.RouteList, "empty", null, Ct);
        Assert.Null(routes.Global.Default);
    }

    [Fact]
    public async Task Ui_tokens_none_two_and_one_revoked()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var none = await api.RecordAsync<UiTokensResponse>(Operations.UiTokens, "none", null, Ct);
        Assert.Empty(none.Items);

        var store = api.Resolve<UiTokenStore>();
        await store.IssueAsync("ada", Ct);
        var (_, second) = await store.IssueAsync("ada", Ct);
        var two = await api.RecordAsync<UiTokensResponse>(Operations.UiTokens, "two", null, Ct);
        Assert.Equal(2, two.Items.Count);

        var revoked = await api.RecordAsync<UiRevokeResponse>(Operations.UiRevoke, "one", new { token_id = second.Id }, Ct);
        Assert.Equal(1, revoked.Revoked);

        // Browsers paired over weeks: the first used an hour ago, one paired two days ago and never used since, and one
        // last used forty days ago — the kind a person clears out.
        await store.IssueAsync("ada", Ct);
        await store.IssueAsync("ada", Ct);
        using (var scope = api.Resolve<IServiceScopeFactory>().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var at = Nine.UtcDateTime;
            var rows = await db.UiTokens.OrderBy(t => t.Id).ToListAsync(Ct);
            (rows[0].IssuedAt, rows[0].LastUsedAt) = (at.AddDays(-10), at.AddHours(-1));
            (rows[1].IssuedAt, rows[1].LastUsedAt) = (at.AddDays(-60), at.AddDays(-40));
            (rows[2].IssuedAt, rows[2].LastUsedAt) = (at.AddDays(-2), null);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(3, (await api.RecordAsync<UiTokensResponse>(Operations.UiTokens, "aged", null, Ct)).Items.Count);
    }

    /// <summary>
    /// What the Work items screen reads — the three tabs and their counts, a page and the next, a person's work, an empty
    /// filter, each kind of item opened, the lists its filters are made from — and then what its panel writes.
    /// </summary>
    [Fact]
    public async Task Work_item_exchanges_for_the_screen()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var seed = await Seed.WorkItemsAsync(api);
        string[] now = ["processing", "scheduled", "awaiting_approval"];
        string[] done = ["succeeded", "failed", "cancelled", "expired"];

        Assert.Equal(3, (await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "now", new { status = now, order = "created", limit = 50 }, Ct)).Items.Count);
        var planned = await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "planned", new { status = new[] { "created" }, order = "due", limit = 3 }, Ct);
        Assert.NotNull(planned.NextCursor);
        var next = await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "planned-next", new { status = new[] { "created" }, order = "due", limit = 3, cursor = planned.NextCursor }, Ct);
        Assert.Null(next.NextCursor);
        Assert.Equal(4, (await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "done", new { status = done, order = "finished", limit = 50 }, Ct)).Items.Count);
        Assert.Single((await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "failed", new { status = new[] { "failed" }, order = "finished", limit = 50 }, Ct)).Items);
        Assert.Single((await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "by-contact", new { contact_id = seed.Tomas, status = new[] { "created" }, order = "due", limit = 50 }, Ct)).Items);
        var nothing = new { campaign_id = seed.SmbTest, kind = "ai_role", role = "manager", status = new[] { "created" }, order = "due" };
        Assert.Empty((await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "empty", new { nothing.campaign_id, nothing.kind, nothing.role, nothing.status, nothing.order, limit = 50 }, Ct)).Items);

        Assert.Equal(3, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "now", new { status = now, order = "created" }, Ct)).Total);
        Assert.Equal(5, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "planned", new { status = new[] { "created" }, order = "due" }, Ct)).Total);
        Assert.Equal(4, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "done", new { status = done, order = "finished" }, Ct)).Total);
        Assert.Equal(1, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "failed", new { status = new[] { "failed" }, order = "finished" }, Ct)).Total);
        Assert.Equal(1, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "filtered", new { campaign_id = seed.Autumn, contact_id = seed.Ada, kind = "ai_role", status = new[] { "created" }, order = "due" }, Ct)).Total);
        Assert.Equal(0, (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, "zero", nothing, Ct)).Total);

        Assert.Equal(WorkItemStatus.Created, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "open", new { work_item_id = seed.Open }, Ct)).Status);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "awaiting", new { work_item_id = seed.Awaiting }, Ct)).Status);
        Assert.Equal(2, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "failed", new { work_item_id = seed.Failed }, Ct)).Attempts!.Count);
        Assert.Equal(WorkItemStatus.Expired, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "expired", new { work_item_id = seed.Expired }, Ct)).Status);
        Assert.Equal(WorkItemStatus.Succeeded, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "succeeded", new { work_item_id = seed.Succeeded }, Ct)).Status);
        Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "by-work-item", new { work_item_id = seed.Awaiting, statuses = new[] { "pending" } }, Ct)).Items);

        Assert.Equal(2, (await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "first-page", new { limit = 10 }, Ct)).Items.Count);
        Assert.Single((await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "query", new { query = "smb", limit = 10 }, Ct)).Items);
        Assert.Equal("SMB test", (await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "one", new { campaign_id = seed.SmbTest }, Ct)).Name);
        Assert.Equal("campaign_not_found", (await api.RecordErrorAsync(Operations.CampaignGet, "unknown", new { campaign_id = "cmp_01K7QN00000000000000000099" }, HttpStatusCode.NotFound, Ct)).Code);
        Assert.Equal(3, (await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "first-page", new { limit = 10 }, Ct)).Items.Count);
        Assert.NotEmpty((await api.RecordAsync<JsonObject>(Operations.RoleList, "all", null, Ct))["items"]!.AsArray());
        Assert.Single((await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "query-email", new { query = "aguilar@", limit = 10 }, Ct)).Items);
        Assert.Single((await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "query-phone", new { query = "+48 601 234 567", limit = 10 }, Ct)).Items);
        Assert.Empty((await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "query-none", new { query = "zz", limit = 10 }, Ct)).Items);
        Assert.Equal("Ada", (await api.RecordAsync<ContactDto>(Operations.ContactGet, "one", new { contact_id = seed.Ada }, Ct)).FirstName);

        // Then what the panel writes, on the same runtime, so every file names the same item by the same scrubbed id:
        // new dates, the refusal a finished item answers with, a cancellation with a reason, and the history that follows.
        var ada = new { type = "human", id = "ada" };

        var moved = await api.RecordAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            "dates",
            new { work_item_id = seed.Open, not_before = "2026-10-16T11:00:00.000Z", due_at = "2026-10-16T15:00:00.000Z", actor = ada },
            Ct);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero), moved.DueAt);
        var refused = await api.RecordErrorAsync(Operations.WorkItemUpdate, "terminal", new { work_item_id = seed.Succeeded, priority = 5, actor = ada }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("workitem_terminal", refused.Code);
        var cancelled = await api.RecordAsync<WorkItemDto>(Operations.WorkItemCancel, "reason", new { work_item_id = seed.Open, reason = "Not this week.", actor = ada }, Ct);
        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
        var history = await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-work-item", new { work_item_id = seed.Open }, Ct);
        Assert.Equal(["workitem_cancelled", "workitem_updated", "workitem_updated"], history.Items.Select(e => e.Kind));
    }

    /// <summary>
    /// What the People screen reads — people by what last happened to them, by campaign, searched and with the archived,
    /// a person with their campaigns, their open work, their journal under each chip, their threads and one thread from
    /// the provider, and the refusals those reads can meet — and then what the card writes: adding a person to a
    /// campaign, re-enrolling an excluded one, and the refusal an archived campaign answers with.
    /// </summary>
    [Fact]
    public async Task People_exchanges_for_the_screen()
    {
        var reads = new FakeProviderReads();
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            clock: new FixedClock(Nine),
            configureServices: services => services.AddSingleton<IProviderReadInvoker>(reads));
        var seed = await Seed.PeopleAsync(api);
        string[] replies = ["inbound_reply_observed", "provider_event_observed"];
        string[] work = ["workitem_succeeded", "workitem_failed", "workitem_cancelled", "workitem_expired", "external_effect_reported"];
        string[] decisions = ["approval_requested", "approval_approved", "approval_rejected", "decision_raised", "decision_answered"];
        string[] changes = ["contact_created", "contact_updated", "contact_archived"];
        string[] all = [.. replies, .. work, .. decisions, .. changes];

        var first = await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "by-activity", new { order = "activity", limit = 3 }, Ct);
        Assert.Equal([seed.Mateo, seed.Aguilar, seed.Paula], first.Items.Select(c => c.Id));
        var rest = await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "by-activity-next", new { order = "activity", limit = 3, cursor = first.NextCursor }, Ct);
        Assert.Equal([seed.Grace], rest.Items.Select(c => c.Id));
        Assert.Equal([seed.Mateo, seed.Aguilar], (await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "by-campaign", new { order = "activity", campaign_id = seed.LatAm, limit = 50 }, Ct)).Items.Select(c => c.Id));
        Assert.Equal([seed.Mateo], (await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "by-activity-query", new { order = "activity", query = "mat", limit = 50 }, Ct)).Items.Select(c => c.Id));
        Assert.Contains(seed.Marcos, (await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "archived", new { order = "activity", include_archived = true, limit = 50 }, Ct)).Items.Select(c => c.Id));

        Assert.Equal(2, (await api.RecordAsync<ContactDto>(Operations.ContactGet, "with-campaigns", new { contact_id = seed.Mateo }, Ct)).Campaigns!.Count);
        Assert.Null((await api.RecordAsync<ContactDto>(Operations.ContactGet, "no-first-name", new { contact_id = seed.Aguilar }, Ct)).FirstName);
        Assert.NotNull((await api.RecordAsync<ContactDto>(Operations.ContactGet, "archived", new { contact_id = seed.Marcos }, Ct)).ArchivedAt);

        Assert.Single((await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "by-contact-open", new { contact_id = seed.Mateo, status = new[] { "created", "awaiting_approval", "scheduled", "processing" }, order = "due", limit = 50 }, Ct)).Items);

        Assert.Equal(6, (await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact", new { contact_id = seed.Mateo, kinds = all, limit = 50 }, Ct)).Items.Count);
        Assert.Single((await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact-replies", new { contact_id = seed.Mateo, kinds = replies, limit = 50 }, Ct)).Items);
        Assert.Equal(2, (await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact-work", new { contact_id = seed.Mateo, kinds = work, limit = 50 }, Ct)).Items.Count);
        Assert.Single((await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact-decisions", new { contact_id = seed.Mateo, kinds = decisions, limit = 50 }, Ct)).Items);
        Assert.Equal(2, (await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact-changes", new { contact_id = seed.Mateo, kinds = changes, limit = 50 }, Ct)).Items.Count);
        Assert.Equal(2, (await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact-failed", new { contact_id = seed.Aguilar, kinds = all, limit = 50 }, Ct)).Items.Count);
        Assert.Empty((await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-contact-empty", new { contact_id = seed.Grace, kinds = all, limit = 50 }, Ct)).Items);

        reads.Answer(
            FakeProviderReads.Result(Seed.Threads(more: false)),
            FakeProviderReads.Result(Seed.Threads(more: true)),
            FakeProviderReads.Failure("rate_limited", FailureClass.Transient),
            FakeProviderReads.Result(Seed.Thread(owner: "16786993", more: false)),
            FakeProviderReads.Result(Seed.Thread(owner: "16786993", more: true)),
            FakeProviderReads.Result(Seed.Thread(owner: "999", more: false)));
        var threads = new { contact_id = seed.Mateo, plugin_id = "reply", limit = 100 };
        Assert.Equal(2, (await api.RecordAsync<ContactThreadsResponse>(Operations.ContactThreads, "two-threads", threads, Ct)).Items.Count);
        Assert.True((await api.RecordAsync<ContactThreadsResponse>(Operations.ContactThreads, "has-more", threads, Ct)).HasMore);
        Assert.Equal("rate_limited", (await api.RecordErrorAsync(Operations.ContactThreads, "provider-failed", threads, HttpStatusCode.BadGateway, Ct)).Code);
        Assert.Equal("contact_not_linked", (await api.RecordErrorAsync(Operations.ContactThreads, "not-linked", new { contact_id = seed.Grace, limit = 100 }, HttpStatusCode.NotFound, Ct)).Code);
        Assert.Equal("no_route", (await api.RecordErrorAsync(Operations.ContactThreads, "no-route", new { contact_id = seed.Paula, limit = 100 }, HttpStatusCode.NotFound, Ct)).Code);
        var thread = new { contact_id = seed.Mateo, plugin_id = "reply", thread_id = "t_501", limit = 20 };
        Assert.Equal(3, (await api.RecordAsync<ContactThreadResponse>(Operations.ContactThread, "messages", thread, Ct)).Messages.Count);
        Assert.True((await api.RecordAsync<ContactThreadResponse>(Operations.ContactThread, "has-more", thread, Ct)).HasMore);
        Assert.Equal("thread_not_this_contact", (await api.RecordErrorAsync(Operations.ContactThread, "not-this-contact", thread, HttpStatusCode.NotFound, Ct)).Code);

        Assert.Equal(2, (await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "for-picker", new { limit = 10 }, Ct)).Items.Count);

        var ada = new { type = "human", id = "ada" };
        var added = await api.RecordAsync<AddContactsResult>(Operations.CampaignAddContacts, "one-person", new { campaign_id = seed.LatAm, contacts = new[] { new { contact_id = seed.Grace } }, reason = "Fits the pilot.", actor = ada }, Ct);
        Assert.Equal(1, added.Summary.Added);
        var reenrolled = await api.RecordAsync<AddContactsResult>(Operations.CampaignAddContacts, "re-enrol-excluded", new { campaign_id = seed.ReEngage, contacts = new[] { new { contact_id = seed.Mateo } }, actor = ada }, Ct);
        Assert.Equal(1, reenrolled.Summary.Added);
        var again = await api.RecordAsync<AddContactsResult>(Operations.CampaignAddContacts, "already-member", new { campaign_id = seed.LatAm, contacts = new[] { new { contact_id = seed.Mateo } }, actor = ada }, Ct);
        Assert.Equal(1, again.Summary.AlreadyMember);
        Assert.Equal("campaign_archived", (await api.RecordErrorAsync(Operations.CampaignAddContacts, "archived-campaign", new { campaign_id = seed.OldList, contacts = new[] { new { contact_id = seed.Grace } }, actor = ada }, HttpStatusCode.Conflict, Ct)).Code);
    }

    /// <summary>Rows written the way the claim and a role write them, with the ids fixed and a preview a person can read.</summary>
    private static class Seed
    {
        internal sealed record PeopleIds(string LatAm, string ReEngage, string OldList, string Mateo, string Aguilar, string Grace, string Paula, string Marcos);

        /// <summary>
        /// Five people and three campaigns at fixed ids and moments. Mateo is on the LatAm pilot and was excluded from the
        /// paused re-engagement, has history under every chip and planned work, and is known to the provider in the routed
        /// account; Aguilar has no first name and a failed enrolment; Paula is known to the provider in an account no route
        /// binds; Grace was imported and nothing has happened to her; Marcos is archived.
        /// </summary>
        public static async Task<PeopleIds> PeopleAsync(RuntimeApiFixture api)
        {
            static string Id(string prefix, int n) => $"{prefix}_01K7QN{n:D20}";
            var day = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
            var routed = new JsonObject { ["workspace"] = "a" };
            var unrouted = new JsonObject { ["workspace"] = "b" };

            var plugins = api.Resolve<PluginRegistry>();
            var schema = JsonNode.Parse("""{ "type": "object", "properties": { "workspace": { "type": "string" } } }""")!.AsObject();
            plugins.Replace(
                new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Nine.UtcDateTime, SnapshotSource.Startup, [TestPlugins.Loaded("reply", ["conversation.list", "conversation.get"], binding: schema)]),
                new ReloadReport(Nine.UtcDateTime, SnapshotSource.Startup, Activated: true, [], []));
            api.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
                PublicId.New(RouteSnapshot.IdPrefix),
                Nine,
                plugins.Snapshot.Id,
                new RouteSet(new Route("reply", routed, BindingIdentity.Of(routed)), RouteSet.Empty.Operations),
                new Dictionary<string, RouteSet>(StringComparer.Ordinal)));

            using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();

            Campaign Campaign(int n, string name, CampaignStatus status)
            {
                var campaign = WorkItemFactory.NewCampaign(name, status, day.AddDays(-30));
                campaign.PublicId = Id("cmp", n);
                campaign.ArchivedAt = status == CampaignStatus.Archived ? day.AddDays(-20) : null;
                db.Campaigns.Add(campaign);
                return campaign;
            }

            var latAm = Campaign(101, "LatAm pilot", CampaignStatus.Active);
            var reEngage = Campaign(102, "Re-engage Q2 no-replies", CampaignStatus.Paused);
            var oldList = Campaign(103, "Old list", CampaignStatus.Archived);

            Contact Person(int n, string? first, string last, string company, string title, string zone, params (string Channel, string Value)[] channels)
            {
                var person = new Contact
                {
                    PublicId = Id("cnt", n),
                    FirstName = first,
                    LastName = last,
                    Company = company,
                    Title = title,
                    TimeZone = zone,
                    CreatedAt = day.AddDays(-15),
                    UpdatedAt = day.AddDays(-15),
                };
                foreach (var (channel, value) in channels)
                {
                    person.Channels.Add(new ContactChannel { Channel = channel, Value = value, IsPrimary = channel == "email" });
                }

                db.Contacts.Add(person);
                return person;
            }

            var mateo = Person(101, "Mateo", "Ruiz", "Soluna", "Head of Sales", "America/Mexico_City",
                ("email", "mateo@soluna.example"), ("linkedin", "https://www.linkedin.com/in/mateo-ruiz-example"), ("phone", "+525512345678"));
            mateo.Custom = new JsonObject { ["segment"] = "fintech-mx", ["source"] = "q2-list", ["series"] = "A, raised 2026-06" };
            var aguilar = Person(102, null, "Aguilar", "Andina Tech", "Director of Sales", "America/Lima", ("email", "aguilar@andina.example"));
            var grace = Person(103, "Grace", "Hopper", "Navy", "Rear Admiral", "America/New_York", ("email", "grace@navy.example"));
            var paula = Person(104, "Paula", "Mendes", "Vela", "Head of Ops", "America/Sao_Paulo", ("email", "paula@vela.example"));
            var marcos = Person(105, "Marcos", "Vidal", "Puerto Digital", "CEO", "America/Santiago", ("email", "marcos@puerto.example"));
            marcos.ArchivedAt = day.AddDays(-2);

            db.CampaignContacts.AddRange(
                new CampaignContact { Campaign = latAm, Contact = mateo, AddedAt = day.AddDays(-13), UpdatedAt = day.AddDays(-13) },
                new CampaignContact { Campaign = reEngage, Contact = mateo, State = MembershipState.Excluded, AddedAt = day.AddDays(-26), UpdatedAt = day.AddDays(-15).AddHours(9.5) },
                new CampaignContact { Campaign = latAm, Contact = aguilar, AddedAt = day.AddDays(-1).AddHours(16), UpdatedAt = day.AddDays(-1).AddHours(16) });

            WorkItem Item(int n, Campaign campaign, Contact contact, WorkItemStatus status, string? role, string? operation = null)
            {
                var item = operation is null ? WorkItemFactory.NewAiRole(campaign, role!, day.AddDays(-1)) : WorkItemFactory.NewProviderOp(campaign, operation, day.AddDays(-1));
                item.PublicId = Id("wi", n);
                item.Contact = contact;
                item.Status = status;
                db.WorkItems.Add(item);
                return item;
            }

            Attempt Ran(WorkItem item, int n, AttemptStatus status, JsonObject? binding)
            {
                var attempt = WorkItemFactory.NewAttempt(item, 1, status, day.AddDays(-1));
                attempt.PublicId = Id("att", n);
                if (item.Kind == WorkItemKind.ProviderOp)
                {
                    attempt.Provenance = new AttemptProvenanceDto(
                        "reply", "0.5.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion, item.Operation, 1,
                        PluginProtocol.SnapshotIdPrefix + "_01K7QN00000000000000000001", "rts_01K7QN00000000000000000001", RouteScope.GlobalDefault,
                        BindingIdentity.Of(binding), CorrelationId: attempt.PublicId);
                }

                db.Attempts.Add(attempt);
                return attempt;
            }

            var mateoResearch = Item(101, latAm, mateo, WorkItemStatus.Succeeded, "researcher");
            var mateoResearched = Ran(mateoResearch, 101, AttemptStatus.Succeeded, null);
            var mateoWrite = Item(102, latAm, mateo, WorkItemStatus.Succeeded, "personalizer");
            var mateoEnrol = Item(103, latAm, mateo, WorkItemStatus.Succeeded, null, "campaign.enroll");
            var mateoEnrolled = Ran(mateoEnrol, 103, AttemptStatus.Succeeded, routed);
            Item(104, latAm, mateo, WorkItemStatus.Created, "responder").NotBefore = day.AddHours(11.5);
            var aguilarResearch = Item(105, latAm, aguilar, WorkItemStatus.Succeeded, "researcher");
            var aguilarResearched = Ran(aguilarResearch, 105, AttemptStatus.Succeeded, null);
            var aguilarEnrol = Item(106, latAm, aguilar, WorkItemStatus.Failed, null, "campaign.enroll");
            aguilarEnrol.LastError = new AttemptErrorDto("provider_call_failed", "The route forbids deriving a first name.", false, Class: FailureClass.Permanent);
            var aguilarFailed = Ran(aguilarEnrol, 106, AttemptStatus.Failed, routed);
            var paulaEnrol = Item(107, reEngage, paula, WorkItemStatus.Succeeded, null, "campaign.enroll");
            var paulaResearch = Item(108, reEngage, paula, WorkItemStatus.Processing, "researcher");
            var paulaEnrolled = Ran(paulaEnrol, 107, AttemptStatus.Succeeded, unrouted);
            db.ExternalIds.AddRange(
                new ExternalId { Contact = mateo, PluginId = "reply", Kind = "contact", Value = "16786993", RecordedAt = day.AddDays(-13), RecordedByAttemptId = mateoEnrolled.PublicId },
                new ExternalId { Contact = paula, PluginId = "reply", Kind = "contact", Value = "20441", RecordedAt = day.AddDays(-10), RecordedByAttemptId = paulaEnrolled.PublicId });
            await db.SaveChangesAsync(Ct);

            // The journal in the order it happened, at fixed ids: the order a person's latest line is found by is the ids',
            // and a cursor carries one, so an id made from the machine's clock would change the recording on every run.
            var line = 100;
            async Task Line(DateTime at, ActorRef actor, string kind, string? key = null, JsonNode? updated = null, string? reason = null, WorkItem? item = null, Contact? about = null, Campaign? campaign = null)
            {
                db.Journal.Add(new JournalEntry
                {
                    PublicId = Id("jrn", ++line),
                    Ts = at,
                    ActorType = actor.Type,
                    ActorId = actor.Id,
                    Kind = kind,
                    CampaignId = campaign?.Id ?? item?.CampaignId,
                    WorkItemId = item?.PublicId,
                    ContactId = about?.Id ?? item?.ContactId,
                    Key = key,
                    New = updated,
                    Reason = reason,
                });
                await db.SaveChangesAsync(Ct);
            }

            await Line(day.AddDays(-15).AddHours(9.4), Actors.ForAttempt(mateoResearched), JournalKinds.ContactCreated, mateo.PublicId, reason: "imported from the Q2 list", about: mateo);
            await Line(day.AddDays(-10), Actors.ForAttempt(paulaEnrolled), JournalKinds.ContactCreated, paula.PublicId, about: paula);
            await Line(day.AddDays(-10).AddHours(1), Actors.Dispatcher, JournalKinds.WorkItemProcessing, "attempt", JsonValue.Create(1), item: paulaResearch);
            await Line(day.AddDays(-1).AddHours(16), Actors.ForAttempt(aguilarResearched), JournalKinds.ContactCreated, aguilar.PublicId, about: aguilar);
            await Line(day.AddDays(-1).AddHours(16).AddMinutes(40), Actors.ForAttempt(mateoResearched), JournalKinds.ContactUpdated, mateo.PublicId, new JsonArray("title", "company"), about: mateo);
            await Line(day.AddDays(-1).AddHours(17).AddMinutes(20), Actors.Runtime, JournalKinds.WorkItemSucceeded, "result_present", JsonValue.Create(true), item: mateoWrite);
            await Line(day.AddHours(7).AddMinutes(58), Actors.Runtime, JournalKinds.WorkItemFailed, aguilarFailed.PublicId, JsonNode.Parse("""{"code":"provider_call_failed","message":"The route forbids deriving a first name.","retriable":false,"class":"permanent"}"""), item: aguilarEnrol);
            await Line(day.AddHours(8).AddMinutes(12), new ActorRef(ActorType.Human, "ada"), JournalKinds.ApprovalApproved, "approval", new JsonObject { ["id"] = Id("apr", 101), ["operation"] = "campaign.enroll" }, item: mateoEnrol);
            await Line(day.AddHours(8).AddMinutes(15), Actors.Runtime, JournalKinds.WorkItemSucceeded, "result_present", JsonValue.Create(true), item: mateoEnrol);
            await Line(day.AddHours(8).AddMinutes(41), Actors.Dispatcher, JournalKinds.InboundReplyObserved, "reply-t_501",
                new JsonObject { ["plugin"] = "reply", ["thread"] = "t_501", ["channel"] = "email", ["last_activity_at"] = "2026-10-16T08:41:00.000Z", ["unread"] = true, ["has_meeting_intent"] = true, ["contact_id"] = mateo.PublicId },
                about: mateo,
                campaign: latAm);

            return new PeopleIds(latAm.PublicId, reEngage.PublicId, oldList.PublicId, mateo.PublicId, aguilar.PublicId, grace.PublicId, paula.PublicId, marcos.PublicId);
        }

        /// <summary>Two of Mateo's threads as the provider lists them: the email he answered, and a LinkedIn one.</summary>
        public static JsonObject Threads(bool more) => JsonNode.Parse(
            $$"""
            {
              "items": [
                {
                  "external_id": "t_501", "channel": "email", "subject": "Re: Soluna's next quarter", "preview": "Thanks, could we talk on Thursday?",
                  "last_activity_at": "2026-10-16T08:41:00.000Z", "unread": true, "contact": { "external_id": "16786993", "name": "Mateo Ruiz" },
                  "campaign_external_id": "c_7714", "has_meeting_intent": true,
                  "vendor": { "is_last_message_planned": false, "state": "needs_attention", "category": null }
                },
                {
                  "external_id": "t_502", "channel": "linkedin", "subject": null, "preview": "Happy to connect.",
                  "last_activity_at": "2026-10-03T18:02:44.000Z", "unread": false, "contact": { "external_id": "16786993", "name": "Mateo Ruiz" },
                  "campaign_external_id": "c_7714", "has_meeting_intent": null,
                  "vendor": { "is_last_message_planned": false, "state": "ok", "category": null }
                }
              ],
              "has_more": {{(more ? "true" : "false")}},
              "vendor": { "ordering": "last_activity_desc" }
            }
            """)!.AsObject();

        /// <summary>
        /// The email thread with its messages as the provider holds them: HTML with a quote and links, and everything the page
        /// must never run or load — a script, a style, an inline style and handler, an image, a frame, a form, a script link —
        /// then a message in plain text, which is what the page has to clean.
        /// </summary>
        public static JsonObject Thread(string owner, bool more) => JsonNode.Parse(
            $$"""
            {
              "thread": {
                "external_id": "t_501", "channel": "email", "subject": "Re: Soluna's next quarter",
                "contact": { "external_id": "{{owner}}", "name": "Mateo Ruiz" }, "campaign_external_id": "c_7714", "unread": true,
                "vendor": { "category": null, "has_ai_draft": false }
              },
              "messages": [
                {
                  "external_id": "<m1@mail.example.com>", "direction": "outbound", "sent_at": "2026-10-16T08:15:00.000Z",
                  "from": "sdr@vendor.example", "to": ["mateo@soluna.example"], "cc": null, "bcc": null, "subject": "Soluna's next quarter",
                  "body": "<p>Hi Mateo, congratulations on the Series A.</p><p style=\"color:red\" onclick=\"steal()\">Worth 20 minutes next week?</p><img src=\"https://t.example/p.gif\"><script>steal()</script><style>p { color: red }</style>",
                  "truncated": false, "vendor": { "state": "ok", "status_code": null, "ai_sent_type": null }
                },
                {
                  "external_id": "<m2@mail.example.com>", "direction": "inbound", "sent_at": "2026-10-16T08:41:00.000Z",
                  "from": "mateo@soluna.example", "to": ["sdr@vendor.example"], "cc": null, "bcc": null, "subject": "Re: Soluna's next quarter",
                  "body": "<p>Thanks, could we talk on Thursday? <a href=\"https://soluna.example/team\">Our team</a> or <a href=\"javascript:steal()\">this</a></p><blockquote>Worth 20 minutes next week?</blockquote><iframe src=\"https://frame.example\"></iframe><form><input name=\"q\"></form>",
                  "truncated": false, "vendor": { "state": "needs_attention", "status_code": null, "ai_sent_type": null }
                },
                {
                  "external_id": "<m3@mail.example.com>", "direction": "outbound", "sent_at": "2026-10-16T08:50:00.000Z",
                  "from": "sdr@vendor.example", "to": ["mateo@soluna.example"], "cc": null, "bcc": null, "subject": "Re: Soluna's next quarter",
                  "body": "Thursday works.\nTen in the morning, Mexico City time & no later?\n\nOn Thu, Mateo <mateo@soluna.example> wrote: <3",
                  "truncated": false, "vendor": { "state": "ok", "status_code": null, "ai_sent_type": null }
                }
              ],
              "has_more": {{(more ? "true" : "false")}},
              "vendor": { "ordering": "oldest_first" }
            }
            """)!.AsObject();

        internal sealed record WorkItemIds(string Autumn, string SmbTest, string Ada, string Tomas, string Open, string Awaiting, string Succeeded, string Failed, string Expired);

        /// <summary>
        /// Work in every status over two campaigns and three people, at fixed ids and moments: running and claimed work,
        /// one item waiting for an approval, five planned (two due at ten, one at two after a retry at a quarter past twelve,
        /// one tomorrow that may not start before half past four, one undated), and one each succeeded, failed after two
        /// attempts, cancelled and expired.
        /// </summary>
        public static async Task<WorkItemIds> WorkItemsAsync(RuntimeApiFixture api)
        {
            static string Id(string prefix, int n) => $"{prefix}_01K7QN{n:D20}";
            var day = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
            DateTime At(int hour, int minute = 0) => day.AddHours(hour).AddMinutes(minute);

            using (var scope = api.Resolve<IServiceScopeFactory>().CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
                var autumn = WorkItemFactory.NewCampaign("Autumn outreach", now: At(7));
                autumn.PublicId = Id("cmp", 1);
                var smb = WorkItemFactory.NewCampaign("SMB test", now: At(7));
                smb.PublicId = Id("cmp", 2);
                db.Campaigns.AddRange(autumn, smb);

                Contact Person(int n, string first, string last, string company, string title, params (string Channel, string Value)[] channels)
                {
                    var person = new Contact { PublicId = Id("cnt", n), FirstName = first, LastName = last, Company = company, Title = title, CreatedAt = At(7), UpdatedAt = At(7) };
                    foreach (var (channel, value) in channels)
                    {
                        person.Channels.Add(new ContactChannel { Channel = channel, Value = value, IsPrimary = true });
                    }

                    db.Contacts.Add(person);
                    return person;
                }

                var ada = Person(1, "Ada", "Lovelace", "Kavali", "CTO", ("email", "ada@kavali.example"), ("phone", "+48601234567"));
                var tomas = Person(3, "Tomás", "Aguilar", "Andina Tech", "Director of Sales", ("email", "t.aguilar@andina.example"));

                WorkItem Role(int n, Campaign campaign, string role, Contact? contact, WorkItemStatus status, Action<WorkItem>? more = null)
                {
                    var item = WorkItemFactory.NewAiRole(campaign, role, At(8), w =>
                    {
                        w.PublicId = Id("wi", n);
                        w.Contact = contact;
                        w.Status = status;
                        more?.Invoke(w);
                    });
                    db.WorkItems.Add(item);
                    return item;
                }

                WorkItem Enroll(int n, Contact contact, WorkItemStatus status)
                {
                    var item = WorkItemFactory.NewProviderOp(autumn, "campaign.enroll", At(8));
                    item.PublicId = Id("wi", n);
                    item.Contact = contact;
                    item.Status = status;
                    db.WorkItems.Add(item);
                    return item;
                }

                Attempt Ran(WorkItem item, int n, int number, AttemptStatus status, DateTime started, DateTime? finished, AttemptErrorDto? error = null)
                {
                    var attempt = WorkItemFactory.NewAttempt(item, number, status, started);
                    attempt.PublicId = Id("att", n);
                    attempt.StartedAt = status == AttemptStatus.Scheduled ? null : started;
                    attempt.FinishedAt = finished;
                    attempt.Error = error;
                    db.Attempts.Add(attempt);
                    return attempt;
                }

                var running = Role(1, autumn, "researcher", ada, WorkItemStatus.Processing);
                Ran(running, 1, 1, AttemptStatus.Running, At(8, 55), null);
                var claimed = Enroll(2, ada, WorkItemStatus.Scheduled);
                Ran(claimed, 2, 1, AttemptStatus.Scheduled, At(8, 59), null);

                Role(4, autumn, "copywriter", ada, WorkItemStatus.Created, w => w.DueAt = At(10));
                Role(5, smb, "personalizer", null, WorkItemStatus.Created, w => w.DueAt = At(10));
                Role(6, autumn, "manager", null, WorkItemStatus.Created, w => { w.DueAt = At(14); w.RetryAfter = At(12, 15); w.AttemptCount = 1; });
                Role(7, smb, "researcher", tomas, WorkItemStatus.Created, w => { w.DueAt = At(33); w.NotBefore = At(16, 30); });
                Role(8, autumn, "analyst", null, WorkItemStatus.Created);

                var succeeded = Enroll(9, ada, WorkItemStatus.Succeeded);
                succeeded.FinishedAt = At(8, 41);
                Ran(succeeded, 9, 1, AttemptStatus.Succeeded, At(8, 40), At(8, 41));

                var refused = new AttemptErrorDto("reply.refused", "Invalid data: first name is required", false, Class: FailureClass.Permanent);
                var failed = Enroll(10, tomas, WorkItemStatus.Failed);
                failed.FinishedAt = At(7, 58);
                failed.MaxAttempts = 3;
                failed.AttemptCount = 2;
                failed.LastError = refused;
                var provenance = new AttemptProvenanceDto("reply", "0.3.1", null, 1, 1, "campaign.enroll", 1, null, null, RouteScope.GlobalDefault, null);
                Ran(failed, 10, 1, AttemptStatus.Failed, At(7, 50), At(7, 51), new AttemptErrorDto("provider_unavailable", "Reply did not answer in time", true, Class: FailureClass.Transient)).Provenance = provenance;
                Ran(failed, 11, 2, AttemptStatus.Failed, At(7, 57), At(7, 58), refused).Provenance = provenance;

                Role(11, smb, "researcher", null, WorkItemStatus.Cancelled, w => w.FinishedAt = At(8, 10));
                Role(12, autumn, "copywriter", null, WorkItemStatus.Expired, w => { w.DueAt = At(6); w.FinishedAt = At(6); });

                await db.SaveChangesAsync(Ct);
            }

            // The waiting item comes with its approval, through the same seed the approval recordings use.
            const string Approval = "apr_01K7QN0000000000000000000W";
            await ApprovalAsync(api, Approval, "Grace", "Hopper", decided: false);

            return new WorkItemIds(Id("cmp", 1), Id("cmp", 2), Id("cnt", 1), Id("cnt", 3), Id("wi", 4), "wi_" + Approval[4..], Id("wi", 9), Id("wi", 10), Id("wi", 12));
        }

        public static async Task ApprovalAsync(RuntimeApiFixture api, string id, string first, string last, bool decided, string? reason = null, ApprovalStatus? status = null)
        {
            var final = status ?? (decided ? ApprovalStatus.Approved : ApprovalStatus.Pending);
            var byPerson = final is ApprovalStatus.Approved or ApprovalStatus.Rejected;
            using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var campaign = await CampaignAsync(db);

            var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
            item.PublicId = "wi_" + id[4..];
            item.Status = decided ? WorkItemStatus.Scheduled : WorkItemStatus.AwaitingApproval;
            item.Contact = new Contact { PublicId = "cnt_" + id[4..], FirstName = first, LastName = last, Company = "Kavali", Title = "CTO", CreatedAt = Noon, UpdatedAt = Noon };
            db.WorkItems.Add(item);
            db.Approvals.Add(new Approval
            {
                PublicId = id,
                WorkItem = item,
                CampaignId = campaign.Id,
                Operation = "campaign.enroll",
                OperationVersion = 1,
                Subject = new JsonObject { ["operation"] = "campaign.enroll", ["contact_id"] = "cnt_" + id[4..], ["campaign_id"] = campaign.PublicId },
                SubjectHash = "sha256:" + new string('a', 64),
                Preview = new JsonObject
                {
                    ["intent"] = $"Put {first} {last} into the campaign at the provider.",
                    ["reach"] = new JsonObject { ["value"] = "act", ["conditional"] = true, ["detail"] = "bookkeeping until the campaign is live" },
                    ["reversibility"] = new JsonObject { ["value"] = "irreversible", ["conditional"] = true, ["detail"] = null },
                    ["cost"] = new JsonObject { ["value"] = "metered", ["conditional"] = true, ["detail"] = null },
                    ["campaign"] = new JsonObject { ["id"] = campaign.PublicId, ["name"] = campaign.Name },
                    ["contact"] = new JsonObject { ["id"] = "cnt_" + id[4..], ["name"] = $"{first} {last}", ["channel"] = "email", ["value"] = $"{first.ToLowerInvariant()}@example.test" },
                },
                PluginId = "reply",
                BindingIdentity = "sha256:" + new string('b', 64),
                RouteScope = RouteScope.GlobalDefault,
                PluginSnapshotId = "snp_01K7QN00000000000000000001",
                RoutingSnapshotId = "rts_01K7QN00000000000000000001",
                Reason = reason ?? ApprovalGate.ApprovalRequired,
                RequestedAt = Noon,
                Status = final,
                DecidedAt = decided ? Noon.AddMinutes(5) : null,
                DecidedByType = !decided ? null : byPerson ? ActorType.Human : ActorType.System,
                DecidedById = !decided ? null : byPerson ? "ada" : "dispatcher",
                DecisionReason = decided && byPerson ? "checked the list" : null,
            });
            await db.SaveChangesAsync(Ct);
        }

        public static async Task DecisionAsync(RuntimeApiFixture api, string id, string question, bool options)
        {
            using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var campaign = await CampaignAsync(db);

            var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
            item.PublicId = "wi_" + id[4..];
            item.Status = WorkItemStatus.Processing;
            var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
            attempt.PublicId = "att_" + id[4..];
            db.WorkItems.Add(item);
            db.Attempts.Add(attempt);
            db.Decisions.Add(new Decision
            {
                PublicId = id,
                Campaign = campaign,
                WorkItem = item,
                Attempt = attempt,
                Question = question,
                Options = options
                    ? new JsonArray(
                        new JsonObject { ["label"] = "Pause until the news", ["detail"] = "Their CFO is leaving; the funding round is in the press next week." },
                        new JsonObject { ["label"] = "Keep going", ["detail"] = null })
                    : null,
                Status = DecisionStatus.Pending,
                RaisedAt = Noon,
            });
            await db.SaveChangesAsync(Ct);
        }

        private static async Task<Campaign> CampaignAsync(JasonDbContext db)
        {
            var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Name == "Autumn outreach", Ct);
            if (campaign is null)
            {
                campaign = WorkItemFactory.NewCampaign("Autumn outreach", now: Noon);
                campaign.PublicId = "cmp_01K7QN00000000000000000001";
                db.Campaigns.Add(campaign);
                await db.SaveChangesAsync(Ct);
            }

            return campaign;
        }
    }
}
