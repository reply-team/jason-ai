using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in as the plugin meets it: the eleven work commands, answered the way reply-cli 0.11.0 documents them —
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
        Assert.Equal("0.11.0", stdout.Trim());
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
