using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
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
    }

    /// <summary>Rows written the way the claim and a role write them, with the ids fixed and a preview a person can read.</summary>
    private static class Seed
    {
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
