using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Json;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The review hash binds a review to the text it read: the operation, the item and its context's input, canonically. It
/// moves when the input moves and with nothing else, so a pin, a rename or a route never voids a review whose text stands.
/// </summary>
public class ReviewSubjectTests
{
    [Fact]
    public void The_hash_is_the_canonical_hash_of_the_operation_the_item_and_the_input()
    {
        var send = Send(new JsonObject { ["body"] = "Hello" });

        var expected = CanonicalJson.Hash(new JsonObject
        {
            ["input"] = new JsonObject { ["body"] = "Hello" },
            ["operation"] = "message.send",
            ["work_item"] = send.PublicId,
        });

        Assert.Equal(expected, ReviewSubject.Hash(send));
        Assert.StartsWith("sha256:", expected, StringComparison.Ordinal);
    }

    [Fact]
    public void An_edit_of_the_input_changes_the_hash()
    {
        var send = Send(new JsonObject { ["body"] = "Hello" });
        var before = ReviewSubject.Hash(send);

        send.Context[WorkItemService.InputKey] = new JsonObject { ["body"] = "Hello again" };

        Assert.NotEqual(before, ReviewSubject.Hash(send));
    }

    [Fact]
    public void Nothing_but_the_input_moves_it_neither_another_context_key_nor_the_campaign()
    {
        var send = Send(new JsonObject { ["body"] = "Hello" });
        var before = ReviewSubject.Hash(send);

        send.Context["note_for_the_manager"] = "anything";
        send.Campaign!.Name = "Renamed";
        send.Campaign.Status = CampaignStatus.Paused;
        send.Priority = 40;

        Assert.Equal(before, ReviewSubject.Hash(send));
    }

    [Fact]
    public void Two_items_with_the_same_input_have_different_hashes()
    {
        var input = new JsonObject { ["body"] = "Hello" };

        Assert.NotEqual(ReviewSubject.Hash(Send(input)), ReviewSubject.Hash(Send(input)));
    }

    [Theory]
    [InlineData("message.send", true)]
    [InlineData("social_invitation.send", true)]
    [InlineData("conversation.reply", true)]
    [InlineData("conversation.get", false)]
    [InlineData("campaign.enroll", false)]
    [InlineData("no.such_operation", false)]
    public void A_send_a_person_decides_each_one_of_is_reviewable_and_no_other_operation_is(string operation, bool reviewable)
    {
        Assert.Equal(reviewable, ReviewSubject.IsReviewable(operation));
    }

    [Fact]
    public void Role_work_is_never_reviewable()
    {
        var campaign = WorkItemFactory.NewCampaign();
        var role = WorkItemFactory.NewAiRole(campaign, configure: item => item.Operation = "message.send");

        Assert.False(ReviewSubject.IsReviewable(role));
    }

    [Fact]
    public void Workitem_get_s_answer_carries_the_hash_on_a_send_and_on_no_other_work()
    {
        var campaign = WorkItemFactory.NewCampaign();
        var send = Send(new JsonObject { ["body"] = "Hello" });
        var read = WorkItemFactory.NewProviderOp(campaign, "conversation.get");
        var role = WorkItemFactory.NewAiRole(campaign);
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(ReviewSubject.Hash(send), WorkItemMapper.ToDto(send, now, [], includeSnapshots: false).ReviewHash);
        Assert.Null(WorkItemMapper.ToDto(read, now, [], includeSnapshots: false).ReviewHash);
        Assert.Null(WorkItemMapper.ToDto(role, now, [], includeSnapshots: false).ReviewHash);
    }

    private static Jason.Runtime.Persistence.WorkItem Send(JsonObject input)
    {
        var send = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(), "message.send");
        send.Context = new JsonObject { [WorkItemService.InputKey] = input.DeepClone() };
        return send;
    }
}
