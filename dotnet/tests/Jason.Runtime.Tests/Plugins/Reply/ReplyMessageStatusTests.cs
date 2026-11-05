using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>message.status</c> as the official package performs it: one direct send, read by <c>reply direct-send get</c> of
/// the reference the runtime composed under <c>send</c>. Reply's state is told in the document's five words — a state
/// that still holds the send, whatever its name, is <c>queued</c>, and a send Reply lost track of while it was going out
/// is <c>unknown</c> — and only a queued send Reply is not already sending is
/// one a cancel can still stop.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyMessageStatusTests
{
    private const string Operation = "message.status";

    internal const string SendItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG";

    internal const string Reference = "email-1001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Each of Reply's states, in the document's word for it, with the moments and the message Reply gives.</summary>
    [Theory]
    [InlineData("queued", "queued", true)]
    [InlineData("waitingForSchedule", "queued", true)]
    [InlineData("heldForReview", "queued", true)]
    [InlineData("sending", "queued", false)]
    [InlineData("sent", "sent", false)]
    [InlineData("cancelled", "cancelled", false)]
    [InlineData("failed", "failed", false)]
    public async Task Reply_s_state_is_told_in_the_document_s_words(string held, string state, bool cancelable)
    {
        using var account = new ReplyAccount().WithDirectSend(Reference, held, sendAfter: "2099-11-03T14:00:00Z");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct);

        Assert.Equal(
            $$"""{"state":"{{state}}","channel":"email","cancelable":{{(cancelable ? "true" : "false")}},"accepted_for":"2099-11-03T14:00:00Z","sent_at":null,"message_id":null,"rendered_subject":null,"rendered_body":null,"failure_reason":null,"failure_code":null,"cancelled_at":null}""",
            ReplyOperations.Succeeded(Operation, result).ToJsonString());
        Assert.Null(Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds);
        Assert.Equal([$"direct-send get {Reference}"], account.Calls.Select(call => call.Line));
        Assert.Equal(["--json", "-q", "direct-send", "get", Reference], account.Calls[0].Args);
    }

    /// <summary>
    /// A send that went says when, in UTC, and the message it became, with the text as it went where Reply kept that copy;
    /// one that failed says Reply's own sentence and code for why, as Reply gives them; one cancelled says when.
    /// </summary>
    [Fact]
    public async Task A_send_that_went_failed_or_was_cancelled_says_what_reply_holds_of_it()
    {
        using var account = new ReplyAccount()
            .WithDirectSend("email-1001", "sent", sentAt: "2026-11-03T09:00:12-05:00", messageId: "<msg-7001@example.com>", renderedSubject: "Hello", renderedBody: "<p>Hi Marta</p>")
            .WithDirectSend("email-1002", "failed", failureReason: "The send's window closed before it went.", failureCode: "directSend.dispatchWindowMissed")
            .WithDirectSend("linkedin-1003", "cancelled", sendAfter: "2099-11-03T14:00:00Z", cancelledAt: "2026-11-02T09:15:00Z");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var sent = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("email-1001"), Ct));
        var failed = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("email-1002"), Ct));
        var cancelled = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("linkedin-1003"), Ct));

        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse("""{"state":"sent","channel":"email","cancelable":false,"accepted_for":null,"sent_at":"2026-11-03T14:00:12Z","message_id":"<msg-7001@example.com>","rendered_subject":"Hello","rendered_body":"<p>Hi Marta</p>","failure_reason":null,"failure_code":null,"cancelled_at":null}"""),
                sent),
            sent.ToJsonString());
        Assert.Equal(
            ("failed", "The send's window closed before it went.", "directSend.dispatchWindowMissed"),
            ((string?)failed["state"], (string?)failed["failure_reason"], (string?)failed["failure_code"]));
        Assert.Equal(
            ("cancelled", "linkedin", "2026-11-02T09:15:00Z", "2099-11-03T14:00:00Z"),
            ((string?)cancelled["state"], (string?)cancelled["channel"], (string?)cancelled["cancelled_at"], (string?)cancelled["accepted_for"]));
    }

    /// <summary>
    /// A deferred LinkedIn send, read by the <c>linkedin-scheduled-</c> reference it keeps for its whole life, as Reply reads
    /// it once it has handed it to the account's queue, and once it failed: <c>sending</c> still holds the send and no cancel can stop it; a send whose window passed before it could go
    /// (<c>directLinkedIn.missedWindow</c>), and one whose account's allowance stayed spent until its window passed — a
    /// block code as the send's fate, not a refusal when Reply took it — is <c>failed</c>, nothing having gone, with Reply's
    /// code carried as given and never <c>unknown</c>. So is every other <c>directLinkedIn.</c> code Reply gives a send's
    /// fate once it times deferred LinkedIn sends itself, <c>directLinkedIn.unknown</c> among them, and one this version
    /// has never seen, <c>directLinkedIn.contactNotFound</c>; and a failure after Reply handed the send on, which carries
    /// no code at all and is never a malformed answer. The stand-in prints each record as reply-cli 0.18.0 does (reply-cli's
    /// <c>src/operations/direct-sends.ts</c>:54): the state as Reply gives it, and <c>failure_code</c> beside it.
    /// </summary>
    [Theory]
    [InlineData("sending", null, "queued")]
    [InlineData("failed", "directLinkedIn.missedWindow", "failed")]
    [InlineData("failed", "directLinkedIn.weeklyLimitReached", "failed")]
    [InlineData("failed", "directLinkedIn.dailyLimitReached", "failed")]
    [InlineData("failed", null, "failed")]
    [InlineData("failed", "directLinkedIn.featureDisabled", "failed")]
    [InlineData("failed", "directLinkedIn.unknown", "failed")]
    [InlineData("failed", "directLinkedIn.noLinkedInProfile", "failed")]
    [InlineData("failed", "directLinkedIn.linkedInAccountNotFound", "failed")]
    [InlineData("failed", "directLinkedIn.linkedInAccountInactive", "failed")]
    [InlineData("failed", "directLinkedIn.invalidCookie", "failed")]
    [InlineData("failed", "directLinkedIn.notConnected", "failed")]
    [InlineData("failed", "directLinkedIn.scheduleNotFound", "failed")]
    [InlineData("failed", "directLinkedIn.contactTimeZoneMissing", "failed")]
    [InlineData("failed", "directLinkedIn.contactNotFound", "failed")]
    public async Task A_linkedin_send_reply_is_sending_or_that_failed_at_its_window_is_told_as_reply_holds_it(string held, string? code, string state)
    {
        using var account = new ReplyAccount().WithDirectSend("linkedin-scheduled-1001", held, sendAfter: "2099-11-03T14:00:00Z", failureCode: code);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("linkedin-scheduled-1001"), Ct));

        Assert.Equal(
            (state, "linkedin", false, null, null, code),
            ((string?)read["state"], (string?)read["channel"], (bool)read["cancelable"]!, (string?)read["sent_at"], (string?)read["message_id"], (string?)read["failure_code"]));
    }

    /// <summary>
    /// A LinkedIn send Reply holds until its schedule's next opening, once Reply times deferred LinkedIn sends itself, is
    /// <c>waitingForSchedule</c> with no moment of its own, under its <c>linkedin-scheduled-</c> reference: <c>queued</c>,
    /// still cancelable, and naming no moment.
    /// </summary>
    [Fact]
    public async Task A_linkedin_send_waiting_for_its_schedule_is_queued_and_still_cancelable()
    {
        using var account = new ReplyAccount().WithDirectSend("linkedin-scheduled-1001", "waitingForSchedule");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("linkedin-scheduled-1001"), Ct));

        Assert.Equal(
            ("queued", "linkedin", true, null),
            ((string?)read["state"], (string?)read["channel"], (bool)read["cancelable"]!, (string?)read["accepted_for"]));
    }

    /// <summary>
    /// A send waiting for its schedule is read by reply-cli 0.18.0 with the moment Reply expects to send it,
    /// <c>estimated_send_at</c>. An estimate is not a moment Reply holds the send for, so the send is <c>queued</c>, still
    /// cancelable, and names no moment, an email and a LinkedIn send alike.
    /// </summary>
    [Theory]
    [InlineData("email-1001", "email")]
    [InlineData("linkedin-scheduled-1001", "linkedin")]
    public async Task A_send_waiting_for_its_schedule_names_no_moment_from_reply_s_estimate(string reference, string channel)
    {
        using var account = new ReplyAccount().WithDirectSend(reference, "waitingForSchedule", estimatedSendAt: "2099-11-04T14:00:00Z");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(reference), Ct));

        Assert.Equal(
            ("queued", channel, true, null),
            ((string?)read["state"], (string?)read["channel"], (bool)read["cancelable"]!, (string?)read["accepted_for"]));
        Assert.DoesNotContain("2099-11-04", read.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The channel is the reference's own word: <c>email</c> for an <c>email-</c> reference, <c>linkedin</c> for a
    /// <c>linkedin-</c> one. Reply writes a LinkedIn send's channel in its own enum, <c>linkedIn</c>, and reply-cli passes it
    /// on as given.
    /// </summary>
    [Theory]
    [InlineData("linkedin-1001", "linkedin")]
    [InlineData("email-1001", "email")]
    public async Task The_channel_is_the_reference_s_own_word(string reference, string channel)
    {
        using var account = new ReplyAccount().WithDirectSend(reference, "sent", sentAt: "2026-11-03T14:00:12Z");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(reference), Ct));

        Assert.Equal(("sent", channel), ((string?)read["state"], (string?)read["channel"]));
    }

    /// <summary>Reply's own channel field is never read for the channel told: whatever it writes there, or none, the reference says it.</summary>
    [Theory]
    [InlineData("""{"reference":"email-1001","channel":"sms","state":"sent"}""")]
    [InlineData("""{"reference":"email-1001","state":"sent"}""")]
    public async Task Reply_s_channel_field_is_not_read_for_the_channel(string answer)
    {
        using var account = new ReplyAccount();
        account.Returns("direct-send get", JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("sent", "email"), ((string?)read["state"], (string?)read["channel"]));
    }

    /// <summary>
    /// The send read is the one the runtime named under <c>send.reference</c>, and nothing the caller wrote: arguments that
    /// named another reference would have been refused at the claim, so the package never reads them for one.
    /// </summary>
    [Fact]
    public async Task The_reference_is_the_runtime_s_never_the_caller_s()
    {
        using var account = new ReplyAccount().WithDirectSend("email-1001", "queued").WithDirectSend("email-1002", "sent");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var input = Input("email-1002");
        input["args"]!["reference"] = "email-1001";

        var result = ReplyOperations.Succeeded(
            Operation, await ReplyOperations.InvokeAsync(api, Operation, input, Ct, claimRefuses: ["/args/reference"]));

        Assert.Equal("sent", (string?)result["state"]);
        Assert.Equal(["direct-send get email-1002"], account.Calls.Select(call => call.Line));
    }

    /// <summary>
    /// A reference Reply holds no send under, or not one this account may see, is <c>send_not_found</c>, permanent; so is
    /// one reply-cli refuses to read as a reference at all. A 404 with no code of Reply's own says Reply's API changed under
    /// this package version, and is <c>provider_call_failed</c>, permanent.
    /// </summary>
    [Theory]
    [InlineData("missing", "send_not_found")]
    [InlineData("not owned", "send_not_found")]
    [InlineData("not a reference", "send_not_found")]
    [InlineData("bare 404", "provider_call_failed")]
    public async Task A_send_reply_does_not_hold_for_this_account_is_not_found(string why, string code)
    {
        using var account = new ReplyAccount();
        var reference = why == "not a reference" ? "sms-1001" : Reference;
        if (why == "not owned")
        {
            account.Refuses("direct-send get", "direct_send.not_owned", status: 403, replyCode: "directSend.notOwned");
        }
        else if (why == "bare 404")
        {
            account.NotOffering("direct_send");
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(reference), Ct));

        Assert.Equal((code, FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal([$"direct-send get {reference}"], account.Calls.Select(call => call.Line));
    }

    /// <summary>
    /// A LinkedIn send Reply holds for a moment ahead or for its schedule is called by its <c>linkedin-scheduled-</c>
    /// reference for its whole life, which reply-cli takes from 0.18.0: the read reaches Reply and tells the send as Reply
    /// holds it.
    /// </summary>
    [Fact]
    public async Task A_deferred_linkedin_send_is_read_by_its_scheduled_reference()
    {
        const string scheduled = "linkedin-scheduled-4021";
        using var account = new ReplyAccount().WithDirectSend(scheduled, "queued", sendAfter: "2099-11-03T14:00:00Z");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(scheduled), Ct));

        Assert.Equal(
            ("queued", "linkedin", true, "2099-11-03T14:00:00Z"),
            ((string?)read["state"], (string?)read["channel"], (bool)read["cancelable"]!, (string?)read["accepted_for"]));
        Assert.Equal([$"direct-send get {scheduled}"], account.Calls.Select(call => call.Line));
    }

    /// <summary>
    /// An answer about another send, with no state, or with a field in a shape the document does not take is no answer:
    /// a read changes nothing, so it is asked again.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1009","channel":"email","state":"sent"}""")]
    [InlineData("""{"reference":"email-1001","channel":"email"}""")]
    [InlineData("""{"reference":"email-1001","channel":"email","state":7}""")]
    [InlineData("""{"reference":"email-1001","channel":"email","state":"sent","message_id":{"id":1}}""")]
    public async Task An_answer_that_does_not_say_what_became_of_this_send_is_asked_again(string answer)
    {
        using var account = new ReplyAccount();
        account.Returns("direct-send get", JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
    }

    /// <summary>
    /// A send Reply attempted and lost track of while it was going out may have gone: it is <c>unknown</c>, final and never
    /// cancelable. Every other failure is <c>failed</c>, a state that still holds the send stays <c>queued</c> whatever
    /// code it carries, and Reply's code is given as Reply gave it, never interpreted.
    /// </summary>
    [Theory]
    [InlineData("failed", "directSend.dispatchInterrupted", "unknown")]
    [InlineData("failed", "directSend.dispatchWindowMissed", "failed")]
    [InlineData("failed", "directSend.somethingLater", "failed")]
    [InlineData("failed", null, "failed")]
    [InlineData("sent", "directSend.dispatchInterrupted", "sent")]
    [InlineData("queued", "directSend.dispatchInterrupted", "queued")]
    public async Task A_send_reply_lost_track_of_while_it_was_going_out_is_unknown(string held, string? code, string state)
    {
        using var account = new ReplyAccount().WithDirectSend(Reference, held, failureReason: "Reply's sentence.", failureCode: code);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(
            (state, state == "queued", "Reply's sentence.", code),
            ((string?)read["state"], (bool)read["cancelable"]!, (string?)read["failure_reason"], (string?)read["failure_code"]));
    }

    /// <summary>A code past the document's bound, or one not written as text, is none given rather than cut.</summary>
    [Theory]
    [InlineData(129)]
    [InlineData(0)]
    public async Task A_code_the_document_cannot_carry_whole_is_none_given(int length)
    {
        using var account = new ReplyAccount();
        account.Returns("direct-send get", new JsonObject
        {
            ["reference"] = Reference,
            ["state"] = "failed",
            ["failure_code"] = length == 0 ? JsonValue.Create(7) : new string('c', length),
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("failed", null), ((string?)read["state"], (string?)read["failure_code"]));
    }

    /// <summary>
    /// A moment that cannot be read is none given, and the read answers the state all the same: a send Reply says went
    /// went, whatever it wrote for when.
    /// </summary>
    [Fact]
    public async Task A_moment_that_cannot_be_read_is_none_given_and_the_state_answers()
    {
        using var account = new ReplyAccount();
        account.Returns("direct-send get", JsonNode.Parse("""{"reference":"email-1001","channel":"email","state":"sent","send_after":"soon","sent_at":"tomorrow"}""")!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("sent", null, null), ((string?)read["state"], (string?)read["accepted_for"], (string?)read["sent_at"]));
    }

    /// <summary>
    /// Reply writes a moment with two digits of a second's fraction; the read writes it in UTC, to the second, or to the
    /// millisecond where Reply names a fraction.
    /// </summary>
    [Theory]
    [InlineData("2026-11-03T14:00:12.00Z", "2026-11-03T14:00:12Z")]
    [InlineData("2026-11-03T14:00:12.37Z", "2026-11-03T14:00:12.370Z")]
    public async Task Reply_s_own_form_of_a_moment_is_written_to_the_second_or_to_the_millisecond(string written, string sentAt)
    {
        using var account = new ReplyAccount().WithDirectSend("email-1001", "sent", sendAfter: "2026-11-03T14:00:00.00Z", sentAt: written);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input(), Ct));

        Assert.Equal(("2026-11-03T14:00:00Z", sentAt), ((string?)read["accepted_for"], (string?)read["sent_at"]));
    }

    /// <summary>
    /// What Reply gives beyond what the document holds is never cut into something it did not say: a failure reason is
    /// Reply's word, cut to its bound; a rendered copy too long to carry whole, and a message identifier past its bound,
    /// are none given.
    /// </summary>
    [Fact]
    public async Task What_does_not_fit_is_cut_where_it_is_a_word_and_left_out_where_it_is_a_copy()
    {
        using var account = new ReplyAccount()
            .WithDirectSend("email-1001", "sent", messageId: new string('m', 999), renderedSubject: new string('s', 999), renderedBody: "<p>kept</p>")
            .WithDirectSend("email-1002", "failed", failureReason: new string('r', 600));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var sent = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("email-1001"), Ct));
        var failed = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("email-1002"), Ct));

        Assert.Equal((null, null, "<p>kept</p>"), ((string?)sent["message_id"], (string?)sent["rendered_subject"], (string?)sent["rendered_body"]));
        Assert.Equal(new string('r', 512), (string?)failed["failure_reason"]);
    }

    /// <summary>The input the runtime composes: the work item that made the send, and the reference it holds for it.</summary>
    internal static JsonObject Input(string reference = Reference, string operation = "message.send") => new()
    {
        ["args"] = new JsonObject { ["send"] = SendItem },
        ["send"] = new JsonObject { ["work_item"] = SendItem, ["operation"] = operation, ["reference"] = reference },
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject(),
        },
    };
}
