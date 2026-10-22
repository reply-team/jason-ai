using System.Text.Json.Nodes;
using Jason.Contracts.Channels;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>message.send</c> as the official package performs it, against the stand-in: the subject and the text held to what
/// Reply would send as written, the account to the address the person approving read, the person found and their contact
/// read before anything goes — opted out at Reply is a stop, another address or profile is a refusal, no LinkedIn profile
/// is filled — and then the one command made for the format, with the text on stdin: <c>reply contact send-email</c>, the
/// text written as HTML, or <c>linkedin-message</c>, <c>linkedin-inmail</c> or <c>linkedin-voice</c>. A person Reply holds
/// nobody for is imported by that same command, from their fields on stdin beside the text. What went out is read back
/// from the account.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyMessageSendTests
{
    private const string Operation = "message.send";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string Address = "marta@example.com";
    private const string Profile = "https://www.linkedin.com/in/marta-example";
    private const string Mailbox = "ann@example.com";
    private const string SenderProfile = "https://www.linkedin.com/in/ann-owner";
    private const string Subject = "The Lisbon meetup";
    private const string Body = "Hi Marta, thanks for the chat at the meetup.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A person reached by email, and the caller's mailbox.</summary>
    private static ReplyAccount ByEmail(string? email = Address, bool optedOut = false) =>
        new ReplyAccount().WithContact(1001, email, "Marta", optedOut: optedOut).WithSender(12, "email", "Ann Owner", Mailbox);

    /// <summary>A person reached on LinkedIn, the caller's LinkedIn account, and the two connected.</summary>
    private static ReplyAccount OnLinkedIn(string? linkedIn = Profile, bool optedOut = false, bool connected = true)
    {
        var account = new ReplyAccount().WithContact(1001, null, "Marta", optedOut: optedOut, linkedIn: linkedIn).WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        return connected ? account.WithConnection(1001, 34) : account;
    }

    // -------------------------------------------------------------------------------------------------------
    // Each format, its command and what it carries
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Each format runs the one command made for it, naming the account by the option the CLI takes for that channel, and
    /// carries its text on stdin under the CLI's own names: a voice message's text is the script Reply speaks. Nothing but
    /// the account's list, the contact's read and the send is asked; and no moment is passed on, whatever Reply answers.
    /// </summary>
    [Theory]
    [InlineData("email", "contact send-email", "--email-account-id", "12", """{"subject":"The Lisbon meetup","body":"Hi Marta, thanks for the chat at the meetup."}""", "<60001.direct@mail.example.com>")]
    [InlineData("linkedin_message", "contact linkedin-message", "--linkedin-account-id", "34", """{"body":"Hi Marta, thanks for the chat at the meetup."}""", "70001")]
    [InlineData("linkedin_inmail", "contact linkedin-inmail", "--linkedin-account-id", "34", """{"subject":"The Lisbon meetup","body":"Hi Marta, thanks for the chat at the meetup."}""", "70001")]
    [InlineData("linkedin_voice", "contact linkedin-voice", "--linkedin-account-id", "34", """{"script":"Hi Marta, thanks for the chat at the meetup."}""", "70001")]
    public async Task Each_format_runs_its_own_command_with_its_text_on_stdin(string format, string command, string option, string sender, string stdin, string identifier)
    {
        var channel = format == "email" ? "email" : "linkedin";
        using var account = channel == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(format, pinned: "1001"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!.AsObject();
        Assert.Equal((ContactId, "sent", channel, format, sender), ((string?)item["contact_id"], (string?)item["status"], (string?)item["channel"], (string?)item["format"], (string?)item["sender_external_id"]));
        Assert.Equal(identifier, (string?)item["message"]!["external_id"]);
        Assert.Null(item["message"]!["sent_at"]);
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());

        Assert.Equal(["account list", "contact get 1001", command], Lines(account));
        Assert.Equal(channel, account.Calls[0].Option("--channel"));
        var send = account.Calls[^1];
        Assert.Equal(["--json", "-q", .. command.Split(' '), "--contact-id", "1001", option, sender, "--message", "-"], send.Args);
        Assert.Equal(stdin, JsonNode.Parse(send.Body!)!.ToJsonString());
        var went = Assert.Single(account.Sent)!;
        Assert.Equal((command, 1001, long.Parse(sender, System.Globalization.CultureInfo.InvariantCulture)), ((string?)went["command"], (int)went["contactId"]!, (long)went["accountId"]!));
    }

    /// <summary>
    /// Reply takes an email's body as HTML, and the text a person approved is plain text: every character HTML reads as
    /// markup is escaped, and every line break is one <c>&lt;br&gt;</c>, so the recipient reads exactly the text approved.
    /// Spaces and tabs stay as written. The subject is a header, and goes as written.
    /// </summary>
    [Fact]
    public async Task An_email_s_text_is_written_as_html_that_shows_exactly_the_text()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        const string written = "Tom & Jerry <tom@example.com>\r\nsaid \"5 > 3\" and 'yes'.\nThen\rnothing\u2028more\u2029  two  spaces\tand a tab.";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001", subject: "Tom & Jerry <1 > 0>", body: written)));

        var text = Assert.Single(account.Sent)!["text"]!;
        Assert.Equal(
            "Tom &amp; Jerry &lt;tom@example.com&gt;<br>said &quot;5 &gt; 3&quot; and &#39;yes&#39;.<br>Then<br>nothing<br>more<br>  two  spaces\tand a tab.",
            (string?)text["body"]);
        Assert.Equal("Tom & Jerry <1 > 0>", (string?)text["subject"]);
    }

    /// <summary>
    /// Only an email's text is written as HTML: a LinkedIn text of every format goes as written, so its recipient never
    /// reads an escape or a break tag the person approving did not write. A voice message's text is the script, and an
    /// InMail's subject goes as written too.
    /// </summary>
    [Theory]
    [InlineData("linkedin_message", "body")]
    [InlineData("linkedin_inmail", "body")]
    [InlineData("linkedin_voice", "script")]
    public async Task A_linkedin_text_goes_exactly_as_written(string format, string field)
    {
        using var account = OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        const string written = "Tom & Jerry <tom@example.com>\nsaid \"5 > 3\" and 'yes'.";
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(format, pinned: "1001", subject: "Tom & Jerry <1 > 0>", body: written)));

        var text = Assert.Single(account.Sent)!["text"]!;
        Assert.Equal(written, (string?)text[field]);
        if (format == "linkedin_inmail")
        {
            Assert.Equal("Tom & Jerry <1 > 0>", (string?)text["subject"]);
        }
    }

    /// <summary>
    /// The lengths the document holds each format to are the ones the CLI takes: the longest text of each format the
    /// document allows, counted in characters, goes through the CLI and out.
    /// </summary>
    [Theory]
    [InlineData("email", 12000, 998)]
    [InlineData("linkedin_message", 8000, 0)]
    [InlineData("linkedin_inmail", 1900, 200)]
    [InlineData("linkedin_voice", 1000, 0)]
    public async Task The_longest_text_the_document_allows_is_one_the_cli_takes(string format, int longest, int subject)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var body = string.Concat(Enumerable.Repeat("😀", longest));
        var input = Input(format, pinned: "1001", subject: subject == 0 ? null : string.Concat(Enumerable.Repeat("😀", subject)), body: body);

        var result = await InvokeAsync(api, input);

        Assert.True(result.Outcome is InvocationOutcome.Succeeded, $"the longest {format} the document allows did not go: {(result.Outcome as InvocationOutcome.Failed)?.Error.Code}.");
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Reply reads a double opening brace as the start of a placeholder of its own, so a subject or a text with one is
    /// refused before anything is called, whether or not it closes and whatever the format: a subject is content too.
    /// </summary>
    [Theory]
    // Critical: no provider template expansion.
    [InlineData("email", "Hi {{firstName}}", null, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X3)])]
    [InlineData("email", null, "Braces {{ stay as typed.")]
    [InlineData("linkedin_inmail", "{{ company }} and us", null)]
    [InlineData("linkedin_message", null, "Hi {{firstName}}")]
    [InlineData("linkedin_voice", null, "Hi {{ name }}")]
    public async Task A_double_opening_brace_in_the_subject_or_the_text_is_refused_before_anything_is_called(string format, string? subject, string? body)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var takesSubject = format is "email" or "linkedin_inmail";
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001", subject: takesSubject ? subject ?? Subject : null, body: body ?? Body)));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The document ties a message's format to its channel, and the claim holds it there; the package holds it too, behind
    /// both: an email's format is the email channel's and every other format is LinkedIn's, so a format of the other
    /// channel is refused before anything is called, even where the input reaches the package without the document read.
    /// </summary>
    [Theory]
    [InlineData("email", "linkedin_message")]
    [InlineData("linkedin", "email")]
    public async Task A_format_of_the_other_channel_is_refused_before_anything_is_called_though_the_document_was_not_read(string channel, string format)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", linkedIn: Profile)
            .WithSender(12, "email", "Ann Owner", Mailbox).WithSender(34, "linkedin", "Ann Owner", SenderProfile).WithConnection(1001, 34);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(format, pinned: "1001");
        input["args"]!["channel"] = channel;
        var error = ReplyOperations.Failed(Operation, await InvokeUnreadAsync(api, input));

        Assert.Equal(("message_invalid", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
        Assert.Empty(account.Sent);
    }

    // -------------------------------------------------------------------------------------------------------
    // The sender
    // -------------------------------------------------------------------------------------------------------

    /// <summary>Reply numbers its mailboxes in 64 bits, so a mailbox past 32 bits is named and sent from.</summary>
    [Fact]
    public async Task A_mailbox_numbered_past_thirty_two_bits_is_named_and_sent_from()
    {
        const long Large = 9_007_199_254_740_991;
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(Large, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001", sender: "9007199254740991")))["items"]![0]!;

        Assert.Equal("9007199254740991", (string?)item["sender_external_id"]);
        Assert.Equal("9007199254740991", account.Calls[^1].Option("--email-account-id"));
        Assert.Equal(Large, (long)Assert.Single(account.Sent)!["accountId"]!);
    }

    /// <summary>
    /// A mailbox's address is compared as the runtime keys an address, on both sides, since both are Reply's spelling: in
    /// another case it is the account approved; one the register keys apart is another, though JavaScript would lowercase
    /// both to one text; and one the runtime would not key is nobody's, so it is never named. An address named exactly as
    /// Reply writes it, capitals and all, is the account approved: it is what <c>sender.list</c> returned.
    /// </summary>
    [Theory]
    // Critical: the approved sender is the sender.
    [InlineData("Ann@Example.COM", "ann@example.com", true, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    [InlineData("Ann@Example.COM", "Ann@Example.COM", true)]
    [InlineData("xİ@example.com", "xi\u0307@example.com", false)]
    [InlineData("ann@example", "ann@example", false)]
    public async Task A_mailbox_is_the_one_approved_only_where_the_runtime_keys_both_addresses_alike(string held, string given, bool alike)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001", senderAddress: given));

        if (alike)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("sender_mismatch", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal(["account list"], Lines(account));
        Assert.Empty(account.Sent);
    }

    [Theory]
    [InlineData("email", "13")]
    [InlineData("email", "s_12")]
    [InlineData("email", "9007199254740992")]
    [InlineData("linkedin_message", "2147483648")]
    public async Task A_sender_reply_does_not_hold_is_not_found_and_nothing_is_sent(string format, string sender)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001", sender: sender)));

        Assert.Equal(("sender_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.True(Lines(account).All(line => line == "account list"), $"the calls were {string.Join(", ", Lines(account))}.");
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task A_disconnected_mailbox_is_refused_before_the_email()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithSender(12, "email", "Ann Owner", Mailbox, status: "disconnected");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("sender_unavailable", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list"], Lines(account));
    }

    // -------------------------------------------------------------------------------------------------------
    // The person
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_person_with_no_pin_is_found_by_the_address_and_pinned_and_nobody_is_made()
    {
        using var account = ByEmail();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact find", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal($$"""{"email":"{{Address}}"}""", account.Calls[1].Body);
        Assert.Single(account.Contacts);
    }

    /// <summary>
    /// Reply holds nobody under the address: nobody is read, and the send's own command imports the person from their
    /// fields — the consumed address, never another — with the text beside them in one object on stdin, then sends. The
    /// contact it made is the one pinned.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_nobody_for_is_imported_by_the_send_and_pinned()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        var item = ReplyOperations.Succeeded(Operation, result)["items"]![0]!;
        Assert.Equal("1001", (string?)item["external_ids"]!["contact"]);
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", "contact send-email"], Lines(account));
        var send = account.Calls[^1];
        Assert.Equal(["--json", "-q", "contact", "send-email", "--contact", "-", "--email-account-id", "12", "--message", "-"], send.Args);
        Assert.Equal(
            $$$"""{"contact":{"email":"{{{Address}}}","first_name":"Marta","last_name":"Alvarez","company":"Puerto Analytics","title":"Head of Growth"},"message":{"subject":"{{{Subject}}}","body":"{{{Body}}}"}}""",
            JsonNode.Parse(send.Body!)!.ToJsonString());
        var made = Assert.Single(account.Contacts)!;
        Assert.Equal((1001, Address, "Marta"), ((int)made["id"]!, (string?)made["email"], (string?)made["firstName"]));
        Assert.Equal(1001, (int)Assert.Single(account.Sent)!["contactId"]!);
    }

    /// <summary>
    /// On LinkedIn the person is named by the consumed profile alone, as the runtime keys it, and imported keyed by it; an
    /// InMail needs no connection, so it reaches a person nobody held a moment before.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_nobody_for_on_linkedin_is_imported_by_the_profile_and_sent_to()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("linkedin_inmail"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["account list", "contact find", "contact linkedin-inmail"], Lines(account));
        var person = JsonNode.Parse(account.Calls[^1].Body!)!["contact"]!.AsObject();
        Assert.Equal(["company", "first_name", "last_name", "linkedin_url", "title"], person.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(Profile, (string?)person["linkedin_url"]);
        Assert.Equal(Profile, (string?)Assert.Single(account.Contacts)!["linkedInUrl"]);
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// A person with no first name is imported under one derived from what Jason holds, exactly as an add derives it, and
    /// a route that forbids deriving refuses the message instead: nothing is imported and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_person_with_no_first_name_is_imported_under_a_derived_one_unless_the_route_forbids_it(bool forbidden)
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var binding = forbidden ? new JsonObject { ["derive_first_name"] = false } : null;
        var result = await ReplyOperations.InvokeAsync(api, Operation, Input("email", firstName: null), Ct, binding: binding);

        if (forbidden)
        {
            var error = ReplyOperations.Failed(Operation, result);
            Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
            Assert.Equal("the route forbids deriving a first name", (string?)error.Details!["reason"]);
            Assert.Equal(["account list", "contact find"], Lines(account));
            Assert.Empty(account.Contacts);
            Assert.Empty(account.Sent);
            return;
        }

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("Alvarez", (string?)JsonNode.Parse(account.Calls[^1].Body!)!["contact"]!["first_name"]);
        Assert.Equal("Alvarez", (string?)Assert.Single(account.Contacts)!["firstName"]);
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// A profile stored before the key is no key: imported, Reply would hold it under a spelling the register was not
    /// asked about, so the message is refused before the import, as it is before a profile is written onto a contact.
    /// </summary>
    [Fact]
    public async Task A_value_stored_before_the_key_is_never_imported_and_nothing_is_sent()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_inmail", value: ReplyProfileKeys.LeftAsItStood)));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact find"], Lines(account));
        Assert.Empty(account.Contacts);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The command's own refusals of the import, in the words the add operations report them in: an account that holds
    /// no further contact, an address or a profile Reply will not take, and a second contact under the address. Nothing is
    /// sent, and nobody is pinned.
    /// </summary>
    [Theory]
    [InlineData("email", "account.contact_limit_reached", 400, "contact.limitExceeded", null, "limit_reached", "permanent")]
    [InlineData("email", "contact.invalid_email", 400, null, null, "invalid_channel_value", "validation")]
    [InlineData("email", "reply.invalid_request", 400, null, "email", "invalid_channel_value", "validation")]
    [InlineData("linkedin_inmail", "contact.invalid_linkedin_url", 400, null, null, "invalid_channel_value", "validation")]
    [InlineData("email", "contact.not_unique", 200, null, null, "contact_not_unique", "permanent")]
    public async Task A_refusal_of_the_import_is_reported_as_an_add_reports_it(
        string format, string code, int status, string? replyCode, string? field, string expected, string expectedClass)
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox).WithSender(34, "linkedin", "Ann Owner", SenderProfile);
        account.Refuses(FormatCommand(format), code, status: status, replyCode: replyCode, details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format)));

        Assert.Equal((expected, expectedClass), (error.Code, error.Class.ToString().ToLowerInvariant()));
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The lookup found nobody, and the command's own import matched a contact Reply already held, so the command sent
    /// nothing and named it. That contact is read before anything is sent, as every contact Reply holds is, and the message
    /// then goes to it by its identifier, which is pinned: one more command, never a send to a contact nobody read.
    /// </summary>
    [Fact]
    public async Task A_person_the_import_matched_though_the_lookup_missed_is_read_first_and_then_sent_to_by_its_identifier()
    {
        using var account = ByEmail().Unlisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email"));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", "contact send-email", "contact get 1001", "contact send-email"], Lines(account));
        Assert.Equal("-", account.Calls[2].Option("--contact"));
        Assert.Equal(["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--email-account-id", "12", "--message", "-"], account.Calls[^1].Args);
        Assert.Equal($$"""{"subject":"{{Subject}}","body":"{{Body}}"}""", account.Calls[^1].Body);
        Assert.Single(account.Contacts);
        Assert.Equal(1001, (int)Assert.Single(account.Sent)!["contactId"]!);
    }

    /// <summary>
    /// A contact the import matched is held to everything a contact Reply holds is held to, because it is read first: one
    /// Reply holds as opted out is a stop, on either channel, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("linkedin_inmail")]
    public async Task A_person_the_import_matched_though_the_lookup_missed_held_as_opted_out_is_suppressed_and_nothing_is_sent(string format)
    {
        using var account = (format == "email" ? ByEmail(optedOut: true) : OnLinkedIn(optedOut: true)).Unlisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format)));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", FormatCommand(format), "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The command said Reply already held the person and did not say whom, so there is no contact to read first: nothing
    /// is sent, and the attempt ends in the package's own word for it rather than as a refusal it has no word for.
    /// </summary>
    [Fact]
    public async Task A_command_that_says_the_person_is_held_without_naming_them_sends_nothing()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        account.Refuses("contact send-email", "contact.already_held", status: 200, ids: new JsonObject { ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("did not say which", error.Message, StringComparison.Ordinal);
        Assert.Null(error.ExternalIds);
        Assert.Equal(["account list", "contact find", "contact send-email"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A contact that this attempt was not pinned to — one the command's own import made, or one the lookup found — that
    /// Reply then answers it holds no longer is no stale pin: the attempt fails without calling it one, and nobody is
    /// pinned.
    /// </summary>
    [Theory]
    [InlineData("contact send-email")]
    [InlineData("contact get")]
    public async Task A_contact_reply_named_and_then_denies_is_no_stale_pin_and_is_pinned_nowhere(string denies)
    {
        using var account = denies == "contact get" ? ByEmail() : new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        account.Refuses(
            denies, "contact.not_found", status: 404, replyCode: "contact.notFound",
            ids: denies == "contact get" ? null : new JsonObject { ["contact_id"] = 1009, ["email_account_id"] = 12 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("contact.not_found", (string?)error.Details!["cli_code"]);
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A person the lookup found, with no pin, is read before anything is sent exactly as a pinned one is: one Reply holds
    /// as opted out is a stop, and the contact found is pinned on the failure.
    /// </summary>
    [Fact]
    public async Task A_person_the_lookup_found_held_as_opted_out_is_suppressed_and_nothing_is_sent()
    {
        using var account = ByEmail(optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact find", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A runtime whose documents project the person's identifier and channel alone hands over no first name, last name,
    /// company, title or time zone at all — absent, which is not the null a runtime that projects them writes for a field
    /// Jason holds nothing in. A person imported from that would be named from their address, so the message is refused
    /// before anything is called, naming the fields.
    /// </summary>
    [Theory]
    [InlineData("first_name,last_name,company,title,time_zone")]
    [InlineData("time_zone")]
    public async Task An_input_without_the_person_s_projected_fields_is_refused_before_anything_is_called(string absent)
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input("email");
        foreach (var field in absent.Split(','))
        {
            input["contacts"]![0]!.AsObject().Remove(field);
        }

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(ReplyOperations.ProjectionRefused, (error.Code, error.Class));
        Assert.Equal(ReplyOperations.ProjectionReason(absent), (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// An import whose answer was lost before the send may have been followed by the send: the command's lost answer is
    /// ambiguous and never repeated, and the contact the command had named by then is pinned.
    /// </summary>
    [Fact]
    public async Task A_lost_answer_to_a_send_that_imported_is_ambiguous_and_pins_the_contact_it_named()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann Owner", Mailbox);
        account.LosesTheOutcome("contact send-email");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Reply's contact is reached at the address the register was asked about or not at all: one Reply holds in another
    /// case is the same, one at another address — or at none — is refused, and an address is never written onto a contact.
    /// </summary>
    [Theory]
    [InlineData("Marta@Example.COM", true)]
    [InlineData("marta.work@example.com", false)]
    [InlineData(null, false)]
    public async Task An_email_goes_only_where_reply_s_contact_is_reached_at_the_consumed_address(string? held, bool alike)
    {
        using var account = ByEmail(email: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("email", pinned: "1001"));

        Assert.DoesNotContain(account.Calls, call => call.Command == "contact update");
        if (alike)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The address Reply holds is keyed by the runtime's own rule, never by JavaScript's lowercasing: a capital I with a dot
    /// at Reply is not the i with a combining dot above the register was asked about, so nothing is sent.
    /// </summary>
    [Fact]
    [Critical(Family.F1, "the key decides who the person is")]
    public async Task An_address_the_register_keys_apart_is_another_person_s_and_nothing_is_sent()
    {
        const string held = "xİ@example.com";
        var consumed = ChannelRules.KeyOf("email", "xi\u0307@example.com");
        Assert.True(consumed is not null && consumed != ChannelRules.KeyOf("email", held), $"the runtime keys '{held}' as '{ChannelRules.KeyOf("email", held)}' and the work item's as '{consumed}'.");
        using var account = ByEmail(email: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001", value: consumed)));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// An address Reply holds that is longer than the runtime stores is keyed as it stands, never cut first: cut to the
    /// bound this one would be the consumed address, and the address reached would be another. It is nobody's.
    /// </summary>
    [Fact]
    public async Task An_address_reply_holds_past_the_bound_is_never_cut_to_meet_the_consumed_one()
    {
        var consumed = new string('a', 488) + "@example.com";
        var held = consumed + "m";
        Assert.True(
            consumed.Length == 500 && ChannelRules.KeyOf("email", consumed) == consumed && ChannelRules.KeyOf("email", held) is null,
            $"an address of {consumed.Length} characters is not its own key, or one of {held.Length} keys.");
        using var account = ByEmail(email: held);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001", value: consumed)));

        Assert.True((error.Code, error.Class) == ("contact_channel_conflict", FailureClass.Permanent), $"Reply held {held.Length} characters, and the email ended {error.Code}.");
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply does not refuse a send outside a sequence to a person it holds as opted out, so its contact's mark is the stop, on either channel.</summary>
    [Theory]
    [InlineData("email")]
    [InlineData("linkedin_message")]
    public async Task A_person_reply_holds_as_opted_out_is_suppressed_and_nothing_is_sent(string format)
    {
        using var account = format == "email" ? ByEmail(optedOut: true) : OnLinkedIn(optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    /// <summary>Reply consults its blacklist on a direct email, and refuses one to a person it holds: that is a stop too.</summary>
    [Fact]
    [Critical(Family.S1, "the provider's own register")]
    public async Task A_person_on_reply_s_blacklist_is_suppressed_and_nothing_is_sent()
    {
        using var account = ByEmail().WithBlacklisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("email", pinned: "1001")));

        Assert.Equal(("suppressed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("contact.contactInBlackList", (string?)error.Details!["provider_code"]);
        Assert.Empty(account.Sent);
    }

    /// <summary>A LinkedIn message to a contact carrying no profile gives it the consumed one first, as the invitation does, and then goes.</summary>
    [Fact]
    public async Task A_contact_with_no_profile_is_given_the_consumed_one_first_and_then_messaged()
    {
        using var account = OnLinkedIn(linkedIn: null);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal(["account list", "contact get 1001", "contact update 1001", "contact linkedin-message"], Lines(account));
        Assert.Equal($$"""{"linkedin_url":"{{Profile}}"}""", account.Calls[2].Body);
        Assert.Single(account.Sent);
    }

    [Fact]
    public async Task A_contact_with_another_profile_is_not_changed_and_nothing_is_sent()
    {
        using var account = OnLinkedIn(linkedIn: "https://www.linkedin.com/in/marta-somebody-else");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_inmail", pinned: "1001")));

        Assert.Equal(("contact_channel_conflict", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["account list", "contact get 1001"], Lines(account));
        Assert.Empty(account.Sent);
    }

    // -------------------------------------------------------------------------------------------------------
    // What Reply refuses
    // -------------------------------------------------------------------------------------------------------

    /// <summary>A LinkedIn message and a voice message go only to a connection; an InMail is how one reaches anybody else.</summary>
    [Theory]
    [InlineData("linkedin_message", true)]
    [InlineData("linkedin_voice", true)]
    [InlineData("linkedin_inmail", false)]
    public async Task A_message_to_a_person_not_connected_is_refused_where_the_format_needs_a_connection(string format, bool needsOne)
    {
        using var account = OnLinkedIn(connected: false);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(format, pinned: "1001"));

        if (!needsOne)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.Single(account.Sent);
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal(("not_connected", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Contains("social_invitation.send", error.Message, StringComparison.Ordinal);
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// What Reply refuses that the plugin did not catch first comes back in the operation's own words, Reply's code in the
    /// trace, and nothing went. A field Reply refuses by itself, and a template refusal on an email that slipped past the
    /// plugin's own check, have no word of their own here: they are reported as they come.
    /// </summary>
    [Theory]
    [InlineData("linkedin_inmail", "linkedin.no_inmail_credits", 400, "directLinkedIn.noInMailCredits", null, "insufficient_credits", "permanent")]
    [InlineData("linkedin_inmail", "linkedin.sales_navigator_required", 400, "directLinkedIn.salesNavRequired", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_inmail", "linkedin.sales_navigator_disconnected", 400, "directLinkedIn.salesNavDisconnected", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_voice", "linkedin.voice_profile_missing", 400, "directLinkedIn.voiceProfileMissing", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_voice", "linkedin.voice_too_long", 400, "directLinkedIn.voiceTooLong", null, "message_too_long", "validation")]
    [InlineData("linkedin_message", "linkedin.message_too_long", 400, "directLinkedIn.messageTooLong", null, "message_too_long", "validation")]
    [InlineData("linkedin_message", "linkedin.missing_variables", 400, "directLinkedIn.missingVariables", null, "message_invalid", "validation")]
    [InlineData("linkedin_message", "linkedin.account_inactive", 409, "directLinkedIn.linkedInAccountInactive", null, "sender_unavailable", "permanent")]
    [InlineData("linkedin_message", "linkedin.feature_disabled", 403, "directLinkedIn.featureDisabled", null, "feature_unavailable", "permanent")]
    [InlineData("linkedin_message", "contact.no_linkedin_profile", 404, "directLinkedIn.noLinkedInProfile", null, "contact_channel_conflict", "permanent")]
    [InlineData("email", "email_account.not_found", 400, "contact.emailAccountNotFound", null, "sender_not_found", "permanent")]
    [InlineData("email", "contact.not_found", 404, "contact.notFound", null, "contact_not_found", "permanent")]
    [InlineData("email", "reply.invalid_request", 400, null, "body", "provider_call_failed", "permanent")]
    [InlineData("email", "reply.refused", 400, "contact.unknown", null, "provider_call_failed", "permanent")]
    public async Task A_refusal_reply_makes_of_the_message_is_reported_in_the_operation_s_words(
        string format, string cliCode, int status, string? replyCode, string? field, string code, string klass)
    {
        var channel = format == "email" ? "email" : "linkedin";
        using var account = channel == "email" ? ByEmail() : OnLinkedIn();
        account.Refuses(
            FormatCommand(format), cliCode, status: status, replyCode: replyCode,
            details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) },
            ids: channel == "email"
                ? new JsonObject { ["contact_id"] = 1001, ["email_account_id"] = 12 }
                : new JsonObject { ["contact_id"] = 1001, ["linkedin_account_id"] = 34 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal((code, klass), (error.Code, error.Class.ToString().ToLowerInvariant()));
        Assert.Equal(replyCode, (string?)error.Details!["provider_code"]);
        Assert.Empty(account.Sent);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("weekly")]
    public async Task A_sender_whose_allowance_is_spent_says_which_and_nothing_is_sent(string limit)
    {
        using var account = OnLinkedIn().WithSender(34, "linkedin", "Ann Owner", SenderProfile, limit: limit);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("linkedin_message", pinned: "1001")));

        Assert.Equal(("sender_limit_reached", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(limit, (string?)error.Details!["limit"]);
        Assert.Empty(account.Sent);
    }

    // -------------------------------------------------------------------------------------------------------
    // A lost answer
    // -------------------------------------------------------------------------------------------------------

    /// <summary>A message whose answer was lost may have gone, and no read tells it from another: it stops for a person, on every format.</summary>
    [Theory]
    // Critical: a lost send is never repeated.
    [InlineData("email", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W5)])]
    [InlineData("linkedin_message")]
    [InlineData("linkedin_inmail")]
    [InlineData("linkedin_voice")]
    public async Task A_lost_message_is_ambiguous_never_repeated_and_the_person_stays_pinned(string format)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        account.LosesTheOutcome(FormatCommand(format));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format)));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.False(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
        Assert.Equal("""{"contact":"1001"}""", error.ExternalIds!.ToJsonString());
        Assert.Single(account.Sent);
    }

    /// <summary>An answer that does not say the message went, or names it by an identifier of another shape, may come from one that went.</summary>
    [Theory]
    [InlineData("email", """{"status":"queued","contact_id":1001,"message_id":null,"sent_at":null,"reply_status":"new"}""")]
    [InlineData("email", """{"status":"sent","contact_id":1001,"message_id":17,"sent_at":null,"reply_status":"sent"}""")]
    [InlineData("linkedin_message", """{"status":"sent","contact_id":1001,"activity_id":"70001","sent_at":null}""")]
    [InlineData("linkedin_voice", """{"status":"sent","contact_id":"1001","activity_id":70001,"sent_at":null}""")]
    public async Task An_answer_that_does_not_say_the_message_went_may_hide_one_that_did(string format, string answer)
    {
        using var account = format == "email" ? ByEmail() : OnLinkedIn();
        account.Returns(FormatCommand(format), JsonNode.Parse(answer)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(format, pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
    }

    /// <summary>Reply's answer to an email names no message for a send it took without one: the message is answered null, and it went.</summary>
    [Fact]
    public async Task An_email_reply_took_without_a_message_identifier_is_sent_with_none()
    {
        using var account = ByEmail();
        account.Returns("contact send-email", new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["message_id"] = null, ["sent_at"] = null, ["reply_status"] = "new" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001")))["items"]![0]!;

        Assert.Equal("""{"external_id":null,"sent_at":null}""", item["message"]!.ToJsonString());
    }

    /// <summary>
    /// A Message-ID is answered whole or not at all: one past the 998 characters the document holds an identifier to names
    /// nothing once cut, so the message went and its identifier is none, never the start of another.
    /// </summary>
    [Theory]
    [InlineData(998, true)]
    [InlineData(999, false)]
    public async Task An_email_message_identifier_past_the_documents_bound_is_none_and_never_cut(int length, bool kept)
    {
        var identifier = "<" + new string('a', length - 2) + ">";
        using var account = ByEmail();
        account.Returns("contact send-email", new JsonObject { ["status"] = "sent", ["contact_id"] = 1001, ["message_id"] = identifier, ["sent_at"] = null, ["reply_status"] = "sent" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var item = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("email", pinned: "1001")))["items"]![0]!;

        var answered = (string?)item["message"]!["external_id"];
        Assert.True(answered == (kept ? identifier : null), $"an identifier of {length} characters was answered as {(answered is null ? "none" : $"one of {answered.Length}")}.");
    }

    // -------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------

    private static string FormatCommand(string format) => format switch
    {
        "email" => "contact send-email",
        "linkedin_message" => "contact linkedin-message",
        "linkedin_inmail" => "contact linkedin-inmail",
        _ => "contact linkedin-voice",
    };

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);

    /// <summary>The input as it stands, the document not read: the package's own checks, which hold behind the document's.</summary>
    private static Task<PluginInvocationResult> InvokeUnreadAsync(RuntimeApiFixture api, JsonObject input) =>
        PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(
                ReplyPlugins.PluginId,
                Operation,
                input,
                Binding: null,
                CorrelationId: "att_01K0REPLYMESSAGESEND",
                AttemptId: "att_01K0REPLYMESSAGESEND",
                AttemptNumber: 1,
                WorkItemId: "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK",
                CampaignId: "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"),
            Ct);

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    /// <summary>The package's own diagnostic lines, as the child wrote them on stderr.</summary>
    private static IReadOnlyList<JsonObject> PluginLines(PluginInvocationResult result)
    {
        var log = Path.Combine(result.Launch!.WorkDir, "stderr.log");
        return [.. (File.Exists(log) ? File.ReadAllText(log) : string.Empty).Split('\n')
            .Where(line => line.TrimStart().StartsWith('{'))
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(line => (string?)line["source"] == "plugin")];
    }

    /// <summary>
    /// One message of the format named, its subject where the format takes one; the channel and the account follow the
    /// format: the caller's mailbox on email, its LinkedIn account otherwise.
    /// </summary>
    private static JsonObject Input(
        string format,
        string? pinned = null,
        string? subject = Subject,
        string body = Body,
        string? sender = null,
        string? senderAddress = null,
        string? value = null,
        string? firstName = "Marta")
    {
        var channel = format == "email" ? "email" : "linkedin";
        var message = new JsonObject { ["format"] = format, ["body"] = body };
        if (format is "email" or "linkedin_inmail" && subject is not null)
        {
            message["subject"] = subject;
        }

        return new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = channel,
                ["sender"] = new JsonObject
                {
                    ["external_id"] = sender ?? (channel == "email" ? "12" : "34"),
                    ["address"] = senderAddress ?? (channel == "email" ? Mailbox : SenderProfile),
                },
                ["message"] = message,
            },
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = ContactId,
                ["first_name"] = firstName,
                ["last_name"] = "Alvarez",
                ["company"] = "Puerto Analytics",
                ["title"] = "Head of Growth",
                ["time_zone"] = null,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = value ?? (channel == "email" ? Address : Profile), ["primary"] = true }),
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
}
