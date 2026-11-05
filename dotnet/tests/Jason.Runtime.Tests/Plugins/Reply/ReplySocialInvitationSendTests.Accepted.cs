using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What Reply took, and the work item's own key, for an invitation as for a message: the invitation is made under the
/// work item's idempotency key, a moment still ahead goes as <c>--send-after</c> with nothing read before it for it, and
/// the result says what Reply said of the invitation: <c>sent</c> or <c>accepted</c> by Reply's state, the moment
/// it holds it for, its reference and whether it can still be cancelled.
/// </summary>
public partial class ReplySocialInvitationSendTests
{
    private const string Key = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK";

    [Fact]
    public async Task An_invitation_for_a_moment_ahead_is_held_for_it_and_can_still_be_cancelled()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, WithMoment(Input(pinned: "1001"), "2099-11-03T16:30:00+02:00"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(("accepted", "2099-11-03T14:30:00Z", "linkedin-scheduled-1001", true), ((string?)item["status"], (string?)item["accepted_for"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["invitation"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"linkedin-scheduled-1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
        Assert.Equal(("2099-11-03T16:30:00+02:00", Key), (account.Calls[^1].Option("--send-after"), account.Calls[^1].Option("--idempotency-key")));
        Assert.All(account.Calls.SkipLast(1), call => Assert.Null(call.Option("--idempotency-key")));
        Assert.Equal("queued", (string?)Assert.Single(account.DirectSends)!["state"]);
    }

    /// <summary>
    /// An invitation held for a moment ahead, which Reply answers with no activity and no moment and names by a
    /// <c>linkedin-scheduled-</c> reference for its whole life, is accepted under that reference as given, with no
    /// identifier yet. The stand-in prints the answer as reply-cli 0.17.2 does (reply-cli's
    /// <c>src/operations/direct-send.ts</c>, <c>result_of</c>: a null <c>linkedInActivityId</c> is <c>activity_id</c> null).
    /// </summary>
    [Fact]
    public async Task A_deferred_invitation_is_accepted_under_the_reference_reply_gives_it_with_no_identifier_yet()
    {
        using var account = Planted();
        account.Returns("contact linkedin-connect", new JsonObject
        {
            ["status"] = "sent", ["contact_id"] = 1001, ["contact_created"] = false, ["activity_id"] = null, ["sent_at"] = null,
            ["reference"] = "linkedin-scheduled-4021", ["send_after"] = "2099-11-03T14:30:00Z", ["state"] = "queued",
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, WithMoment(Input(pinned: "1001"), "2099-11-03T16:30:00+02:00"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(("accepted", "2099-11-03T14:30:00Z", "linkedin-scheduled-4021", true), ((string?)item["status"], (string?)item["accepted_for"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["invitation"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"linkedin-scheduled-4021"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
    }

    /// <summary>
    /// A moment this package cannot read fails final, its message naming the argument; nothing is read or sent, neither the
    /// sender nor the invitation an earlier attempt returned.
    /// </summary>
    [Theory]
    [InlineData("2099-12-31T23:59:60Z", null, null)]
    [InlineData("2099-11-03T25:00:00Z", null, null)]
    [InlineData("2099-12-31T23:59:60Z", "35", null)]
    [InlineData("2099-12-31T23:59:60Z", null, "linkedin-1001")]
    public async Task A_moment_this_package_cannot_read_fails_final_and_nothing_is_sent(string moment, string? sender, string? reference)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            Operation,
            await ReplyOperations.InvokeAsync(
                api, Operation, WithMoment(Input(pinned: "1001", sender: sender ?? "34"), moment), Ct,
                attempt: reference is null ? 1 : 2, unsettledSince: reference is null ? null : DateTimeOffset.UtcNow.AddHours(-1),
                unsettledReference: reference, claimRefuses: ["/args/send_after"]));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("`send_after`", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(moment, error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task A_moment_already_past_goes_now_with_no_moment_and_no_read_first()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, WithMoment(Input(pinned: "1001"), "2020-01-01T00:00:00Z")))["items"]![0]!;

        Assert.Null(item["accepted_for"]);
        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
        Assert.Null(account.Calls[^1].Option("--send-after"));
    }

    /// <summary>
    /// Reply's direct-send routes are on every account, so nothing is read before an invitation to learn whether Reply
    /// keeps them: one with a moment ahead and a repeat after an attempt that may have sent each go with no
    /// <c>direct-send</c> read before them.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nothing_is_read_before_an_invitation_to_learn_whether_reply_keeps_direct_sends(bool repeat)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = repeat
            ? await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: Unsettled)
            : await InvokeAsync(api, WithMoment(Input(pinned: "1001"), "2099-11-03T14:30:00Z"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
        Assert.DoesNotContain(account.Calls, call => call.Line.StartsWith("direct-send get", StringComparison.Ordinal));
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// A moment is not dropped: where Reply answers the invitation with a 404 that carries no code of Reply's own, it
    /// ends <c>provider_call_failed</c>, final, with <c>reply.refused</c> and the 404 in its details, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_moment_whose_invitation_meets_a_404_without_reply_s_code_is_a_provider_call_failed()
    {
        using var account = Planted();
        account.Refuses("contact linkedin-connect", "reply.refused", status: 404);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, WithMoment(Input(pinned: "1001"), "2099-11-03T14:30:00Z")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(("reply.refused", 404), ((string?)error.Details!["cli_code"], (int?)error.Details["status"]));
        Assert.Null(error.Details["provider_code"]);
        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
        Assert.Equal("2099-11-03T14:30:00Z", account.Calls[^1].Option("--send-after"));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A repeat after an attempt that may have sent goes under the key with nothing read first; where Reply answers it with
    /// a 404 that carries no code of Reply's own, the repeat cannot tell whether the earlier attempt went, so it is not
    /// settled, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_repeat_whose_invitation_meets_a_404_without_reply_s_code_is_not_settled_and_nothing_is_sent()
    {
        using var account = Planted();
        account.Refuses("contact linkedin-connect", "reply.refused", status: 404);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: Unsettled));

        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(("reply.refused", 404), ((string?)error.Details!["cli_code"], (int?)error.Details["status"]));
        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
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
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo));

        if (repeated)
        {
            Assert.Equal("sent", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["status"]);
            Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("send_not_settled", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("23 hours", error.Message, StringComparison.Ordinal);
        Assert.Equal(["account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>A later attempt after attempts that all settled is a first send again: nothing is read before it.</summary>
    [Fact]
    public async Task A_later_attempt_after_settled_ones_reads_nothing_first()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 3));

        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
        Assert.Single(account.Sent);
    }

    [Fact]
    public async Task An_invitation_while_the_first_under_its_key_is_still_being_finished_is_transient()
    {
        using var account = Planted().WithKeyInUse(Key);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2, unsettledSince: Unsettled));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Contains("the first attempt under this key is still being finished", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply holds the key for another request, and says what it holds under it: that is the work item's invitation, accepted.</summary>
    [Fact]
    public async Task A_key_reply_holds_for_another_request_answers_the_first_invitation_accepted()
    {
        using var account = Planted();
        account.Refuses(
            "contact linkedin-connect", "idempotency_key.reused", status: 409, replyCode: "directLinkedIn.idempotencyKeyReused",
            ids: new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 },
            details: new JsonObject { ["reference"] = "linkedin-1007", ["state"] = "queued", ["send_after"] = "2099-11-03T14:30:00Z" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001", note: null), attempt: 2, unsettledSince: Unsettled);

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(("accepted", "linkedin-1007", "2099-11-03T14:30:00Z", true), ((string?)item["status"], (string?)item["reference"], (string?)item["accepted_for"], (bool)item["cancelable"]!));
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["invitation"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"linkedin-1007"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// An invitation reads nothing of the provider's own object: one that names a sending schedule makes the same calls
    /// as one without it, with no <c>--schedule-id</c>, and answers with the same result.
    /// </summary>
    [Fact]
    public async Task An_invitation_carrying_the_provider_object_runs_as_one_without_it()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var with = Input(pinned: "1001");
        with["args"]!["provider"] = new JsonObject { ["schedule_id"] = "4021" };

        var plain = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));
        var mark = account.Mark();
        var slotted = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, with));

        Assert.Equal(account.Calls.Take(mark).Select(call => call.Args), account.CallsSince(mark).Select(call => call.Args));
        Assert.All(account.Calls, call => Assert.Null(call.Option("--schedule-id")));
        Assert.Equal(plain.ToJsonString(), slotted.ToJsonString());
    }

    /// <summary>An earlier attempt that may have sent, claimed a few minutes ago: well inside the window a repeat goes under the key in.</summary>
    private static DateTimeOffset Unsettled => DateTimeOffset.UtcNow.AddMinutes(-5);

    private static JsonObject WithMoment(JsonObject input, string moment)
    {
        input["args"]!["send_after"] = moment;
        return input;
    }
}
