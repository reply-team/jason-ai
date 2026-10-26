using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's commands for a send outside any sequence, answered as the reply-cli release <see
/// cref="Jason.ReplyCliStub.ReplyCliRelease"/> names documents them: <c>auth whoami</c>, who the credential signs in
/// as; <c>account list</c>, the mailboxes and LinkedIn accounts a send can go from and whose each one is; <c>contact
/// linkedin-connect</c>, an invitation with or without a note; and the four messages, <c>contact send-email</c>,
/// <c>contact linkedin-message</c>, <c>contact linkedin-inmail</c> and <c>contact linkedin-voice</c>. The text travels
/// on stdin and nowhere else, and what went out is read back from the account.
/// </summary>
public class ReplyStubDirectSendTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Profile = "https://www.linkedin.com/in/marta-example";

    [Fact]
    public async Task Whoami_answers_the_signed_in_user_as_reply_gives_it()
    {
        using var account = new ReplyAccount().WithIdentity(777, "ann@example.com", 31);

        var (exit, stdout, _) = await account.RunAsync(Line("auth", "whoami"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"userId":777,"username":"ann@example.com","teamId":31}""", JsonNode.Parse(stdout)!.ToJsonString());
    }

    [Fact]
    public async Task Account_list_answers_the_mailboxes_first_then_the_linkedin_accounts_and_whose_each_one_is()
    {
        using var account = new ReplyAccount()
            .WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner", status: "cookieInvalid")
            .WithSender(9_007_199_254_740_991, "email", "Ann Owner", "ann@example.com")
            .WithSender(13, "email", null, "lena@example.com", owner: 502, status: "disconnected")
            .WithSender(35, "linkedin", "Lena", null, owner: null, status: "dailyLimitReached");

        var (exit, stdout, _) = await account.RunAsync(Line("account", "list"), Ct);

        Assert.Equal(0, exit);
        var items = JsonNode.Parse(stdout)!["items"]!.AsArray();
        Assert.Equal(
            """[{"id":9007199254740991,"channel":"email","name":"Ann Owner","address":"ann@example.com","owner_user_id":501,"status":"active","reply_status":"connected"},"""
            + """{"id":13,"channel":"email","name":null,"address":"lena@example.com","owner_user_id":502,"status":"disconnected","reply_status":"disconnected"},"""
            + """{"id":34,"channel":"linkedin","name":"Ann Owner","address":"https://www.linkedin.com/in/ann-owner","owner_user_id":501,"status":"disconnected","reply_status":"cookieInvalid"},"""
            + """{"id":35,"channel":"linkedin","name":"Lena","address":null,"owner_user_id":null,"status":"active","reply_status":"dailyLimitReached"}]""",
            items.ToJsonString());

        var (one, linkedIn, _) = await account.RunAsync(Line("account", "list", "--channel", "linkedin"), Ct);
        Assert.Equal(0, one);
        Assert.Equal([34L, 35L], JsonNode.Parse(linkedIn)!["items"]!.AsArray().Select(item => (long)item!["id"]!));

        var (bad, _, said) = await account.RunAsync(Line("account", "list", "--channel", "sms"), Ct);
        Assert.Equal((2, "usage.choice"), (bad, (string?)Error(said)["code"]));
    }

    /// <summary>
    /// Without <c>--channel</c> both lists are read, and one the plan or role does not include fails the whole command,
    /// its Reply code naming which list; asked for alone, the other list still answers.
    /// </summary>
    [Fact]
    public async Task Account_list_without_a_channel_fails_whole_when_one_list_is_not_included()
    {
        using var account = new ReplyAccount().WithSender(12, "email", "Ann", "ann@example.com").Without("linkedInAccounts");

        var (whole, _, said) = await account.RunAsync(Line("account", "list"), Ct);
        Assert.Equal(1, whole);
        Assert.Equal(("access.feature_unavailable", 403, "linkedInAccount.forbidden"), ((string?)Error(said)["code"], (int)Error(said)["status"]!, (string?)Error(said)["reply_code"]));

        var (email, stdout, _) = await account.RunAsync(Line("account", "list", "--channel", "email"), Ct);
        Assert.Equal(0, email);
        Assert.Single(JsonNode.Parse(stdout)!["items"]!.AsArray());
    }

    [Fact]
    public async Task An_invitation_takes_its_note_from_stdin_only_and_goes_out_once()
    {
        using var account = Planted();

        var (exit, stdout, _) = await account.RunAsync(Connect("--message", "-"), """{"note":"Hi Marta, we met at the meetup."}""", Ct);

        Assert.Equal(0, exit);
        var sent = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal(["activity_id", "contact_created", "contact_id", "sent_at", "status"], sent.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("sent", 1001), ((string?)sent["status"], (int)sent["contact_id"]!));
        Assert.True((long)sent["activity_id"]! > 0, $"the activity id was {sent["activity_id"]}.");
        Assert.NotNull((string?)sent["sent_at"]);
        var went = Assert.Single(account.Sent)!;
        Assert.Equal(
            """{"command":"contact linkedin-connect","contactId":1001,"accountId":34,"text":{"note":"Hi Marta, we met at the meetup."}}""",
            went.ToJsonString());

        // The note never rides on the argument list, and nothing but the note is taken.
        var (inline, _, inlineSaid) = await account.RunAsync(Connect("--message", """{"note":"x"}"""), Ct);
        Assert.Equal((2, "usage.message"), (inline, (string?)Error(inlineSaid)["code"]));
        var (extra, _, extraSaid) = await account.RunAsync(Connect("--message", "-"), """{"note":"x","subject":"y"}""", Ct);
        Assert.Equal((2, "usage.message"), (extra, (string?)Error(extraSaid)["code"]));
        var (blank, _, blankSaid) = await account.RunAsync(Connect("--message", "-"), """{"note":"   "}""", Ct);
        Assert.Equal((2, "usage.message"), (blank, (string?)Error(blankSaid)["code"]));
        Assert.Single(account.Sent);
    }

    [Fact]
    public async Task An_invitation_without_a_message_carries_no_note()
    {
        using var account = Planted();

        var (exit, _, _) = await account.RunAsync(Connect(), Ct);

        Assert.Equal(0, exit);
        Assert.Null(Assert.Single(account.Sent)!["text"]);
        Assert.Null(account.Calls[0].Body);
    }

    [Fact]
    public async Task An_invitation_to_a_connection_sends_nothing_and_says_already_connected()
    {
        using var account = Planted().WithConnection(1001, 34);

        var (exit, stdout, _) = await account.RunAsync(Connect("--message", "-"), """{"note":"Hi"}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"status":"already_connected","contact_id":1001,"contact_created":false,"activity_id":null,"sent_at":null}""", JsonNode.Parse(stdout)!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply refuses a note over 200 characters with a 400 that carries no code of its own, only the field it refused,
    /// which reply-cli reports as an invalid request naming the field; and it says so before it looks at the ids.
    /// </summary>
    [Fact]
    public async Task A_note_over_two_hundred_characters_is_refused_by_the_field_before_the_ids_are_read()
    {
        using var account = new ReplyAccount();

        var (exit, _, said) = await account.RunAsync(Connect("--message", "-"), new JsonObject { ["note"] = new string('a', 201) }.ToJsonString(), Ct);

        Assert.Equal(1, exit);
        var error = Error(said);
        Assert.Equal(("reply.invalid_request", 400, """["message"]"""), ((string?)error["code"], (int)error["status"]!, error["details"]!["fields"]!.ToJsonString()));
        Assert.Null(error["reply_code"]);
        Assert.DoesNotContain("aaaa", said, StringComparison.Ordinal);

        var (two, _, twoSaid) = await account.RunAsync(Connect("--message", "-"), new JsonObject { ["note"] = new string('a', 200) }.ToJsonString(), Ct);
        Assert.Equal((1, "contact.not_found"), (two, (string?)Error(twoSaid)["code"]));
    }

    [Theory]
    [InlineData("contact", "contact.not_found", 404)]
    [InlineData("sender", "linkedin.account_not_found", 404)]
    [InlineData("inactive", "linkedin.account_inactive", 409)]
    [InlineData("cookie", "linkedin.invalid_cookie", 409)]
    [InlineData("profile", "contact.no_linkedin_profile", 404)]
    [InlineData("feature", "linkedin.feature_disabled", 403)]
    [InlineData("braces", "linkedin.missing_variables", 400)]
    public async Task An_invitation_reply_refuses_sends_nothing_and_says_why(string what, string code, int status)
    {
        using var account = what switch
        {
            "contact" => new ReplyAccount().WithSender(34, "linkedin", "Ann", null),
            "sender" => new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile),
            "inactive" => Planted().WithSender(34, "linkedin", "Ann", null, status: "disabled"),
            "cookie" => Planted().WithSender(34, "linkedin", "Ann", null, status: "cookieInvalid"),
            "profile" => new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").WithSender(34, "linkedin", "Ann", null),
            "feature" => Planted().Without("directLinkedIn"),
            _ => Planted(),
        };
        var note = what == "braces" ? "Hi {{firstName}}" : "Hi";

        var (exit, _, said) = await account.RunAsync(Connect("--message", "-"), new JsonObject { ["note"] = note }.ToJsonString(), Ct);

        Assert.Equal(1, exit);
        Assert.Equal((code, status), ((string?)Error(said)["code"], (int)Error(said)["status"]!));
        Assert.Equal("""{"contact_id":1001,"linkedin_account_id":34}""", Error(said)["ids"]!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("weekly")]
    public async Task An_account_whose_allowance_is_spent_sends_nothing_and_says_which(string limit)
    {
        using var account = Planted().WithSender(34, "linkedin", "Ann", null, limit: limit);

        var (exit, _, said) = await account.RunAsync(Connect(), Ct);

        Assert.Equal(1, exit);
        Assert.Equal(("linkedin.limit_reached", 429, limit), ((string?)Error(said)["code"], (int)Error(said)["status"]!, (string?)Error(said)["details"]!["limit"]));
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task A_lost_invitation_has_gone_and_says_which_contact_and_account_it_was_about()
    {
        using var account = Planted();
        account.LosesTheOutcome("contact linkedin-connect");

        var (exit, _, said) = await account.RunAsync(Connect(), Ct);

        Assert.Equal((3, "outcome.unknown"), (exit, (string?)Error(said)["code"]));
        Assert.Equal("""{"contact_id":1001,"linkedin_account_id":34}""", Error(said)["ids"]!.ToJsonString());
        Assert.Single(account.Sent);
    }

    [Fact]
    public async Task An_id_that_is_not_a_reply_number_or_a_missing_one_is_a_usage_error()
    {
        using var account = Planted();

        var (contact, _, contactSaid) = await account.RunAsync(Line("contact", "linkedin-connect", "--contact-id", "abc", "--linkedin-account-id", "34"), Ct);
        var (sender, _, senderSaid) = await account.RunAsync(Line("contact", "linkedin-connect", "--contact-id", "1001"), Ct);

        Assert.Equal((2, "usage.id"), (contact, (string?)Error(contactSaid)["code"]));
        Assert.Equal((2, "usage.id"), (sender, (string?)Error(senderSaid)["code"]));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// An email names its mailbox by an identifier Reply writes in 64 bits, takes its subject and its body from stdin and
    /// nowhere else, and answers Reply's Message-ID with no moment: Reply's answer to a direct email carries none.
    /// </summary>
    [Fact]
    public async Task An_email_takes_its_subject_and_body_from_stdin_only_and_answers_no_moment()
    {
        using var account = new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").WithSender(Mailbox, "email", "Ann Owner", "ann@example.com");

        var (exit, stdout, _) = await account.RunAsync(Email("--message", "-"), """{"subject":"Tuesday","body":"<p>Hi Marta</p>"}""", Ct);

        Assert.Equal(0, exit);
        var sent = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal(["contact_created", "contact_id", "message_id", "reply_status", "sent_at", "status"], sent.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("sent", 1001, "sent"), ((string?)sent["status"], (int)sent["contact_id"]!, (string?)sent["reply_status"]));
        Assert.StartsWith("<", (string?)sent["message_id"], StringComparison.Ordinal);
        Assert.Null(sent["sent_at"]);
        var went = Assert.Single(account.Sent)!;
        Assert.Equal(("contact send-email", 1001, Mailbox), ((string?)went["command"], (int)went["contactId"]!, (long)went["accountId"]!));
        Assert.Equal(("Tuesday", "<p>Hi Marta</p>"), ((string?)went["text"]!["subject"], (string?)went["text"]!["body"]));
        Assert.Equal(["body", "subject"], went["text"]!.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal));

        var (inline, _, inlineSaid) = await account.RunAsync(Email("--message", """{"subject":"x","body":"y"}"""), Ct);
        Assert.Equal((2, "usage.message"), (inline, (string?)Error(inlineSaid)["code"]));
        var (missing, _, missingSaid) = await account.RunAsync(Email("--message", "-"), """{"subject":"x"}""", Ct);
        Assert.Equal((2, "usage.message"), (missing, (string?)Error(missingSaid)["code"]));
        var (extra, _, extraSaid) = await account.RunAsync(Email("--message", "-"), """{"subject":"x","body":"y","note":"z"}""", Ct);
        Assert.Equal((2, "usage.message"), (extra, (string?)Error(extraSaid)["code"]));
        var (past, _, pastSaid) = await account.RunAsync(Line("contact", "send-email", "--contact-id", "1001", "--email-account-id", "9007199254740992", "--message", "-"), """{"subject":"x","body":"y"}""", Ct);
        Assert.Equal((2, "usage.id"), (past, (string?)Error(pastSaid)["code"]));
        Assert.Single(account.Sent);
    }

    /// <summary>
    /// Reply's refusals of a direct email, each definite and each saying which contact and mailbox it was about. A tag
    /// Reply cannot fill, here one naming no field it holds, is refused under a code that says nothing of it.
    /// </summary>
    [Theory]
    [InlineData("contact", "contact.not_found", 404, "contact.notFound")]
    [InlineData("mailbox", "email_account.not_found", 400, "contact.emailAccountNotFound")]
    [InlineData("blacklisted", "contact.blacklisted", 400, "contact.contactInBlackList")]
    [InlineData("braces", "reply.refused", 400, "contact.unknown")]
    public async Task An_email_reply_refuses_sends_nothing_and_says_why(string what, string code, int status, string replyCode)
    {
        using var account = what switch
        {
            "contact" => new ReplyAccount().WithSender(Mailbox, "email", "Ann", "ann@example.com"),
            "mailbox" => new ReplyAccount().WithContact(1001, "marta@example.com", "Marta"),
            "blacklisted" => new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").WithBlacklisted(1001).WithSender(Mailbox, "email", "Ann", "ann@example.com"),
            _ => new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").WithSender(Mailbox, "email", "Ann", "ann@example.com"),
        };
        var subject = what == "braces" ? "Tuesday {{ day }}" : "Tuesday";

        var (exit, _, said) = await account.RunAsync(Email("--message", "-"), new JsonObject { ["subject"] = subject, ["body"] = "Hi" }.ToJsonString(), Ct);

        Assert.Equal(1, exit);
        Assert.Equal((code, status, replyCode), ((string?)Error(said)["code"], (int)Error(said)["status"]!, (string?)Error(said)["reply_code"]));
        Assert.Equal("""{"contact_id":1001,"email_account_id":9007199254740991}""", Error(said)["ids"]!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply fills a direct email's tags in for the person before it sends: the contact's fields, a custom field by its
    /// key, and the mailbox's names and address, with a fallback, a condition and its other branch. What went out is
    /// recorded as written and as rendered.
    /// </summary>
    [Fact]
    public async Task An_email_written_with_tags_is_rendered_over_the_contact_and_the_mailbox()
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta", lastName: "Alvarez", company: "Puerto Analytics", title: "Head of Growth")
            .WithCustomValue(1001, "team", "Data")
            .WithSender(Mailbox, "email", "Ann Owner", "ann@example.com");
        var text = new JsonObject
        {
            ["subject"] = "Notes for {{FirstName}}",
            ["body"] = "Hi {{ FirstName }} {{LastName}}, {{#if Title == 'Head of Growth'}}growth{{else}}thanks{{/if}} at {{Company}} for {{team}}"
                + "{{#if Title != 'CEO'}}!{{/if}} {{SendingEmail.SenderFirstName}} {{SendingEmail.SenderLastName}} ({{SendingEmail.SenderName}}, "
                + "{{SendingEmail.Email}}) to {{Email}}, {{region | 'everywhere'}}.",
        };

        var (exit, _, said) = await account.RunAsync(Email("--message", "-"), text.ToJsonString(), Ct);

        Assert.True(exit == 0, said);
        var went = Assert.Single(account.Sent)!;
        Assert.Equal(text.ToJsonString(), went["text"]!.ToJsonString());
        Assert.Equal(
            """{"subject":"Notes for Marta","body":"Hi Marta Alvarez, growth at Puerto Analytics for Data! Ann Owner (Ann Owner, ann@example.com) to marta@example.com, everywhere."}""",
            went["rendered"]!.ToJsonString());
    }

    /// <summary>
    /// Reply counts more as no value than null, empty and white space: <c>false</c> and <c>0</c> too, so a fallback and
    /// the other branch are written for them as well.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("false")]
    [InlineData("0")]
    public async Task A_field_reply_reads_as_no_value_takes_the_fallback_and_the_other_branch(string? title)
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta", title: title)
            .WithSender(Mailbox, "email", "Ann Owner", "ann@example.com");
        var text = new JsonObject { ["subject"] = "Hi {{Title | 'there'}}", ["body"] = "{{#if Title}}set{{else}}unset{{/if}}" };

        var (exit, _, said) = await account.RunAsync(Email("--message", "-"), text.ToJsonString(), Ct);

        Assert.True(exit == 0, said);
        Assert.Equal("""{"subject":"Hi there","body":"unset"}""", Assert.Single(account.Sent)!["rendered"]!.ToJsonString());
    }

    /// <summary>
    /// A comparison reads a white-space value as empty text, as it reads no value, and an operator inside its quoted
    /// text is part of the text.
    /// </summary>
    [Theory]
    [InlineData(" ", "{{#if Title == ''}}empty{{else}}set{{/if}}", "empty")]
    [InlineData("CEO", "{{#if Title != 'a==b'}}other{{else}}same{{/if}}", "other")]
    [InlineData("a==b", "{{#if Title == 'a==b'}}same{{else}}other{{/if}}", "same")]
    public async Task A_comparison_reads_white_space_as_empty_and_an_operator_in_its_quoted_text_as_text(string title, string body, string rendered)
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta", title: title)
            .WithSender(Mailbox, "email", "Ann Owner", "ann@example.com");

        var (exit, _, said) = await account.RunAsync(Email("--message", "-"), new JsonObject { ["subject"] = "Tuesday", ["body"] = body }.ToJsonString(), Ct);

        Assert.True(exit == 0, said);
        Assert.Equal(rendered, (string?)Assert.Single(account.Sent)!["rendered"]!["body"]);
    }

    /// <summary>
    /// A tag Reply has no value for and no fallback, or one never closed, is refused with Reply's own words under a
    /// code that says nothing of it, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("Hi {{Title}}", "RenderingFail: One or more values for variables are missing or incorrect.")]
    [InlineData("Hi {{region}}", "RenderingFail: One or more values for variables are missing or incorrect.")]
    [InlineData("Hi {{SendingEmail.SenderLastName}}", "RenderingFail: One or more values for variables are missing or incorrect.")]
    [InlineData("Hi {{FirstName", "Reached end of template before the expression was closed.")]
    [InlineData("Hi {{FirstName}}}", "RenderingFail: One or more values for variables are missing or incorrect.")]
    public async Task A_tag_reply_cannot_fill_is_refused_in_its_words_and_nothing_is_sent(string body, string detail)
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta")
            .WithSender(Mailbox, "email", "Ann", "ann@example.com");

        var (exit, _, said) = await account.RunAsync(Email("--message", "-"), new JsonObject { ["subject"] = "Tuesday", ["body"] = body }.ToJsonString(), Ct);

        Assert.Equal(1, exit);
        var error = Error(said);
        Assert.Equal(("reply.refused", 400, "contact.unknown", detail), ((string?)error["code"], (int)error["status"]!, (string?)error["reply_code"], (string?)error["detail"]));
        Assert.Equal("""{"contact_id":1001,"email_account_id":9007199254740991}""", error["ids"]!.ToJsonString());
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Each LinkedIn send takes its text under its own names, and the CLI refuses a text longer than Reply publishes before
    /// anything is sent, counted in characters: one outside the basic plane counts once.
    /// </summary>
    [Theory]
    [InlineData("linkedin-message", "body", 8000)]
    [InlineData("linkedin-inmail", "subject", 200)]
    [InlineData("linkedin-inmail", "body", 1900)]
    [InlineData("linkedin-voice", "script", 1000)]
    public async Task Each_linkedin_send_takes_its_own_fields_and_holds_the_lengths_reply_publishes(string verb, string field, int longest)
    {
        using var account = Planted().WithConnection(1001, 34);
        var fields = Fields[verb];

        var (exit, stdout, _) = await account.RunAsync(LinkedIn(verb), TextOf(fields, field, string.Concat(Enumerable.Repeat("😀", longest))), Ct);

        Assert.True(exit == 0, $"the longest {field} Reply publishes was refused: exit {exit}.");
        var sent = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal(["activity_id", "contact_created", "contact_id", "sent_at", "status"], sent.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("contact " + verb, (string?)Assert.Single(account.Sent)!["command"]);

        var (over, _, said) = await account.RunAsync(LinkedIn(verb), TextOf(fields, field, string.Concat(Enumerable.Repeat("😀", longest + 1))), Ct);
        Assert.Equal((2, "usage.message"), (over, (string?)Error(said)["code"]));

        var (other, _, otherSaid) = await account.RunAsync(LinkedIn(verb), new JsonObject { ["note"] = "Hi" }.ToJsonString(), Ct);
        Assert.Equal((2, "usage.message"), (other, (string?)Error(otherSaid)["code"]));
        Assert.Single(account.Sent);
    }

    /// <summary>A message and a voice message go only to a connection of the account; an InMail is how one reaches anybody else.</summary>
    [Theory]
    [InlineData("linkedin-message", true)]
    [InlineData("linkedin-voice", true)]
    [InlineData("linkedin-inmail", false)]
    public async Task A_message_to_a_person_not_connected_sends_nothing_where_the_send_needs_a_connection(string verb, bool needsOne)
    {
        using var account = Planted();

        var (exit, _, said) = await account.RunAsync(LinkedIn(verb), TextOf(Fields[verb], null, null), Ct);

        if (needsOne)
        {
            Assert.Equal(1, exit);
            Assert.Equal(("linkedin.not_connected", 409, "directLinkedIn.notConnected"), ((string?)Error(said)["code"], (int)Error(said)["status"]!, (string?)Error(said)["reply_code"]));
            Assert.Equal("""{"contact_id":1001,"linkedin_account_id":34}""", Error(said)["ids"]!.ToJsonString());
            Assert.Empty(account.Sent);
        }
        else
        {
            Assert.Equal(0, exit);
            Assert.Single(account.Sent);
        }
    }

    [Theory]
    [InlineData("linkedin-message", "braces", "linkedin.missing_variables", 400)]
    [InlineData("linkedin-inmail", "profile", "contact.no_linkedin_profile", 404)]
    [InlineData("linkedin-voice", "feature", "linkedin.feature_disabled", 403)]
    [InlineData("linkedin-message", "cookie", "linkedin.invalid_cookie", 409)]
    public async Task A_linkedin_message_reply_refuses_sends_nothing_and_says_why(string verb, string what, string code, int status)
    {
        using var account = what switch
        {
            "profile" => new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").WithSender(34, "linkedin", "Ann", null),
            "feature" => Planted().WithConnection(1001, 34).Without("directLinkedIn"),
            "cookie" => Planted().WithConnection(1001, 34).WithSender(34, "linkedin", "Ann", null, status: "cookieInvalid"),
            _ => Planted().WithConnection(1001, 34),
        };

        var (exit, _, said) = await account.RunAsync(LinkedIn(verb), TextOf(Fields[verb], Fields[verb][^1], what == "braces" ? "Hi {{firstName}}" : null), Ct);

        Assert.Equal(1, exit);
        Assert.Equal((code, status), ((string?)Error(said)["code"], (int)Error(said)["status"]!));
        Assert.Empty(account.Sent);
    }

    [Fact]
    public async Task A_lost_email_has_gone_and_says_which_contact_and_mailbox_it_was_about()
    {
        using var account = new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").WithSender(Mailbox, "email", "Ann", "ann@example.com");
        account.LosesTheOutcome("contact send-email");

        var (exit, _, said) = await account.RunAsync(Email("--message", "-"), """{"subject":"Tuesday","body":"Hi"}""", Ct);

        Assert.Equal((3, "outcome.unknown"), (exit, (string?)Error(said)["code"]));
        Assert.Equal("""{"contact_id":1001,"email_account_id":9007199254740991}""", Error(said)["ids"]!.ToJsonString());
        Assert.Single(account.Sent);
    }

    // -------------------------------------------------------------------------------------------------------
    // The person given by their fields: found first by the address the send reaches, else imported
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A send given the person by their fields looks them up by every address it was given, the one it reaches them at
    /// first, and acts only on a contact it creates: a person Reply holds under either address is named as
    /// <c>contact.already_held</c>, exit 1, with nothing sent and nobody imported. The person and the text are one object
    /// on stdin when both flags read it.
    /// </summary>
    [Theory]
    [InlineData("""{"email":"Marta@Example.com"}""")]
    [InlineData("""{"email":"marta.work@example.com","linkedin_url":"https://www.linkedin.com/in/Marta-Example","first_name":"Marta"}""")]
    public async Task A_send_given_a_person_reply_holds_sends_nothing_and_names_them_as_already_held(string person)
    {
        using var account = new ReplyAccount().WithContact(1001, "marta@example.com", "Marta", linkedIn: Profile).WithSender(Mailbox, "email", "Ann Owner", "ann@example.com");

        var (exit, _, said) = await account.RunAsync(
            Line("contact", "send-email", "--contact", "-", "--email-account-id", "9007199254740991", "--message", "-"),
            $$$"""{"contact":{{{person}}},"message":{"subject":"Tuesday","body":"Hi"}}""",
            Ct);

        Assert.Equal((1, "contact.already_held"), (exit, (string?)Error(said)["code"]));
        Assert.Equal((1001, Mailbox), ((int)Error(said)["ids"]!["contact_id"]!, (long)Error(said)["ids"]!["email_account_id"]!));
        Assert.Single(account.Contacts);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// Reply documents neither how its exact lookup nor how its import compares a value, so the two may disagree: a contact
    /// the lookup does not return can still be the one the import matches — by the email, or by the LinkedIn URL where the
    /// person has no email. The send refuses it as <c>contact.already_held</c>, naming it, and sends nothing.
    /// </summary>
    [Theory]
    [InlineData("send-email", "--email-account-id", "9007199254740991", """{"email":"marta@example.com","first_name":"Marta"}""", """{"subject":"Tuesday","body":"Hi"}""")]
    [InlineData("linkedin-inmail", "--linkedin-account-id", "34", """{"linkedin_url":"https://www.linkedin.com/in/Marta-Example","first_name":"Marta"}""", """{"subject":"Tuesday","body":"Hi"}""")]
    public async Task A_send_whose_import_matches_a_contact_the_lookup_missed_sends_nothing_and_names_it(
        string verb, string option, string sender, string person, string text)
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta", linkedIn: Profile)
            .Unlisted(1001)
            .WithSender(Mailbox, "email", "Ann Owner", "ann@example.com")
            .WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner");

        var (exit, _, said) = await account.RunAsync(
            Line("contact", verb, "--contact", "-", option, sender, "--message", "-"), $$$"""{"contact":{{{person}}},"message":{{{text}}}}""", Ct);

        Assert.Equal((1, "contact.already_held"), (exit, (string?)Error(said)["code"]));
        Assert.Equal(1001, (int)Error(said)["ids"]!["contact_id"]!);
        Assert.Single(account.Contacts);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// The combined stdin holds exactly the two members its flags name: another member, or one left out, is
    /// <c>usage.stdin</c>; a member that is not an object is refused by its own flag's reading, as reply-cli hands each flag
    /// its member. Nothing is imported or sent.
    /// </summary>
    [Theory]
    [InlineData("""{"contact":{"email":"marta@example.com","first_name":"Marta"},"message":{"subject":"Tuesday","body":"Hi"},"note":{}}""", "usage.stdin")]
    [InlineData("""{"contact":{"email":"marta@example.com","first_name":"Marta"}}""", "usage.stdin")]
    [InlineData("""{"contact":"marta@example.com","message":{"subject":"Tuesday","body":"Hi"}}""", "usage.contact")]
    [InlineData("""{"contact":{"email":"marta@example.com","first_name":"Marta"},"message":"Hi"}""", "usage.message")]
    public async Task The_combined_stdin_holds_exactly_its_two_members_each_read_by_its_own_flag(string stdin, string code)
    {
        using var account = new ReplyAccount().WithSender(Mailbox, "email", "Ann Owner", "ann@example.com");

        var (exit, _, said) = await account.RunAsync(
            Line("contact", "send-email", "--contact", "-", "--email-account-id", "9007199254740991", "--message", "-"), stdin, Ct);

        Assert.Equal((2, code), (exit, (string?)Error(said)["code"]));
        Assert.Empty(account.Contacts);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A person Reply holds nobody for is imported as the add commands import one — keyed by the LinkedIn URL when there is
    /// no email, written as its key — and then sent to; a person with no first name is Reply's refusal of the import, and
    /// nothing is sent.
    /// </summary>
    [Fact]
    public async Task A_send_given_a_person_reply_holds_nobody_for_imports_them_first()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner");

        var (nameless, _, said) = await account.RunAsync(
            Line("contact", "linkedin-inmail", "--contact", "-", "--linkedin-account-id", "34", "--message", "-"),
            """{"contact":{"linkedin_url":"https://www.linkedin.com/in/Marta-Example"},"message":{"subject":"Tuesday","body":"Hi"}}""",
            Ct);
        Assert.Equal((1, "reply.refused"), (nameless, (string?)Error(said)["code"]));
        Assert.Empty(account.Contacts);
        Assert.Empty(account.Sent);

        var (exit, stdout, _) = await account.RunAsync(
            Line("contact", "linkedin-inmail", "--contact", "-", "--linkedin-account-id", "34", "--message", "-"),
            """{"contact":{"linkedin_url":"https://www.linkedin.com/in/Marta-Example","first_name":"Marta"},"message":{"subject":"Tuesday","body":"Hi"}}""",
            Ct);

        Assert.Equal(0, exit);
        var sent = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal((1001, true), ((int)sent["contact_id"]!, (bool)sent["contact_created"]!));
        var made = Assert.Single(account.Contacts)!;
        Assert.Equal((Profile, "Marta", (string?)null), ((string?)made["linkedInUrl"], (string?)made["firstName"], (string?)made["email"]));
        Assert.Equal(1001, (int)Assert.Single(account.Sent)!["contactId"]!);
    }

    /// <summary>
    /// An invitation without a note reads stdin as the person alone; with a note, the two travel as one object, and one that
    /// is not that object is a usage error, nothing sent.
    /// </summary>
    [Fact]
    public async Task An_invitation_reads_the_person_alone_or_beside_its_note()
    {
        using var account = new ReplyAccount().WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner");
        const string person = """{"linkedin_url":"https://www.linkedin.com/in/marta-example","first_name":"Marta"}""";

        var (stray, _, straySaid) = await account.RunAsync(
            Line("contact", "linkedin-connect", "--contact", "-", "--linkedin-account-id", "34", "--message", "-"), person, Ct);
        Assert.Equal((2, "usage.stdin"), (stray, (string?)Error(straySaid)["code"]));
        Assert.Empty(account.Contacts);

        var (alone, _, _) = await account.RunAsync(Line("contact", "linkedin-connect", "--contact", "-", "--linkedin-account-id", "34"), person, Ct);
        Assert.Equal(0, alone);
        Assert.Null(Assert.Single(account.Sent)!["text"]);

        // A second person: the first is now one Reply holds, which a send given a person's fields does not go to.
        const string other = """{"linkedin_url":"https://www.linkedin.com/in/ana-example","first_name":"Ana"}""";
        var (both, _, _) = await account.RunAsync(
            Line("contact", "linkedin-connect", "--contact", "-", "--linkedin-account-id", "34", "--message", "-"),
            $$$"""{"contact":{{{other}}},"message":{"note":"Hi Ana"}}""",
            Ct);
        Assert.Equal(0, both);
        Assert.Equal("""{"note":"Hi Ana"}""", account.Sent[^1]!["text"]!.ToJsonString());
        Assert.Equal(2, account.Contacts.Count);
    }

    /// <summary>
    /// A send given a person without the address it goes to is refused before anything is asked: an email without an email,
    /// a LinkedIn send without a LinkedIn URL.
    /// </summary>
    [Fact]
    public async Task A_send_given_a_person_without_the_address_it_goes_to_is_a_usage_error()
    {
        using var account = new ReplyAccount().WithContact(1001, "marta@example.com", "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", null);

        var (exit, _, said) = await account.RunAsync(
            Line("contact", "linkedin-connect", "--contact", "-", "--linkedin-account-id", "34"), """{"email":"marta@example.com","first_name":"Marta"}""", Ct);

        Assert.Equal((2, "usage.contact"), (exit, (string?)Error(said)["code"]));
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// A LinkedIn send given a person by their email as well looks them up by the profile first and then by the email: a
    /// contact held under the email alone is <c>contact.already_held</c>, never read or given the profile, and nothing is
    /// sent.
    /// </summary>
    [Fact]
    public async Task A_linkedin_send_to_a_person_held_under_their_email_alone_sends_nothing_and_changes_nothing()
    {
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta")
            .WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner");

        var (exit, _, said) = await account.RunAsync(
            Line("contact", "linkedin-inmail", "--contact", "-", "--linkedin-account-id", "34", "--message", "-"),
            $$$"""{"contact":{"email":"marta@example.com","linkedin_url":"{{{Profile}}}","first_name":"Marta"},"message":{"subject":"Tuesday","body":"Hi"}}""",
            Ct);

        Assert.Equal((1, "contact.already_held"), (exit, (string?)Error(said)["code"]));
        Assert.Equal((1001, 34), ((int)Error(said)["ids"]!["contact_id"]!, (int)Error(said)["ids"]!["linkedin_account_id"]!));
        Assert.Null((string?)account.ContactOf(1001)!["linkedInUrl"]);
        Assert.Single(account.Contacts);
        Assert.Empty(account.Sent);
    }

    /// <summary>
    /// <c>contact update</c> acts on the contact its fields name, made or not: one the import matched though the lookup did
    /// not return it is changed, and answered with <c>contact_created: false</c>.
    /// </summary>
    [Fact]
    public async Task Contact_update_acts_on_a_contact_the_import_matched_though_the_lookup_missed_it()
    {
        using var account = new ReplyAccount().WithContact(1001, "marta@example.com", "Marta").Unlisted(1001);

        var (exit, stdout, _) = await account.RunAsync(
            Line("contact", "update", "--contact", "-", "--fields", "-"),
            """{"contact":{"email":"marta@example.com","first_name":"Marta"},"fields":{"title":"CTO"}}""",
            Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"status":"updated","contact_id":1001,"contact_created":false}""", JsonNode.Parse(stdout)!.ToJsonString());
        Assert.Equal("CTO", (string?)account.ContactOf(1001)!["title"]);
        Assert.Single(account.Contacts);
    }

    /// <summary>
    /// <c>contact update</c> takes the contact as its operand or by the contact flags, never both: a person given by their
    /// fields is found or imported, and the change is the fields alone; its custom field names are read before anybody is
    /// imported; and the answer says whether it made the contact.
    /// </summary>
    [Fact]
    public async Task Contact_update_finds_or_imports_the_person_given_by_their_fields()
    {
        using var account = new ReplyAccount().WithContact(1001, "ana@example.com", "Ana").WithCustomField(1, "leadSource");

        var (unknown, _, unknownSaid) = await account.RunAsync(
            Line("contact", "update", "--contact", "-", "--fields", "-"),
            """{"contact":{"email":"marta@example.com","first_name":"Marta"},"fields":{"custom_fields":[{"name":"colour","value":"teal"}]}}""",
            Ct);
        Assert.Equal((1, "contact.field_not_found"), (unknown, (string?)Error(unknownSaid)["code"]));
        Assert.Single(account.Contacts);

        var (made, madeOut, _) = await account.RunAsync(
            Line("contact", "update", "--contact", "-", "--fields", "-"),
            """{"contact":{"email":"marta@example.com","first_name":"Marta","title":"Head of Growth"},"fields":{"title":"CTO"}}""",
            Ct);
        Assert.Equal(0, made);
        Assert.Equal("""{"status":"updated","contact_id":1002,"contact_created":true}""", JsonNode.Parse(madeOut)!.ToJsonString());
        Assert.Equal(("Marta", "CTO"), ((string?)account.ContactOf(1002)!["firstName"], (string?)account.ContactOf(1002)!["title"]));

        var (held, heldOut, _) = await account.RunAsync(
            Line("contact", "update", "--contact", "-", "--fields", "-"),
            """{"contact":{"email":"ana@example.com","first_name":"Someone Else"},"fields":{"title":"VP"}}""",
            Ct);
        Assert.Equal(0, held);
        Assert.Equal("""{"status":"updated","contact_id":1001,"contact_created":false}""", JsonNode.Parse(heldOut)!.ToJsonString());
        Assert.Equal(("Ana", "VP"), ((string?)account.ContactOf(1001)!["firstName"], (string?)account.ContactOf(1001)!["title"]));

        var (byId, byIdOut, _) = await account.RunAsync(Line("contact", "update", "1001", "--fields", "-"), """{"title":"CEO"}""", Ct);
        Assert.Equal(0, byId);
        Assert.Equal("""{"status":"updated","contact_id":1001,"contact_created":false}""", JsonNode.Parse(byIdOut)!.ToJsonString());

        var (twice, _, twiceSaid) = await account.RunAsync(Line("contact", "update", "1001", "--contact-id", "1001", "--fields", "-"), """{"title":"CEO"}""", Ct);
        Assert.Equal((2, "usage.contact"), (twice, (string?)Error(twiceSaid)["code"]));
    }

    /// <summary>A mailbox numbered at the most a JavaScript number holds exactly, as Reply's 64-bit identifiers reach it.</summary>
    private const long Mailbox = 9_007_199_254_740_991;

    /// <summary>The fields each LinkedIn send takes on stdin, as reply-cli names them.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["linkedin-message"] = ["body"],
        ["linkedin-inmail"] = ["subject", "body"],
        ["linkedin-voice"] = ["script"],
    };

    /// <summary>Every field the send takes, written "Hi", and <paramref name="field"/> written as <paramref name="value"/>.</summary>
    private static string TextOf(string[] fields, string? field, string? value)
    {
        var text = new JsonObject();
        foreach (var name in fields)
        {
            text[name] = name == field && value is not null ? value : "Hi";
        }

        return text.ToJsonString();
    }

    private static ReplyAccount Planted() =>
        new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner");

    private static string[] Connect(params string[] message) =>
        Line(["contact", "linkedin-connect", "--contact-id", "1001", "--linkedin-account-id", "34", .. message]);

    private static string[] Email(params string[] message) =>
        Line(["contact", "send-email", "--contact-id", "1001", "--email-account-id", "9007199254740991", .. message]);

    private static string[] LinkedIn(string verb) =>
        Line("contact", verb, "--contact-id", "1001", "--linkedin-account-id", "34", "--message", "-");

    private static string[] Line(params string[] command) => ["--json", "-q", .. command];

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();
}
