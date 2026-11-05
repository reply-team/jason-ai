using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A person's word given from inside a run. The CLI says where it runs with one header, <c>Jason-Attempt</c>, whenever its
/// environment names the attempt it was launched for; the five verbs that are a person's own word — the two decisions on an
/// approval, a standing approval set or taken back, and an answer — refuse any request that carries it, whatever actor
/// the body claims, and record nothing. Every other verb reads past the header: the fenced ones keep their attempt in the
/// body, and the header is never their fence.
/// </summary>
public class InsideARunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The attempt the header names: none this runtime knows, since the runtime does not look it up.</summary>
    private const string HeaderAttempt = "att_01K7QN0000000000000000000R";

    private static readonly Dictionary<string, string> Inside = new() { ["Jason-Attempt"] = HeaderAttempt };

    /// <summary>The fixtures' own person, named as a person: the claim the header has to outweigh.</summary>
    private static readonly object Ada = new { type = "human", id = "ada" };

    public static TheoryData<string> PersonsVerbs =>
    [
        Operations.ApprovalApprove,
        Operations.ApprovalReject,
        Operations.ApprovalSetStanding,
        Operations.ApprovalUnsetStanding,
        Operations.DecisionAnswer,
    ];

    /// <summary>
    /// The request is the one a person would send, naming the person; the header alone refuses it, before its actor is
    /// read, and nothing about the approval, the decision or the chronicle moves. Sent again without the header, it is
    /// answered as it always was, so the refusal is the header's and nothing else's.
    /// </summary>
    [Theory]
    [MemberData(nameof(PersonsVerbs))]
    public async Task A_persons_verb_sent_from_inside_a_run_is_refused_whatever_actor_it_names_and_changes_nothing(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var body = await ArrangeAsync(api, operation);
        var before = await StateAsync(api);

        var (status, text) = await api.PostAsync(operation, body, Inside, Ct);

        Assert.True(status == HttpStatusCode.Forbidden, $"{operation} answered {(int)status}: {text}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        Assert.Equal("decided_inside_a_run", error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(
            $"This request comes from inside attempt `{HeaderAttempt}`. An approval or an answer is a person's own word, given outside the run that asked: leave it to the person (`jason approval list`, `jason decision list`) or raise it as a decision.",
            error.Message);
        Assert.Equal(before, await StateAsync(api));

        var (plain, plainText) = await api.PostAsync(operation, body, Ct);

        Assert.True(plain == HttpStatusCode.OK, $"{operation} without the header answered {(int)plain}: {plainText}");
        Assert.NotEqual(before, await StateAsync(api));
    }

    /// <summary>
    /// Any value refuses, and it is not checked against anything: presence is the fact. A body that names no person
    /// at all, or no approval the runtime has, is refused by the header first, so the caller learns the one thing that
    /// no repair of the body would change.
    /// </summary>
    [Theory]
    [InlineData("att_nothing-like-an-id")]
    [InlineData("x")]
    public async Task Any_value_refuses_before_the_body_is_read(string value)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        var (status, text) = await api.PostAsync(
            Operations.ApprovalApprove, new { approval_id = "apr_01K7QN0000000000000000000Z" }, new Dictionary<string, string> { ["Jason-Attempt"] = value }, Ct);

        Assert.True(status == HttpStatusCode.Forbidden, text);
        Assert.Equal("decided_inside_a_run", JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error.Code);
    }

    /// <summary>
    /// The refusal stands before the body is read, so a body the runtime could not read at all is refused by the header
    /// and not answered as malformed: a body that reads checks no actor, so only one that cannot read tells where the
    /// check stands.
    /// </summary>
    [Theory]
    [MemberData(nameof(PersonsVerbs))]
    public async Task A_body_that_cannot_be_read_is_refused_by_the_header_first(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        var (status, text) = await api.PostRawAsync(operation, "{not json", Inside, Ct);

        Assert.True(status == HttpStatusCode.Forbidden, $"{operation} answered {(int)status}: {text}");
        Assert.Equal("decided_inside_a_run", JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error.Code);
    }

    /// <summary>
    /// The five and no more. The other verbs that take no default actor — a preference set or unset, a block's clear, a
    /// resume, a report and a subscription's rewind — are not among them: a resume and a report are a run's to make, and
    /// the rest are a person's already by their actor. A sixth is a decision of its own, made here on purpose.
    /// </summary>
    [Fact]
    public void Exactly_the_five_verbs_that_are_a_persons_own_word_refuse()
    {
        Assert.Equal(
            PersonsVerbs.Select(row => row.Data).Order(StringComparer.Ordinal),
            Jason.Runtime.Api.InsideARun.Refusing.Order(StringComparer.Ordinal));
    }

    public static TheoryData<string> OtherVerbs =>
    [
        Operations.WorkItemWait,
        Operations.ReportSubmit,
        Operations.CampaignGet,
        Operations.WorkItemResume,
    ];

    /// <summary>
    /// Every other verb answers a request with the header exactly as one without it. Two runtimes are given the same
    /// world and the same request, one with the header: the answers' statuses agree, and so do the worlds after. The
    /// header names an attempt other than the one a wait carries in its body, and the wait is still fenced by its body.
    /// </summary>
    [Theory]
    [MemberData(nameof(OtherVerbs))]
    public async Task Every_other_verb_answers_a_request_from_inside_a_run_as_it_answers_one_from_outside(string operation)
    {
        var inside = await AnswerAsync(operation, Inside);
        var outside = await AnswerAsync(operation, headers: null);

        Assert.True(outside.Status == HttpStatusCode.OK, $"{operation} from outside answered {(int)outside.Status}: {outside.Body}");
        Assert.True(inside.Status == HttpStatusCode.OK, $"{operation} from inside answered {(int)inside.Status}: {inside.Body}");
        Assert.Equal(outside.State, inside.State);
    }

    private static async Task<(HttpStatusCode Status, string Body, string State)> AnswerAsync(string operation, IReadOnlyDictionary<string, string>? headers)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var body = await ArrangeAsync(api, operation);
        var (status, text) = await api.PostAsync(operation, body, headers, Ct);
        return (status, text, await StateAsync(api));
    }

    /// <summary>The world each verb needs, made without the header, and the body a caller would send.</summary>
    private static async Task<object> ArrangeAsync(RuntimeApiFixture api, string operation)
    {
        switch (operation)
        {
            case Operations.ApprovalApprove:
            case Operations.ApprovalReject:
                return new { approval_id = await ApprovalAsync(api), actor = Ada, reason = "checked the list" };

            case Operations.ApprovalSetStanding:
                return new { campaign_id = await CampaignAsync(api), category = "interested", actor = Ada };

            case Operations.ApprovalUnsetStanding:
                {
                    var campaign = await CampaignAsync(api);
                    await api.PostOkAsync<StandingApprovalDto>(Operations.ApprovalSetStanding, new { campaign_id = campaign, category = "interested", actor = Ada }, Ct);
                    return new { campaign_id = campaign, category = "interested", actor = Ada };
                }

            case Operations.DecisionAnswer:
                return new { decision_id = (await RunningAsync(api, raiseDecision: true)).Decision, answer = "Keep going", actor = Ada };

            case Operations.WorkItemWait:
                {
                    var running = await RunningAsync(api, raiseDecision: false);
                    return new { work_item_id = running.Item, attempt_id = running.Attempt, until = Nine.AddDays(1) };
                }

            case Operations.WorkItemResume:
                {
                    var running = await RunningAsync(api, raiseDecision: false);
                    await api.PostOkAsync<WorkItemDto>(Operations.WorkItemWait, new { work_item_id = running.Item, attempt_id = running.Attempt, until = Nine.AddDays(1) }, Ct);
                    return new { work_item_id = running.Item, actor = Ada, reason = "the news came early" };
                }

            case Operations.ReportSubmit:
                return new { effect = "email_sent", tool = "some-other-cli", summary = "Sent by hand, outside the runtime.", actor = Ada, idempotency_key = "report-1" };

            case Operations.CampaignGet:
                return new { campaign_id = await CampaignAsync(api) };

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "No world is arranged for this verb.");
        }
    }

    /// <summary>What a refusal must leave as it was: every line of the chronicle, and where each approval, question and item stands.</summary>
    private static async Task<string> StateAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var lines = await db.Journal.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct);
        var approvals = await db.Approvals.AsNoTracking().OrderBy(a => a.Id).Select(a => a.Status).ToListAsync(Ct);
        var decisions = await db.Decisions.AsNoTracking().OrderBy(d => d.Id).Select(d => d.Status).ToListAsync(Ct);
        var items = await db.WorkItems.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Status).ToListAsync(Ct);
        var reports = await db.Reports.AsNoTracking().CountAsync(Ct);
        return string.Join(
            " | ",
            string.Join(",", lines),
            string.Join(",", approvals),
            string.Join(",", decisions),
            string.Join(",", items),
            reports.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct)).Id;

    /// <summary>A provider operation parked for a person's approval, as the claim leaves one.</summary>
    private static async Task<string> ApprovalAsync(RuntimeApiFixture api)
    {
        const string id = "apr_01K7QN0000000000000000000A";
        var now = Nine.UtcDateTime;
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach", now: now);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", now);
        item.Status = WorkItemStatus.AwaitingApproval;
        item.Contact = new Contact { PublicId = "cnt_" + id[4..], FirstName = "Ada", LastName = "Lovelace", CreatedAt = now, UpdatedAt = now };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        db.Approvals.Add(new Approval
        {
            PublicId = id,
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = "campaign.enroll",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "campaign.enroll", ["contact_id"] = "cnt_" + id[4..], ["campaign_id"] = campaign.PublicId },
            SubjectHash = "sha256:" + new string('a', 64),
            Preview = new JsonObject { ["intent"] = "Put Ada Lovelace into the campaign at the provider." },
            PluginId = "reply",
            BindingIdentity = "sha256:" + new string('b', 64),
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_01K7QN00000000000000000001",
            RoutingSnapshotId = "rts_01K7QN00000000000000000001",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = now,
            Status = ApprovalStatus.Pending,
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    /// <summary>A role's work under its running attempt, and, when asked, the question that run raised for a person.</summary>
    private static async Task<(string Item, string Attempt, string? Decision)> RunningAsync(RuntimeApiFixture api, bool raiseDecision)
    {
        var now = Nine.UtcDateTime;
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach", now: now);
        var item = WorkItemFactory.NewAiRole(campaign, "planner", now: now);
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        string? decision = null;
        if (raiseDecision)
        {
            decision = "dec_01K7QN0000000000000000000A";
            db.Decisions.Add(new Decision
            {
                PublicId = decision,
                Campaign = campaign,
                WorkItem = item,
                Attempt = attempt,
                Question = "Which subject line for the SMB segment?",
                Status = DecisionStatus.Pending,
                RaisedAt = now,
            });
        }

        await db.SaveChangesAsync(Ct);
        return (item.PublicId, attempt.PublicId, decision);
    }
}
