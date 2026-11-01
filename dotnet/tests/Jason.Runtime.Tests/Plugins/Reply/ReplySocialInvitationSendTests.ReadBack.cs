using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// An invitation repeated after an attempt that may have sent it and returned it, <c>context.unsettled_reference</c>,
/// reads that send first with <c>reply direct-send get</c>, as a message does, inside the window and past it; only where
/// the read says nothing of the send does the window decide.
/// </summary>
public partial class ReplySocialInvitationSendTests
{
    private const string Recorded = "linkedin-1007";

    /// <summary>Reply's record of the invitation answers the work item, and nothing more is called or sent.</summary>
    [Theory]
    [InlineData("queued", "\"2099-11-03T14:30:00Z\"", "accepted", "2099-11-03T14:30:00Z", true, 0.5)]
    [InlineData("queued", "\"2099-11-03T14:30:00Z\"", "accepted", "2099-11-03T14:30:00Z", true, 30)]
    [InlineData("sent", "null", "sent", null, false, 30)]
    [InlineData("waitingForSchedule", "null", "accepted", null, true, 0.5)]
    public async Task The_invitation_an_earlier_attempt_returned_is_read_first_and_answers_the_work_item(
        string state, string sendAfter, string status, string? acceptedFor, bool cancelable, double hoursAgo)
    {
        using var account = Planted();
        account.Returns("direct-send get", Record(state, sendAfter), match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded);

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal((status, Recorded, acceptedFor, cancelable), ((string?)item["status"], (string?)item["reference"], (string?)item["accepted_for"], (bool)item["cancelable"]!));
        Assert.Equal("34", (string?)item["sender_external_id"]);
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["invitation"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"linkedin-1007"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["direct-send get linkedin-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>A record that says the invitation failed or was cancelled is final, and nothing goes.</summary>
    [Theory]
    [InlineData("failed", "The account is restricted.", "says it did not go", 30)]
    [InlineData("cancelled", null, "was cancelled at Reply", 0.5)]
    public async Task An_invitation_an_earlier_attempt_returned_that_failed_or_was_cancelled_is_final(string state, string? reason, string said, double hoursAgo)
    {
        using var account = Planted();
        var record = Record(state, "null");
        record["failure_reason"] = reason;
        account.Returns("direct-send get", record, match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal("""{"contact":"1001","send":"linkedin-1007"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["direct-send get linkedin-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read that finds nothing says nothing of the invitation: inside the window the repeat goes under the key, past it
    /// it is not settled and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(30)]
    public async Task A_read_that_finds_no_invitation_leaves_the_repeat_to_the_window(double hoursAgo)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded);

        if (hoursAgo < 23)
        {
            Assert.Equal("sent", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["status"]);
            Assert.Equal(["direct-send get linkedin-1007", "account list", "direct-send get email-1", "contact get 1001", "contact linkedin-connect"], Lines(account));
            Assert.Equal(Key, account.Calls[^1].Option("--idempotency-key"));
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["direct-send get linkedin-1007", "account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read the CLI refuses for want of a working credential is the CLI's own refusal, not a read that found nothing:
    /// the repeat fails as unauthorized, and nothing more is called or sent.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(30)]
    public async Task A_read_the_cli_refuses_for_its_credential_fails_as_unauthorized(double hoursAgo)
    {
        using var account = Planted();
        account.Refuses("direct-send get", "auth.not_logged_in", exitCode: 2, match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded));

        Assert.Equal(("unauthorized", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["direct-send get linkedin-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read Reply answers with <c>direct_send.not_owned</c> says the invitation an earlier attempt returned is held by an
    /// account this one is not: a repeat under the key from here is not settled by what Reply keeps there, so it is not
    /// settled, inside the window and past it, and nothing more is called or sent.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(30)]
    public async Task A_read_reply_answers_as_another_account_s_is_not_settled_and_nothing_is_sent(double hoursAgo)
    {
        using var account = Planted();
        account.Refuses("direct-send get", "direct_send.not_owned", status: 403, replyCode: "directSend.notOwned", match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded));

        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["direct-send get linkedin-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply's record of the invitation an earlier attempt returned, as <c>reply direct-send get</c> prints it.</summary>
    private static JsonObject Record(string state, string sendAfter) => JsonNode.Parse(
        $$"""{"reference":"{{Recorded}}","channel":"linkedin","contact_id":1001,"state":"{{state}}","send_after":{{sendAfter}},"sent_at":null,"message_id":null,"failure_reason":null,"cancelled_at":null}""")!.AsObject();
}
