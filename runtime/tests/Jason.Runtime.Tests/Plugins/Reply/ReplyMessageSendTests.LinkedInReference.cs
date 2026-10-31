using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A LinkedIn send's reference and identifier, as Reply answers them on each path. A send Reply holds for a later moment
/// has not gone, so Reply names no activity and no moment for it until it does, and calls it by a reference of its own,
/// <c>linkedin-scheduled-&lt;n&gt;</c>, for its whole life; a send that goes inside the call keeps
/// <c>linkedin-&lt;activity&gt;</c> and the activity. The package passes either reference on as given and pins it once.
/// The stand-in prints each answer as reply-cli 0.17.2 does (reply-cli's <c>src/operations/direct-send.ts</c>,
/// <c>result_of</c>): <c>activity_id</c> is Reply's <c>linkedInActivityId</c> where it is a positive integer and null
/// otherwise, <c>sent_at</c> its <c>sentAtUtc</c> where it is a string and null otherwise, and the reference, the moment
/// and the state as Reply gives them.
/// </summary>
public partial class ReplyMessageSendTests
{
    /// <summary>
    /// A LinkedIn send held for a moment ahead, which Reply answers with no activity, no moment and a
    /// <c>linkedin-scheduled-</c> reference, is accepted: the reference is the one pinned, as given, and the message has no
    /// identifier yet. No identifier is not a lost answer.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message")]
    [InlineData("linkedin_inmail")]
    [InlineData("linkedin_voice")]
    public async Task A_deferred_linkedin_send_is_accepted_under_the_reference_reply_gives_it_with_no_identifier_yet(string format)
    {
        using var account = OnLinkedIn();
        account.Returns(FormatCommand(format), new JsonObject
        {
            ["status"] = "sent", ["contact_id"] = 1001, ["contact_created"] = false, ["activity_id"] = null, ["sent_at"] = null,
            ["reference"] = "linkedin-scheduled-4021", ["send_after"] = "2099-11-03T14:00:00Z", ["state"] = "queued",
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, WithMoment(Input(format, pinned: "1001"), "2099-11-03T09:00:00-05:00"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(
            ("accepted", "linkedin-scheduled-4021", "2099-11-03T14:00:00Z", true),
            ((string?)item["status"], (string?)item["reference"], (string?)item["accepted_for"], (bool)item["cancelable"]!));
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"linkedin-scheduled-4021"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
    }

    /// <summary>
    /// A repeat told that an earlier attempt returned a deferred LinkedIn send's <c>linkedin-scheduled-</c> reference reads
    /// it first, which reply-cli takes from 0.18.0: Reply's record of the send answers the work item, inside the window and
    /// past it, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(30)]
    public async Task A_repeat_after_a_deferred_linkedin_send_s_scheduled_reference_reads_it_and_sends_nothing(double hoursAgo)
    {
        const string scheduled = "linkedin-scheduled-4021";
        using var account = OnLinkedIn().WithDirectSend(scheduled, "waitingForSchedule");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(
            api, Input("linkedin_message", pinned: "1001"), attempt: 2, unsettledSince: DateTimeOffset.UtcNow.AddHours(-hoursAgo), unsettledReference: scheduled);

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(("accepted", scheduled, true), ((string?)item["status"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.Equal([$"direct-send get {scheduled}"], Lines(account));
        Assert.Null(account.Calls[0].Option("--idempotency-key"));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A LinkedIn send that goes inside the call keeps the reference Reply always gave it, <c>linkedin-&lt;activity&gt;</c>,
    /// and the activity as the message's identifier. The stand-in's own send, whose reference numbers its record instead,
    /// is <c>Each_format_runs_its_own_command_with_its_text_on_stdin</c>.
    /// </summary>
    [Fact]
    public async Task An_immediate_linkedin_send_keeps_its_activity_reference_and_identifier()
    {
        using var account = OnLinkedIn();
        account.Returns("contact linkedin-message", new JsonObject
        {
            ["status"] = "sent", ["contact_id"] = 1001, ["contact_created"] = false, ["activity_id"] = 70001, ["sent_at"] = "2026-11-03T14:00:12Z",
            ["reference"] = "linkedin-70001", ["state"] = "sent",
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("linkedin_message", pinned: "1001"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(("sent", "linkedin-70001", false), ((string?)item["status"], (string?)item["reference"], (bool)item["cancelable"]!));
        Assert.Equal("""{"external_id":"70001","sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001","send":"linkedin-70001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
    }
}
