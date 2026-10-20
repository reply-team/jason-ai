using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's commands for a send outside any sequence, answered as reply-cli 0.14.0 documents them: <c>auth whoami</c>,
/// who the credential signs in as; <c>account list</c>, the mailboxes and LinkedIn accounts a send can go from and whose
/// each one is; and <c>contact linkedin-connect</c>, an invitation with or without a note. The text travels on stdin and
/// nowhere else, and what went out is read back from the account.
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
        Assert.Equal(["activity_id", "contact_id", "sent_at", "status"], sent.Select(member => member.Key).Order(StringComparer.Ordinal));
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
        Assert.Equal("""{"status":"already_connected","contact_id":1001,"activity_id":null,"sent_at":null}""", JsonNode.Parse(stdout)!.ToJsonString());
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

    private static ReplyAccount Planted() =>
        new ReplyAccount().WithContact(1001, null, "Marta", linkedIn: Profile).WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner");

    private static string[] Connect(params string[] message) =>
        Line(["contact", "linkedin-connect", "--contact-id", "1001", "--linkedin-account-id", "34", .. message]);

    private static string[] Line(params string[] command) => ["--json", "-q", .. command];

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();
}
