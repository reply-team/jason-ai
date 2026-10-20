using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>conversation.reply</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against threads planted in the shape Reply holds them. The thread is read first, and nothing is
/// sent unless the thread is on the channel the call names and its person is the work item's — by the pin, and in every
/// case by the address on that channel. A reply that may have gone is never sent again.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyConversationReplyTests
{
    private const string Operation = "conversation.reply";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRP";
    private const string Profile = "https://www.linkedin.com/in/marta-alvarez";
    private const string Body = "Thursday at 10 works. I will send an invitation.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // Sending, and who it is sent to
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A person with no pin is found through the thread: its person's address on the channel is theirs, whatever its
    /// case, so the reply goes, and the thread's person is pinned. The thread is read once, for one message, and the text
    /// reaches the CLI on stdin and nowhere else.
    /// </summary>
    [Fact]
    public async Task A_reply_reads_the_thread_then_sends_its_text_on_stdin_and_pins_the_thread_s_person()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(value: "Marta@Example.com"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal((ContactId, "sent", "501", "email"), ((string?)item["contact_id"], (string?)item["status"], (string?)item["thread_external_id"], (string?)item["channel"]));

        // Reply answers an email reply with no message identifier, and its moment two hours ahead with a `Z`: neither is
        // an identifier or a time to record, so the message is answered null on both.
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001"}""", item["external_ids"]!.ToJsonString());
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());

        Assert.Equal(["inbox get 501", "inbox reply 501"], Paths(account));
        Assert.Equal(["--json", "-q", "inbox", "get", "501", "--limit", "1", "--offset", "0"], account.Calls[0].Args);
        Assert.Equal(["--json", "-q", "inbox", "reply", "501", "--message", "-"], account.Calls[1].Args);
        Assert.Null(account.Calls[0].Body);
        Assert.Equal($$"""{"body":"{{Body}}"}""", JsonNode.Parse(account.Calls[1].Body!)!.ToJsonString());

        // What goes out is the text, then Reply's own quote of the thread's earlier messages.
        var stored = (string?)Assert.Single(account.OutboundIn(501))["body"];
        Assert.StartsWith(Body + "<div>", stored, StringComparison.Ordinal);
        Assert.Contains("<blockquote>", stored, StringComparison.Ordinal);
    }

    /// <summary>
    /// An identifier the send's answer does carry is the provider's, and is passed on; the moment that answer gives is
    /// not, whatever it says, because Reply stamps it in the account's own zone and writes it as UTC.
    /// </summary>
    [Fact]
    public async Task An_identifier_the_answer_carries_is_passed_on_and_its_moment_never_is()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Returns("inbox reply", new JsonObject
        {
            ["status"] = "sent",
            ["thread_id"] = 501,
            ["channel"] = "email",
            ["message_id"] = "<r501-9@stand-in.example>",
            ["sent_at"] = "2026-10-03T09:42:10.25Z",
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()))["items"]![0]!;

        Assert.Equal(["external_id", "sent_at"], item["message"]!.AsObject().Select(member => member.Key));
        Assert.Equal("<r501-9@stand-in.example>", (string?)item["message"]!["external_id"]);
        Assert.Null(item["message"]!["sent_at"]);
    }

    [Fact]
    public async Task A_pinned_person_is_replied_to_when_the_thread_is_theirs()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")))["items"]![0]!;

        Assert.Equal("sent", (string?)item["status"]);
        Assert.Single(account.OutboundIn(501));
    }

    /// <summary>
    /// Each way the thread can be somebody else's: another pin, another address, the pinned person under an address that
    /// is not the one the claim checked the register for, and a person the provider no longer holds. Nothing is sent, the
    /// one call is the read, and the thread's person is never pinned to somebody they are not.
    /// </summary>
    [Theory]
    [InlineData("1002", "marta@example.com", false)]
    [InlineData(null, "lena@example.com", false)]
    [InlineData("1001", "marta.work@example.com", false)]
    [InlineData(null, "marta@example.com", true)]
    [InlineData(null, "arta@example.com", false)]
    [InlineData(null, "amarta@example.com", false)]
    public async Task A_thread_that_is_not_this_person_s_sends_nothing_and_pins_nobody(string? pinned, string value, bool deleted)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z", deleted: deleted));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(value: value, pinned: pinned)));

        Assert.Equal(("thread_contact_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["inbox get 501"], Paths(account));
        Assert.Empty(account.OutboundIn(501));
    }

    /// <summary>
    /// A person Reply marks deleted is not one it still holds, even where it keeps their identifier on the thread: the
    /// reply is refused, and the identifier is not pinned to anybody.
    /// </summary>
    [Fact]
    public async Task A_thread_whose_person_reply_marks_deleted_sends_nothing_even_with_their_identifier_kept()
    {
        var thread = ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z");
        thread["contact"]!["isDeleted"] = true;
        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("thread_contact_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.OutboundIn(501));
    }

    /// <summary>
    /// Reply says a person has no LinkedIn profile with an empty string. Such a person cannot be the one whose profile the
    /// work item consumes, whoever is pinned: nothing is sent, and the reason says what was missing.
    /// </summary>
    [Fact]
    public async Task A_linkedin_thread_whose_person_reply_holds_no_profile_for_sends_nothing()
    {
        var thread = ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn");
        thread["contact"]!["linkedInProfileUrl"] = string.Empty;
        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "502", channel: "linkedin", pinned: "1001")));

        Assert.Equal(("thread_contact_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Contains("no LinkedIn profile", (string?)error.Details!["reason"], StringComparison.Ordinal);
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.OutboundIn(502));
    }

    [Fact]
    public async Task A_thread_on_another_channel_sends_nothing()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z", channel: "linkedIn"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("channel_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(["inbox get 501"], Paths(account));
        Assert.Empty(account.OutboundIn(501));
    }

    /// <summary>
    /// On LinkedIn the person is the profile, held under the key the runtime gives a profile link for its suppression
    /// register: the profile's name after <c>/in/</c>, decoded once, composed and lowercased, whatever host, scheme,
    /// slashes, query or fragment the link carried. Reply's spelling is keyed so; the work item's is that key already.
    /// Reply gives no identifier for a LinkedIn message.
    /// </summary>
    [Fact]
    public async Task A_linkedin_thread_is_answered_for_the_profile_it_names()
    {
        var thread = ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn");
        thread["contact"]!["linkedInProfileUrl"] = "HTTPS://WWW.LinkedIn.com/in/Marta-Alvarez/#about";
        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(thread: "502", channel: "linkedin", value: Profile));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal(("linkedin", null), ((string?)item["channel"], (string?)item["message"]!["external_id"]));
        Assert.Equal("""{"body":"Thursday at 10 works. I will send an invitation."}""", JsonNode.Parse(account.Calls[1].Body!)!.ToJsonString());
    }

    /// <summary>
    /// The thread's person and the work item's are compared as the register keys a profile link: Reply holds the link as
    /// somebody typed it, the work item carries the key the runtime stored for it, and they are one profile, so the reply
    /// goes. Only Reply's spelling is keyed, and by the runtime's own rule.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplyProfileKeys.Alike), MemberType = typeof(ReplyProfileKeys))]
    public async Task A_linkedin_profile_the_register_keys_alike_is_the_thread_s_person_and_the_reply_goes(string held)
    {
        var thread = ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn");
        thread["contact"]!["linkedInProfileUrl"] = held;
        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var consumed = ReplyProfileKeys.RuntimeKey(held);
        var result = await InvokeAsync(api, Input(thread: "502", channel: "linkedin", value: consumed));

        Assert.True(result.Outcome is InvocationOutcome.Succeeded, $"Reply held '{held}', the work item carried '{consumed}', and the reply was refused.");
        Assert.Single(account.OutboundIn(502));
    }

    /// <summary>
    /// A value at Reply the runtime keys as no profile — a link to another kind of page, a link on another host — is
    /// nobody's, so the thread is not this person's and nothing is sent.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplyProfileKeys.Apart), MemberType = typeof(ReplyProfileKeys))]
    public async Task A_value_at_reply_the_runtime_keys_as_no_profile_is_nobody_s_and_nothing_is_sent(string held)
    {
        var thread = ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn");
        thread["contact"]!["linkedInProfileUrl"] = held;
        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "502", channel: "linkedin", value: ReplyProfileKeys.RuntimeKey(ReplyProfileKeys.Plain))));

        Assert.True(
            (error.Code, error.Class) == ("thread_contact_mismatch", FailureClass.Validation),
            $"Reply held '{held}', and the reply ended {error.Code}.");
        Assert.Equal(["inbox get 502"], Paths(account));
        Assert.Empty(account.OutboundIn(502));
    }

    /// <summary>
    /// A person whose channel still holds a value from before the key — left as it stood where the same person held the key
    /// in another — is reached at a value the register keys as no profile. Reply holds the profile under its plain
    /// spelling, whose key that value is not: the plugin compares the work item's value as it stands, so the thread is not
    /// this person's and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_value_stored_before_the_key_is_never_the_profile_reply_holds_and_nothing_is_sent()
    {
        var thread = ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn");
        thread["contact"]!["linkedInProfileUrl"] = ReplyProfileKeys.Plain;
        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "502", channel: "linkedin", value: ReplyProfileKeys.LeftAsItStood)));

        Assert.Equal(("thread_contact_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(["inbox get 502"], Paths(account));
        Assert.Empty(account.OutboundIn(502));
    }

    /// <summary>
    /// Every profile the register keys apart is another profile here: a profile whose name differs, and one whose name is
    /// the start of the other's. The register could hold a stop under the thread's profile while the claim asked it about
    /// the work item's, so the reply is refused rather than sent to a person the check was never about.
    /// </summary>
    [Theory]
    [InlineData(null, "https://www.linkedin.com/in/marta-alvarez-2")]
    [InlineData(null, "https://www.linkedin.com/in/marta")]
    [InlineData("https://www.linkedin.com/in/marta-alvarez-2", "https://www.linkedin.com/in/marta-alvarez")]
    public async Task A_linkedin_profile_the_register_keys_apart_is_another_profile_and_nothing_is_sent(string? held, string value)
    {
        var planted = ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn");
        if (held is not null)
        {
            planted["contact"]!["linkedInProfileUrl"] = held;
        }

        using var account = new ReplyAccount().WithThread(planted);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "502", channel: "linkedin", value: value)));

        Assert.True(
            (error.Code, error.Class) == ("thread_contact_mismatch", FailureClass.Validation),
            $"Reply held '{held ?? Profile}', the work item carried '{value}', and the reply ended {error.Code}.");
        Assert.Equal(["inbox get 502"], Paths(account));
        Assert.Empty(account.OutboundIn(502));
    }

    [Fact]
    public async Task A_reply_to_one_message_names_it_on_stdin()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(replyTo: "<m501@mail.example.com>")));

        var message = JsonNode.Parse(account.Calls[1].Body!)!.AsObject();
        Assert.Equal(["body", "reply_to_message_id"], message.Select(member => member.Key));
        Assert.Equal((Body, "<m501@mail.example.com>"), ((string?)message["body"], (string?)message["reply_to_message_id"]));
        Assert.Equal("<m501@mail.example.com>", (string?)Assert.Single(account.OutboundIn(501))["replyToMessageId"]);
    }

    // -------------------------------------------------------------------------------------------------------
    // When it may have gone, and when it did not
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The reply went and its answer did not come back. That is the one ending after which a message may be in
    /// somebody's inbox, and the document says `never`: the item stops for a person rather than being sent again, and
    /// the thread's person travels with the failure so whoever looks knows who it was for.
    /// </summary>
    [Fact]
    public async Task A_lost_answer_is_never_repeated_and_carries_the_thread_s_person()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.LosesTheOutcome("inbox reply");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.False(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
        Assert.Single(account.OutboundIn(501));
    }

    /// <summary>
    /// The CLI answers that the reply was sent, but on another channel than the one the thread is on and the work item
    /// asked for. That is not this reply's answer — what went out, and to whom, is unknown — so it is a lost answer, never
    /// repeated, and the thread's person travels with it.
    /// </summary>
    [Fact]
    public async Task A_send_answered_on_another_channel_than_the_one_asked_is_a_lost_answer_and_never_a_success()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(502, "2026-10-03T07:41:09Z", channel: "linkedIn"));
        account.Returns("inbox reply", new JsonObject
        {
            ["status"] = "sent",
            ["thread_id"] = 502,
            ["channel"] = "email",
            ["message_id"] = null,
            ["sent_at"] = "2026-10-03T09:00:00.000Z",
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "502", channel: "linkedin")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.False(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["inbox get 502", "inbox reply 502"], Paths(account));
    }

    [Fact]
    public async Task An_answer_that_is_not_a_send_is_a_lost_answer_and_never_repeated()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Returns("inbox reply", new JsonObject { ["status"] = "queued", ["thread_id"] = 501, ["channel"] = "email" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
    }

    [Fact]
    public async Task Reply_s_own_register_refuses_the_person_and_nothing_is_sent()
    {
        using var account = new ReplyAccount()
            .WithContact(1001, ReplyOperations.Address, "Marta", optedOut: true)
            .WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Empty(account.OutboundIn(501));
    }

    [Fact]
    public async Task A_reply_reply_could_not_deliver_is_refused_with_reply_s_reason_in_the_trace()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"))
            .FailsToDeliverIn(501, "linkedInSendingLimitExceeded");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("delivery_refused", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("linkedInSendingLimitExceeded", (string?)error.Details!["provider_status_code"]);
        Assert.Equal("inboxThread.threadSendFailed", (string?)error.Details["provider_code"]);
        Assert.Empty(account.OutboundIn(501));
    }

    [Fact]
    public async Task A_thread_reply_does_not_hold_is_not_found_and_nothing_is_sent()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "999")));

        Assert.Equal(("conversation_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["inbox get 999"], Paths(account));
    }

    /// <summary>
    /// The read before the send failed, so nothing was sent: the attempt is tried again rather than stopped for a person,
    /// which is the whole difference between a read that failed and a send that may have gone.
    /// </summary>
    [Fact]
    public async Task A_thread_that_could_not_be_read_sends_nothing_and_is_tried_again()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Refuses("inbox get", "reply.unavailable", status: 503);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal(["inbox get 501"], Paths(account));
        Assert.Empty(account.OutboundIn(501));
    }

    /// <summary>
    /// A read that answered nothing this package can read — not JSON, or no thread in it — is a read that changed
    /// nothing, whatever the CLI exited with: the attempt is tried again, never stopped as if a reply may have gone.
    /// </summary>
    [Theory]
    [InlineData("not an answer")]
    [InlineData("""{"messages":{"items":[],"has_more":false}}""")]
    public async Task A_read_of_the_thread_that_answered_nothing_readable_sends_nothing_and_is_tried_again(string stdout)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Prints("inbox get", 0, stdout);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal(["inbox get 501"], Paths(account));
        Assert.Empty(account.OutboundIn(501));
    }

    /// <summary>
    /// Reply renders a placeholder it finds in a reply's text, and refuses the send when it cannot fill one in: a 400 with
    /// its code for invalid input and a sentence that starts <c>RenderingFail</c>. That is the text a person approved not
    /// being what would go out, so it is the operation's own word for it, and nothing was sent.
    /// </summary>
    [Fact]
    public async Task Reply_refusing_to_render_the_text_is_an_invalid_message_and_nothing_is_sent()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Refuses(
            "inbox reply", "reply.refused", status: 400, replyCode: "inboxThread.invalidInput",
            detail: "RenderingFail: One or more values for variables are missing or incorrect.");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(("reply.refused", "inboxThread.invalidInput"), ((string?)error.Details!["cli_code"], (string?)error.Details["provider_code"]));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["inbox get 501", "inbox reply 501"], Paths(account));
        Assert.Empty(account.OutboundIn(501));
    }

    /// <summary>
    /// The word is kept for exactly that refusal: Reply's code for invalid input with a sentence whose first word is
    /// <c>RenderingFail</c>. Any other invalid input, the same sentence under another code, and a first word that only
    /// starts the same way are refusals this version has no word for.
    /// </summary>
    [Theory]
    [InlineData("inboxThread.invalidInput", "The body is not valid.")]
    [InlineData("inboxThread.notAllowed", "RenderingFail: One or more values for variables are missing or incorrect.")]
    [InlineData("inboxThread.invalidInput", "RenderingFailure: the template could not be read.")]
    public async Task Any_other_refusal_of_the_send_keeps_the_word_for_a_refusal_with_no_word(string replyCode, string detail)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Refuses("inbox reply", "reply.refused", status: 400, replyCode: replyCode, detail: detail);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.OutboundIn(501));
    }

    [Fact]
    public async Task The_cli_s_own_channel_refusal_is_a_channel_mismatch()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.Refuses("inbox reply", "inbox.channel_mismatch", status: 400, replyCode: "inboxThread.channelMismatch", details: new JsonObject { ["thread_channel"] = null });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("channel_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.OutboundIn(501));
    }

    // -------------------------------------------------------------------------------------------------------
    // Refused before anything is asked
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A double opening brace is how Reply writes a placeholder it fills in for each person, so a text carrying one might
    /// go out as something nobody approved. It is refused before anything is called, until a live account shows Reply
    /// sends it as written — wherever in the text it stands, its very start included.
    /// </summary>
    [Theory]
    [InlineData("Hi {{firstName}}, Thursday works.")]
    [InlineData("{{firstName}}, Thursday works.")]
    public async Task A_placeholder_in_the_text_is_refused_before_anything_is_called(string body)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(body: body)));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task An_identifier_that_is_not_a_reply_thread_number_names_no_thread_and_nothing_is_called()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(thread: "t_99812")));

        Assert.Equal("conversation_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    // -------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Input(
        string thread = "501",
        string channel = "email",
        string? value = null,
        string? pinned = null,
        string body = Body,
        string? replyTo = null)
    {
        var arguments = new JsonObject
        {
            ["channel"] = channel,
            ["thread"] = new JsonObject { ["external_id"] = thread },
            ["body"] = body,
        };
        if (replyTo is not null)
        {
            arguments["reply_to_message"] = new JsonObject { ["external_id"] = replyTo };
        }

        return new JsonObject
        {
            ["args"] = arguments,
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = ContactId,
                ["channels"] = new JsonArray(new JsonObject
                {
                    ["channel"] = channel,
                    ["value"] = value ?? (channel == "email" ? ReplyOperations.Address : Profile),
                    ["primary"] = true,
                }),
                ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
            }),
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = new JsonObject { ["campaign"] = "7" },
            },
            ["idempotency_key"] = WorkItemId,
        };
    }

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct);

    private static List<string> Paths(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];
}
