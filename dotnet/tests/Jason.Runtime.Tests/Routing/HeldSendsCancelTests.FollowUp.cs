using System.Text.Json.Nodes;
using Jason.Runtime.Contacts;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A follow-up into an earlier letter's thread never explains a move: the provider does not move the thread's moment when
/// one lands, so a line's moment is never its doing. A touch that follows no work item, or that has no follows at all, is a
/// letter of its own and explains a move as before.
/// </summary>
public partial class HeldSendsCancelTests
{
    /// <summary>
    /// The person answered, and Jason's touch went after the answer, before the line was read: the line is Jason's own move
    /// only where that touch is a letter of its own. A touch whose <c>follows</c> names a work item is a follow-up, which
    /// never explains the move; one whose <c>follows</c> names none, or that has none, still does.
    /// </summary>
    [Theory]
    [InlineData("no follows", true)]
    [InlineData("follows naming no work item", true)]
    [InlineData("follows naming a work item", false)]
    public async Task Only_a_letter_of_its_own_and_never_a_follow_up_explains_a_move(string follows, bool ownMove)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var first = await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, wentAt: Noon.AddHours(-2));
        var answered = Noon.AddMinutes(-10);
        var touch = await SentAsync(harness, campaign, person, sendAfter: null, cancelable: false, acceptedFor: Moment(answered.AddMinutes(2)));
        if (follows != "no follows")
        {
            await FollowsAsync(harness, touch, follows == "follows naming a work item" ? first : null);
        }

        await using var db = harness.Open();
        Assert.Equal(ownMove, await LineHold.OwnMoveAsync(db, campaign, person, "th-1", answered, Noon, Ct));
    }

    /// <summary>Records on the touch the decision of what it follows, naming <paramref name="workItem"/> or no work item.</summary>
    private static async Task FollowsAsync(DispatchHarness harness, string touch, string? workItem)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == touch, Ct);
        var context = (JsonObject)item.Context.DeepClone();
        context["follows"] = new JsonObject { ["work_item"] = workItem, ["operation"] = workItem is null ? null : "message.send" };
        item.Context = context;
        await db.SaveChangesAsync(Ct);
    }
}
