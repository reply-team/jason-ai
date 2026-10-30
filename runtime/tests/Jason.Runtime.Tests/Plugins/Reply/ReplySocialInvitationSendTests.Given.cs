using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What the result says, as for a message: its status is what happened, by Reply's state, and its fields are what
/// reply-cli gave. An answer that gives no state and names no send is <c>sent</c> alone, a state Reply adds later is
/// one it still holds the invitation in, and a person who is a connection already was sent nothing, so none of the
/// three comes back.
/// </summary>
public partial class ReplySocialInvitationSendTests
{
    private static readonly string[] Taken = ["accepted_for", "reference", "cancelable"];

    [Fact]
    public async Task An_invitation_answered_with_none_of_the_three_is_sent_alone()
    {
        using var account = Planted();
        account.Returns("contact linkedin-connect", new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["activity_id"] = 5, ["sent_at"] = null });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal("sent", (string?)item["status"]);
        Assert.Equal("", string.Join(",", Taken.Where(item.ContainsKey)));
        Assert.Equal("""{"external_id":"5","sent_at":null}""", item["invitation"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"reference":"linkedin-1001","state":"sent"}""", "sent", "reference,cancelable", false)]
    [InlineData("""{"reference":"linkedin-1001","state":"queued","send_after":"2099-11-03T14:30:00Z"}""", "accepted", "accepted_for,reference,cancelable", true)]
    [InlineData("""{"reference":"linkedin-1001","state":"sending"}""", "accepted", "reference,cancelable", false)]
    [InlineData("""{"reference":"linkedin-1001","state":null,"send_after":null}""", "accepted", "reference", null)]
    [InlineData("""{"reference":"linkedin-1001","state":"waitingForSchedule","estimated_send_at":"2099-11-03T14:30:00Z"}""", "accepted", "reference,cancelable", true)]
    [InlineData("""{"reference":"linkedin-1001","state":"heldForReview"}""", "accepted", "reference,cancelable", true)]
    public async Task The_status_is_what_happened_and_each_field_comes_back_only_where_reply_cli_gave_it(
        string given, string status, string expected, bool? cancelable)
    {
        var answer = new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["activity_id"] = null, ["sent_at"] = null };
        foreach (var (name, value) in JsonNode.Parse(given)!.AsObject())
        {
            answer[name] = value?.DeepClone();
        }

        using var account = Planted();
        account.Returns("contact linkedin-connect", answer);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")))["items"]![0]!.AsObject();

        Assert.Equal(status, (string?)item["status"]);
        Assert.Equal(expected, string.Join(",", Taken.Where(item.ContainsKey)));
        Assert.Equal(cancelable, (bool?)item["cancelable"]);
        Assert.Equal("""{"contact":"1001","send":"linkedin-1001"}""", item["external_ids"]!.ToJsonString());
    }

    /// <summary>
    /// An answer of another shape may hide an invitation that went, so it is the lost answer; a reference it names in the
    /// shape the document takes is still kept on the attempt, for the next one to read first, whatever field was refused.
    /// </summary>
    [Theory]
    [InlineData("""{"status":"sent","contact_id":1001,"activity_id":null,"sent_at":null,"reference":"linkedin-1001","send_after":"next week","state":"queued"}""", "linkedin-1001")]
    [InlineData("""{"status":"sent","contact_id":1001,"activity_id":null,"sent_at":null,"reference":"linkedin-1001","state":7}""", "linkedin-1001")]
    [InlineData("""{"status":"sent","contact_id":1001,"activity_id":null,"sent_at":null,"reference":17,"state":"queued"}""", null)]
    public async Task An_invitation_answered_in_another_shape_is_lost_and_keeps_a_reference_reply_named(string answer, string? kept)
    {
        using var account = Planted();
        account.Returns("contact linkedin-connect", JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal(kept is null ? """{"contact":"1001"}""" : $$"""{"contact":"1001","send":"{{kept}}"}""", error.ExternalIds!.ToJsonString());
    }

    /// <summary>An answer that took the invitation and says it failed, or was cancelled, is final, as for a message.</summary>
    [Theory]
    [InlineData("failed", "says it did not go")]
    [InlineData("cancelled", "was cancelled at Reply")]
    public async Task An_invitation_reply_answers_as_failed_or_cancelled_is_final(string state, string said)
    {
        using var account = Planted();
        account.Returns("contact linkedin-connect", new JsonObject
        {
            ["status"] = "sent", ["contact_id"] = 1001, ["activity_id"] = null, ["sent_at"] = null, ["reference"] = "linkedin-1001", ["state"] = state,
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.Equal("""{"contact":"1001","send":"linkedin-1001"}""", error.ExternalIds!.ToJsonString());
    }
}
