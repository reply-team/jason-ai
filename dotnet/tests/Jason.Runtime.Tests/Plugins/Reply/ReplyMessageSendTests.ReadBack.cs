using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A repeat told which send an earlier attempt that may have acted returned, <c>context.unsettled_reference</c>, reads
/// that send first with <c>reply direct-send get</c>, inside the window a repeat goes under the key in and past it: a
/// read never makes a send twice. What Reply says became of the send answers the work item, and nothing is sent; only
/// where the read finds nothing, or gets no answer, does the window decide.
/// </summary>
public partial class ReplyMessageSendTests
{
    private const string Recorded = "email-1007";

    /// <summary>
    /// Reply's record of the send answers the work item as a key Reply holds for another request does: its state says
    /// what happened, each field comes back only where Reply gave its source, and a state the package does not know is
    /// still a send Reply holds. Nothing more is called and nothing goes, inside the window and past it.
    /// </summary>
    [Theory]
    [InlineData("queued", "\"2099-11-03T14:00:00Z\"", "accepted", "2099-11-03T14:00:00Z", true, 0.5)]
    [InlineData("queued", "\"2099-11-03T14:00:00Z\"", "accepted", "2099-11-03T14:00:00Z", true, 30)]
    [InlineData("sending", "null", "accepted", null, false, 0.5)]
    [InlineData("sending", "null", "accepted", null, false, 30)]
    [InlineData("sent", "null", "sent", null, false, 0.5)]
    [InlineData("sent", "null", "sent", null, false, 30)]
    [InlineData("waitingForSchedule", "null", "accepted", null, true, 0.5)]
    [InlineData("waitingForSchedule", "null", "accepted", null, true, 30)]
    [InlineData("heldForReview", "\"2099-11-03T14:00:00Z\"", "accepted", "2099-11-03T14:00:00Z", true, 0.5)]
    [InlineData("heldForReview", "\"2099-11-03T14:00:00Z\"", "accepted", "2099-11-03T14:00:00Z", true, 30)]
    public async Task The_send_an_earlier_attempt_returned_is_read_first_and_answers_the_work_item(
        string state, string sendAfter, string status, string? acceptedFor, bool cancelable, double hoursAgo)
    {
        using var account = ByEmail();
        account.Returns("direct-send get", Record(state, sendAfter), match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded);

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal((status, Recorded, acceptedFor, cancelable), ((string?)item["status"], (string?)item["reference"], (string?)item["accepted_for"], (bool)item["cancelable"]!));
        Assert.Equal(acceptedFor is not null, item.AsObject().ContainsKey("accepted_for"));
        Assert.Equal("12", (string?)item["sender_external_id"]);
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"email-1007"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["direct-send get email-1007"], Lines(account));
        Assert.Null(account.Calls[0].Option("--idempotency-key"));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A record that says the send failed or was cancelled says what became of the work item's send: that is final, with
    /// Reply's reason where it gave one, the send kept as the work item's, and nothing goes, inside the window and past it.
    /// </summary>
    [Theory]
    [InlineData("failed", "The mailbox refused the address.", "says it did not go", 0.5)]
    [InlineData("failed", "The mailbox refused the address.", "says it did not go", 30)]
    [InlineData("cancelled", null, "was cancelled at Reply", 0.5)]
    [InlineData("cancelled", null, "was cancelled at Reply", 30)]
    public async Task A_send_an_earlier_attempt_returned_that_failed_or_was_cancelled_is_final(string state, string? reason, string said, double hoursAgo)
    {
        using var account = ByEmail();
        var record = Record(state, "null");
        record["failure_reason"] = reason;
        account.Returns("direct-send get", record, match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal(reason is not null, error.Message.EndsWith(" Reply said: " + reason, StringComparison.Ordinal));
        Assert.Equal("""{"contact":"1001","send":"email-1007"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["direct-send get email-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read that finds nothing, gets no answer, names another send or answers in another shape says nothing of the send,
    /// and the window decides as it does without a reference: inside it the repeat reads that Reply keeps the key and goes
    /// under it; past it, it is not settled and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("not found", 0.5)]
    [InlineData("not found", 30)]
    [InlineData("unreachable", 0.5)]
    [InlineData("unreachable", 30)]
    [InlineData("another send", 0.5)]
    [InlineData("another send", 30)]
    [InlineData("another shape", 0.5)]
    [InlineData("another shape", 30)]
    public async Task A_read_that_says_nothing_of_the_send_leaves_the_repeat_to_the_window(string read, double hoursAgo)
    {
        using var account = ByEmail();
        switch (read)
        {
            case "unreachable":
                account.Refuses("direct-send get", "network", match: Recorded);
                break;
            case "another send":
                var other = Record("queued", "null");
                other["reference"] = "email-1009";
                account.Returns("direct-send get", other, match: Recorded);
                break;
            case "another shape":
                account.Returns("direct-send get", Record("queued", "\"next week\""), match: Recorded);
                break;
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded);

        if (hoursAgo < 23)
        {
            Assert.Equal("sent", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["status"]);
            Assert.Equal(["direct-send get email-1007", "account list", "contact get 1001", "contact send-email"], Lines(account));
            Assert.Equal(Key, account.Calls[^1].Option("--idempotency-key"));
            Assert.All(account.Calls.SkipLast(1), call => Assert.Null(call.Option("--idempotency-key")));
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("23 hours", error.Message, StringComparison.Ordinal);
        Assert.Equal(["direct-send get email-1007", "account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read the CLI refuses for want of a working credential is no read that found nothing: it is the CLI's own
    /// refusal, so the repeat fails as every command the profile cannot make does, inside the window and past it, and
    /// nothing more is called or sent.
    /// </summary>
    [Theory]
    [InlineData("auth.not_logged_in", 0.5)]
    [InlineData("auth.not_logged_in", 30)]
    [InlineData("auth.required", 0.5)]
    public async Task A_read_the_cli_refuses_for_its_credential_fails_as_unauthorized(string code, double hoursAgo)
    {
        using var account = ByEmail();
        account.Refuses("direct-send get", code, exitCode: 2, match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded));

        Assert.Equal(("unauthorized", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(code, error.Details!["cli_code"]!.GetValue<string>());
        Assert.Equal(["direct-send get email-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read Reply answers with <c>direct_send.not_owned</c> says the send an earlier attempt returned is held by an
    /// account this one is not: a repeat under the key from here is not settled by what Reply keeps there, so it is not
    /// settled, inside the window and past it, and nothing more is called or sent.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(30)]
    public async Task A_read_reply_answers_as_another_account_s_is_not_settled_and_nothing_is_sent(double hoursAgo)
    {
        using var account = ByEmail();
        account.Refuses("direct-send get", "direct_send.not_owned", status: 403, replyCode: "directSend.notOwned", match: Recorded);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: Recorded));

        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["direct-send get email-1007"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply's record of the send an earlier attempt returned, as <c>reply direct-send get</c> prints it.</summary>
    private static JsonObject Record(string state, string sendAfter) => JsonNode.Parse(
        $$"""{"reference":"{{Recorded}}","channel":"email","contact_id":1001,"state":"{{state}}","send_after":{{sendAfter}},"sent_at":null,"message_id":null,"failure_reason":null,"cancelled_at":null}""")!.AsObject();
}
