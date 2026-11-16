using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// What the Work items page reads and writes around a message that waits for a person, each story in one runtime so
/// that every id a list row carries is the id the reads after it answer: the Now tab and its counts, the item's panel,
/// the approval it waits for read whole, and then either the approval approved, or the text moved under it by another
/// hand before the press, which the press finds when it checks the message against its work item.
/// </summary>
public class WorkItemPanelApprovalExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly object Ada = new { type = "human", id = "ada" };
    private static readonly string[] Now = ["processing", "scheduled", "awaiting_approval"];
    private static readonly string[] Planned = ["created"];
    private static readonly string[] Done = ["succeeded", "failed", "cancelled", "expired"];

    [Fact]
    public async Task A_doubted_send_is_read_whole_from_its_work_item_and_approved()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var (approval, item) = await SendModeApprovalExchangesTests.SeedAsync(api, SendModeReasons.Doubted, "cold");

        // The Now tab lists the send that waits beside the critic's running review, and counts each tab.
        var listed = await ListAsync(api, "panel-doubted");
        Assert.Contains(listed.Items, row => row.Id == item && row.Status == WorkItemStatus.AwaitingApproval);
        await CountAsync(api, "panel-doubted", (2, 0, 0));

        // The panel: the item, its history, the approval it waits for, and that approval read whole, doubt and all.
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "panel-doubted", new { work_item_id = item }, Ct)).Status);
        await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "panel-doubted", new { work_item_id = item }, Ct);
        var row = Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "panel-doubted", new { work_item_id = item, statuses = new[] { "pending" } }, Ct)).Items);
        Assert.Equal((approval, item), (row.Id, row.WorkItemId));
        var whole = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "panel-doubted", new { approval_id = approval }, Ct);
        Assert.Equal((item, SendModeReasons.Doubted), (whole.WorkItemId, whole.Reason));
        Assert.Single(whole.Preview[Doubts.PreviewKey]!.AsArray());

        // Approved from the panel: the item goes back to the queue, and the panel and the tab read it so.
        Assert.Equal(ApprovalStatus.Approved, (await api.RecordAsync<ApprovalDto>(Operations.ApprovalApprove, "panel-doubted", new { approval_id = approval, actor = Ada }, Ct)).Status);
        Assert.NotEqual(WorkItemStatus.AwaitingApproval, (await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "panel-doubted-approved", new { work_item_id = item }, Ct)).Status);
        await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "panel-doubted-approved", new { work_item_id = item }, Ct);
        Assert.DoesNotContain((await ListAsync(api, "panel-doubted-approved")).Items, each => each.Id == item);
        await CountAsync(api, "panel-doubted-approved", (1, 1, 0));
    }

    [Fact]
    public async Task A_send_whose_text_moved_after_it_was_read_is_found_moved_at_the_press()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var (approval, item) = await SendModeApprovalExchangesTests.SeedAsync(api, SendModeReasons.SentFromChat, "cold");

        await ListAsync(api, "panel-moved");
        await CountAsync(api, "panel-moved", (1, 0, 0));
        var waiting = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "panel-moved", new { work_item_id = item }, Ct);
        await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "panel-moved", new { work_item_id = item }, Ct);
        Assert.Equal(approval, Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "panel-moved", new { work_item_id = item, statuses = new[] { "pending" } }, Ct)).Items).Id);
        await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "panel-moved", new { approval_id = approval }, Ct);

        // Another hand, the agent's chat say, edits the text while the panel is open: the runtime withdraws the approval.
        var input = waiting.Context["input"]!.DeepClone().AsObject();
        input["message"]!["body"] = "Hi Ines, thanks for the talk at the meetup. Shall we talk on Tuesday?";
        var (status, body) = await api.PostAsync(Operations.WorkItemUpdate, new { work_item_id = item, set = new { input }, reason = "Edited in the chat.", actor = Ada }, Ct);
        Assert.True(status == HttpStatusCode.OK, body);

        // The press checks the message against its work item and finds the text moved; the panel reads the item again,
        // and the tab, which no longer lists it as waiting.
        var moved = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "panel-moved-edited", new { work_item_id = item }, Ct);
        Assert.Equal("Hi Ines, thanks for the talk at the meetup. Shall we talk on Tuesday?", (string?)moved.Context["input"]!["message"]!["body"]);
        Assert.NotEqual(WorkItemStatus.AwaitingApproval, moved.Status);
        await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "panel-moved-edited", new { work_item_id = item }, Ct);
        Assert.Empty((await ListAsync(api, "panel-moved-edited")).Items);
        await CountAsync(api, "panel-moved-edited", (0, 1, 0));
    }

    private static Task<Page<WorkItemSummaryDto>> ListAsync(RuntimeApiFixture api, string @case) =>
        api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, @case + "-now", new { status = Now, order = "created", limit = 50 }, Ct);

    private static async Task CountAsync(RuntimeApiFixture api, string @case, (int Now, int Planned, int Done) expected)
    {
        var now = (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, @case + "-now", new { status = Now, order = "created" }, Ct)).Total;
        var planned = (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, @case + "-planned", new { status = Planned, order = "due" }, Ct)).Total;
        var done = (await api.RecordAsync<WorkItemCountResponse>(Operations.WorkItemCount, @case + "-done", new { status = Done, order = "finished" }, Ct)).Total;
        Assert.Equal(expected, (now, planned, done));
    }
}
