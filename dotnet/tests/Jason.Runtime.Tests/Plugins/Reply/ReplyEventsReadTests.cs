using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>events.read</c> as the official package answers it, from Reply's change feed through <c>reply inbox changes</c>: one
/// moved thread per item, who moved it read from the thread's last message, a cursor of the package's own in one of three
/// forms — a read going on, a read that ended at a moment of the package's clock and is read again from an hour before
/// it, and a span — a read by date started no further back than the feed holds, and every ending of the CLI in the events
/// contract's own codes.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyEventsReadTests
{
    private const string Feed = "inbox changes";

    /// <summary>
    /// The class's moment, read once, for the threads' activity and the moments a read is handed, where only their order
    /// matters. A moment the package computes from its own clock is held to the clock read around that read instead: the
    /// class runs for minutes, so this one falls behind.
    /// </summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EventsContract Contract => EventsCatalog.Find("events.read")!;

    [Fact]
    public async Task A_page_of_the_feed_is_one_moved_thread_per_item_held_to_the_events_contract()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(30), meetingIntent: true))
            .WithThread(Replied(ReplyConversationListTests.Thread(502, Ago(20), contactId: 1002, channel: "linkedIn", sequence: 8, inbound: false)))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(10), contactId: 1003, inbound: false));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var since = Moment(Now.AddHours(-2));

        var page = Page(await ReadAsync(api, Input(since: since), Ct));

        Assert.Equal(
            JsonNode.Parse($$"""
                [{"kind":"thread_moved","id":null,"occurred_at":"{{Utc(Ago(30))}}","thread":"501","contact":"1001","campaign":"7",
                  "moved_by":"other_side","channel":"email","unread":null,"has_meeting_intent":true},
                 {"kind":"thread_moved","id":null,"occurred_at":"{{Utc(Ago(20))}}","thread":"502","contact":"1002","campaign":"8",
                  "moved_by":null,"channel":"linkedin","unread":null,"has_meeting_intent":false}]
                """)!.ToJsonString(),
            page["events"]!.ToJsonString());
        Assert.False((bool)page["has_more"]!);
        Assert.StartsWith("m|", (string)page["cursor"]!, StringComparison.Ordinal);
        Assert.DoesNotContain("Marta", page.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(["--json", "-q", "inbox", "changes", "--since", since, "--top", "100"], Assert.Single(account.Calls).Args);
    }

    /// <summary>
    /// Who moved the thread, from Reply's last message and its flag that the thread holds a reply: the other side's
    /// message is <c>other_side</c>; the account's on a thread that holds a reply names nobody, because the feed cannot
    /// say whether that reply came after the last read; the account's on one that holds none is the account's move, which
    /// is read and not kept; and nobody this package can name where Reply gave no last message. A thread moves in the feed
    /// only when a message lands on it, so a last activity later than the last message's date is never read as a move of
    /// Reply's own.
    /// </summary>
    [Theory]
    [InlineData("inbound", "other_side")]
    [InlineData("outbound", "kept out")]
    [InlineData("replied", null)]
    [InlineData("later", "other_side")]
    [InlineData("none", null)]
    public async Task Who_moved_a_thread_is_read_from_its_last_message(string last, string? mover)
    {
        var thread = ReplyConversationListTests.Thread(501, Ago(10), inbound: last is not ("outbound" or "replied"));
        if (last == "replied")
        {
            Replied(thread);
        }

        var message = thread["messages"]![0]!.AsObject();
        if (last == "later")
        {
            message["date"] = Ago(15);
        }

        if (last == "none")
        {
            thread["messages"] = new JsonArray();
        }

        using var account = new ReplyAccount().WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        if (mover == "kept out")
        {
            Assert.Empty(page["events"]!.AsArray());
            return;
        }

        Assert.Equal(mover, (string?)Assert.Single(page["events"]!.AsArray())!["moved_by"]);
    }

    /// <summary>
    /// The four answers, as Reply's item says them: the direction of the last message decides the other side's move alone;
    /// the account's message is nobody's where Reply flags a reply on the thread, and the account's move, left out of the
    /// page, where it flags none or says nothing of one.
    /// </summary>
    [Theory]
    [InlineData("""{"isOutbound":false}""", """{"hasReply":false}""", "other_side")]
    [InlineData("""{"isOutbound":true}""", """{"hasReply":true}""", null)]
    [InlineData("""{"isOutbound":true}""", """{"hasReply":false}""", "kept out")]
    [InlineData("""{"isOutbound":true}""", null, "kept out")]
    [InlineData(null, """{"hasReply":true}""", null)]
    public async Task Who_moved_a_thread_is_its_last_message_s_direction_and_reply_s_flag_of_a_reply(string? last, string? flags, string? mover)
    {
        var item = new JsonObject { ["threadId"] = 501, ["channel"] = "email", ["lastActivityDate"] = Ago(10) };
        if (last is not null)
        {
            item["lastMessage"] = JsonNode.Parse(last);
        }

        if (flags is not null)
        {
            item["flags"] = JsonNode.Parse(flags);
        }

        using var account = new ReplyAccount().Returns(Feed, new JsonObject { ["items"] = new JsonArray(item), ["has_more"] = false, ["cursor"] = "AAAA" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var events = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct))["events"]!.AsArray();

        if (mover == "kept out")
        {
            Assert.Empty(events);
            return;
        }

        Assert.Equal(mover, (string?)Assert.Single(events)!["moved_by"]);
    }

    /// <summary>
    /// A page of nothing but the account's moves hands back no events, and still says where the read goes on from and
    /// whether more follows, so the next page is read from past them.
    /// </summary>
    [Fact]
    public async Task A_page_of_the_account_s_moves_alone_is_empty_and_goes_on()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(30), inbound: false))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(20), inbound: false))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), limit: 2), Ct));
        var second = Page(await ReadAsync(api, Input(cursor: (string)first["cursor"]!, limit: 2), Ct));

        Assert.Empty(first["events"]!.AsArray());
        Assert.True((bool)first["has_more"]!);
        var going = (string)first["cursor"]!;
        Assert.StartsWith("c|", going, StringComparison.Ordinal);
        Assert.Equal(going[2..], account.Calls[1].Option("--cursor"));
        Assert.Equal(["503"], Threads(second));
    }

    /// <summary>
    /// An account's move is held to the end of a span before it is left out: one at or past the moment a span ends at ends
    /// it, though it is not kept, so nothing after it is read and the span is spent.
    /// </summary>
    [Fact]
    public async Task An_account_s_move_past_the_end_of_a_span_still_ends_it()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(30)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(20), inbound: false))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var until = Moment(Now.AddMinutes(-25));

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), until: until, limit: 2), Ct));

        Assert.Equal(["501"], Threads(page));
        Assert.False((bool)page["has_more"]!);
        Assert.Equal("u|" + until + "|", (string)page["cursor"]!);
    }

    /// <summary>A last message that does not say which way it went names nobody: no direction is guessed.</summary>
    [Theory]
    [InlineData("""{"messageId":9001,"date":"2026-10-01T09:00:00Z"}""")]
    [InlineData("""{"messageId":9001,"isOutbound":null}""")]
    [InlineData("""{"messageId":9001,"isOutbound":"true"}""")]
    [InlineData("""{"messageId":9001,"isOutbound":1}""")]
    [InlineData("""[{"isOutbound":true}]""")]
    public async Task A_last_message_that_does_not_say_its_direction_names_nobody(string last)
    {
        using var account = new ReplyAccount().Returns(Feed, JsonNode.Parse($$"""
            {"items":[{"threadId":501,"contactId":1001,"channel":"email","lastActivityDate":"{{Ago(10)}}","lastMessage":{{last}}}],
             "has_more":false,"cursor":"AAAA"}
            """)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Null((string?)Assert.Single(page["events"]!.AsArray())!["moved_by"]);
    }

    /// <summary>A channel Reply names that the events contract has no word for is <c>other</c>; the move is still read.</summary>
    [Fact]
    public async Task A_channel_the_contract_has_no_word_for_is_other()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(10), channel: "whatsApp"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        var moved = Assert.Single(page["events"]!.AsArray())!;
        Assert.Equal(("501", "other"), ((string?)moved["thread"], (string?)moved["channel"]));
    }

    /// <summary>
    /// While a read goes on, the next page is asked for from Reply's own cursor and nothing is read twice — which is also
    /// where a read the runtime stopped at its page limit goes on from. Once the feed says nothing more follows, the cursor
    /// is the moment the read ended, by the package's clock.
    /// </summary>
    [Fact]
    public async Task A_read_goes_on_from_reply_s_cursor_with_no_overlap_and_ends_at_a_moment()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(30)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(20)))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), limit: 2), Ct));
        var before = DateTimeOffset.UtcNow;
        var second = Page(await ReadAsync(api, Input(cursor: (string)first["cursor"]!, limit: 2), Ct));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(["501", "502"], Threads(first));
        Assert.True((bool)first["has_more"]!);
        var going = (string)first["cursor"]!;
        Assert.StartsWith("c|", going, StringComparison.Ordinal);
        Assert.Equal(["--json", "-q", "inbox", "changes", "--cursor", going[2..], "--top", "2"], account.Calls[1].Args);
        Assert.Equal(["503"], Threads(second));
        Assert.False((bool)second["has_more"]!);
        var ended = (string)second["cursor"]!;
        Assert.StartsWith("m|", ended, StringComparison.Ordinal);
        Assert.InRange(DateTimeOffset.Parse(ended[2..], CultureInfo.InvariantCulture), before.AddSeconds(-1), after.AddSeconds(1));
    }

    /// <summary>
    /// A read after one that read everything starts an hour before the moment that one ended: a thread's last activity is
    /// when its last message was sent, so a reply Reply takes in late lands behind the point the feed had passed, and the
    /// hour reads it still. What it reads again is the runtime's to drop by the events' keys.
    /// </summary>
    [Fact]
    public async Task A_read_after_one_that_read_everything_reads_the_hour_before_it_again()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(80)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(60)))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(5)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var ended = Now.AddMinutes(-10);

        var page = Page(await ReadAsync(api, Input(cursor: "m|" + Moment(ended)), Ct));

        Assert.Equal(["502", "503"], Threads(page));
        Assert.Equal(Moment(ended.AddHours(-1)), Assert.Single(account.Calls).Option("--since"));
        Assert.Null(account.Calls[0].Option("--cursor"));
        Assert.StartsWith("m|", (string)page["cursor"]!, StringComparison.Ordinal);
    }

    /// <summary>The moment a read ended at is read from no further back than the feed holds, and is never refused.</summary>
    [Fact]
    public async Task A_read_from_a_moment_older_than_the_feed_holds_starts_where_the_feed_starts()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow;
        var page = Page(await ReadAsync(api, Input(cursor: "m|" + Moment(Now.AddDays(-40))), Ct));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(["501"], Threads(page));
        AsksFromTheFeedsStart(account, before, after);
    }

    /// <summary>
    /// Reply's feed holds 30 days, and a read by date asks for no more than that, an hour inside it, so that the moment it
    /// asks for is still in the window when Reply reads it by Reply's own clock, which this machine's may trail. What is
    /// older is what Reply no longer holds; that is no failure.
    /// </summary>
    [Fact]
    public async Task A_read_by_date_starts_no_further_back_than_the_feed_holds()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(60)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var before = DateTimeOffset.UtcNow;
        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddDays(-90))), Ct));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(["501"], Threads(page));
        AsksFromTheFeedsStart(account, before, after);
    }

    /// <summary>
    /// A span that starts further back than the feed holds starts where the feed starts, and still ends where it was asked
    /// to: its moment is kept in the package's form, whatever form it was handed in.
    /// </summary>
    [Fact]
    public async Task A_span_that_starts_before_the_feed_holds_starts_where_the_feed_starts()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(20)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(1)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var end = Now.AddMinutes(-5);
        var until = end.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

        var before = DateTimeOffset.UtcNow;
        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddDays(-40)), until: until), Ct));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(["501"], Threads(page));
        AsksFromTheFeedsStart(account, before, after);
        Assert.Equal("u|" + Moment(DateTimeOffset.Parse(until, CultureInfo.InvariantCulture)) + "|", (string)page["cursor"]!);
    }

    /// <summary>
    /// A read going on from a cursor Reply no longer holds, or one Reply says it never issued, cannot go on: either is a
    /// cursor the source does not know, and the read by date the runtime makes next reads the account again.
    /// </summary>
    [Theory]
    [InlineData("aged")]
    [InlineData("refused")]
    public async Task A_cursor_reply_cannot_read_from_is_events_cursor_unknown_and_the_read_by_date_after_it_heals(string cursor)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var given = cursor == "aged" ? ReplyStubChangesTests.Cursor(Now.AddDays(-31), 501) : "AAAA";

        var failed = Failure(await ReadAsync(api, Input(cursor: "c|" + given), Ct));
        var healed = Page(await ReadAsync(api, Input(since: Moment(Now.AddDays(-30))), Ct));

        Assert.Equal(("events_cursor_unknown", FailureClass.Permanent), (failed.Code, failed.Class));
        Assert.Equal(given, account.Calls[0].Option("--cursor"));
        Assert.Equal(["501"], Threads(healed));
    }

    /// <summary>
    /// A refusal at 400 that Reply explained by a code of its own, of a read handed a cursor, is a cursor Reply will not
    /// read from: asked again it would be refused again, so the source does not know it, and the runtime reads by date.
    /// The same refusal of a read by date names no cursor, and is read again later.
    /// </summary>
    [Theory]
    [InlineData("c|AAAA", "events_cursor_unknown", FailureClass.Permanent)]
    [InlineData("u|2026-10-01T09:00:00.000Z|AAAA", "events_cursor_unknown", FailureClass.Permanent)]
    [InlineData(null, "events_unreachable", FailureClass.Transient)]
    public async Task A_cursor_reply_refuses_by_a_code_of_its_own_is_events_cursor_unknown(string? cursor, string code, FailureClass failure)
    {
        using var account = new ReplyAccount().Refuses(Feed, "reply.refused", 1, 400, "inboxThread.invalidPagination", "Invalid pagination.");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, cursor is null ? Input(since: Moment(Now.AddHours(-1))) : Input(cursor: cursor), Ct));

        Assert.Equal((code, failure), (failed.Code, failed.Class));
        Assert.Single(account.Calls);
    }

    [Fact]
    public async Task A_thread_reply_deleted_is_never_read()
    {
        var deleted = ReplyConversationListTests.Thread(502, Ago(20));
        deleted["_deleted"] = true;
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(30))).WithThread(deleted);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Equal(["501"], Threads(page));
    }

    /// <summary>
    /// A read of the change feed that Reply answers with a 404 carrying no code of its own is <c>events_unsupported</c>,
    /// final: nothing is asked first, and the inbound sync goes on reading the account's threads page by page.
    /// </summary>
    [Fact]
    public async Task A_feed_read_answered_with_a_404_without_reply_s_code_is_events_unsupported()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(10))).NotOffering("inbox_changes");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Equal(("events_unsupported", FailureClass.Permanent), (failed.Code, failed.Class));
        Assert.Contains("page by page", failed.Message, StringComparison.Ordinal);
        Assert.Equal([Feed], account.Calls.Select(call => call.Line));
    }

    /// <summary>
    /// Every ending of the CLI in the events contract's codes. A cursor the source does not know is the CLI's word for one of
    /// two refusals, a window Reply no longer holds and a cursor Reply never issued; any other refusal Reply gives without a
    /// code of its own is read again later, from where the source stands.
    /// </summary>
    [Theory]
    [InlineData("reply.unavailable", 1, null, null, "events_unreachable", FailureClass.Transient)]
    [InlineData("rate_limited", 1, 429, null, "events_unreachable", FailureClass.Transient)]
    [InlineData("reply.invalid_request", 1, 400, null, "events_unreachable", FailureClass.Transient)]
    [InlineData("reply.refused", 1, 409, "inboxThread.somethingElse", "events_unreachable", FailureClass.Transient)]
    [InlineData("inbox.something_new", 1, 400, null, "events_unreachable", FailureClass.Transient)]
    [InlineData("auth.not_logged_in", 2, null, null, "events_unauthorized", FailureClass.Permanent)]
    [InlineData("access.denied", 1, 403, null, "events_unauthorized", FailureClass.Permanent)]
    [InlineData("access.feature_unavailable", 1, 403, null, "events_unauthorized", FailureClass.Permanent)]
    [InlineData("usage.inbox", 2, null, null, "events_unsupported", FailureClass.Permanent)]
    [InlineData("reply.refused", 1, 400, "inboxThread.invalidPagination", "events_unreachable", FailureClass.Transient)]
    [InlineData("inbox.cursor_expired", 1, 400, "inboxThread.changeWindowExpired", "events_cursor_unknown", FailureClass.Permanent)]
    [InlineData("inbox.cursor_refused", 1, 400, null, "events_cursor_unknown", FailureClass.Permanent)]
    public async Task Every_ending_of_the_cli_is_one_of_the_events_contract_s_codes(
        string cli, int exit, int? status, string? replyCode, string code, FailureClass failure)
    {
        using var account = new ReplyAccount().Refuses(Feed, cli, exit, status, replyCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Equal((code, failure), (failed.Code, failed.Class));
    }

    /// <summary>
    /// The CLI refusing the read before making it is one of two things. Without a code of its own it is commander's refusal
    /// of a command or a flag it does not know: the reload takes no reply-cli older than the one with <c>inbox changes</c>,
    /// so this is one that says it is and lacks it. With a code, it refused what the call was built from — a value this
    /// package wrote, or the profile the route names — and that is no sign of an old CLI: a repeat would build the same
    /// call, so it is reported rather than retried.
    /// </summary>
    [Theory]
    [InlineData(null, "does not know")]
    [InlineData("usage.date", "a repeat would build the same call")]
    [InlineData("usage.range", "a repeat would build the same call")]
    [InlineData("usage.inbox", "a repeat would build the same call")]
    [InlineData("usage.profile", "a repeat would build the same call")]
    public async Task The_cli_refusing_the_read_before_making_it_says_whose_the_fault_is(string? cli, string words)
    {
        using var account = cli is null
            ? new ReplyAccount().Prints(Feed, 2, string.Empty, "error: unknown command 'changes'")
            : new ReplyAccount().Refuses(Feed, cli, 2);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Equal(("events_unsupported", FailureClass.Permanent), (failed.Code, failed.Class));
        Assert.Contains(words, failed.Message, StringComparison.Ordinal);
        if (cli is not null)
        {
            Assert.DoesNotContain("Update reply-cli", failed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Install", failed.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>An answer that is not a page of the feed, or a lost one, is read again later: a read changes nothing.</summary>
    [Theory]
    [InlineData(0, "not json")]
    [InlineData(0, """{"items":"nothing","has_more":false,"cursor":"c"}""")]
    [InlineData(0, """{"items":[],"has_more":false}""")]
    [InlineData(0, """{"items":[],"has_more":false,"cursor":7}""")]
    [InlineData(0, """{"items":[],"has_more":true,"cursor":""}""")]
    [InlineData(0, """{"items":[],"has_more":true,"cursor":"a|b"}""")]
    [InlineData(0, """{"items":[{"threadId":null,"lastActivityDate":"2026-10-01T09:00:00Z"}],"has_more":false,"cursor":"c"}""")]
    [InlineData(0, """{"items":[{"threadId":501,"lastActivityDate":"yesterday"}],"has_more":false,"cursor":"c"}""")]
    [InlineData(3, "")]
    public async Task An_answer_that_is_no_page_of_the_feed_is_read_again_later(int exit, string stdout)
    {
        using var account = new ReplyAccount().Prints(Feed, exit, stdout);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Equal(("events_unreachable", FailureClass.Transient), (failed.Code, failed.Class));
    }

    /// <summary>
    /// A page holds no more than the read asked for. One holding more is no page of the feed as it was asked: cutting it
    /// short would hand back a cursor past events never read, so it is not taken, and it is read again later.
    /// </summary>
    [Fact]
    public async Task A_page_holding_more_than_the_read_asked_for_is_not_taken()
    {
        using var account = new ReplyAccount().Returns(Feed, JsonNode.Parse($$"""
            {"items":[{"threadId":501,"lastActivityDate":"{{Ago(30)}}"},{"threadId":502,"lastActivityDate":"{{Ago(20)}}"},
                      {"threadId":503,"lastActivityDate":"{{Ago(10)}}"}],
             "has_more":true,"cursor":"AAAA"}
            """)!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), limit: 2), Ct));

        Assert.Equal(("events_unreachable", FailureClass.Transient), (failed.Code, failed.Class));
        Assert.Equal("2", Assert.Single(account.Calls).Option("--top"));
    }

    /// <summary>
    /// Reply's cursor is needed only where a read goes on from it. A read that reached the end of the feed ends at a moment
    /// and a spent span at its own, so an empty cursor there is no loss; one a read would go on from is a lost answer.
    /// </summary>
    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 3, false)]
    [InlineData(true, 3, false)]
    public async Task An_empty_cursor_from_reply_is_lost_only_where_the_read_goes_on_from_it(bool span, int threads, bool taken)
    {
        using var account = new ReplyAccount().WithEmptyFeedCursors();
        for (var thread = 0; thread < threads; thread++)
        {
            account.WithThread(ReplyConversationListTests.Thread(501 + thread, Ago(30 - thread)));
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var until = Moment(Now.AddMinutes(-1));

        var result = await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), until: span ? until : null, limit: 2), Ct);

        if (!taken)
        {
            var failed = Failure(result);
            Assert.Equal(("events_unreachable", FailureClass.Transient), (failed.Code, failed.Class));
            return;
        }

        var page = Page(result);
        Assert.Equal(["501"], Threads(page));
        Assert.False((bool)page["has_more"]!);
        if (span)
        {
            Assert.Equal("u|" + until + "|", (string)page["cursor"]!);
        }
        else
        {
            Assert.StartsWith("m|", (string)page["cursor"]!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A read that ends at a moment keeps that moment in its own cursor, because the pages after the first are asked for by
    /// the cursor alone: each stays before it, and the first item at or past it ends the read. Reply is handed its own
    /// cursor, never the package's, and a span reads from where it was asked to start, with no hour before it.
    /// </summary>
    [Fact]
    public async Task A_read_that_ends_at_a_moment_keeps_it_across_the_cursors_it_chains_and_stops_there()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(50)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(40)))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(30)))
            .WithThread(ReplyConversationListTests.Thread(504, Ago(20)))
            .WithThread(ReplyConversationListTests.Thread(505, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var until = Moment(Now.AddMinutes(-25));

        var first = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), until: until, limit: 2), Ct));
        var second = Page(await ReadAsync(api, Input(cursor: (string)first["cursor"]!, limit: 2), Ct));

        Assert.Equal(["501", "502"], Threads(first));
        Assert.Equal(Moment(Now.AddHours(-1)), account.Calls[0].Option("--since"));
        Assert.True((bool)first["has_more"]!);
        Assert.StartsWith("u|" + until + "|", (string)first["cursor"]!, StringComparison.Ordinal);
        Assert.Equal(["503"], Threads(second));
        Assert.False((bool)second["has_more"]!);
        Assert.Equal("u|" + until + "|", (string)second["cursor"]!);
        var replys = ((string)first["cursor"]!)[("u|" + until + "|").Length..];
        Assert.Equal(replys, account.Calls[1].Option("--cursor"));

        // A spent span is read no further: the cursor it ended at, handed back, is none a read goes on from.
        var spent = Failure(await ReadAsync(api, Input(cursor: (string)second["cursor"]!), Ct));
        Assert.Equal(("events_cursor_unknown", FailureClass.Permanent), (spent.Code, spent.Class));
        Assert.Equal(2, account.Calls.Count);
    }

    [Theory]
    [InlineData("AQjfObhYHZgAAADjVg")]
    [InlineData("x|y")]
    [InlineData("c|")]
    [InlineData("c|abc|def")]
    [InlineData("m|not-a-moment")]
    [InlineData("m|0")]
    [InlineData("m|2026-10-01")]
    [InlineData("m|2026-10-01T09:00:00Z")]
    [InlineData("m|2026-10-01T09:00:00.000+00:00")]
    [InlineData("m|2026-02-30T09:00:00.000Z")]
    [InlineData("u|2026|abc")]
    [InlineData("u|2026-10-01T09:00:00+00:00|abc")]
    [InlineData("m|2026-10-01T09:00:00.000Z|abc")]
    [InlineData("u|not-a-moment|abc")]
    [InlineData("u|2026-10-01T09:00:00.000Z|")]
    [InlineData("u|2026-10-01T09:00:00.000Z|abc|def")]
    public async Task A_cursor_this_package_did_not_write_is_events_cursor_unknown_and_nothing_is_asked(string cursor)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var failed = Failure(await ReadAsync(api, Input(cursor: cursor), Ct));

        Assert.Equal(("events_cursor_unknown", FailureClass.Permanent), (failed.Code, failed.Class));
        Assert.Empty(account.Calls);
    }

    /// <summary>The thread, with a reply from the other side before its one message: Reply then flags that it holds one.</summary>
    private static JsonObject Replied(JsonObject thread)
    {
        var messages = thread["messages"]!.AsArray();
        var last = messages[0]!.AsObject();
        var reply = last.DeepClone().AsObject();
        reply["messageId"] = $"<r{thread["id"]}@mail.example.com>";
        reply["isOutbound"] = false;
        reply["date"] = DateTimeOffset.Parse((string)last["date"]!, CultureInfo.InvariantCulture).AddMinutes(-1)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ff'Z'", CultureInfo.InvariantCulture);
        messages.Insert(0, reply);
        return thread;
    }

    /// <summary>
    /// The one call asked from where the feed starts by the package's clock during the read: 30 days back and an hour
    /// inside, to the millisecond its clock reads.
    /// </summary>
    private static void AsksFromTheFeedsStart(ReplyAccount account, DateTimeOffset before, DateTimeOffset after)
    {
        var asked = DateTimeOffset.Parse(Assert.Single(account.Calls).Option("--since")!, CultureInfo.InvariantCulture);
        var earliest = before.AddTicks(-(before.Ticks % TimeSpan.TicksPerMillisecond)).AddDays(-30).AddHours(1);
        var latest = after.AddDays(-30).AddHours(1);
        Assert.InRange(asked, earliest, latest);
    }

    /// <summary>A moment as the runtime hands one over, to the millisecond.</summary>
    private static string Moment(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A moment some minutes ago, as Reply writes a thread's last activity.</summary>
    private static string Ago(int minutes) =>
        Now.AddMinutes(-minutes).ToString("yyyy-MM-dd'T'HH:mm:ss.ff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Reply's moment as the package writes it, which is how conversation.list writes the same moment.</summary>
    private static string Utc(string written) => Moment(DateTimeOffset.Parse(written, CultureInfo.InvariantCulture));

    private static JsonObject Input(string? cursor = null, string? since = null, string? until = null, int limit = 100) => new()
    {
        ["cursor"] = cursor,
        ["since"] = since,
        ["until"] = until,
        ["limit"] = limit,
    };

    private static async Task<PluginInvocationResult> ReadAsync(RuntimeApiFixture api, JsonObject input, CancellationToken kill)
    {
        // The input is what the contract says it is before anything runs.
        Assert.Empty(SchemaValidator.Validate(input, Contract.InputSchema));
        return await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(ReplyPlugins.PluginId, "events.read", input, null, "evr_01K0REPLYEVENTSREAD", Purpose: InvocationPurpose.Events),
            kill);
    }

    /// <summary>The page the package answered, held to the events contract's output and carrying no pins.</summary>
    private static JsonObject Page(PluginInvocationResult result)
    {
        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Empty(Contract.CheckResult(succeeded.Result));
        Assert.Null(succeeded.ExternalIds);
        return succeeded.Result!.AsObject();
    }

    /// <summary>The failure the package declared, in a code the events contract publishes, in its class, and pinning nothing.</summary>
    private static OutcomeError Failure(PluginInvocationResult result)
    {
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome).Error;
        var declared = Assert.Single(Contract.FailureCodes, declared => declared.Code == failed.Code);
        Assert.Equal(declared.Class, failed.Class);
        Assert.Null(failed.ExternalIds);
        return failed;
    }

    private static string[] Threads(JsonObject page) => [.. page["events"]!.AsArray().Select(item => (string)item!["thread"]!)];
}
