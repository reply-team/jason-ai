using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// What waits on a person across every campaign, as the desktop's Needs you reads it with <c>approval.list</c> and no
/// filter: a first message of one campaign beside a drafted answer to a reply of another, each read whole, the person's
/// own edit of the message's words, and a list longer than the runtime's default page, read a page at a time by its
/// cursor.
/// </summary>
public class NeedsExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Noon = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OperationContract Send = OperationCatalog.Find("message.send")!;
    private static readonly OperationContract Reply = OperationCatalog.Find("conversation.reply")!;

    // Fixed ULID-shaped ids: the listing orders by id, so the email sorts before the reply on every machine.
    private const string EmailApproval = "apr_01K7QR0000000000000000000A";
    private const string ReplyApproval = "apr_01K7QR0000000000000000000B";

    [Fact]
    public async Task What_waits_across_campaigns_holds_an_answer_to_a_reply_beside_a_first_message_and_pages_past_the_default()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var (spring, autumn) = await SeedAsync(api);

        var inbox = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "needs-inbox", new { }, Ct);
        Assert.Equal(
            [("message.send", "Spring launch", "Ines Duarte"), ("conversation.reply", "Autumn outreach", "Kai Lindt")],
            inbox.Items.Select(a => (a.Operation, a.CampaignName, a.ContactName)));
        Assert.Null(inbox.NextCursor);

        var reply = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "needs-reply", new { approval_id = ReplyApproval }, Ct);
        var seen = reply.Preview["highlights"]!.AsObject();
        Assert.Equal("Thanks Kai, Thursday at ten works for me.", (string?)seen["/args/body"]);
        Assert.Equal("kai@example.test", (string?)seen["/contacts/0/channels/0/value"]);
        var message = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "needs-message", new { approval_id = EmailApproval }, Ct);
        Assert.Equal("message.send", message.Operation);

        // The person's own edit of the first message's words: the work item read, then patched with the text replaced, and
        // the runtime supersedes the approval, so what waits is the answer to the reply alone until it asks again.
        var item = "wi_" + EmailApproval[4..];
        var parked = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "needs-message-parked", new { work_item_id = item }, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, parked.Status);
        var edited = EmailArgs("Ines", "Hi Ines, thanks for the talk at the meetup. Shall we talk next week?");
        var patched = await api.RecordAsync<WorkItemDto>(Operations.WorkItemUpdate, "needs-message-edited", new { work_item_id = item, set = new { input = edited }, reason = "Changed the message's words before deciding on it.", actor = new { type = "human", id = "ada" } }, Ct);
        Assert.True(JsonNode.DeepEquals(edited, patched.Context["input"]));
        var after = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "needs-after-edit", new { }, Ct);
        Assert.Equal([ReplyApproval], after.Items.Select(a => a.Id));

        // A question the autumn campaign's AI raised, with its options: what waits on a person beside the approvals.
        var questions = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "needs-questions", new { }, Ct);
        var question = Assert.Single(questions.Items);
        Assert.Equal(("Autumn outreach", autumn.PublicId), (question.CampaignName, question.CampaignId));
        var asked = await api.RecordAsync<DecisionDto>(Operations.DecisionGet, "needs-question", new { decision_id = question.Id }, Ct);
        Assert.Equal(2, asked.Options!.Count);

        // A hundred and one more first messages: two more than the default page holds, so the list has a second page.
        using (var scope = api.Resolve<IServiceScopeFactory>().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            db.Attach(spring);
            for (var n = 1; n <= 101; n++)
            {
                var number = n.ToString("D3", CultureInfo.InvariantCulture);
                AddSend(db, spring, "apr_01K7QS00000000000000000" + number, "Person", number, "person" + number + "@example.test");
            }
            await db.SaveChangesAsync(Ct);
        }

        var first = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "needs-first-page", new { }, Ct);
        Assert.Equal(100, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var next = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "needs-next-page", new { cursor = first.NextCursor }, Ct);
        Assert.Equal(2, next.Items.Count);
        Assert.Null(next.NextCursor);
        Assert.Equal(102, first.Items.Concat(next.Items).Select(a => a.Id).Distinct().Count());
        Assert.Equal(autumn.PublicId, Assert.Single(first.Items, a => a.Operation == "conversation.reply").CampaignId);

        // The first approval of the second page read whole, checked against its work item and approved; the second page,
        // read again by the same cursor, then holds the one after it.
        var later = next.Items[0].Id;
        var whole = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "needs-page-two", new { approval_id = later }, Ct);
        await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "needs-page-two-parked", new { work_item_id = whole.WorkItemId }, Ct);
        await api.RecordAsync<ApprovalDto>(Operations.ApprovalApprove, "needs-page-two-approved", new { approval_id = later, actor = new { type = "human", id = "ada" } }, Ct);
        var rest = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "needs-next-page-after", new { cursor = first.NextCursor }, Ct);
        Assert.Equal([next.Items[1].Id], rest.Items.Select(a => a.Id));
    }

    private static async Task<(Campaign Spring, Campaign Autumn)> SeedAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var spring = WorkItemFactory.NewCampaign("Spring launch", now: Noon);
        spring.PublicId = "cmp_01K7QR00000000000000000001";
        var autumn = WorkItemFactory.NewCampaign("Autumn outreach", now: Noon);
        autumn.PublicId = "cmp_01K7QR00000000000000000002";
        db.Campaigns.AddRange(spring, autumn);
        await db.SaveChangesAsync(Ct);

        AddSend(db, spring, EmailApproval, "Ines", "Duarte", "ines@example.test");
        var contact = NewContact(ReplyApproval, "Kai", "Lindt", "kai@example.test");
        var input = new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = "email",
                ["thread"] = new JsonObject { ["external_id"] = "t_501" },
                ["body"] = "Thanks Kai, Thursday at ten works for me.",
            },
            ["contacts"] = ContactsOf(contact, "kai@example.test"),
            ["campaign"] = CampaignOf(autumn),
            ["idempotency_key"] = "answer-t_501-1",
        };
        AddApproval(db, autumn, ReplyApproval, contact, Reply, input);

        var role = WorkItemFactory.NewAiRole(autumn, now: Noon);
        role.PublicId = "wi_01K7QR0000000000000000000C";
        role.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(role, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(role);
        db.Attempts.Add(attempt);
        db.Decisions.Add(new Decision
        {
            PublicId = "dec_01K7QR0000000000000000000C",
            Campaign = autumn,
            WorkItem = role,
            Attempt = attempt,
            Question = "Kai asked to move the call to Thursday. Keep the follow-up for Friday?",
            Options = new JsonArray(
                new JsonObject { ["label"] = "Keep Friday", ["detail"] = null },
                new JsonObject { ["label"] = "Move it to Monday", ["detail"] = "A day after the call, so it can mention it." }),
            Status = DecisionStatus.Pending,
            RaisedAt = Noon,
        });
        await db.SaveChangesAsync(Ct);
        return (spring, autumn);
    }

    private static void AddSend(JasonDbContext db, Campaign campaign, string id, string first, string last, string address)
    {
        var contact = NewContact(id, first, last, address);
        var input = new JsonObject
        {
            ["args"] = EmailArgs(first, $"Hi {first}, thanks for the talk at the meetup."),
            ["contacts"] = ContactsOf(contact, address),
            ["campaign"] = CampaignOf(campaign),
        };
        AddApproval(db, campaign, id, contact, Send, input);
    }

    private static JsonObject EmailArgs(string first, string body) =>
        new()
        {
            ["channel"] = "email",
            ["sender"] = new JsonObject { ["external_id"] = "s_12", ["address"] = "owner@example.test" },
            ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Notes from the meetup", ["body"] = body },
        };

    private static Contact NewContact(string approvalId, string first, string last, string address)
    {
        var contact = new Contact { PublicId = "cnt_" + approvalId[4..], FirstName = first, LastName = last, Company = "Kavali", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = address, IsPrimary = true });
        return contact;
    }

    private static JsonArray ContactsOf(Contact contact, string address) =>
        new(new JsonObject
        {
            ["id"] = contact.PublicId,
            ["first_name"] = contact.FirstName,
            ["last_name"] = contact.LastName,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = address, ["primary"] = true }),
            ["external_ids"] = new JsonObject(),
        });

    private static JsonObject CampaignOf(Campaign campaign) =>
        new() { ["id"] = campaign.PublicId, ["name"] = campaign.Name, ["status"] = "active", ["external_ids"] = new JsonObject() };

    private static void AddApproval(JasonDbContext db, Campaign campaign, string id, Contact contact, OperationContract contract, JsonObject input)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, contract.Id, Noon);
        item.PublicId = "wi_" + id[4..];
        item.Status = WorkItemStatus.AwaitingApproval;
        item.Contact = contact;
        item.Context = new JsonObject { ["input"] = input["args"]!.DeepClone() };
        db.WorkItems.Add(item);

        var binding = new JsonObject { ["workspace"] = "a" };
        var subject = ApprovalSubject.Of(
            new ProviderOpPlan(TestPlugins.Loaded("reply", [contract.Id]), "snp_01K7QR00000000000000000001", "rts_01K7QR00000000000000000001",
                RouteScope.GlobalDefault, binding, BindingIdentity.Of(binding), contract, input),
            item);

        db.Approvals.Add(new Approval
        {
            PublicId = id,
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = contract.Id,
            OperationVersion = contract.Version,
            Subject = subject.Document,
            SubjectHash = subject.Hash,
            Preview = ApprovalPreview.Of(contract, subject, campaign, contact, "email"),
            PluginId = "reply",
            BindingIdentity = BindingIdentity.Of(binding),
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_01K7QR00000000000000000001",
            RoutingSnapshotId = "rts_01K7QR00000000000000000001",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = Noon,
            Status = ApprovalStatus.Pending,
        });
    }
}
