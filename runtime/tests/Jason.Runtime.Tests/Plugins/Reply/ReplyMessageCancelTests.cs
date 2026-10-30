using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>message.cancel</c> as the official package performs it: <c>reply direct-send cancel</c> of the reference the runtime
/// composed under <c>send</c>. A send Reply still holds is cancelled, and one cancelled before answers the same; one that
/// went, that Reply is sending, or that failed is <c>send_not_pending</c>, permanent, with the state that says which —
/// never reported as cancelled.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyMessageCancelTests
{
    private const string Operation = "message.cancel";

    private const string Key = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRF";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A queued send is cancelled, and a second cancel answers the same: cancelled when it first was.</summary>
    [Theory]
    [InlineData("queued")]
    [InlineData("waitingForSchedule")]
    public async Task A_send_reply_still_holds_is_cancelled_and_a_second_cancel_answers_the_same(string held)
    {
        using var account = new ReplyAccount().WithDirectSend(ReplyMessageStatusTests.Reference, held, sendAfter: "2099-11-03T14:00:00Z");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct);
        var second = await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct);

        var cancelled = ReplyOperations.Succeeded(Operation, first);
        Assert.Equal(cancelled.ToJsonString(), ReplyOperations.Succeeded(Operation, second).ToJsonString());
        var item = Assert.Single(cancelled["items"]!.AsArray())!;
        Assert.Equal("cancelled", (string?)item["state"]);
        Assert.Equal((string?)account.DirectSends[0]!["cancelledAt"], (string?)item["cancelled_at"]);
        Assert.NotNull((string?)item["cancelled_at"]);
        Assert.Null(Assert.IsType<InvocationOutcome.Succeeded>(first.Outcome).ExternalIds);
        Assert.Equal("cancelled", (string?)account.DirectSends[0]!["state"]);
        Assert.Equal([$"direct-send cancel {ReplyMessageStatusTests.Reference}", $"direct-send cancel {ReplyMessageStatusTests.Reference}"], account.Calls.Select(call => call.Line));
        Assert.All(account.Calls, call => Assert.Equal(["--json", "-q", "direct-send", "cancel", ReplyMessageStatusTests.Reference], call.Args));
        Assert.All(account.Calls, call => Assert.Null(call.Option("--idempotency-key")));
    }

    /// <summary>
    /// A send that went, that Reply is sending now, or that failed cannot be cancelled: <c>send_not_pending</c>,
    /// permanent, with the state in the details, and Reply's record is left as it was. A send Reply lost track of while
    /// it was going out is failed as Reply holds it, though <c>message.status</c> tells it as <c>unknown</c>.
    /// </summary>
    [Theory]
    [InlineData("sent", null, "sent", "went")]
    [InlineData("sending", null, "sent", "went")]
    [InlineData("failed", null, "failed", "failed")]
    [InlineData("failed", "directSend.dispatchInterrupted", "failed", "failed")]
    public async Task A_send_that_went_or_failed_is_not_pending_and_never_reported_as_cancelled(string held, string? code, string state, string said)
    {
        using var account = new ReplyAccount().WithDirectSend(ReplyMessageStatusTests.Reference, held, failureCode: code);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("send_not_pending", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(state, (string?)error.Details!["state"]);
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal(held, (string?)account.DirectSends[0]!["state"]);
    }

    /// <summary>
    /// A cancel Reply answers with a state that says the send went or failed is that refusal too, whatever the exit: it is
    /// never reported as cancelled. One that answers about another send, or in a state that still holds it, is no answer,
    /// and the cancel is repeated, which the document says is safe.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1001","state":"sent"}""", "send_not_pending")]
    [InlineData("""{"reference":"email-1001","state":"failed"}""", "send_not_pending")]
    [InlineData("""{"reference":"email-1001","state":"failed","failure_code":"directSend.dispatchInterrupted"}""", "send_not_pending")]
    [InlineData("""{"reference":"email-1001","state":"queued"}""", "provider_answer_lost")]
    [InlineData("""{"reference":"email-1009","state":"cancelled"}""", "provider_answer_lost")]
    public async Task A_cancel_answered_in_another_state_is_never_a_cancelled_send(string answer, string code)
    {
        using var account = new ReplyAccount().WithDirectSend(ReplyMessageStatusTests.Reference, "queued");
        account.Returns("direct-send cancel", JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(code, error.Code);
        Assert.Equal(code == "send_not_pending" ? FailureClass.Permanent : FailureClass.Ambiguous, error.Class);
        Assert.Equal(code != "send_not_pending", OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
    }

    /// <summary>
    /// A cancelled send whose moment cannot be read is still cancelled: the moment is none given, and the state answers.
    /// Reply's own form of a moment, two digits of a second's fraction, is written to the second.
    /// </summary>
    [Theory]
    [InlineData("soon", null)]
    [InlineData("2026-11-03T14:00:12.00Z", "2026-11-03T14:00:12Z")]
    public async Task A_cancelled_send_is_cancelled_whatever_moment_reply_writes_for_it(string written, string? cancelledAt)
    {
        using var account = new ReplyAccount().WithDirectSend(ReplyMessageStatusTests.Reference, "queued");
        account.Returns("direct-send cancel", new JsonObject { ["reference"] = ReplyMessageStatusTests.Reference, ["state"] = "cancelled", ["cancelled_at"] = written });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = Assert.Single(ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct))["items"]!.AsArray())!;

        Assert.Equal(("cancelled", cancelledAt), ((string?)item["state"], (string?)item["cancelled_at"]));
    }

    /// <summary>A lost answer is repeated: cancelling a cancelled send is a success, so the repeat is safe.</summary>
    [Fact]
    public async Task A_lost_answer_is_handed_out_again_and_the_repeat_answers_cancelled()
    {
        using var account = new ReplyAccount().WithDirectSend(ReplyMessageStatusTests.Reference, "queued");
        account.LosesTheOutcome("direct-send cancel");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));
        var repeat = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct, attempt: 2));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
        Assert.Equal("cancelled", (string?)repeat["items"]![0]!["state"]);
    }

    /// <summary>
    /// A send Reply does not hold for this account is <c>send_not_found</c>; a route Reply's API does not have is the
    /// capability not offered. Nothing is cancelled either way.
    /// </summary>
    [Theory]
    [InlineData("missing", "send_not_found")]
    [InlineData("not owned", "send_not_found")]
    [InlineData("not offered", "capability_not_offered")]
    public async Task A_send_reply_does_not_hold_for_this_account_is_not_found(string why, string code)
    {
        using var account = new ReplyAccount();
        if (why == "not owned")
        {
            account.Refuses("direct-send cancel", "direct_send.not_owned", status: 403, replyCode: "directSend.notOwned");
        }
        else if (why == "not offered")
        {
            account.NotOffering("direct_send");
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal((code, FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal([$"direct-send cancel {ReplyMessageStatusTests.Reference}"], account.Calls.Select(call => call.Line));
    }

    /// <summary>
    /// The send cancelled is the one the runtime named under <c>send.reference</c>, and nothing the caller wrote: another
    /// send the account holds is left as it was.
    /// </summary>
    [Fact]
    public async Task The_reference_is_the_runtime_s_never_the_caller_s()
    {
        using var account = new ReplyAccount().WithDirectSend("email-1001", "queued").WithDirectSend("email-1002", "queued");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var input = Input("email-1002");
        input["args"]!["reference"] = "email-1001";

        ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, input, Ct, claimRefuses: ["/args/reference"]));

        Assert.Equal(["queued", "cancelled"], account.DirectSends.Select(send => (string?)send!["state"]));
        Assert.Equal(["direct-send cancel email-1002"], account.Calls.Select(call => call.Line));
    }

    private static JsonObject Input(string reference = ReplyMessageStatusTests.Reference)
    {
        var input = ReplyMessageStatusTests.Input(reference);
        input["idempotency_key"] = Key;
        return input;
    }
}
