using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A send Reply's route took and that did not go. Where Reply names the send, its reference is kept on the attempt as the
/// work item's <c>send</c>, whatever the ending's class. Reply keeps no key for a send it answered with a failure, so a
/// repeat under the key would be a new send: every answer of Reply's at a status of 400 to 499 about the send is final, its
/// own <c>directLinkedIn.unknown</c> in every shape among them; a 409 and a 429 are not about it. Only an ending with no
/// answer of Reply's about the send — none at all, one at 500 or above, a 409 or a 429 — is the lost answer, repeated
/// under the work item's key.
/// </summary>
public partial class ReplyMessageSendTests
{
    private const string LinkedInUnknown = "directLinkedIn.unknown";
    private const string Reason = "The LinkedIn account was signed out while the message was being sent.";

    /// <summary>
    /// An email Reply took and could not send names the send on its refusal: the ending keeps its class, final, and the
    /// reference stays on the attempt; a refusal that names none keeps none.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_email_reply_could_not_send_is_final_and_keeps_the_reference_reply_named(bool named)
    {
        using var account = ByEmail().FailsTheNextSend("reply.refused", 1, status: 400, replyCode: "contact.emailSendingFailed", named: named);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(named ? """{"contact":"1001","send":"email-1001"}""" : """{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply's own code for a LinkedIn send it lost track of is a failure Reply answered, in every shape: a failed state
    /// with or without a reference, a reference in another state, the bare code. The send is final, Reply's reason in the
    /// message and its reference, where Reply named one, on the attempt. reply-cli from 0.17.1 answers the code as a lost
    /// answer, and 0.17.0 as a refusal; the rule is the same for both.
    /// </summary>
    [Theory]
    [InlineData("outcome.unknown", 3, true, "failed")]
    [InlineData("outcome.unknown", 3, false, "failed")]
    [InlineData("outcome.unknown", 3, true, "sending")]
    [InlineData("outcome.unknown", 3, false, null)]
    [InlineData("reply.refused", 1, true, "failed")]
    [InlineData("reply.refused", 1, false, "failed")]
    [InlineData("reply.refused", 1, true, "sending")]
    [InlineData("reply.refused", 1, false, null)]
    public async Task A_linkedin_send_reply_lost_track_of_is_final_in_every_shape(string cliCode, int exit, bool named, string? state)
    {
        using var account = OnLinkedIn().FailsTheNextSend(cliCode, exit, status: 400, replyCode: LinkedInUnknown, detail: Reason, named: named, state: state);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(named ? """{"contact":"1001","send":"linkedin-1001"}""" : """{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Contains(Reason, error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// On an email, Reply's LinkedIn code is nothing of its own, and still a failure Reply answered at 400: final, whether
    /// reply-cli passes it on as a refusal or as a lost answer.
    /// </summary>
    [Theory]
    [InlineData("reply.refused", 1)]
    [InlineData("outcome.unknown", 3)]
    public async Task On_an_email_reply_s_linkedin_code_is_a_failure_reply_answered(string cliCode, int exit)
    {
        using var account = ByEmail().FailsTheNextSend(cliCode, exit, status: 400, replyCode: LinkedInUnknown, detail: Reason, named: true, state: "failed");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", error.ExternalIds!.ToJsonString());
    }

    /// <summary>
    /// A send Reply answered with a failure at a status of 400 to 499 that reply-cli passes on as a lost answer is final,
    /// so no repeat is made under its key: Reply released the key with that answer, and a repeat would be a new send. The
    /// stand-in records each of these as a send that went, the worst such an answer can hide; it stays the one send.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", 400, LinkedInUnknown)]
    [InlineData("linkedin_message", 400, null)]
    [InlineData("email", 400, null)]
    [InlineData("email", 422, null)]
    public async Task A_send_reply_answered_with_a_failure_is_final_and_never_repeated_under_its_key(string format, int status, string? replyCode)
    {
        using var account = (format == "email" ? ByEmail() : OnLinkedIn())
            .FailsTheNextSend("outcome.unknown", 3, status: status, replyCode: replyCode, detail: Reason, went: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.False(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!, packageSettlesByKey: true));
        Assert.Contains(Reason, error.Message, StringComparison.Ordinal);
        Assert.Single(account.Sent);
        Assert.Single(account.KeyedArrivals);
    }

    /// <summary>
    /// A send that ended with no answer of Reply's about it — the CLI's lost answer with no status, one at 500 or above, the
    /// key still held at 409 or a 429 refused before the key was touched — may have gone, so it is repeated under the same
    /// key, and the repeat is the send where the first did not go: once. A send that failed with Reply's own 500 left no
    /// record, and a 500 or above from in front of Reply left the key as the request did.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", null)]
    [InlineData("linkedin_message", 502)]
    [InlineData("linkedin_message", 429)]
    [InlineData("email", null)]
    [InlineData("email", 500)]
    [InlineData("email", 409)]
    public async Task A_send_whose_answer_was_lost_is_repeated_under_its_key_and_sent(string format, int? status)
    {
        using var account = (format == "email" ? ByEmail() : OnLinkedIn()).FailsTheNextSend("outcome.unknown", 3, status: status);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));
        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(format, pinned: "1001"), attempt: 2, unsettledSince: Unsettled))["items"]![0]!;

        Assert.Equal("sent", (string?)item["status"]);
        Assert.Single(account.Sent);
        Assert.All(account.KeyedArrivals, arrival => Assert.Equal(Key, (string?)arrival!["key"]));
        Assert.Equal(2, account.KeyedArrivals.Count);
    }
}
