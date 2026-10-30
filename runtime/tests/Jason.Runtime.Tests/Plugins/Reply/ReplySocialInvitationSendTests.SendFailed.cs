using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// An invitation Reply's route took and that did not go, as for a message: Reply keeps no key for a send it answered with
/// a failure, so its own <c>directLinkedIn.unknown</c> is final in every shape, as is any other answer of Reply's at a
/// status of 400 to 499; only an ending with no answer of Reply's is repeated under the work item's key. A reference Reply
/// names is kept on the attempt.
/// </summary>
public partial class ReplySocialInvitationSendTests
{
    private const string LinkedInUnknown = "directLinkedIn.unknown";
    private const string Reason = "The LinkedIn account was signed out while the invitation was being sent.";

    [Theory]
    [InlineData("outcome.unknown", 3, true, "failed")]
    [InlineData("reply.refused", 1, true, "failed")]
    [InlineData("outcome.unknown", 3, false, "failed")]
    [InlineData("outcome.unknown", 3, true, "queued")]
    [InlineData("reply.refused", 1, false, null)]
    public async Task An_invitation_reply_lost_track_of_is_final_in_every_shape(string cliCode, int exit, bool named, string? state)
    {
        using var account = Planted().FailsTheNextSend(cliCode, exit, status: 400, replyCode: LinkedInUnknown, detail: Reason, named: named, state: state);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(named ? """{"contact":"1001","send":"linkedin-1001"}""" : """{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Contains(Reason, error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// An invitation Reply answered with a failure at 400 to 499 is final even where it went: the stand-in records it as
    /// sent, and no repeat is made under the key Reply released.
    /// </summary>
    [Theory]
    [InlineData(400, LinkedInUnknown)]
    [InlineData(400, null)]
    [InlineData(422, null)]
    public async Task An_invitation_reply_answered_with_a_failure_is_final_and_never_repeated_under_its_key(int status, string? replyCode)
    {
        using var account = Planted().FailsTheNextSend("outcome.unknown", 3, status: status, replyCode: replyCode, detail: Reason, went: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.False(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!, packageSettlesByKey: true));
        Assert.Single(account.Sent);
        Assert.Single(account.KeyedArrivals);
    }

    /// <summary>
    /// An invitation that ended with no answer of Reply's about it — one at 500 or above, the key still held at 409, or a
    /// 429 refused before the key was touched — is repeated under its key, and the repeat is the invitation: once.
    /// </summary>
    [Theory]
    [InlineData(503)]
    [InlineData(409)]
    [InlineData(429)]
    public async Task An_invitation_whose_answer_was_lost_is_repeated_under_its_key_and_sent(int status)
    {
        using var account = Planted().FailsTheNextSend("outcome.unknown", 3, status: status);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Assert.Equal(FailureClass.Ambiguous, ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001"))).Class);
        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: Unsettled))["items"]![0]!;

        Assert.Equal("sent", (string?)item["status"]);
        Assert.Single(account.Sent);
        Assert.Equal(2, account.KeyedArrivals.Count);
    }
}
