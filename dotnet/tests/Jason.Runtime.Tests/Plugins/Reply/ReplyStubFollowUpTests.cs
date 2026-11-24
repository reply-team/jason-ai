using System.Globalization;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's follow-up of a direct email, answered as the reply-cli release that takes <c>--follows</c> will: a
/// follow-up goes into the thread of the earlier direct email it names, as a reply to it, under that email's subject with
/// <c>Re: </c> before it, or it does not go. Each refusal at the request sends nothing and records nothing; one that is
/// held for a later moment reads the send it follows when it comes due; and a Reply that does not offer follow-ups answers
/// the route as one it does not have.
/// </summary>
public class ReplyStubFollowUpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const long Mailbox = 9_007_199_254_740_991;
    private const long OtherMailbox = 4242;
    private const string Key = "follow-up-key-1";

    [Fact]
    public async Task A_follow_up_goes_into_the_thread_of_the_send_it_names_under_that_subject_with_Re()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday");

        var (exit, stdout, _) = await account.RunAsync(Follow(first), """{"body":"Any news?"}""", Ct);

        Assert.Equal(0, exit);
        var sent = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal(["contact_created", "contact_id", "message_id", "reference", "reply_status", "sent_at", "state", "status"], sent.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("email-1002", "sent"), ((string?)sent["reference"], (string?)sent["state"]));
        Assert.Equal(2, account.DirectSends.Count);

        var thread = Assert.Single(account.Threads)!.AsObject();
        var (read, readOut, _) = await account.RunAsync(["--json", "-q", "inbox", "get", thread["id"]!.ToString()], Ct);
        Assert.Equal(0, read);
        var shown = JsonNode.Parse(readOut)!.AsObject();
        Assert.Equal("Re: Tuesday", (string?)shown["thread"]!["subject"]);
        var messages = shown["messages"]!["items"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal(("Tuesday", "Hi Marta"), ((string?)messages[0]!["subject"], (string?)messages[0]!["body"]));
        Assert.Equal(("Re: Tuesday", "Any news?", true), ((string?)messages[1]!["subject"], (string?)messages[1]!["body"], (bool)messages[1]!["isOutbound"]!));
        Assert.Equal((string?)messages[0]!["messageId"], (string?)messages[1]!["replyToMessageId"]);

        var (got, gotOut, _) = await account.RunAsync(["--json", "-q", "direct-send", "get", "email-1002"], Ct);
        Assert.Equal(0, got);
        Assert.Equal("email-1001", (string?)JsonNode.Parse(gotOut)!["follows"]);
        var (firstGot, firstOut, _) = await account.RunAsync(["--json", "-q", "direct-send", "get", first], Ct);
        Assert.Equal(0, firstGot);
        Assert.Null(JsonNode.Parse(firstOut)!.AsObject()["follows"]);
    }

    [Fact]
    public async Task A_subject_that_already_begins_with_Re_is_not_doubled()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Re: Tuesday");

        var (exit, _, _) = await account.RunAsync(Follow(first), """{"body":"Hi"}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal("Re: Tuesday", ThreadSubject(account));
    }

    [Fact]
    public async Task A_follow_up_takes_no_subject_and_nothing_is_recorded()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday");
        var (sent, direct, threads) = (account.Sent.Count, account.DirectSends.Count, account.Threads.Count);

        var (exit, _, said) = await account.RunAsync(Follow(first), """{"subject":"Wednesday","body":"Hi"}""", Ct);

        Assert.Equal((2, "usage.message"), (exit, (string?)Error(said)["code"]));
        Assert.Equal((sent, direct, threads), (account.Sent.Count, account.DirectSends.Count, account.Threads.Count));
    }

    [Fact]
    public async Task The_flag_is_the_send_email_command_s_alone_and_takes_a_reference_of_an_email()
    {
        using var account = Planted().WithConnection(1001, 34).WithSender(34, "linkedin", "Ann", null);
        var first = await FirstAsync(account, "Tuesday");

        var (other, _, otherSaid) = await account.RunAsync(
            ["--json", "-q", "contact", "linkedin-message", "--contact-id", "1001", "--linkedin-account-id", "34", "--message", "-", "--follows", first], """{"body":"Hi"}""", Ct);
        var (linkedIn, _, linkedInSaid) = await account.RunAsync(Follow("linkedin-1001"), """{"body":"Hi"}""", Ct);
        var (bad, _, badSaid) = await account.RunAsync(Follow("tuesday"), """{"body":"Hi"}""", Ct);

        Assert.Equal(2, other);
        Assert.Contains("--follows", otherSaid, StringComparison.Ordinal);
        Assert.Equal((2, "usage.reference"), (linkedIn, (string?)Error(linkedInSaid)["code"]));
        Assert.Equal((2, "usage.reference"), (bad, (string?)Error(badSaid)["code"]));
        Assert.Single(account.DirectSends);
    }

    [Theory]
    [InlineData("not_found", "follows.not_found", 404, "contact.followedSendNotFound")]
    [InlineData("other_contact", "follows.other_contact", 400, "contact.followedSendOtherContact")]
    [InlineData("other_mailbox", "follows.other_mailbox", 400, "contact.followedSendOtherMailbox")]
    [InlineData("cancelled", "follows.never_sent", 409, "contact.followedSendNeverSent")]
    [InlineData("failed", "follows.never_sent", 409, "contact.followedSendNeverSent")]
    [InlineData("not_gone", "follows.not_sent", 409, "contact.followedSendNotSent")]
    [InlineData("not_later", "follows.too_early", 409, "contact.followUpBeforeFollowedSend")]
    public async Task A_follow_up_refused_at_the_request_sends_nothing_and_takes_nothing(string what, string code, int status, string replyCode)
    {
        using var account = Planted();
        var first = what is "not_gone" or "not_later" or "cancelled" or "failed"
            ? await FirstAsync(account, "Tuesday", "--send-after", Moment(3))
            : await FirstAsync(account, "Tuesday");
        if (what is "cancelled" or "failed")
        {
            account.SendBecomes(first, what);
        }

        var line = what switch
        {
            "not_found" => Follow("email-999"),
            "other_contact" => Follow(first, contact: "1002"),
            "other_mailbox" => Follow(first, mailbox: OtherMailbox.ToString(CultureInfo.InvariantCulture)),
            "not_later" => [.. Follow(first), "--send-after", Moment(2)],
            "cancelled" or "failed" => [.. Follow(first), "--send-after", Moment(4)],
            _ => Follow(first),
        };
        var (sent, direct, threads) = (account.Sent.Count, account.DirectSends.Count, account.Threads.Count);

        var (exit, _, said) = await account.RunAsync(line, """{"body":"Hi"}""", Ct);

        Assert.Equal(1, exit);
        var error = Error(said);
        Assert.Equal((code, status, replyCode), ((string?)error["code"], (int)error["status"]!, (string?)error["reply_code"]));
        Assert.Equal((sent, direct, threads), (account.Sent.Count, account.DirectSends.Count, account.Threads.Count));
    }

    [Fact]
    public async Task A_follow_up_without_a_mailbox_goes_from_the_mailbox_of_the_send_it_names()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday");

        var (exit, _, _) = await account.RunAsync(
            ["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--follows", first, "--message", "-"], """{"body":"Hi"}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal(Mailbox, (long)account.Sent[1]!["accountId"]!);
    }

    [Fact]
    public async Task A_follow_up_that_waits_is_queued_and_goes_into_the_thread_when_the_send_it_names_has_gone()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday", "--send-after", Moment(2));
        var (exit, stdout, _) = await account.RunAsync([.. Follow(first), "--send-after", Moment(4)], """{"body":"Any news?"}""", Ct);
        Assert.Equal(0, exit);
        var reference = (string)JsonNode.Parse(stdout)!["reference"]!;
        Assert.Equal("queued", (string?)JsonNode.Parse(stdout)!["state"]);
        Assert.Empty(account.Threads);

        account.SendBecomes(first, "sent").FollowUpComesDue(reference);
        var (got, gotOut, _) = await account.RunAsync(["--json", "-q", "direct-send", "get", reference], Ct);

        Assert.Equal(0, got);
        var record = JsonNode.Parse(gotOut)!.AsObject();
        Assert.Equal(("sent", first), ((string?)record["state"], (string?)record["follows"]));
        Assert.NotNull(record["sent_at"]);
        Assert.Equal("Re: Tuesday", ThreadSubject(account));
        Assert.Equal(2, Assert.Single(account.Threads)!["messages"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("cancelled", "contact.followedSendNeverSent")]
    [InlineData("failed", "contact.followedSendNeverSent")]
    [InlineData("waiting", "contact.followedSendNotSent")]
    public async Task A_follow_up_that_comes_due_reads_the_send_it_names_and_fails_final_where_that_will_not_have_gone(string then, string code)
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday", "--send-after", Moment(3));
        string[] line = then == "waiting" ? [.. Follow(first), "--schedule-id", "7"] : [.. Follow(first), "--send-after", Moment(4)];
        var (exit, stdout, _) = await account.RunAsync(line, """{"body":"Any news?"}""", Ct);
        Assert.Equal(0, exit);
        var reference = (string)JsonNode.Parse(stdout)!["reference"]!;
        if (then != "waiting")
        {
            account.SendBecomes(first, then);
        }

        account.FollowUpComesDue(reference);
        var (got, gotOut, _) = await account.RunAsync(["--json", "-q", "direct-send", "get", reference], Ct);

        Assert.Equal(0, got);
        var record = JsonNode.Parse(gotOut)!.AsObject();
        Assert.Equal(("failed", code, first), ((string?)record["state"], (string?)record["failure_code"], (string?)record["follows"]));
        Assert.NotNull(record["failure_reason"]);
        Assert.Empty(account.Threads);
    }

    [Fact]
    public async Task A_repeat_under_the_same_key_and_reference_answers_the_first_send_and_under_another_reference_is_another_request()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday");
        var second = await FirstAsync(account, "Wednesday");

        var (exit, one, _) = await account.RunAsync([.. Follow(first), "--idempotency-key", Key], """{"body":"Hi"}""", Ct);
        var (again, two, _) = await account.RunAsync([.. Follow(first), "--idempotency-key", Key], """{"body":"Hi"}""", Ct);
        var (other, _, said) = await account.RunAsync([.. Follow(second), "--idempotency-key", Key], """{"body":"Hi"}""", Ct);

        Assert.Equal((0, 0), (exit, again));
        Assert.Equal(JsonNode.Parse(one)!.ToJsonString(), JsonNode.Parse(two)!.ToJsonString());
        Assert.Equal((1, "idempotency_key.reused"), (other, (string?)Error(said)["code"]));
        Assert.Equal(3, account.DirectSends.Count);
    }

    [Fact]
    public async Task A_mailbox_that_cannot_place_a_follow_up_fails_it_under_its_own_code_and_the_read_says_so()
    {
        using var account = Planted();
        var first = await FirstAsync(account, "Tuesday");
        account.CannotPlaceTheNextFollowUp();

        var (exit, _, said) = await account.RunAsync(Follow(first), """{"body":"Hi"}""", Ct);

        Assert.Equal(1, exit);
        var error = Error(said);
        Assert.Equal(("follows.not_placed", "contact.followUpNotPlaced", "email-1002"), ((string?)error["code"], (string?)error["reply_code"], (string?)error["details"]!["reference"]));
        var (got, gotOut, _) = await account.RunAsync(["--json", "-q", "direct-send", "get", "email-1002"], Ct);
        Assert.Equal(0, got);
        Assert.Equal(("failed", "contact.followUpNotPlaced"), ((string?)JsonNode.Parse(gotOut)!["state"], (string?)JsonNode.Parse(gotOut)!["failure_code"]));
        Assert.Single(account.Threads);
        Assert.Single(account.Threads[0]!["messages"]!.AsArray());
    }

    [Fact]
    public async Task A_reply_that_does_not_offer_follow_ups_answers_the_route_as_one_it_does_not_have()
    {
        using var account = Planted().NotOffering("direct_send_follow_up");
        var first = await FirstAsync(account, "Tuesday");
        var (sent, direct) = (account.Sent.Count, account.DirectSends.Count);

        var (exit, _, said) = await account.RunAsync(Follow(first), """{"body":"Hi"}""", Ct);

        Assert.Equal(1, exit);
        var error = Error(said);
        Assert.Equal(("follows.not_available", 404), ((string?)error["code"], (int)error["status"]!));
        Assert.Null(error["reply_code"]);
        Assert.Equal((sent, direct), (account.Sent.Count, account.DirectSends.Count));

        // The first letter's own route is untouched.
        var (plain, _, _) = await account.RunAsync(Send(), """{"subject":"Wednesday","body":"Hi"}""", Ct);
        Assert.Equal(0, plain);
    }

    private static ReplyAccount Planted() =>
        new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta")
            .WithContact(1002, "lena@example.com", "Lena")
            .WithSender(Mailbox, "email", "Ann Owner", "ann@example.com")
            .WithSender(OtherMailbox, "email", "Bo Owner", "bo@example.com");

    /// <summary>A first direct email to the first contact from the first mailbox, and the reference Reply gave it.</summary>
    private static async Task<string> FirstAsync(ReplyAccount account, string subject, params string[] more)
    {
        var (exit, stdout, said) = await account.RunAsync(
            [.. Send(), .. more], new JsonObject { ["subject"] = subject, ["body"] = "Hi Marta" }.ToJsonString(), Ct);
        Assert.True(exit == 0, said);
        return (string)JsonNode.Parse(stdout)!["reference"]!;
    }

    private static string[] Send() =>
        ["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--email-account-id", Mailbox.ToString(CultureInfo.InvariantCulture), "--message", "-"];

    private static string[] Follow(string reference, string contact = "1001", string? mailbox = null) =>
        ["--json", "-q", "contact", "send-email", "--contact-id", contact, "--email-account-id", mailbox ?? Mailbox.ToString(CultureInfo.InvariantCulture), "--message", "-", "--follows", reference];

    private static string Moment(int hours) =>
        DateTimeOffset.UtcNow.AddHours(hours).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? ThreadSubject(ReplyAccount account) =>
        (string?)(Assert.Single(account.Threads)!["messages"]!.AsArray().Last()!["subject"]);

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();
}
