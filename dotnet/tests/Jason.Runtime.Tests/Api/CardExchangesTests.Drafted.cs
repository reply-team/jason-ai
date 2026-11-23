using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

public partial class CardExchangesTests
{
    private const string Drafted = "Thanks Kai,<br><br>Thursday at ten works for me.";
    private const string Plain = "Thanks Kai,\n\nThursday at ten works for me.";

    /// <summary>
    /// A reply drafted with markup and parked before its document refused markup, as the card reads it after the update:
    /// the yes is refused with the document's reason and nothing moves; the text edited on the card withdraws the question;
    /// the runtime asks again about the plain text, and the yes then goes through.
    /// </summary>
    /// <remarks>
    /// The one state here the API cannot make is the draft itself, since the document refuses its text at every door now:
    /// the reply is parked through the claim with plain text, and its arguments, its question and its preview are then given
    /// the drafted text, as they held it all along under the older document.
    /// </remarks>
    [Fact]
    public async Task A_reply_drafted_with_markup_before_the_update_is_refused_its_yes_then_edited_and_approved()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var scene = await Scene.OpenAsync(api, "Spring launch", more: ["conversation.reply"]);
        var kai = await scene.PersonAsync("Kai", "Lindt", "email", "kai@example.test");
        var item = await scene.SendAsync(kai, "conversation.reply", Replying("Thanks Kai, Thursday at ten works for me."));
        await scene.ClaimAsync();
        await DraftedBeforeTheUpdateAsync(api, item, "Thanks Kai, Thursday at ten works for me.", Drafted);

        // (a) What the card lists and reads, the drafted text in the question and in the item alike.
        var row = Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-pending", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items);
        Assert.Equal((item, "conversation.reply"), (row.WorkItemId, row.Operation));
        Assert.Equal([row.Id], (await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-all-pending", new { }, Ct)).Items.Select(a => a.Id));
        var asked = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-reply-marked", new { approval_id = row.Id }, Ct);
        Assert.Equal(Drafted, (string?)asked.Subject["input"]!["args"]!["body"]);
        var parked = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "card-reply-marked-parked", new { work_item_id = item }, Ct);
        Assert.True(JsonNode.DeepEquals(parked.Context["input"], asked.Subject["input"]!["args"]), "the item holds the arguments its approval asked about");

        // (b) The yes, refused with the document's own reason; the question still waits and the item is still parked.
        var refused = await api.RecordErrorAsync(Operations.ApprovalApprove, "card-reply-marked-refused", new { approval_id = row.Id, actor = Ada }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(("validation_failed", false), (refused.Code, refused.Retryable));
        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("context.input/body", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("never a tag and never an entity", detail.Message, StringComparison.Ordinal);
        Assert.Equal(ApprovalStatus.Pending, (await api.PostOkAsync<ApprovalDto>(Operations.ApprovalGet, new { approval_id = row.Id }, Ct)).Status);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct)).Status);
        Assert.DoesNotContain(
            (await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { work_item_id = item }, Ct)).Items,
            line => line.Kind is "approval_approved" or "workitem_released");

        // (c) The text edited on the card with the tags taken out: the edit passes and withdraws the question.
        var plain = Replying(Plain);
        var patched = await api.RecordAsync<WorkItemDto>(Operations.WorkItemUpdate, "card-reply-marked-edited", new { work_item_id = item, set = new { input = plain }, reason = EditReason, actor = Ada }, Ct);
        Assert.Equal((item, WorkItemStatus.Created), (patched.Id, patched.Status));
        Assert.Empty((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-after-edit", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items);
        Assert.Empty((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-all-after-edit", new { }, Ct)).Items);
        Assert.Equal(WorkItemStatus.Created, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "card-reply-marked-requeued", new { work_item_id = item }, Ct)).Status);
        Assert.Empty((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-item-after-edit", new { work_item_id = item, statuses = new[] { "pending" } }, Ct)).Items);
        var history = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-item-all-after-edit", new { work_item_id = item, statuses = AllApprovalStatuses }, Ct);
        Assert.Equal([(row.Id, ApprovalStatus.Superseded)], history.Items.Select(a => (a.Id, a.Status)));

        // (d) The runtime asks again about the plain text, and the yes goes through.
        await scene.ClaimAsync();
        var reasked = Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-reasked", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items);
        Assert.NotEqual(row.Id, reasked.Id);
        Assert.Equal([reasked.Id], (await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-all-reasked", new { }, Ct)).Items.Select(a => a.Id));
        Assert.Equal([reasked.Id], (await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-item-reasked", new { work_item_id = item, statuses = new[] { "pending" } }, Ct)).Items.Select(a => a.Id));
        Assert.Equal([row.Id, reasked.Id], (await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-reply-marked-item-all-reasked", new { work_item_id = item, statuses = AllApprovalStatuses }, Ct)).Items.Select(a => a.Id).Order());
        var again = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-reply-marked-reasked", new { approval_id = reasked.Id }, Ct);
        Assert.True(JsonNode.DeepEquals(plain, again.Subject["input"]!["args"]), "the question asked again holds the plain text");
        var reparked = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "card-reply-marked-reparked", new { work_item_id = item }, Ct);
        Assert.True(JsonNode.DeepEquals(plain, reparked.Context["input"]), "the item holds the arguments the new question asked about");
        var approved = await api.RecordAsync<ApprovalDto>(Operations.ApprovalApprove, "card-reply-marked-reasked", new { approval_id = reasked.Id, actor = Ada }, Ct);
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
    }

    private static JsonObject Replying(string body) => new()
    {
        ["channel"] = "email",
        ["thread"] = new JsonObject { ["external_id"] = "t_501" },
        ["body"] = body,
    };

    /// <summary>
    /// The parked reply as it stood under the document of its day: its arguments, its question and the preview the person
    /// reads all carry the drafted text where the claim put <paramref name="written"/>.
    /// </summary>
    private static async Task DraftedBeforeTheUpdateAsync(RuntimeApiFixture api, string item, string written, string drafted)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var parked = await db.WorkItems.SingleAsync(w => w.PublicId == item, Ct);
        var approval = await db.Approvals.SingleAsync(a => a.WorkItemId == parked.Id, Ct);
        parked.Context = (JsonObject)Rewritten(parked.Context, written, drafted)!;
        approval.Subject = (JsonObject)Rewritten(approval.Subject, written, drafted)!;
        approval.Preview = (JsonObject)Rewritten(approval.Preview, written, drafted)!;
        await db.SaveChangesAsync(Ct);
        Assert.Equal(drafted, (string?)parked.Context[WorkItemService.InputKey]!["body"]);
    }

    /// <summary>A copy of <paramref name="node"/> with every text that holds <paramref name="written"/> holding <paramref name="drafted"/> in its place.</summary>
    private static JsonNode? Rewritten(JsonNode? node, string written, string drafted) => node switch
    {
        JsonObject map => new JsonObject(map.Select(pair => KeyValuePair.Create(pair.Key, Rewritten(pair.Value, written, drafted)))),
        JsonArray list => new JsonArray([.. list.Select(each => Rewritten(each, written, drafted))]),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(text.Replace(written, drafted, StringComparison.Ordinal)),
        _ => node?.DeepClone(),
    };
}
