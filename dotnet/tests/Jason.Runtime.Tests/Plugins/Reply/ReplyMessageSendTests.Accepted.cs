using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What Reply took, and the work item's own key. The send is made under the work item's idempotency key, and no other
/// command of the attempt carries one; a moment still ahead goes as <c>--send-after</c>, with nothing read before the
/// send for it. The result says what Reply said of the message: <c>sent</c> or <c>accepted</c> by Reply's
/// state, the moment it holds it for, its reference and whether it can still be cancelled.
/// </summary>
public partial class ReplyMessageSendTests
{
    private const string Key = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK";

    [Fact]
    public async Task A_message_reply_sent_comes_back_with_its_reference_and_only_the_send_carries_the_key()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal(("sent", "email-1001", false), ((string?)item["status"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.Null(item["accepted_for"]);
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", item["external_ids"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());

        // A first attempt that names no moment reads nothing more than it always read.
        Assert.Equal(["account list", "contact find", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal(Key, account.Calls[^1].Option("--idempotency-key"));
        Assert.All(account.Calls.SkipLast(1), call => Assert.Null(call.Option("--idempotency-key")));
        Assert.Null(account.Calls[^1].Option("--send-after"));
    }

    /// <summary>
    /// A moment still ahead goes as written, on every channel, and Reply holds the message for it.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("linkedin_message")]
    public async Task A_message_for_a_moment_ahead_is_held_for_it_and_can_still_be_cancelled(string format)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithMoment(Input(format, pinned: "1001"), "2099-11-03T09:00:00-05:00")))["items"]![0]!;

        Assert.Equal(("accepted", "2099-11-03T14:00:00Z", true), ((string?)item["status"], (string?)item["accepted_for"], (bool)item["cancelable"]!));
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001", FormatCommand(format)], Lines(account));
        Assert.Equal("2099-11-03T09:00:00-05:00", account.Calls[^1].Option("--send-after"));
        Assert.Equal("queued", (string?)Assert.Single(account.DirectSends)!["state"]);
    }

    /// <summary>
    /// Every form of a moment the document admits is one this package reads: a fraction of one to three digits, and
    /// <c>Z</c> or an offset either way. The moment goes as written.
    /// </summary>
    [Theory]
    [InlineData("2099-11-03T14:00:00Z")]
    [InlineData("2099-11-03T14:00:00.5Z")]
    [InlineData("2099-11-03T14:00:00.25+02:00")]
    [InlineData("2099-11-03T14:00:00.125-05:30")]
    public async Task A_moment_in_every_form_the_document_admits_is_read_and_goes_as_written(string moment)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithMoment(Input("email", pinned: "1001"), moment)))["items"]![0]!;

        Assert.Equal("accepted", (string?)item["status"]);
        Assert.Equal(moment, account.Calls[^1].Option("--send-after"));
    }

    /// <summary>
    /// A moment this package cannot read is not one already past: the message would go now where the person approving read
    /// a later moment, so it fails final, its message naming the argument, and nothing is read or sent: not the sender,
    /// so a sender Reply does not hold is not the answer, and not the send an earlier attempt returned. The claim refuses
    /// both forms before a plugin runs; this is the package's own floor beneath that.
    /// </summary>
    [Theory]
    [InlineData("2099-12-31T23:59:60Z", null, null)]
    [InlineData("2099-11-03T25:00:00Z", null, null)]
    [InlineData("2099-12-31T23:59:60Z", "13", null)]
    [InlineData("2099-12-31T23:59:60Z", null, "email-1001")]
    public async Task A_moment_this_package_cannot_read_fails_final_and_nothing_is_sent(string moment, string? sender, string? reference)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await ReplyOperations.InvokeAsync(
                api, Operation, WithMoment(Input("email", pinned: "1001", sender: sender), moment), Ct,
                attempt: reference is null ? 1 : 2, unsettledSince: reference is null ? null : DateTimeOffset.UtcNow.AddHours(-1),
                unsettledReference: reference, claimRefuses: ["/args/send_after"]));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("`send_after`", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(moment, error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>A moment already past is no moment: the message goes as one that names none, and nothing is read first.</summary>
    [Fact]
    public async Task A_moment_already_past_goes_now_with_no_moment_and_no_read_first()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithMoment(Input("email", pinned: "1001"), "2020-01-01T00:00:00Z")))["items"]![0]!;

        Assert.Null(item["accepted_for"]);
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Null(account.Calls[^1].Option("--send-after"));
    }

    /// <summary>
    /// Reply's direct-send routes are on every account, so nothing is read before a send to learn whether Reply keeps
    /// them: a send with a moment ahead, one timed by a schedule and a repeat after an attempt that may have sent each go
    /// with no <c>direct-send</c> read before them, and the package names no reference to read for it.
    /// </summary>
    [Theory]
    [InlineData("moment")]
    [InlineData("schedule")]
    [InlineData("repeat")]
    public async Task Nothing_is_read_before_a_send_to_learn_whether_reply_keeps_direct_sends(string send)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = send switch
        {
            "moment" => WithMoment(Input("email", pinned: "1001"), "2099-11-03T14:00:00Z"),
            "schedule" => WithSchedule(Input("email", pinned: "1001")),
            _ => Input("email", pinned: "1001"),
        };
        var result = send == "repeat"
            ? await InvokeAsync(api, input, attempt: 2, unsettledSince: Unsettled)
            : await InvokeAsync(api, input);

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.DoesNotContain(account.Calls, call => call.Line.StartsWith("direct-send get", StringComparison.Ordinal));
        Assert.Single(account.Sent);
        var module = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "modules", "send.js"));
        Assert.DoesNotContain("email-1\"", module, StringComparison.Ordinal);
        Assert.DoesNotContain("offered", module, StringComparison.Ordinal);
    }

    /// <summary>
    /// The window a repeat goes under the key in is one constant of the package, in hours, and the README states it as
    /// the package holds it.
    /// </summary>
    [Fact]
    public void The_window_a_repeat_goes_under_the_key_in_is_one_constant_the_readme_states()
    {
        var module = File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "modules", "send.js"));
        var readme = Regex.Replace(File.ReadAllText(Path.Combine(ReplyPlugins.PackageSource, "README.md")), @"\s+", " ");

        var named = Assert.Single(Regex.Matches(module, @"^const KEY_WINDOW_MS = (\d+) \* 60 \* 60 \* 1000;$", RegexOptions.Multiline));
        Assert.Equal("23", named.Groups[1].Value);
        Assert.Contains($"only within {named.Groups[1].Value} hours of that moment", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// A repeat after an attempt that may have sent goes under the key with nothing read first; where Reply answers its
    /// send with a 404 that carries no code of Reply's own, the repeat cannot tell whether the earlier attempt went, so it
    /// fails <c>send_not_settled</c>, final, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_repeat_whose_send_meets_a_404_without_reply_s_code_is_not_settled_and_nothing_is_sent()
    {
        using var account = ByEmail().NotOffering("send_direct_email");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: Unsettled));

        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(("reply.refused", 404), ((string?)error.Details!["cli_code"], (int?)error.Details["status"]));
        Assert.Null(error.Details["provider_code"]);
        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The key settles a repeat only while Reply still keeps it: inside the package's window from the moment the earliest
    /// attempt that may have sent was claimed, the repeat goes under the key; past it, it is not settled, nothing is read
    /// and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData(0.5, true)]
    [InlineData(22.5, true)]
    [InlineData(23, false)]
    [InlineData(72, false)]
    public async Task A_repeat_goes_under_the_key_only_inside_the_window_from_the_earliest_unsettled_attempt(double hoursAgo, bool repeated)
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo));

        if (repeated)
        {
            Assert.Equal("sent", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["status"]);
            Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
            Assert.Equal(Key, account.Calls[^1].Option("--idempotency-key"));
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("23 hours", error.Message, StringComparison.Ordinal);
        Assert.Equal(["account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A later attempt after attempts that all settled — each ended in an answer of Reply's that sent nothing — is a first
    /// send again: nothing is read before it.
    /// </summary>
    [Fact]
    public async Task A_later_attempt_after_settled_ones_reads_nothing_first()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 3));

        Assert.Equal(["account list", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Single(account.Sent);
    }

    /// <summary>A repeat while Reply is still finishing the first send under the key sent nothing, and is tried again.</summary>
    [Fact]
    public async Task A_send_while_the_first_under_its_key_is_still_being_finished_is_transient()
    {
        using var account = ByEmail().WithKeyInUse(Key);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: Unsettled));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Contains("the first attempt under this key is still being finished", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply holds the key for another request — the repeat names the person or the moment otherwise than the first did
    /// — and says what it holds under it: the send the first attempt made. That is the work item's send, and nothing goes
    /// twice. Reply's stored state says what happened to it, a send it names with no state being accepted; a moment Reply
    /// does not say is none, whatever the arguments name.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1007","state":"queued","send_after":"2099-11-03T14:00:00Z"}""", "accepted", "2099-11-03T14:00:00Z", true)]
    [InlineData("""{"reference":"email-1007","state":"sent"}""", "sent", null, false)]
    [InlineData("""{"reference":"email-1007"}""", "accepted", null, null)]
    [InlineData("""{"reference":"email-1007","state":"waitingForSchedule","estimated_send_at":"2099-11-03T14:00:00Z"}""", "accepted", null, true)]
    public async Task A_key_reply_holds_for_another_request_answers_the_first_send(string details, string status, string? acceptedFor, bool? cancelable)
    {
        using var account = ByEmail();
        account.Refuses(
            "contact send-email", "idempotency_key.reused", status: 409, replyCode: "contact.idempotencyKeyReused",
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }, details: JsonNode.Parse(details)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, WithMoment(Input("email", pinned: "1001"), "2099-11-03T09:00:00-05:00"), attempt: 2, unsettledSince: Unsettled);

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal((status, "email-1007", acceptedFor, cancelable), ((string?)item["status"], (string?)item["reference"], (string?)item["accepted_for"], (bool?)item["cancelable"]));
        Assert.Equal(acceptedFor is not null, item.AsObject().ContainsKey("accepted_for"));
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"email-1007"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A key Reply holds for another request, under which the send it holds failed or was cancelled, says what became of
    /// the work item's send: that is final, with Reply's reason where it gave one, and nothing goes.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1007","state":"failed","failure_reason":"The mailbox refused the address."}""", "says it did not go", "The mailbox refused the address.")]
    [InlineData("""{"reference":"email-1007","state":"cancelled"}""", "was cancelled at Reply", null)]
    public async Task A_key_reply_holds_for_a_send_that_failed_or_was_cancelled_is_final(string details, string said, string? reason)
    {
        using var account = ByEmail();
        account.Refuses(
            "contact send-email", "idempotency_key.reused", status: 409, replyCode: "contact.idempotencyKeyReused",
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }, details: JsonNode.Parse(details)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001"), attempt: 2, unsettledSince: Unsettled));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal(reason is not null, error.Message.EndsWith(" Reply said: " + reason, StringComparison.Ordinal));
        Assert.Equal("""{"contact":"1001","send":"email-1007"}""", error.ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>A reference or a moment of another shape is no answer, and may come from a message that went.</summary>
    [Theory]
    [InlineData("""{"status":"sent","contact_id":1001,"message_id":null,"sent_at":null,"reply_status":"sent","reference":17,"state":"sent"}""", null)]
    [InlineData("""{"status":"sent","contact_id":1001,"message_id":null,"sent_at":null,"reply_status":"sent","reference":"","state":"sent"}""", null)]
    [InlineData("""{"status":"sent","contact_id":1001,"message_id":null,"sent_at":null,"reply_status":"sent","reference":"email-1","send_after":"next week","state":"queued"}""", "email-1")]
    [InlineData("""{"status":"sent","contact_id":1001,"message_id":null,"sent_at":null,"reply_status":"sent","reference":"email-1","state":7}""", "email-1")]
    public async Task A_reference_or_a_moment_of_another_shape_may_hide_a_message_that_went(string answer, string? kept)
    {
        using var account = ByEmail();
        account.Returns("contact send-email", JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal(kept is null ? """{"contact":"1001"}""" : $$"""{"contact":"1001","send":"{{kept}}"}""", error.ExternalIds!.ToJsonString());
    }

    /// <summary>An earlier attempt that may have sent, claimed a few minutes ago: well inside the window a repeat goes under the key in.</summary>
    private static DateTimeOffset Unsettled => DateTimeOffset.UtcNow.AddMinutes(-5);

    private static JsonObject WithMoment(JsonObject input, string moment)
    {
        input["args"]!["send_after"] = moment;
        return input;
    }
}
