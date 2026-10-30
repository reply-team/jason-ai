using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What the result says is two things, read apart. Its status is what happened: <c>sent</c> where Reply's state is
/// <c>sent</c>, or where it gives no state and names no send, as a Reply that sends inside the call answers; and
/// <c>accepted</c> for any other state Reply still holds a send in, one it adds later included. Its fields are what
/// reply-cli gave: the moment Reply holds the send for, its reference and whether it can still be cancelled each come
/// back only where reply-cli gave its source.
/// </summary>
public partial class ReplyMessageSendTests
{
    private static readonly string[] Taken = ["accepted_for", "reference", "cancelable"];

    [Theory]
    [InlineData("email", """{"status":"sent","contact_id":1001,"message_id":"<60001.direct@mail.example.com>","sent_at":null,"reply_status":"sent"}""", "<60001.direct@mail.example.com>")]
    [InlineData("linkedin_message", """{"status":"sent","contact_id":1001,"activity_id":70001,"sent_at":null}""", "70001")]
    public async Task A_message_answered_with_none_of_the_three_is_sent_alone(string format, string answer, string identifier)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        account.Returns(FormatCommand(format), JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(format, pinned: "1001"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal("sent", (string?)item["status"]);
        Assert.Equal("", string.Join(",", Taken.Where(item.ContainsKey)));
        Assert.Equal(identifier, (string?)item["message"]!["external_id"]);
        Assert.Equal("""{"contact":"1001"}""", item["external_ids"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
    }

    /// <summary>
    /// Each of the three comes back where reply-cli gave its source and is absent where it did not; a null is none given.
    /// The state decides the status, and a send Reply names with no state is accepted: it was taken, and that it went is
    /// not known. A send Reply is sending can no longer be cancelled. A state Reply adds later is one it still holds the
    /// send in — accepted, and cancelable — and a moment it only estimates is read as none.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1001","state":"sent"}""", "sent", "reference,cancelable", false)]
    [InlineData("""{"reference":"email-1001","state":"queued","send_after":"2099-11-03T14:00:00Z"}""", "accepted", "accepted_for,reference,cancelable", true)]
    [InlineData("""{"reference":"email-1001","state":"sending"}""", "accepted", "reference,cancelable", false)]
    [InlineData("""{"reference":"email-1001"}""", "accepted", "reference", null)]
    [InlineData("""{"reference":"email-1001","state":null,"send_after":null}""", "accepted", "reference", null)]
    [InlineData("""{"reference":"email-1001","state":"waitingForSchedule","estimated_send_at":"2099-11-03T14:00:00Z"}""", "accepted", "reference,cancelable", true)]
    [InlineData("""{"reference":"email-1001","state":"heldForReview"}""", "accepted", "reference,cancelable", true)]
    public async Task The_status_is_what_happened_and_each_field_comes_back_only_where_reply_cli_gave_it(
        string given, string status, string expected, bool? cancelable)
    {
        using var account = ByEmail();
        account.Returns("contact send-email", Answer(given));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001")))["items"]![0]!.AsObject();

        Assert.Equal(status, (string?)item["status"]);
        Assert.Equal(expected, string.Join(",", Taken.Where(item.ContainsKey)));
        Assert.Equal(cancelable, (bool?)item["cancelable"]);
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", item["external_ids"]!.ToJsonString());
    }

    /// <summary>
    /// An answer that took the send and says it failed, or was cancelled, says what became of it: that is final, with
    /// Reply's reason where it gave one, and the send is kept as the work item's.
    /// </summary>
    [Theory]
    [InlineData("""{"reference":"email-1001","state":"failed","failure_reason":"The mailbox refused the address."}""", "says it did not go", "The mailbox refused the address.")]
    [InlineData("""{"reference":"email-1001","state":"failed"}""", "says it did not go", null)]
    [InlineData("""{"reference":"email-1001","state":"cancelled"}""", "was cancelled at Reply", null)]
    public async Task A_send_reply_answers_as_failed_or_cancelled_is_final(string given, string said, string? reason)
    {
        using var account = ByEmail();
        account.Returns("contact send-email", Answer(given));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal(reason is not null, error.Message.EndsWith(" Reply said: " + reason, StringComparison.Ordinal));
        Assert.Equal("""{"contact":"1001","send":"email-1001"}""", error.ExternalIds!.ToJsonString());
    }

    /// <summary>reply-cli's answer to a direct email that went, with the members <paramref name="given"/> names over it.</summary>
    private static JsonObject Answer(string given)
    {
        var answer = new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["message_id"] = null, ["sent_at"] = null, ["reply_status"] = "sent" };
        foreach (var (name, value) in JsonNode.Parse(given)!.AsObject())
        {
            answer[name] = value?.DeepClone();
        }

        return answer;
    }
}
