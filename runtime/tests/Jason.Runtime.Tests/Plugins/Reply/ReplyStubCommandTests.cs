using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in as the plugin meets it: the sixteen work commands, answered the way reply-cli 0.12.0 documents them —
/// a result on stdout and exit 0, or one <c>{"error":…}</c> line on stderr with exit 1, 2 or 3 — from a small record of
/// what the account holds. It is a program run from outside, never a model of Reply's API.
/// </summary>
public class ReplyStubCommandTests
{
    private const string Person = """{"email":"marta@example.com","first_name":"Marta"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_version_is_the_one_the_plugin_requires()
    {
        using var account = new ReplyAccount();

        var (exit, stdout, _) = await account.RunAsync(["--version"], Ct);

        Assert.Equal(0, exit);
        Assert.Equal("0.12.0", stdout.Trim());
    }

    [Fact]
    public async Task A_sequence_is_read_as_reply_holds_it_and_one_the_account_does_not_hold_is_not_found()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11, 12).WithoutSendingAccount(7);

        var (exit, stdout, _) = await account.RunAsync(Line("sequence", "get", "7"), Ct);
        Assert.Equal(0, exit);
        var sequence = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal("active", (string?)sequence["status"]);
        Assert.False(sequence.ContainsKey("_sendingAccounts"));

        var (missing, nothing, said) = await account.RunAsync(Line("sequence", "get", "8"), Ct);
        Assert.Equal(1, missing);
        Assert.Equal(string.Empty, nothing.Trim());
        Assert.Equal("sequence.not_found", (string?)Error(said)["code"]);
    }

    [Theory]
    [InlineData("007")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public async Task An_id_that_is_not_a_reply_number_is_a_usage_error(string id)
    {
        using var account = new ReplyAccount();

        var (exit, _, stderr) = await account.RunAsync(Line("sequence", "get", id), Ct);

        Assert.Equal(2, exit);
        Assert.Equal("usage.id", (string?)Error(stderr)["code"]);
    }

    [Fact]
    public async Task An_option_the_command_does_not_take_is_refused_before_anything_happens()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active");

        var (exit, stdout, stderr) = await account.RunAsync(Line("sequence", "pause", "7", "--idempotency-key", "wi_1"), Ct);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.StartsWith("error: unknown option", stderr.Trim(), StringComparison.Ordinal);
        Assert.Equal("active", (string?)account.Sequences.Single()!["status"]);
    }

    [Fact]
    public async Task Sequence_contacts_pages_newest_first_and_counts_everybody()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11)
            .WithContact(1001, "a@example.com", "A").WithEnrollment(7, 1001, addedAt: "2026-10-01T09:00:00Z")
            .WithContact(1002, "b@example.com", "B").WithEnrollment(7, 1002, addedAt: "2026-10-02T09:00:00Z")
            .WithContact(1003, "c@example.com", "C").WithEnrollment(7, 1003, addedAt: "2026-10-03T09:00:00Z");

        var (_, first, _) = await account.RunAsync(Line("sequence", "contacts", "7", "--limit", "2"), Ct);
        var page = JsonNode.Parse(first)!.AsObject();
        Assert.Equal([1003, 1002], page["items"]!.AsArray().Select(row => (int)row!["contactId"]!));
        Assert.True((bool)page["has_more"]!);
        Assert.Equal(3, (int)page["total"]!);

        var (_, second, _) = await account.RunAsync(Line("sequence", "contacts", "7", "--limit", "2", "--offset", "2"), Ct);
        var rest = JsonNode.Parse(second)!.AsObject();
        Assert.Equal([1001], rest["items"]!.AsArray().Select(row => (int)row!["contactId"]!));
        Assert.False((bool)rest["has_more"]!);
    }

    [Fact]
    public async Task Stats_refuse_a_window_that_does_not_end_after_it_starts_before_anything_is_read()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active");

        var (refused, _, stderr) = await account.RunAsync(
            Line("sequence", "stats", "7", "--from", "2026-10-02T00:00:00Z", "--to", "2026-10-01T00:00:00Z"), Ct);
        Assert.Equal(2, refused);
        Assert.Equal("usage.stats", (string?)Error(stderr)["code"]);

        var (exit, stdout, _) = await account.RunAsync(Line("sequence", "stats", "7", "--preset", "last-month"), Ct);
        Assert.Equal(0, exit);
        Assert.Equal(0, (int)JsonNode.Parse(stdout)!["emailOverview"]!["contacted"]!);
    }

    [Fact]
    public async Task Add_contact_imports_a_person_from_stdin_and_enrols_them()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11, 12);

        var (exit, stdout, _) = await account.RunAsync(Line("sequence", "add-contact", "7", "--contact", "-"), Person, Ct);

        Assert.Equal(0, exit);
        Assert.Equal(
            """{"status":"added","sequence_id":7,"contact_id":1001,"contact_created":true,"sequence_active":true}""",
            JsonNode.Parse(stdout)!.ToJsonString());
        Assert.Equal([1001], account.EnrolledIn(7));
    }

    [Fact]
    public async Task Add_contact_without_a_first_name_is_refused_as_reply_refuses_it_even_for_somebody_it_holds()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11)
            .WithContact(1001, "marta@example.com", "Marta");

        var (exit, _, stderr) = await account.RunAsync(
            Line("sequence", "add-contact", "7", "--contact", "-"), """{"email":"marta@example.com"}""", Ct);

        Assert.Equal(1, exit);
        var error = Error(stderr);
        Assert.Equal("reply.refused", (string?)error["code"]);
        Assert.Equal("Invalid data", (string?)error["detail"]);
        Assert.Empty(account.EnrolledIn(7));
    }

    /// <summary>
    /// reply-cli trims every field of a person with JavaScript's own trim and drops one left empty, so a first name of
    /// white space never reaches Reply and the import is refused as Reply refuses a missing one. The stand-in has to
    /// draw the same line, or a test passes on a first name the real CLI would never send.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\uFEFF")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\u00A0\u3000")]
    public async Task A_first_name_of_nothing_but_white_space_is_no_first_name(string blank)
    {
        using var account = new ReplyAccount().WithList(9, "Q3 LatAm founders");
        var person = new JsonObject { ["email"] = "marta@example.com", ["first_name"] = blank }.ToJsonString();

        var (exit, _, stderr) = await account.RunAsync(Line("contact-list", "add-contact", "9", "--contact", "-"), person, Ct);

        Assert.Equal(1, exit);
        var error = Error(stderr);
        Assert.Equal("reply.refused", (string?)error["code"]);
        Assert.Equal("Invalid data", (string?)error["detail"]);
        Assert.Empty(account.Contacts);
    }

    /// <summary>The same refusal for somebody Reply holds: its import wants a first name even for a match.</summary>
    [Fact]
    public async Task A_blank_first_name_is_refused_even_for_a_person_reply_holds()
    {
        using var account = new ReplyAccount().WithList(9, "Q3 LatAm founders").WithContact(2001, "marta@example.com", "Marta");
        var before = account.Contacts.ToJsonString();

        var (exit, _, stderr) = await account.RunAsync(
            Line("contact-list", "add-contact", "9", "--contact", "-"), """{"email":"marta@example.com","first_name":" "}""", Ct);

        Assert.Equal(1, exit);
        Assert.Equal("Invalid data", (string?)Error(stderr)["detail"]);
        Assert.Equal(before, account.Contacts.ToJsonString());
        Assert.Empty(account.MembersOf(9));
    }

    /// <summary>A derived first name is an ordinary one: stored as sent, case and punctuation kept.</summary>
    [Fact]
    public async Task A_first_name_made_of_an_address_s_local_part_is_stored_as_it_was_sent()
    {
        using var account = new ReplyAccount().WithList(9, "Q3 LatAm founders");

        var (exit, _, _) = await account.RunAsync(
            Line("contact-list", "add-contact", "9", "--contact", "-"),
            """{"email":"Marta.Alvarez+q3@example.com","first_name":"Marta.Alvarez+q3"}""",
            Ct);

        Assert.Equal(0, exit);
        Assert.Equal("Marta.Alvarez+q3", (string?)Assert.Single(account.Contacts)!["firstName"]);
    }

    /// <summary>
    /// The other side of the same line: U+0085 is white space to .NET and not to JavaScript, so the real CLI keeps a
    /// first name made of it and sends it. The stand-in keeps it too, or a test fails on a name the CLI would send.
    /// </summary>
    [Fact]
    public async Task A_first_name_javascript_does_not_trim_is_kept_as_it_was_sent()
    {
        using var account = new ReplyAccount().WithList(9, "Q3 LatAm founders");

        var (exit, _, _) = await account.RunAsync(
            Line("contact-list", "add-contact", "9", "--contact", "-"),
            new JsonObject { ["email"] = "marta@example.com", ["first_name"] = "\u0085" }.ToJsonString(),
            Ct);

        Assert.Equal(0, exit);
        Assert.Equal("\u0085", (string?)Assert.Single(account.Contacts)!["firstName"]);
    }

    [Fact]
    public async Task Add_contact_for_a_person_already_taking_part_answers_already_in_sequence_and_writes_nothing()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11, 12)
            .WithContact(1001, "marta@example.com", "Marta").WithEnrollment(7, 1001);

        var (exit, stdout, _) = await account.RunAsync(
            Line("sequence", "add-contact", "7", "--contact-id", "1001", "--start-step", "2"), Ct);

        Assert.Equal(0, exit);
        Assert.Equal("already_in_sequence", (string?)JsonNode.Parse(stdout)!["status"]);
        Assert.Equal(1, (int)account.Enrollments.Single()!["currentStep"]!);
    }

    [Fact]
    public async Task Add_contact_refuses_an_opted_out_person_and_says_whom_it_learned()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11)
            .WithContact(1001, "marta@example.com", "Marta", optedOut: true);

        var (exit, _, stderr) = await account.RunAsync(Line("sequence", "add-contact", "7", "--contact", "-"), Person, Ct);

        Assert.Equal(1, exit);
        var error = Error(stderr);
        Assert.Equal("contact.opted_out", (string?)error["code"]);
        Assert.Equal("""{"sequence_id":7,"contact_id":1001}""", error["ids"]!.ToJsonString());
    }

    [Fact]
    public async Task Add_contact_starts_at_the_named_step_of_a_chain_or_says_there_is_none()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11, 12, 13)
            .WithContact(1001, "marta@example.com", "Marta");

        var (beyond, _, stderr) = await account.RunAsync(
            Line("sequence", "add-contact", "7", "--contact-id", "1001", "--start-step", "4"), Ct);
        Assert.Equal(1, beyond);
        Assert.Equal("sequence.step_not_found", (string?)Error(stderr)["code"]);

        var (exit, _, _) = await account.RunAsync(
            Line("sequence", "add-contact", "7", "--contact-id", "1001", "--start-step", "2"), Ct);
        Assert.Equal(0, exit);
        Assert.Equal(12, (int)account.Enrollments.Single()!["currentStep"]!["stepId"]!);
    }

    [Fact]
    public async Task Remove_contact_takes_a_participant_out_and_a_person_nobody_holds_takes_part_in_nothing()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11)
            .WithContact(1001, "marta@example.com", "Marta").WithEnrollment(7, 1001);

        var (_, nobody, _) = await account.RunAsync(
            Line("sequence", "remove-contact", "7", "--contact", "-"), """{"email":"nobody@example.com"}""", Ct);
        Assert.Equal("""{"status":"not_in_sequence","sequence_id":7,"contact_id":null}""", JsonNode.Parse(nobody)!.ToJsonString());

        var (exit, removed, _) = await account.RunAsync(
            Line("sequence", "remove-contact", "7", "--contact", "-"), """{"email":"MARTA@example.com"}""", Ct);
        Assert.Equal(0, exit);
        Assert.Equal("removed", (string?)JsonNode.Parse(removed)!["status"]);
        Assert.Empty(account.EnrolledIn(7));
    }

    [Fact]
    public async Task Pause_refuses_a_sequence_that_never_started_and_pauses_a_live_one()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "new").WithSequence(8, "Q4", "active");

        var (refused, _, stderr) = await account.RunAsync(Line("sequence", "pause", "7"), Ct);
        Assert.Equal(1, refused);
        Assert.Equal("sequence.not_pausable", (string?)Error(stderr)["code"]);
        Assert.Equal("sequenceAction.status", (string?)Error(stderr)["reply_code"]);

        var (exit, stdout, _) = await account.RunAsync(Line("sequence", "pause", "8"), Ct);
        Assert.Equal(0, exit);
        Assert.Equal("""{"status":"paused","sequence_id":8}""", JsonNode.Parse(stdout)!.ToJsonString());
    }

    [Fact]
    public async Task Start_with_expected_contacts_says_what_it_left_and_starts_nothing_for_another_count()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "paused", false, 11)
            .WithContact(1001, "a@example.com", "A").WithEnrollment(7, 1001)
            .WithContact(1002, "b@example.com", "B").WithEnrollment(7, 1002);

        var (moved, _, stderr) = await account.RunAsync(Line("sequence", "start", "7", "--expect-contacts", "3"), Ct);
        Assert.Equal(1, moved);
        Assert.Equal("sequence.contact_count_changed", (string?)Error(stderr)["code"]);
        Assert.Equal("""{"expected":3,"actual":2}""", Error(stderr)["details"]!.ToJsonString());
        Assert.Equal("paused", (string?)account.Sequences.Single()!["status"]);

        var (_, started, _) = await account.RunAsync(Line("sequence", "start", "7", "--expect-contacts", "2"), Ct);
        Assert.Equal(
            """{"status":"started","sequence_id":7,"previous_status":"paused","contacts":2}""",
            JsonNode.Parse(started)!.ToJsonString());

        var (_, again, _) = await account.RunAsync(Line("sequence", "start", "7", "--expect-contacts", "2"), Ct);
        Assert.Equal(
            """{"status":"already_active","sequence_id":7,"previous_status":"active","contacts":2}""",
            JsonNode.Parse(again)!.ToJsonString());
    }

    [Fact]
    public async Task A_list_add_refuses_an_opted_out_person_only_when_asked()
    {
        using var account = new ReplyAccount().WithList(9, "Q3").WithList(10, "Q4")
            .WithContact(1001, "marta@example.com", "Marta", optedOut: true);

        var (exit, stdout, _) = await account.RunAsync(Line("contact-list", "add-contact", "9", "--contact-id", "1001"), Ct);
        Assert.Equal(0, exit);
        Assert.Equal("""{"status":"added","list_id":9,"contact_id":1001,"contact_created":false}""", JsonNode.Parse(stdout)!.ToJsonString());
        Assert.Equal([1001], account.MembersOf(9));

        var (refused, _, stderr) = await account.RunAsync(
            Line("contact-list", "add-contact", "10", "--contact-id", "1001", "--refuse-opted-out"), Ct);
        Assert.Equal(1, refused);
        Assert.Equal("contact.opted_out", (string?)Error(stderr)["code"]);
        Assert.Empty(account.MembersOf(10));
    }

    [Fact]
    public async Task An_opt_out_finds_the_person_before_it_would_create_anybody()
    {
        using var account = new ReplyAccount().WithContact(1001, "Marta@Example.com", "Marta");

        var (exit, stdout, _) = await account.RunAsync(
            Line("contact", "opt-out", "--contact", "-"), """{"email":"marta@example.com"}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"status":"opted_out","contact_id":1001,"contact_created":false}""", JsonNode.Parse(stdout)!.ToJsonString());
        Assert.Equal([1001], account.OptOuts.Select(entry => (int)entry!));
        Assert.Single(account.Contacts);
    }

    [Fact]
    public async Task Inbox_list_keeps_a_thread_that_moved_exactly_at_since_and_reads_one_bucket()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active").WithThread(Thread());

        var (_, inbox, _) = await account.RunAsync(
            Line("inbox", "list", "--sequence-id", "7", "--source", "inbox", "--since", "2026-10-01T09:00:00Z"), Ct);
        Assert.Equal([501], JsonNode.Parse(inbox)!["items"]!.AsArray().Select(thread => (int)thread!["id"]!));

        var (_, sent, _) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--source", "sent"), Ct);
        Assert.Empty(JsonNode.Parse(sent)!["items"]!.AsArray());
    }

    [Fact]
    public async Task Inbox_get_leaves_the_history_out_and_takes_the_latest_email_s_subject()
    {
        using var account = new ReplyAccount().WithThread(Thread());

        var (exit, stdout, _) = await account.RunAsync(Line("inbox", "get", "501", "--limit", "1"), Ct);

        Assert.Equal(0, exit);
        var read = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal("Re: Hi", (string?)read["thread"]!["subject"]);
        Assert.False(read["thread"]!.AsObject().ContainsKey("messages"));
        Assert.Single(read["messages"]!["items"]!.AsArray());
        Assert.True((bool)read["messages"]!["has_more"]!);
    }

    [Fact]
    public async Task Inbox_list_filters_by_channel_in_reply_s_spelling_and_by_category()
    {
        var linkedIn = Thread();
        linkedIn["id"] = 502;
        linkedIn["channel"] = "linkedIn";
        linkedIn["category"] = new JsonObject { ["id"] = 3, ["name"] = "Interested" };
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active").WithThread(Thread()).WithThread(linkedIn);

        var (_, onLinkedIn, _) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--channel", "linkedin"), Ct);
        Assert.Equal([502], JsonNode.Parse(onLinkedIn)!["items"]!.AsArray().Select(thread => (int)thread!["id"]!));

        var (_, byEmail, _) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--channel", "email"), Ct);
        Assert.Equal([501], JsonNode.Parse(byEmail)!["items"]!.AsArray().Select(thread => (int)thread!["id"]!));

        var (_, filed, _) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--category-id", "3"), Ct);
        Assert.Equal([502], JsonNode.Parse(filed)!["items"]!.AsArray().Select(thread => (int)thread!["id"]!));

        var (refused, _, said) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--channel", "sms"), Ct);
        Assert.Equal(2, refused);
        Assert.Equal("usage.choice", (string?)Error(said)["code"]);
    }

    [Fact]
    public async Task Inbox_reply_takes_its_message_from_stdin_only_and_answers_on_the_thread_s_own_channel()
    {
        using var account = new ReplyAccount().WithThread(Thread());

        var (exit, stdout, _) = await account.RunAsync(
            Line("inbox", "reply", "501", "--message", "-"), """{"body":"Thursday works.","reply_to_message_id":"m2"}""", Ct);

        Assert.Equal(0, exit);
        var sent = JsonNode.Parse(stdout)!.AsObject();
        Assert.Equal(("sent", 501, "email"), ((string?)sent["status"], (int)sent["thread_id"]!, (string?)sent["channel"]));
        Assert.StartsWith("<", (string?)sent["message_id"], StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParse((string?)sent["sent_at"], System.Globalization.CultureInfo.InvariantCulture, out _), $"sent_at is '{sent["sent_at"]}'.");
        var reply = account.OutboundIn(501)[^1];
        Assert.Equal(("Thursday works.", "m2"), ((string?)reply["body"], (string?)reply["replyToMessageId"]));

        // The text never rides on the argument list, and nothing but the two fields is taken.
        var (inline, _, inlineSaid) = await account.RunAsync(Line("inbox", "reply", "501", "--message", """{"body":"x"}"""), Ct);
        Assert.Equal((2, "usage.message"), (inline, (string?)Error(inlineSaid)["code"]));
        var (copied, _, copiedSaid) = await account.RunAsync(
            Line("inbox", "reply", "501", "--message", "-"), """{"body":"x","cc":"lena@example.com"}""", Ct);
        Assert.Equal((2, "usage.message"), (copied, (string?)Error(copiedSaid)["code"]));
        Assert.Equal(2, account.OutboundIn(501).Count);
    }

    [Fact]
    public async Task Inbox_reply_refuses_before_sending_what_the_thread_cannot_take()
    {
        var linkedIn = Thread();
        linkedIn["id"] = 502;
        linkedIn["channel"] = "linkedIn";
        var other = Thread();
        other["id"] = 503;
        other["channel"] = "sms";
        using var account = new ReplyAccount().WithThread(linkedIn).WithThread(other);

        var (toOne, _, toOneSaid) = await account.RunAsync(
            Line("inbox", "reply", "502", "--message", "-"), """{"body":"Yes.","reply_to_message_id":"m2"}""", Ct);
        Assert.Equal((1, "inbox.channel_mismatch", "linkedin"), (toOne, (string?)Error(toOneSaid)["code"], (string?)Error(toOneSaid)["details"]!["thread_channel"]));

        var (neither, _, neitherSaid) = await account.RunAsync(Line("inbox", "reply", "503", "--message", "-"), """{"body":"Yes."}""", Ct);
        Assert.Equal((1, "inbox.channel_mismatch"), (neither, (string?)Error(neitherSaid)["code"]));

        var (missing, _, missingSaid) = await account.RunAsync(Line("inbox", "reply", "999", "--message", "-"), """{"body":"Yes."}""", Ct);
        Assert.Equal((1, "inbox.thread_not_found", 999), (missing, (string?)Error(missingSaid)["code"], (int)Error(missingSaid)["ids"]!["thread_id"]!));

        Assert.Single(account.OutboundIn(502));
        Assert.Single(account.OutboundIn(503));
    }

    [Fact]
    public async Task Inbox_reply_refuses_an_opted_out_person_and_says_why_a_reply_it_took_could_not_be_delivered()
    {
        var limited = Thread();
        limited["id"] = 502;
        limited["contact"] = new JsonObject { ["id"] = 1002, ["fullName"] = "Lena Okafor", ["isDeleted"] = false };
        using var account = new ReplyAccount()
            .WithContact(1001, "marta@example.com", "Marta", optedOut: true)
            .WithThread(Thread())
            .WithThread(limited)
            .FailsToDeliverIn(502, "linkedInSendingLimitExceeded");

        var (stopped, _, stoppedSaid) = await account.RunAsync(Line("inbox", "reply", "501", "--message", "-"), """{"body":"Yes."}""", Ct);
        Assert.Equal((1, "contact.opted_out"), (stopped, (string?)Error(stoppedSaid)["code"]));

        var (failed, _, failedSaid) = await account.RunAsync(Line("inbox", "reply", "502", "--message", "-"), """{"body":"Yes."}""", Ct);
        var error = Error(failedSaid);
        Assert.Equal(
            (1, "inbox.send_failed", "inboxThread.threadSendFailed", "linkedInSendingLimitExceeded"),
            (failed, (string?)error["code"], (string?)error["reply_code"], (string?)error["details"]!["status_code"]));
        Assert.Single(account.OutboundIn(501));
        Assert.Single(account.OutboundIn(502));
    }

    [Fact]
    public async Task Inbox_categories_lists_the_categories_and_categorize_files_a_thread_or_clears_it()
    {
        using var account = new ReplyAccount().WithCategory(3, "Interested").WithCategory(4, "Not interested", reserved: true).WithThread(Thread());

        var (_, listed, _) = await account.RunAsync(Line("inbox", "categories"), Ct);
        Assert.Equal(["Interested", "Not interested"], JsonNode.Parse(listed)!["items"]!.AsArray().Select(category => (string)category!["name"]!));

        var (filed, filedOut, _) = await account.RunAsync(Line("inbox", "categorize", "501", "--category-id", "3"), Ct);
        Assert.Equal(0, filed);
        Assert.Equal("""{"status":"categorized","thread_id":501,"category_id":3}""", JsonNode.Parse(filedOut)!.ToJsonString());
        Assert.Equal(3, (int)account.ThreadOf(501)["category"]!["id"]!);

        var (cleared, clearedOut, _) = await account.RunAsync(Line("inbox", "categorize", "501", "--clear"), Ct);
        Assert.Equal(0, cleared);
        Assert.Equal("""{"status":"cleared","thread_id":501,"category_id":null}""", JsonNode.Parse(clearedOut)!.ToJsonString());
        Assert.Null(account.ThreadOf(501)["category"]);

        // A live account answers a category it does not hold, on a thread it does, as a missing thread: one 404, and the
        // thread's code. The thread is left as it was.
        var (noCategory, _, noCategorySaid) = await account.RunAsync(Line("inbox", "categorize", "501", "--category-id", "9"), Ct);
        Assert.Equal(
            (1, "inbox.thread_not_found", 404, "inboxThread.notFound"),
            (noCategory, (string?)Error(noCategorySaid)["code"], (int)Error(noCategorySaid)["status"]!, (string?)Error(noCategorySaid)["reply_code"]));
        var (noThread, _, noThreadSaid) = await account.RunAsync(Line("inbox", "categorize", "999", "--category-id", "3"), Ct);
        Assert.Equal((1, "inbox.thread_not_found"), (noThread, (string?)Error(noThreadSaid)["code"]));
        var (both, _, bothSaid) = await account.RunAsync(Line("inbox", "categorize", "501", "--category-id", "3", "--clear"), Ct);
        Assert.Equal((2, "usage.inbox"), (both, (string?)Error(bothSaid)["code"]));
        Assert.Null(account.ThreadOf(501)["category"]);
    }

    [Fact]
    public async Task Mark_read_and_mark_unread_set_the_read_state_outright()
    {
        using var account = new ReplyAccount().WithThread(Thread());

        foreach (var (verb, word, read) in new[] { ("mark-read", "read", true), ("mark-read", "read", true), ("mark-unread", "unread", false), ("mark-unread", "unread", false) })
        {
            var (exit, stdout, _) = await account.RunAsync(Line("inbox", verb, "501"), Ct);
            Assert.Equal(0, exit);
            Assert.Equal($$"""{"status":"{{word}}","thread_id":501}""", JsonNode.Parse(stdout)!.ToJsonString());
            Assert.Equal(read, (bool)account.ThreadOf(501)["isRead"]!);
        }

        var (missing, _, said) = await account.RunAsync(Line("inbox", "mark-read", "999"), Ct);
        Assert.Equal((1, "inbox.thread_not_found"), (missing, (string?)Error(said)["code"]));
    }

    [Fact]
    public async Task A_scripted_ending_is_printed_as_given_spent_once_and_narrowed_to_one_argument()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active").WithThread(Thread());
        account.Refuses("inbox list", "rate_limited", status: 429, retryAfter: 30, match: "--source sent");

        var (inbox, _, _) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--source", "inbox"), Ct);
        Assert.Equal(0, inbox);

        var (limited, _, stderr) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--source", "sent"), Ct);
        Assert.Equal(1, limited);
        Assert.Equal(30, (int)Error(stderr)["retry_after"]!);

        var (again, _, _) = await account.RunAsync(Line("inbox", "list", "--sequence-id", "7", "--source", "sent"), Ct);
        Assert.Equal(0, again);
    }

    [Fact]
    public async Task A_lost_outcome_does_the_effect_and_then_exits_three_with_what_it_learned()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11);
        account.LosesTheOutcome("sequence add-contact");

        var (exit, stdout, stderr) = await account.RunAsync(Line("sequence", "add-contact", "7", "--contact", "-"), Person, Ct);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, stdout.Trim());
        Assert.Equal("outcome.unknown", (string?)Error(stderr)["code"]);
        Assert.Equal("""{"sequence_id":7,"contact_id":1001}""", Error(stderr)["ids"]!.ToJsonString());
        Assert.Equal([1001], account.EnrolledIn(7));
    }

    [Fact]
    public async Task Every_command_is_recorded_with_its_operands_and_what_it_read_from_stdin()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3", "active", false, 11);

        await account.RunAsync(Line("sequence", "add-contact", "7", "--contact", "-"), Person, Ct);

        var call = Assert.Single(account.Calls);
        Assert.Equal("sequence add-contact", call.Command);
        Assert.Equal(["7"], call.Operands);
        Assert.Equal("sequence add-contact 7", call.Line);
        Assert.Equal("-", call.Option("--contact"));
        Assert.Equal(Person, call.Body);
    }

    private static string[] Line(params string[] command) => ["--json", "-q", .. command];

    private static JsonObject Error(string stderr) =>
        JsonNode.Parse(stderr.Trim().Split('\n')[^1])!["error"]!.AsObject();

    /// <summary>One thread whose latest message is the other side's: in the inbox bucket, and not in the sent one.</summary>
    private static JsonObject Thread() => new()
    {
        ["id"] = 501,
        ["channel"] = "email",
        ["isRead"] = false,
        ["subject"] = "Re: Hi",
        ["bodyPreview"] = "Sounds good",
        ["lastActivityDate"] = "2026-10-01T09:00:00Z",
        ["isLastMessagePlanned"] = false,
        ["contact"] = new JsonObject { ["id"] = 1001, ["fullName"] = "Marta Alvarez", ["isDeleted"] = false },
        ["sequence"] = new JsonObject { ["id"] = 7, ["name"] = "Q3" },
        ["messages"] = new JsonArray(
            new JsonObject
            {
                ["channel"] = "email", ["messageId"] = "m1", ["date"] = "2026-10-01T08:00:00Z", ["body"] = "Hi",
                ["fromName"] = "Us", ["isOutbound"] = true, ["subject"] = "Hi",
            },
            new JsonObject
            {
                ["channel"] = "email", ["messageId"] = "m2", ["date"] = "2026-10-01T09:00:00Z", ["body"] = "Sounds good",
                ["fromName"] = "Marta", ["isOutbound"] = false, ["subject"] = "Re: Hi",
            }),
    };
}
