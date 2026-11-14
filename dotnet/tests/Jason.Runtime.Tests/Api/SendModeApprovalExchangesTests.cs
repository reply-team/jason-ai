using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Approvals;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A message the send mode held for the person, once under each reason the claim parks with, and one it let go, as
/// <c>approval.get</c> answers them: the exchanges a consumer reads to say why a send waits or why it went. Each row is
/// written the way the claim writes it: the preview the claim assembles from the operation's contract and the input, the
/// facts the automatic step decided on under <c>send_mode</c>, the doubts that stood under <c>doubts</c>, and the review
/// lines a running critic wrote through the verb. The claim's own choice of reason is proven where the claim is.
/// </summary>
public class SendModeApprovalExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Noon = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OperationContract Send = OperationCatalog.Find("message.send")!;
    private static readonly ActorRef Marta = new(ActorType.Human, "Marta");

    private static readonly ModeInForce Automatic = new(SendMode.Automatic, SendModeSource.Preference, Marta);

    /// <summary>Each reason the claim parks a send with, and the temperature its facts say: none where a reply waits.</summary>
    public static TheoryData<string, string?> Parks => new()
    {
        { SendModeReasons.SentFromChat, "cold" },
        { SendModeReasons.Doubted, "cold" },
        { SendModeReasons.AwaitingJudgement, null },
        { SendModeReasons.AwaitingNature, null },
        { SendModeReasons.AnswersNoVerdict, "warm" },
        { SendModeReasons.ReplyNotFromAPerson, "warm" },
        { SendModeReasons.Escalated, "warm" },
        { SendModeReasons.EditedAfterReview, "cold" },
        { SendModeReasons.NotReviewed, "cold" },
        { SendModeReasons.DailyCeiling, "cold" },
    };

    [Theory]
    [MemberData(nameof(Parks))]
    public async Task A_send_the_mode_held_says_why_on_its_approval(string reason, string? temperature)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var (approval, item) = await SeedAsync(api, reason, temperature);

        var read = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "park-" + reason.Replace('_', '-'), new { approval_id = approval }, Ct);
        Assert.Equal((ApprovalStatus.Pending, reason), (read.Status, read.Reason));
        var facts = read.Preview[AutomaticRelease.PreviewKey]!.AsObject();
        Assert.Equal(temperature, (string?)facts["temperature"]);
        Assert.Equal(temperature is null ? null : "automatic", (string?)facts["mode"]);

        if (reason == SendModeReasons.Doubted)
        {
            // The doubt that held it is a review's: a running critic's, with the line it wrote and the sentence it gave.
            var doubt = Assert.Single(read.Preview[Doubts.PreviewKey]!.AsArray())!;
            Assert.Equal(("review", "critic", "unsupported_claim", "It promises a discount the brief never offers."),
                ((string?)doubt["source"], (string?)doubt["role"], (string?)doubt["code"], (string?)doubt["note"]));
            Assert.StartsWith("jrn_", (string?)doubt["line"]);
            Assert.Equal("attempt", (string?)doubt["by"]!["type"]);
        }

        if (reason == SendModeReasons.DailyCeiling)
        {
            // At the ceiling the critic's pass stood and the mode would have let it go: the preview says the limit, the
            // releases counted and since when, three numbers and no content.
            Assert.StartsWith("jrn_", (string?)facts["review"]);
            var ceiling = read.Preview[DailyCeiling.PreviewKey]!.AsObject();
            Assert.Equal((20, 20, "2026-10-15T00:00:00Z"), ((int)ceiling["limit"]!, (int)ceiling["released"]!, (string?)ceiling["since"]));
        }

        if (reason == SendModeReasons.EditedAfterReview)
        {
            // The claim shows the pass an edit voided: the critic's line, under the text it read.
            Assert.StartsWith("jrn_", (string?)facts["review"]);

            // The item as it waits holds the edited text the approval asks about.
            var edited = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "parked-edited-send", new { work_item_id = item }, Ct);
            Assert.Equal("Hi Ines, thanks for the talk at the meetup. Shall we talk next week?", (string?)edited.Context["input"]!["message"]!["body"]);
        }

        if (reason == SendModeReasons.NotReviewed)
        {
            // The card checks a message against its item before Approve is offered: the item as it waits, once.
            var waiting = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "parked-send", new { work_item_id = item }, Ct);
            Assert.Equal(WorkItemStatus.AwaitingApproval, waiting.Status);
        }
    }

    [Fact]
    public async Task A_send_the_mode_let_go_says_so_on_its_approval()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var (approval, _) = await SeedAsync(api, ApprovalGate.AutomaticMode, "cold");

        var read = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "released-automatically", new { approval_id = approval }, Ct);
        Assert.Equal((ApprovalStatus.Approved, ApprovalGate.AutomaticMode, "Marta"), (read.Status, read.Reason, read.DecidedBy!.Id));
        var facts = read.Preview[AutomaticRelease.PreviewKey]!.AsObject();
        Assert.Equal(("automatic", "preference", "cold"), ((string?)facts["mode"], (string?)facts["source"], (string?)facts["temperature"]));
        Assert.StartsWith("jrn_", (string?)facts["review"]);
    }

    private static async Task<(string Approval, string Item)> SeedAsync(RuntimeApiFixture api, string reason, string? temperature)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var journal = new JournalWriter(new FixedClock(Noon));
        var campaign = WorkItemFactory.NewCampaign("Spring launch", now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = "cnt_01K7QR00000000000000000001", FirstName = "Ines", LastName = "Duarte", Company = "Kavali", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ines@example.test", IsPrimary = true });
        var input = new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "s_12", ["address"] = "owner@example.test" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Notes from the meetup", ["body"] = "Hi Ines, thanks for the talk at the meetup." },
            },
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = contact.PublicId,
                ["first_name"] = "Ines",
                ["last_name"] = "Duarte",
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = "ines@example.test", ["primary"] = true }),
                ["external_ids"] = new JsonObject(),
            }),
            ["campaign"] = new JsonObject { ["id"] = campaign.PublicId, ["name"] = campaign.Name, ["status"] = "active", ["external_ids"] = new JsonObject { ["campaign"] = "c_7714" } },
        };
        var item = WorkItemFactory.NewProviderOp(campaign, Send.Id, Noon);
        item.Contact = contact;
        item.Context[WorkItemService.InputKey] = input["args"]!.DeepClone();
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        // A review of the send's text, by a running attempt of the critic's work, through the verb, while it is still queued.
        string? review = null;
        var passes = reason == ApprovalGate.AutomaticMode || reason == SendModeReasons.DailyCeiling || reason == SendModeReasons.EditedAfterReview;
        if (reason == SendModeReasons.Doubted || passes)
        {
            var work = WorkItemFactory.NewAiRole(campaign, role: AutomaticRelease.CriticRole, now: Noon);
            work.Status = WorkItemStatus.Processing;
            var run = WorkItemFactory.NewAttempt(work, 1, AttemptStatus.Running, Noon);
            db.WorkItems.Add(work);
            await db.SaveChangesAsync(Ct);
            var doubts = reason == SendModeReasons.Doubted;
            var line = await new ReviewService(db, journal, TheClaimReadsEveryReview.Instance).ReviewAsync(
                new WorkItemReviewRequest(
                    item.PublicId,
                    ReviewSubject.Hash(item),
                    doubts ? ReviewResult.Doubts : ReviewResult.Passed,
                    doubts ? "unsupported_claim" : null,
                    doubts ? "It promises a discount the brief never offers." : null,
                    new ActorRef(ActorType.Attempt, run.PublicId)),
                Ct);
            review = line.Id;
        }

        // Edited after the critic passed it: the pass stands under the text it read, and the claim shows that older line.
        if (reason == SendModeReasons.EditedAfterReview)
        {
            input["args"]!["message"]!["body"] = "Hi Ines, thanks for the talk at the meetup. Shall we talk next week?";
            item.Context[WorkItemService.InputKey] = input["args"]!.DeepClone();
            db.Entry(item).Property(w => w.Context).IsModified = true;
            await db.SaveChangesAsync(Ct);
        }

        var binding = new JsonObject { ["workspace"] = "a" };
        var subject = ApprovalSubject.Of(
            new ProviderOpPlan(TestPlugins.Loaded("reply", [Send.Id]), "snp_01K7QR00000000000000000001", "rts_01K7QR00000000000000000001",
                RouteScope.GlobalDefault, binding, BindingIdentity.Of(binding), Send, input),
            item);
        var preview = ApprovalPreview.Of(Send, subject, campaign, contact, "email");
        var released = reason == ApprovalGate.AutomaticMode;
        Temperature? warmth = temperature switch { "cold" => Temperature.Cold, "warm" => Temperature.Warm, _ => null };
        preview[AutomaticRelease.PreviewKey] = AutomaticRelease.Facts(warmth is null ? null : Automatic, warmth, passes ? review : null);
        if (reason == SendModeReasons.DailyCeiling)
        {
            preview[DailyCeiling.PreviewKey] = DailyCeiling.Preview(new DailyCeiling.Limit(20, Noon.Date), 20);
        }
        if (await Doubts.PreviewAsync(db, item, Ct) is { } standing)
        {
            preview[Doubts.PreviewKey] = standing;
        }

        Doubts.ClearAtPark(item);
        item.Status = released ? WorkItemStatus.Processing : WorkItemStatus.AwaitingApproval;
        var approval = new Approval
        {
            PublicId = "apr_01K7QR0000000000000000000A",
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = Send.Id,
            OperationVersion = Send.Version,
            Subject = subject.Document,
            SubjectHash = subject.Hash,
            Preview = preview,
            PluginId = "reply",
            BindingIdentity = BindingIdentity.Of(binding),
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_01K7QR00000000000000000001",
            RoutingSnapshotId = "rts_01K7QR00000000000000000001",
            Reason = reason,
            RequestedAt = Noon,
            Status = released ? ApprovalStatus.Approved : ApprovalStatus.Pending,
            DecidedAt = released ? Noon : null,
            DecidedByType = released ? Marta.Type : null,
            DecidedById = released ? Marta.Id : null,
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync(Ct);
        return (approval.PublicId, item.PublicId);
    }
}
