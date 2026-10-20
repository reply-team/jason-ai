using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>social_invitation.send</c> as the official package performs it, against the stand-in: the note held to what Reply
/// would send as written, the account to the address the person approving read, the person found and their contact read
/// before anything goes — opted out at Reply is a stop, another profile is a refusal, no profile is filled — and then one
/// <c>reply contact linkedin-connect</c>, with the note on stdin. What went out is read back from the account.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplySocialInvitationSendTests
{
    private const string Operation = "social_invitation.send";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string Profile = "https://www.linkedin.com/in/marta-example";
    private const string SenderProfile = "https://www.linkedin.com/in/ann-owner";
    private const string Note = "Hi Marta, we met at the Lisbon meetup.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReplyAccount Planted(string? linkedIn = Profile, bool optedOut = false) =>
        new ReplyAccount()
            .WithContact(1001, null, "Marta", optedOut: optedOut, linkedIn: linkedIn)
            .WithSender(34, "linkedin", "Ann Owner", SenderProfile);

    [Fact]
    public async Task An_invitation_with_a_note_goes_out_once_with_the_note_on_stdin()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal(("sent", "34", "1001"), ((string?)item["status"], (string?)item["sender_external_id"], (string?)item["external_ids"]!["contact"]));
        Assert.Equal(ContactId, (string?)item["contact_id"]);
        Assert.Equal("70001", (string?)item["invitation"]!["external_id"]);

        // The stand-in answers a moment, and nothing has shown Reply's to be UTC, so none is passed on.
        Assert.Null(item["invitation"]!["sent_at"]);
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());

        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], Lines(account));
        var send = account.Calls[^1];
        Assert.Equal(["--json", "-q", "contact", "linkedin-connect", "--contact-id", "1001", "--linkedin-account-id", "34", "--message", "-"], send.Args);
        Assert.Equal($$"""{"note":"{{Note}}"}""", send.Body);
        Assert.Equal("linkedin", account.Calls[0].Option("--channel"));
        var went = Assert.Single(account.Sent)!;
        Assert.Equal((1001, 34L), ((int)went["contactId"]!, (long)went["accountId"]!));
    }

    [Fact]
    public async Task An_invitation_without_a_note_sends_no_message_at_all()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001", note: null)));

        var send = account.Calls[^1];
        Assert.Null(send.Option("--message"));
        Assert.Null(send.Body);
        Assert.Null(Assert.Single(account.Sent)!["text"]);
    }

    [Fact]
    public async Task A_person_with_no_pin_is_found_by_the_profile_and_pinned_and_nobody_is_made()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input());

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact find", "contact get 1001", "contact linkedin-connect"], Lines(account));
        Assert.Equal($$"""{"linkedin_url":"{{Profile}}"}""", account.Calls[1].Body);
        Assert.Single(account.Contacts);
    }

    [Fact]
    public async Task A_person_reply_holds_nobody_for_is_not_found_and_nothing_is_sent_or_made()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("contact_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("on a list", error.Message, StringComparison.Ordinal);
        Assert.Equal(["account list", "contact find"], Lines(account));
        Assert.Empty(account.Contacts);
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task A_person_who_is_a_connection_already_is_answered_so_and_nothing_is_sent()
    {
        using var account = Planted().WithConnection(1001, 34);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")))["items"]![0]!;

        Assert.Equal("already_connected", (string?)item["status"]);
        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["invitation"]!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply reads a double opening brace as the start of a placeholder of its own, so a note with one is refused before
    /// anything is called, whether or not it closes: the stand-in would answer only some of them.
    /// </summary>
    [Theory]
    [InlineData("Hi {{firstName}}, we met at the meetup.")]
    [InlineData("Hi {{ name }}")]
    [InlineData("Braces {{ stay as typed.")]
    public async Task A_note_with_a_double_opening_brace_is_refused_before_anything_is_called(string note)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", note: note)));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    /// <summary>Reply does not refuse an invitation to a person it holds as opted out, so the read of the contact is the stop.</summary>
    [Fact]
    public async Task A_person_reply_holds_as_opted_out_is_suppressed_and_nothing_is_sent()
    {
        using var account = Planted(optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A contact pinned through another channel may carry no LinkedIn profile at Reply. The one the invitation consumes —
    /// the one the runtime asked its register about — is written onto it first, alone, and only then is the invitation sent.
    /// </summary>
    [Fact]
    public async Task A_contact_with_no_profile_is_given_the_consumed_one_first_and_then_invited()
    {
        using var account = Planted(linkedIn: null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(["account list", "contact get 1001", "contact update 1001", "contact linkedin-connect"], Lines(account));
        Assert.Equal($$"""{"linkedin_url":"{{Profile}}"}""", account.Calls[2].Body);
        Assert.Equal(Profile, (string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Single(account.Sent);
    }

    /// <summary>A profile is filled where it is missing and never changed: a contact carrying another one is refused, as it was.</summary>
    [Fact]
    public async Task A_contact_with_another_profile_is_not_changed_and_nothing_is_sent()
    {
        using var account = Planted(linkedIn: "https://www.linkedin.com/in/marta-somebody-else");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Equal("https://www.linkedin.com/in/marta-somebody-else", (string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply's contact holds the profile as somebody typed it; the runtime keys it alike, by its own rule, so it is the same
    /// profile and the invitation goes. Nothing is written onto the contact.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplyProfileKeys.Alike), MemberType = typeof(ReplyProfileKeys))]
    public async Task A_contact_holding_the_profile_in_another_spelling_is_invited_as_it_is(string held)
    {
        using var account = Planted(linkedIn: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001", profile: ReplyProfileKeys.RuntimeKey(held)));

        Assert.True(result.Outcome is InvocationOutcome.Succeeded, $"Reply held '{held}', the work item carried its key, and the invitation was refused.");
        Assert.DoesNotContain(account.Calls, call => call.Command == "contact update");
        Assert.Equal(held, (string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// A contact at Reply carrying a value the runtime keys as no profile — a link to another kind of page, a link on
    /// another host — is reached at nobody's profile, so it is another than the one consumed: nothing is changed and
    /// nothing is sent.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplyProfileKeys.Apart), MemberType = typeof(ReplyProfileKeys))]
    public async Task A_contact_carrying_a_value_the_runtime_keys_as_no_profile_is_not_changed_and_nothing_is_sent(string held)
    {
        using var account = Planted(linkedIn: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", profile: ReplyProfileKeys.RuntimeKey(ReplyProfileKeys.Plain))));

        Assert.True(
            (error.Code, error.Class) == ("contact_channel_conflict", FailureClass.Permanent),
            $"Reply held '{held}', and the invitation ended {error.Code}.");
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Equal(held, (string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A person whose channel still holds a value from before the key, and Reply holding the profile under its plain
    /// spelling: the work item's value is compared as it stands, and it is not the key of what Reply holds, so nothing is
    /// sent and nothing is changed.
    /// </summary>
    [Fact]
    public async Task A_value_stored_before_the_key_is_never_the_profile_reply_holds_and_nothing_is_sent()
    {
        using var account = Planted(linkedIn: ReplyProfileKeys.Plain);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", profile: ReplyProfileKeys.LeftAsItStood)));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Equal(ReplyProfileKeys.Plain, (string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The same person, and Reply's contact for them carrying no profile: a value from before the key is no key, and
    /// written there it would be a profile Reply holds under a spelling the register was not asked about. So nothing is
    /// written, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_value_stored_before_the_key_is_never_written_onto_a_contact()
    {
        using var account = Planted(linkedIn: null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", profile: ReplyProfileKeys.LeftAsItStood)));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.DoesNotContain(account.Calls, call => call.Command == "contact update");
        Assert.Null((string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The address in the arguments is what the person approving read, so an account Reply holds under another one — or
    /// under one where the arguments name none, or under none where they name one — is not the account that was approved.
    /// </summary>
    [Theory]
    [InlineData(SenderProfile, "https://www.linkedin.com/in/lena-teammate")]
    [InlineData(SenderProfile, null)]
    [InlineData(null, SenderProfile)]
    public async Task A_sender_reply_holds_under_another_address_is_not_the_one_approved_and_nothing_is_sent(string? held, string? given)
    {
        using var account = new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", senderAddress: given)));

        Assert.Equal(("sender_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(["account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The address in the arguments is Reply's own, copied from what <c>sender.list</c> returned, so a profile Reply wrote
    /// in its own spelling is the account named, however it is cased or ended.
    /// </summary>
    [Fact]
    public async Task A_sender_named_by_the_address_reply_wrote_for_it_is_the_one_approved()
    {
        const string AsWritten = "https://www.linkedin.com/in/Ann-Owner/";
        using var account = new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", AsWritten);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001", senderAddress: AsWritten)));

        Assert.Single(account.Sent);
    }

    /// <summary>
    /// An address the plugin cannot read as a profile link is nobody's: two such values are never taken for one account,
    /// so a sender Reply holds under one is refused for another.
    /// </summary>
    [Fact]
    public async Task A_sender_whose_address_is_not_a_link_is_never_taken_for_another_that_is_not_either()
    {
        using var account = new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", "ann-owner");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", senderAddress: "lena-teammate")));

        Assert.Equal(("sender_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(["account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A read of the contact that names another contact than the one asked about, that does not say whether Reply holds
    /// the person as opted out, or that carries no LinkedIn profile key at all — null or empty would say none — is no
    /// answer: nothing is written or sent on it, and the read is asked again.
    /// </summary>
    [Theory]
    [InlineData("another contact")]
    [InlineData("no opt-out mark")]
    [InlineData("no profile key")]
    public async Task A_read_of_the_contact_that_is_no_answer_sends_nothing(string what)
    {
        using var account = Planted();
        var answer = new JsonObject { ["id"] = 1001, ["email"] = null, ["linkedInUrl"] = Profile, ["isOptedOut"] = false };
        switch (what)
        {
            case "another contact":
                answer["id"] = 1002;
                break;
            case "no opt-out mark":
                answer.Remove("isOptedOut");
                break;
            default:
                answer.Remove("linkedInUrl");
                break;
        }

        account.Returns("contact get", answer);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A person Reply holds as opted out is refused before anything is written onto their contact: the stop comes first,
    /// even where the contact carries no profile the invitation would otherwise give it.
    /// </summary>
    [Fact]
    public async Task A_person_opted_out_at_reply_with_no_profile_is_refused_before_anything_is_written()
    {
        using var account = Planted(linkedIn: null, optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.DoesNotContain(account.Calls, call => call.Command == "contact update");
        Assert.Null((string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task A_sender_reply_holds_no_address_for_is_named_by_its_identifier_alone()
    {
        using var account = new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001", senderAddress: null)));

        Assert.Single(account.Sent);
    }

    [Theory]
    [InlineData("35")]
    [InlineData("s_34")]
    [InlineData("2147483648")]
    public async Task A_sender_reply_does_not_hold_is_not_found_and_nothing_is_sent(string sender)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", sender: sender)));

        Assert.Equal(("sender_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.True(Lines(account).All(line => line == "account list"), $"the calls were {string.Join(", ", Lines(account))}.");
        Assert.Empty(account.Sent);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("cookieInvalid")]
    public async Task A_sender_that_cannot_send_now_is_refused_before_the_invitation(string status)
    {
        using var account = Planted().WithSender(34, "linkedin", "Ann Owner", SenderProfile, status: status);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("sender_unavailable", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(status, (string?)error.Details!["provider_code"]);
        Assert.Equal(["account list"], Lines(account));
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("weekly")]
    public async Task A_sender_whose_allowance_is_spent_says_which_and_nothing_is_sent(string limit)
    {
        using var account = Planted().WithSender(34, "linkedin", "Ann Owner", SenderProfile, limit: limit);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("sender_limit_reached", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(limit, (string?)error.Details!["limit"]);
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task An_account_without_linkedin_sends_outside_a_sequence_is_refused_as_a_feature_it_lacks()
    {
        using var account = Planted().Without("directLinkedIn");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("feature_unavailable", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("directLinkedIn.featureDisabled", (string?)error.Details!["provider_code"]);
    }

    /// <summary>
    /// What Reply refuses that the plugin did not catch first — a note Reply finds too long by the field alone, a placeholder
    /// it could not fill, a contact it says has no profile — comes back in the operation's own words, and nothing went.
    /// </summary>
    [Theory]
    [InlineData("reply.invalid_request", 400, null, "message", "message_too_long", "validation")]
    [InlineData("linkedin.message_too_long", 400, "directLinkedIn.invitesNotesLimit", null, "message_too_long", "validation")]
    [InlineData("linkedin.missing_variables", 400, "directLinkedIn.missingVariables", null, "message_invalid", "validation")]
    [InlineData("contact.no_linkedin_profile", 404, "directLinkedIn.noLinkedInProfile", null, "contact_channel_conflict", "permanent")]
    public async Task A_refusal_reply_makes_of_the_invitation_is_reported_in_the_operation_s_words(
        string cliCode, int status, string? replyCode, string? field, string code, string klass)
    {
        using var account = Planted();
        account.Refuses(
            "contact linkedin-connect", cliCode, status: status, replyCode: replyCode,
            details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) },
            ids: new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal((code, klass), (error.Code, error.Class.ToString().ToLowerInvariant()));
        Assert.Empty(account.Sent);
    }

    /// <summary>An invitation whose answer was lost may have gone, and no read tells it from another: it stops for a person.</summary>
    [Fact]
    public async Task A_lost_invitation_is_ambiguous_never_repeated_and_the_person_stays_pinned()
    {
        using var account = Planted();
        account.LosesTheOutcome("contact linkedin-connect");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.False(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Writing the profile onto a contact sent nothing, so a lost answer there is asked again; the next attempt reads the
    /// contact first, finds the profile written, and invites without writing it twice.
    /// </summary>
    [Fact]
    public async Task A_lost_answer_to_the_profile_written_first_is_asked_again_and_nothing_was_sent()
    {
        using var account = Planted(linkedIn: null);
        account.LosesTheOutcome("contact update");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Empty(account.Sent);

        var mark = account.Mark();
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));
        Assert.Equal(["account list", "contact get 1001", "contact linkedin-connect"], account.CallsSince(mark).Select(call => call.Line));
        Assert.Single(account.Sent);
    }

    [Fact]
    public async Task An_answer_that_does_not_say_the_invitation_went_may_hide_one_that_did()
    {
        using var account = Planted();
        account.Returns("contact linkedin-connect", new JsonObject { ["status"] = "queued", ["contact_id"] = 1001, ["activity_id"] = 5, ["sent_at"] = null });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
    }

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    private static JsonObject Input(
        string? pinned = null, string? note = Note, string sender = "34", string? senderAddress = SenderProfile, string profile = Profile) => new()
    {
        ["args"] = new JsonObject
        {
            ["channel"] = "linkedin",
            ["sender"] = new JsonObject { ["external_id"] = sender, ["address"] = senderAddress },
            ["note"] = note,
        },
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = ContactId,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "linkedin", ["value"] = profile, ["primary"] = true }),
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
        }),
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject(),
        },
        ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK",
    };
}
