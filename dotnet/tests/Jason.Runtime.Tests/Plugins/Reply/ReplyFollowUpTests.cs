using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A follow-up from end to end: a real runtime and its dispatcher, the official package in a real plugin-host child, and
/// the stand-in at the other end. The claim decides which earlier letter a message follows and composes it at the root of
/// the input; the package sends a message that follows one with <c>--follows</c> and that letter's reference, its body
/// alone on stdin, and Reply places it in the letter's thread under <c>Re: </c> and the letter's subject, or refuses it
/// with nothing sent. A message that follows none goes as a letter of its own, with its subject, as before.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public partial class ReplyFollowUpTests
{
    /// <summary>A real dispatcher that never ticks on its own, and repeats a retriable attempt at the next scan asked for.</summary>
    private const string Dispatcher = """{"TickSeconds":3600,"RetryDelaySeconds":0}""";

    private const string Address = "marta@example.com";
    private const string Mailbox = "ann@example.com";
    private const string Subject = "The Lisbon meetup";
    private const string Body = "Hi Marta, thanks for the chat at the meetup.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The first letter follows nothing and goes as before: no flag, its own subject. Left to the runtime, the next one
    /// follows the line's root: the flag names the first letter's reference, the body alone goes on stdin, and the
    /// stand-in's thread holds both, the second under <c>Re: </c> and the first one's subject. A letter of its own asked
    /// for goes with its subject into a thread of its own, and a letter named by its work item is followed by its reference.
    /// </summary>
    [Fact]
    public async Task A_message_follows_the_line_s_root_by_default_and_a_letter_of_its_own_goes_with_its_subject()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);

        // 1. The first letter: nothing to follow, so exactly the call of a letter of its own.
        var first = await SendAsync(api, campaign, contact, Message(Body));
        Assert.Equal("no_earlier_letter", Because(first));
        var opening = LastSend(account);
        Assert.Equal(
            ["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--email-account-id", "12", "--message", "-", "--idempotency-key", first.Id],
            opening.Args);
        Assert.Equal($$"""{"subject":"{{Subject}}","body":"{{Body}}"}""", Stdin(opening));

        // 2. Left to the runtime: the line's root, by its reference, and the body alone.
        var second = await SendAsync(api, campaign, contact, Message("Any news?"));
        Assert.Equal("line_root", Because(second));
        var followUp = LastSend(account);
        Assert.Equal(
            ["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--email-account-id", "12", "--message", "-", "--idempotency-key", second.Id, "--follows", "email-1001"],
            followUp.Args);
        Assert.Equal("""{"body":"Any news?"}""", Stdin(followUp));
        Assert.Equal("email-1002", Assert.Single(second.Attempts!).Provenance!.ExternalIdsReturned!["send"]);
        var thread = Assert.Single(account.Threads)!["id"]!.GetValue<int>();
        Assert.Equal(
            [(Subject, Body), ("Re: " + Subject, "Any news?")],
            account.OutboundIn(thread).Select(message => ((string?)message["subject"], (string?)message["body"])));

        // 3. A letter of its own, asked for: no flag, its own subject, a thread of its own.
        var own = Message("A new idea.", subject: "Another thought");
        own["follows"] = "new_thread";
        var third = await SendAsync(api, campaign, contact, own);
        Assert.Equal("asked_new_thread", Because(third));
        Assert.Null(LastSend(account).Option("--follows"));
        Assert.Equal("""{"subject":"Another thought","body":"A new idea."}""", Stdin(LastSend(account)));
        Assert.Equal(2, account.Threads.Count);

        // 4. A letter named by its work item: followed by that letter's reference, whatever the line's root is now.
        var named = Message("Back to Lisbon.");
        named["follows"] = first.Id;
        var fourth = await SendAsync(api, campaign, contact, named);
        Assert.Equal("named", Because(fourth));
        Assert.Equal("email-1001", LastSend(account).Option("--follows"));
        Assert.Equal("""{"body":"Back to Lisbon."}""", Stdin(LastSend(account)));
        Assert.Equal(
            ["Re: " + Subject, "Re: " + Subject],
            account.OutboundIn(thread).Skip(1).Select(message => (string?)message["subject"]));
    }

    /// <summary>
    /// Each refusal reply-cli names for a follow-up ends the work item in the code and the class the package's table gives
    /// it, with nothing sent and the message never sent as a letter of its own: the four the claim's own checks keep from
    /// Reply as the contract's <c>follows_not_found</c>, and a letter Reply will never send as <c>follows_never_sent</c>.
    /// </summary>
    [Theory]
    [InlineData("follows.not_found", 404, "contact.followedSendNotFound", "follows_not_found")]
    [InlineData("follows.not_owned", 403, "contact.followedSendNotOwned", "follows_not_found")]
    [InlineData("follows.other_contact", 400, "contact.followedSendOtherContact", "follows_not_found")]
    [InlineData("follows.other_mailbox", 400, "contact.followedSendOtherMailbox", "follows_not_found")]
    [InlineData("follows.never_sent", 409, "contact.followedSendNeverSent", "follows_never_sent")]
    public async Task A_refusal_of_a_follow_up_ends_in_its_own_permanent_code_and_sends_nothing(string cliCode, int status, string replyCode, string code)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        account.Refuses(
            "contact send-email", cliCode, status: status, replyCode: replyCode,
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });

        var refused = await ApprovedAsync(api, campaign, contact, Message("Any news?"));

        Assert.Equal((WorkItemStatus.Failed, code, FailureClass.Permanent), (refused.Status, refused.LastError?.Code, Assert.Single(refused.Attempts!).Error?.Class));
        Assert.Equal("email-1001", LastSend(account).Option("--follows"));
        Assert.Single(account.Sent);
        Assert.Single(Assert.Single(account.Threads)!["messages"]!.AsArray());
    }

    /// <summary>
    /// A follow-up that would go at once while the letter it follows is still held for its moment is refused for now,
    /// <c>follows_not_sent_yet</c>, <c>transient</c>: nothing goes, and the runtime repeats it, which goes into that letter's
    /// thread once the letter has gone, under the same key and the same reference.
    /// </summary>
    [Fact]
    public async Task A_follow_up_of_a_letter_not_gone_yet_is_repeated_and_goes_into_its_thread_once_it_has_gone()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        var later = Message(Body);
        later["send_after"] = "2099-11-03T09:00:00-05:00";
        var first = await SendAsync(api, campaign, contact, later);
        Assert.Equal("accepted", (string?)first.Result!["items"]![0]!["status"]);

        var waiting = await ApprovedAsync(api, campaign, contact, Message("Any news?"));

        // The item waits for its repeat, so the failure is read on the attempt that met it.
        Assert.NotEqual(WorkItemStatus.Failed, waiting.Status);
        var notYet = Assert.Single(waiting.Attempts!).Error;
        Assert.Equal(("follows_not_sent_yet", FailureClass.Transient), (notYet?.Code, notYet?.Class));
        Assert.Single(account.Sent);
        Assert.Empty(account.Threads);

        account.SendBecomes("email-1001", "sent");
        var gone = await ClaimAsync(api, waiting.Id);

        Assert.Equal((WorkItemStatus.Succeeded, "email-1002"), (gone.Status, Latest(gone).Provenance!.ExternalIdsReturned!["send"]));
        var attempts = account.Calls.Where(call => call.Command == "contact send-email").Skip(1).ToList();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, call => Assert.Equal(("email-1001", waiting.Id), (call.Option("--follows"), call.Option("--idempotency-key"))));
        Assert.Equal(
            ["Re: " + Subject],
            account.OutboundIn(Assert.Single(account.Threads)!["id"]!.GetValue<int>()).Skip(1).Select(message => (string?)message["subject"]));
    }

    /// <summary>
    /// A follow-up held for a moment that is not later than the one the letter it follows is held for would go before that
    /// letter: Reply refuses it, which reply-cli names <c>follows.too_early</c>, and the item ends <c>follows_too_early</c>,
    /// <c>validation</c>, with nothing sent and nothing taken, since the moment it was given is wrong for a follow-up and a
    /// repeat would meet the same answer. The same moment is not later, written as the letter's was or in another offset.
    /// </summary>
    [Theory]
    [InlineData("2099-11-02T09:00:00-05:00")]
    [InlineData("2099-11-03T09:00:00-05:00")]
    [InlineData("2099-11-03T14:00:00Z")]
    public async Task A_follow_up_timed_no_later_than_the_letter_it_follows_ends_follows_too_early_with_nothing_sent(string moment)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        var held = Message(Body);
        held["send_after"] = "2099-11-03T09:00:00-05:00";
        await SendAsync(api, campaign, contact, held);
        var before = Message("Any news?");
        before["send_after"] = moment;

        var refused = await ApprovedAsync(api, campaign, contact, before);

        Assert.Equal(
            (WorkItemStatus.Failed, "follows_too_early", FailureClass.Validation),
            (refused.Status, refused.LastError?.Code, Assert.Single(refused.Attempts!).Error?.Class));
        Assert.Equal(
            ("email-1001", moment),
            (LastSend(account).Option("--follows"), LastSend(account).Option("--send-after")));
        Assert.Single(account.Sent);
        Assert.Single(account.DirectSends);
        Assert.Empty(account.Threads);
    }

    /// <summary>
    /// A mailbox that cannot place the follow-up in its thread fails it, <c>follows_not_placed</c>, <c>permanent</c>, with
    /// nothing sent and never as a letter of its own; the record Reply made of the failed follow-up is kept on the attempt as
    /// the work item's send, so it can be read back by its reference.
    /// </summary>
    [Fact]
    public async Task A_follow_up_the_mailbox_cannot_place_fails_with_nothing_sent_and_keeps_reply_s_record_of_it()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        account.CannotPlaceTheNextFollowUp();

        var refused = await ApprovedAsync(api, campaign, contact, Message("Any news?"));

        var attempt = Assert.Single(refused.Attempts!);
        Assert.Equal((WorkItemStatus.Failed, "follows_not_placed", FailureClass.Permanent), (refused.Status, refused.LastError?.Code, attempt.Error?.Class));
        Assert.Equal("email-1002", attempt.Provenance!.ExternalIdsReturned!["send"]);
        Assert.Single(account.Sent);
        Assert.Single(Assert.Single(account.Threads)!["messages"]!.AsArray());
    }

    /// <summary>
    /// A Reply that does not offer follow-ups answers the follow-up route as one it does not have, which reply-cli names
    /// <c>follows.not_available</c>: the item ends <c>capability_not_offered</c>, naming the operation and <c>follows</c>,
    /// with nothing sent and nothing tried in its place.
    /// </summary>
    [Fact]
    public async Task A_follow_up_against_a_reply_without_follow_ups_ends_capability_not_offered_with_nothing_sent()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        account.NotOffering("direct_send_follow_up");

        var refused = await ApprovedAsync(api, campaign, contact, Message("Any news?"));

        Assert.Equal(
            (WorkItemStatus.Failed, UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent),
            (refused.Status, refused.LastError?.Code, Assert.Single(refused.Attempts!).Error?.Class));
        Assert.StartsWith("message.send: ", refused.LastError!.Message, StringComparison.Ordinal);
        Assert.Contains("`follows`", refused.LastError.Message, StringComparison.Ordinal);
        Assert.Single(account.Sent);
        Assert.Equal(2, account.Calls.Count(call => call.Command == "contact send-email"));
    }

    /// <summary>
    /// A follow-up of a letter held longer than the runtime's retries last is refused at each of them, <c>transient</c>, and
    /// once they are spent the item ends <c>follows_not_sent_yet</c> with nothing sent, never as a letter of its own: the
    /// message is proposed again with a <c>send_after</c> later than that letter's moment.
    /// </summary>
    [Fact]
    public async Task A_follow_up_of_a_letter_held_past_the_retries_ends_follows_not_sent_yet_with_nothing_sent()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        var held = Message(Body);
        held["send_after"] = "2099-11-03T09:00:00-05:00";
        await SendAsync(api, campaign, contact, held);

        var item = await ApprovedAsync(api, campaign, contact, Message("Any news?"));
        for (var scans = 0; item.Status != WorkItemStatus.Failed && scans < 10; scans++)
        {
            item = await ClaimAsync(api, item.Id);
        }

        Assert.Equal((WorkItemStatus.Failed, "follows_not_sent_yet"), (item.Status, item.LastError?.Code));
        Assert.True(item.Attempts!.Count > 1, $"{item.Attempts.Count} attempt(s)");
        Assert.All(item.Attempts, attempt => Assert.Equal(("follows_not_sent_yet", FailureClass.Transient), (attempt.Error?.Code, attempt.Error?.Class)));
        Assert.All(account.Calls.Where(call => call.Command == "contact send-email").Skip(1), call => Assert.Equal("email-1001", call.Option("--follows")));
        Assert.Single(account.Sent);
        Assert.Single(account.DirectSends);
        Assert.Empty(account.Threads);
    }

    /// <summary>
    /// A follow-up whose answer was lost may have gone, and its repeat that meets a Reply without the follow-up route — one
    /// of Reply's hosts not updated yet — says nothing of that attempt: it ends <c>send_not_settled</c>, as a plain send's
    /// repeat that meets a 404 with no code does, and never says that nothing was sent.
    /// </summary>
    [Fact]
    public async Task A_repeat_of_a_lost_follow_up_that_meets_a_reply_without_follow_ups_is_not_settled()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        account.LosesTheOutcome("contact send-email");
        var lost = await ApprovedAsync(api, campaign, contact, Message("Any news?"));
        Assert.Equal("provider_answer_lost", Assert.Single(lost.Attempts!).Error?.Code);
        account.NotOffering("direct_send_follow_up");

        var repeated = await ClaimAsync(api, lost.Id);

        Assert.Equal(
            (WorkItemStatus.Failed, "send_not_settled", FailureClass.Permanent),
            (repeated.Status, repeated.LastError?.Code, Latest(repeated).Error?.Class));
        var attempts = account.Calls.Where(call => call.Command == "contact send-email").Skip(1).ToList();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, call => Assert.Equal(("email-1001", lost.Id), (call.Option("--follows"), call.Option("--idempotency-key"))));
    }

    /// <summary>
    /// A message composed as a template follows the line's root as a final one does: <c>--follows</c> and the root's
    /// reference, and its body alone, in Reply's syntax, which Reply fills in; and a template Reply could not fill in ends
    /// as a letter of its own does, <c>template_unrendered</c>, with nothing sent.
    /// </summary>
    [Fact]
    public async Task A_template_follows_the_line_s_root_with_its_body_alone_in_reply_s_syntax()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));

        var followUp = await SendAsync(api, campaign, contact, Template("Any news, {{contact.first_name}}?"));
        Assert.Equal(("line_root", "email-1001"), (Because(followUp), LastSend(account).Option("--follows")));
        Assert.Equal("""{"body":"Any news, {{FirstName}}?"}""", Stdin(LastSend(account)));
        Assert.Equal(
            ["Re: " + Subject],
            account.OutboundIn(Assert.Single(account.Threads)!["id"]!.GetValue<int>()).Skip(1).Select(message => (string?)message["subject"]));

        var own = Template("Hi {{contact.first_name}}", subject: "Another thought");
        own["follows"] = "new_thread";
        CouldNotFill(account);
        var letter = await ApprovedAsync(api, campaign, contact, own);
        CouldNotFill(account);
        var unfilled = await ApprovedAsync(api, campaign, contact, Template("Any news, {{contact.first_name}}?"));

        Assert.Equal(
            (WorkItemStatus.Failed, "template_unrendered", FailureClass.Validation),
            (letter.Status, letter.LastError?.Code, Assert.Single(letter.Attempts!).Error?.Class));
        Assert.Equal(
            (letter.Status, letter.LastError?.Code, FailureClass.Validation),
            (unfilled.Status, unfilled.LastError?.Code, Assert.Single(unfilled.Attempts!).Error?.Class));
        Assert.Equal("email-1001", LastSend(account).Option("--follows"));
        Assert.Equal(2, account.DirectSends.Count);
    }

    /// <summary>
    /// A mailbox whose lookup of the email followed errors answers the follow-up as Reply answers a letter of its own it
    /// could not send, 400 <c>contact.emailSendingFailed</c>, which reply-cli passes on as <c>reply.refused</c>: the
    /// follow-up ends exactly as such a letter does, with nothing sent, and never goes as a letter of its own instead.
    /// </summary>
    [Fact]
    public async Task A_follow_up_reply_could_not_send_ends_as_a_letter_of_its_own_reply_could_not_send_does()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        var own = Message("A new idea.", subject: "Another thought");
        own["follows"] = "new_thread";

        CouldNotSend(account);
        var letter = await ApprovedAsync(api, campaign, contact, own);
        CouldNotSend(account);
        var followUp = await ApprovedAsync(api, campaign, contact, Message("Any news?"));

        Assert.Equal(
            (WorkItemStatus.Failed, "provider_call_failed", FailureClass.Permanent),
            (letter.Status, letter.LastError?.Code, Assert.Single(letter.Attempts!).Error?.Class));
        Assert.Equal(
            (letter.Status, letter.LastError?.Code, FailureClass.Permanent),
            (followUp.Status, followUp.LastError?.Code, Assert.Single(followUp.Attempts!).Error?.Class));
        Assert.Equal("email-1001", LastSend(account).Option("--follows"));
        Assert.Single(account.Sent);
        Assert.Single(account.DirectSends);
        Assert.Single(Assert.Single(account.Threads)!["messages"]!.AsArray());
    }

    /// <summary>
    /// A follow-up held for its moment may end failed at Reply with no failure code at all — the email it follows could not
    /// be read three times over, or a mailbox's lookup of it kept erroring — and the read of the send says just that:
    /// <c>failed</c>, its code null, never a code of a follow-up's, and never a read that fails for want of a code.
    /// </summary>
    [Fact]
    public async Task A_follow_up_that_ended_failed_with_no_code_reads_failed_with_none()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        var later = Message("Any news?");
        later["send_after"] = "2099-11-03T09:00:00-05:00";
        var held = await SendAsync(api, campaign, contact, later);
        Assert.Equal(
            ("accepted", "email-1001", "email-1002"),
            ((string?)held.Result!["items"]![0]!["status"], LastSend(account).Option("--follows"), Assert.Single(held.Attempts!).Provenance!.ExternalIdsReturned!["send"]));
        account.SendBecomes("email-1002", "failed");

        var read = ReplyOperations.Succeeded(
            "message.status",
            await ReplyOperations.InvokeAsync(api, "message.status", ReplyMessageStatusTests.Input("email-1002"), Ct));

        Assert.Equal(("failed", null), ((string?)read["state"], (string?)read["failure_code"]));
        Assert.Single(Assert.Single(account.Threads)!["messages"]!.AsArray());
    }

    /// <summary>
    /// A follow-up whose answer was lost may have gone, so the runtime repeats it under the same key; the claim keeps its
    /// decision, so the repeat names the same letter, and Reply answers it with the send it took: one follow-up, in the
    /// thread once.
    /// </summary>
    [Fact]
    public async Task A_repeat_after_a_lost_answer_follows_the_same_letter_under_the_same_key()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync();
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);
        await SendAsync(api, campaign, contact, Message(Body));
        account.LosesTheOutcome("contact send-email");

        var lost = await ApprovedAsync(api, campaign, contact, Message("Any news?"));
        var unknown = Assert.Single(lost.Attempts!).Error;
        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (unknown?.Code, unknown?.Class));
        var repeated = await ClaimAsync(api, lost.Id);

        Assert.Equal((WorkItemStatus.Succeeded, "email-1002"), (repeated.Status, Latest(repeated).Provenance!.ExternalIdsReturned!["send"]));
        var attempts = account.Calls.Where(call => call.Command == "contact send-email").Skip(1).ToList();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, call => Assert.Equal(("email-1001", lost.Id), (call.Option("--follows"), call.Option("--idempotency-key"))));
        Assert.Equal(2, account.DirectSends.Count);
        Assert.Equal(2, Assert.Single(account.Threads)!["messages"]!.AsArray().Count);
    }

    /// <summary>
    /// A root <c>follows</c> that names a letter by its work item without the reference Reply gave that letter — empty,
    /// null or left out — cannot be named to Reply: the package refuses it before anything is called,
    /// <c>provider_call_failed</c>, permanent, and never sends the message as a letter of its own. The claim refuses such an
    /// input first, at the pointer named; this is the package's own floor beneath it.
    /// </summary>
    [Theory]
    [InlineData("\"\"", "/follows/reference")]
    [InlineData("null", "/follows")]
    [InlineData(null, "/follows/reference")]
    public async Task A_letter_named_without_its_reference_is_refused_before_anything_is_called(string? reference, string refused)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var follows = new JsonObject { ["work_item"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRA", ["operation"] = "message.send", ["because"] = "named" };
        if (reference is not null)
        {
            follows["reference"] = JsonNode.Parse(reference);
        }

        var input = new JsonObject
        {
            ["args"] = Message("Any news?"),
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["first_name"] = "Marta",
                ["last_name"] = "Alvarez",
                ["company"] = "Puerto Analytics",
                ["title"] = "Head of Growth",
                ["time_zone"] = null,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address, ["primary"] = true }),
                ["external_ids"] = new JsonObject { ["contact"] = "1001" },
            }),
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = new JsonObject(),
            },
            ["follows"] = follows,
            ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK",
        };

        var error = ReplyOperations.Failed(
            "message.send", await ReplyOperations.InvokeAsync(api, "message.send", input, Ct, claimRefuses: [refused]));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// The manifest takes <c>follows</c> on email alone, so the claim hands no letter to follow to a LinkedIn message; a
    /// LinkedIn message handed one all the same is refused before anything is called, never sent as a message of its own.
    /// </summary>
    [Fact]
    public async Task A_linkedin_message_handed_a_letter_to_follow_is_refused_before_anything_is_called()
    {
        using var account = new ReplyAccount()
            .WithContact(1001, null, "Marta", linkedIn: "https://www.linkedin.com/in/marta-example")
            .WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner")
            .WithConnection(1001, 34);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var input = new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = "linkedin",
                ["sender"] = new JsonObject { ["external_id"] = "34", ["address"] = "https://www.linkedin.com/in/ann-owner" },
                ["message"] = new JsonObject { ["format"] = "linkedin_message", ["body"] = "Any news?" },
            },
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["first_name"] = "Marta",
                ["last_name"] = "Alvarez",
                ["company"] = "Puerto Analytics",
                ["title"] = "Head of Growth",
                ["time_zone"] = null,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "linkedin", ["value"] = "https://www.linkedin.com/in/marta-example", ["primary"] = true }),
                ["external_ids"] = new JsonObject { ["contact"] = "1001" },
            }),
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = new JsonObject(),
            },
            ["follows"] = new JsonObject { ["work_item"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRA", ["operation"] = "message.send", ["reference"] = "linkedin-1001", ["because"] = "named" },
            ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK",
        };

        var error = ReplyOperations.Failed("message.send", await ReplyOperations.InvokeAsync(api, "message.send", input, Ct));

        Assert.Equal((UnjudgedOutcomeCodes.CapabilityNotOffered, FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("`follows`", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
    }

    private static ReplyAccount Planted() =>
        new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox);

    /// <summary>The next direct email is refused as Reply refuses one it could not send, and reply-cli passes it on.</summary>
    private static void CouldNotSend(ReplyAccount account) =>
        account.Refuses(
            "contact send-email", "reply.refused", status: 400, replyCode: "contact.emailSendingFailed",
            detail: "The email could not be sent.", ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });

    /// <summary>The next direct email is refused as Reply refuses a template it could not fill in for the person.</summary>
    private static void CouldNotFill(ReplyAccount account) =>
        account.Refuses(
            "contact send-email", "contact.missing_variables", status: 400, replyCode: "contact.missingVariables",
            details: new JsonObject { ["missing_variables"] = new JsonArray("nickname") },
            ids: new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 });

    private static JsonObject Template(string body, string subject = Subject)
    {
        var message = Message(body, subject);
        message["message"]!["compose"] = "template";
        return message;
    }

    private static async Task<RuntimeApiFixture> StartAsync() =>
        await ReplyPlugins.StartAsync(
            Ct,
            paths =>
            {
                // The grant is in the settings file already: the dispatcher's section goes beside it, not over it.
                var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
                settings["Dispatcher"] = JsonNode.Parse(Dispatcher);

                // A start summons nothing here: the review it would summon is the campaign's claim the sends wait behind.
                StartlessTriggers.Narrow(settings);
                File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId));
            });

    private static JsonObject Message(string body, string subject = Subject) => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "12", ["address"] = Mailbox },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = subject, ["body"] = body },
    };

    /// <summary>Why the claim decided what the message follows, as it recorded the decision on the work item.</summary>
    private static string? Because(WorkItemDto item) => (string?)item.Context["follows"]?["because"];

    /// <summary>The item's attempt of the highest number: the one that ran last.</summary>
    private static AttemptDto Latest(WorkItemDto item) => item.Attempts!.MaxBy(attempt => attempt.Number)!;

    /// <summary>The last direct email the package asked the CLI to send.</summary>
    private static ReplyCall LastSend(ReplyAccount account) => account.Calls.Last(call => call.Command == "contact send-email");

    private static string Stdin(ReplyCall call) => JsonNode.Parse(call.Body!)!.ToJsonString();

    private static async Task<(string Campaign, string Contact)> WorkAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 LatAm founders" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var contact = await api.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new { first_name = "Marta", channels = new[] { new { channel = "email", value = Address } } },
            Ct);
        await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign.Id, contacts = new[] { new { contact_id = contact.Id } } },
            Ct);
        return (campaign.Id, contact.Id);
    }

    /// <summary>A send that succeeds: parked by the claim for a person, approved, and claimed again.</summary>
    private static async Task<WorkItemDto> SendAsync(RuntimeApiFixture api, string campaign, string contact, JsonObject arguments)
    {
        var sent = await ApprovedAsync(api, campaign, contact, arguments);
        Assert.True(sent.Status == WorkItemStatus.Succeeded, $"{sent.Status}: {sent.LastError?.Code} {sent.LastError?.Message}");
        return sent;
    }

    /// <summary>A send, parked by the claim for a person, approved, and claimed again once: the item as that attempt left it.</summary>
    private static async Task<WorkItemDto> ApprovedAsync(RuntimeApiFixture api, string campaign, string contact, JsonObject arguments)
    {
        var item = (await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "provider_op", operation = "message.send", contact_id = contact, context = new JsonObject { ["input"] = arguments } },
            Ct)).Id;
        Assert.Equal(0, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);

        var approval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        Assert.Equal(item, approval.WorkItemId);
        await api.PostOkAsync<ApprovalDto>(
            Operations.ApprovalApprove,
            new { approval_id = approval.Id, actor = new { type = "human", id = "ada" }, reason = "go ahead" },
            Ct);

        return await ClaimAsync(api, item);
    }

    private static async Task<WorkItemDto> ClaimAsync(RuntimeApiFixture api, string item)
    {
        Assert.Equal(1, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
        Assert.True(await api.Resolve<HandlerPool>().DrainAsync(TimeSpan.FromSeconds(60)));
        return await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item, include_snapshots = true }, Ct);
    }
}
