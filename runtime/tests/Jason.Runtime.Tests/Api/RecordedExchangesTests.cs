using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
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

    /// <summary>Rows written the way the claim and a role write them, with the ids fixed and a preview a person can read.</summary>
    private static class Seed
    {
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
