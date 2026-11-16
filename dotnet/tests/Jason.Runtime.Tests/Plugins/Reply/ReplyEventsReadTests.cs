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
                  "moved_by":null,"channel":"linkedin","unread":null,"has_meeting_intent":false},
                 {"kind":"message_sent","id":null,"occurred_at":"{{Utc(Ago(10))}}","contact":"1003","campaign":"7","thread":"503",
                  "message":"<m503@mail.example.com>","channel":"email"}]
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
    /// is read as a send, a <c>message_sent</c>; and nobody this package can name where Reply gave no last message. A thread moves in the feed
    /// only when a message lands on it, so a last activity later than the last message's date is never read as a move of
    /// Reply's own.
    /// </summary>
    [Theory]
    [InlineData("inbound", "other_side")]
    [InlineData("outbound", "message_sent")]
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

        var read = Assert.Single(page["events"]!.AsArray())!;
        if (mover == "message_sent")
        {
            Assert.Equal("message_sent", (string?)read["kind"]);
            return;
        }

        Assert.Equal(mover, (string?)read["moved_by"]);
    }

    /// <summary>
    /// The four answers, as Reply's item says them: the direction of the last message decides the other side's move alone;
    /// the account's message is nobody's where Reply flags a reply on the thread, and a send, a <c>message_sent</c>, where
    /// it flags none or says nothing of one.
    /// </summary>
    [Theory]
    [InlineData("""{"isOutbound":false}""", """{"hasReply":false}""", "other_side")]
    [InlineData("""{"isOutbound":true}""", """{"hasReply":true}""", null)]
    [InlineData("""{"isOutbound":true}""", """{"hasReply":false}""", "message_sent")]
    [InlineData("""{"isOutbound":true}""", null, "message_sent")]
    [InlineData(null, """{"hasReply":true}""", null)]
    public async Task Who_moved_a_thread_is_its_last_message_s_direction_and_reply_s_flag_of_a_reply(string? last, string? flags, string? mover)
    {
        var item = new JsonObject { ["threadId"] = 501, ["contactId"] = 1001, ["channel"] = "email", ["lastActivityDate"] = Ago(10) };
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

        var read = Assert.Single(events)!;
        if (mover == "message_sent")
        {
            Assert.Equal("message_sent", (string?)read["kind"]);
            return;
        }

        Assert.Equal(mover, (string?)read["moved_by"]);
    }

    /// <summary>
    /// A bounce notice that lands on the thread of a send is the thread's newest message, inbound, and the feed flags the
    /// thread as holding a bounce and no reply: that is a bounce, never the other side's move. It is one <c>bounce</c>
    /// event — the thread's contact and sequence, no message and no kind, since the feed names neither, and the notice's
    /// own moment — and no <c>thread_moved</c> for that thread. Read again, as the hour a read after a finished one reads
    /// again brings it back, it is the same event, so the runtime keeps it once.
    /// </summary>
    [Fact]
    public async Task A_bounce_notice_on_top_of_a_thread_is_one_bounce_event_and_no_move_and_reads_the_same_twice()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyStubChangesTests.Bounced(ReplyConversationListTests.Thread(501, Ago(40), inbound: false), Ago(30)))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(20), contactId: 1002));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));
        var again = Page(await ReadAsync(api, Input(cursor: (string)first["cursor"]!), Ct));

        Assert.Equal(
            JsonNode.Parse($$"""
                [{"kind":"bounce","id":null,"occurred_at":"{{Utc(Ago(30))}}","contact":"1001","campaign":"7","message":null,"bounce":null},
                 {"kind":"thread_moved","id":null,"occurred_at":"{{Utc(Ago(20))}}","thread":"502","contact":"1002","campaign":"7",
                  "moved_by":"other_side","channel":"email","unread":null,"has_meeting_intent":false}]
                """)!.ToJsonString(),
            first["events"]!.ToJsonString());
        Assert.Equal(first["events"]!.ToJsonString(), again["events"]!.ToJsonString());
        Assert.DoesNotContain("Undelivered", first.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What Reply's item says decides it. A bounce is the flag of a bounce and no other of Reply's reply flags — no reply,
    /// no automatic reply, no absence — and an inbound last message;
    /// its moment is that message's own, not the thread's last activity, and its campaign the item's sequence or none.
    /// A thread flagged with a bounce and any other of them is a moved thread as it was, an automatic reply or an absence
    /// landing on a thread that once bounced among them: the flags hold for good and the feed cannot say which came last,
    /// so the reader of the thread says what it is. A flag on a thread whose last message is the account's, or where
    /// Reply gave none, changes nothing either; nor does a flag that is not a boolean true, or a feed that never sets one.
    /// A bounce Reply names no person for has nobody to name and is left out, as the account's moves are.
    /// </summary>
    [Theory]
    [InlineData("""{"hasBounce":true,"hasReply":false}""", "in", "bounce")]
    [InlineData("""{"hasBounce":true}""", "in", "bounce")]
    [InlineData("""{"hasBounce":true,"hasReply":false}""", "in, no sequence", "bounce")]
    [InlineData("""{"hasBounce":true,"hasReply":false}""", "in, no date", "bounce")]
    [InlineData("""{"hasBounce":true,"hasReply":false,"hasAutoReply":false,"hasOutOfOffice":false}""", "in", "bounce")]
    [InlineData("""{"hasBounce":true,"hasReply":true}""", "in", "other_side")]
    [InlineData("""{"hasBounce":true,"hasReply":false,"hasAutoReply":true}""", "in", "other_side")]
    [InlineData("""{"hasBounce":true,"hasReply":false,"hasOutOfOffice":true}""", "in", "other_side")]
    [InlineData("""{"hasBounce":true,"hasReply":false}""", "out", "message_sent")]
    [InlineData("""{"hasBounce":true,"hasReply":true}""", "out", null)]
    [InlineData("""{"hasBounce":true,"hasReply":false}""", "none", null)]
    [InlineData("""{"hasBounce":"true","hasReply":false}""", "in", "other_side")]
    [InlineData("""{"hasBounce":false,"hasReply":false}""", "in", "other_side")]
    [InlineData("""{"hasReply":false}""", "in", "other_side")]
    [InlineData(null, "in", "other_side")]
    [InlineData("""{"hasBounce":true,"hasReply":false}""", "in, no contact", "kept out")]
    public async Task A_bounce_is_reply_s_bounce_flag_without_its_reply_flag_on_an_inbound_last_message(string? flags, string last, string? read)
    {
        var item = new JsonObject
        {
            ["threadId"] = 501, ["contactId"] = 1001, ["channel"] = "email", ["lastActivityDate"] = Ago(10), ["sequenceId"] = 7,
        };
        if (last != "none")
        {
            item["lastMessage"] = new JsonObject { ["messageId"] = "<b501@mailer.example.com>", ["isOutbound"] = last == "out", ["date"] = Ago(12) };
        }

        if (last == "in, no sequence")
        {
            item.Remove("sequenceId");
        }

        if (last == "in, no date")
        {
            item["lastMessage"]!.AsObject().Remove("date");
        }

        if (last == "in, no contact")
        {
            item.Remove("contactId");
        }

        if (flags is not null)
        {
            item["flags"] = JsonNode.Parse(flags);
        }

        using var account = new ReplyAccount().Returns(Feed, new JsonObject { ["items"] = new JsonArray(item), ["has_more"] = false, ["cursor"] = "AAAA" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var events = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct))["events"]!.AsArray();

        if (read == "kept out")
        {
            Assert.Empty(events);
            return;
        }

        var only = Assert.Single(events)!;
        if (read == "message_sent")
        {
            Assert.Equal("message_sent", (string?)only["kind"]);
            return;
        }

        if (read != "bounce")
        {
            Assert.Equal(("thread_moved", read), ((string?)only["kind"], (string?)only["moved_by"]));
            return;
        }

        Assert.Equal(
            JsonNode.Parse($$"""
                {"kind":"bounce","id":null,"occurred_at":"{{Utc(last == "in, no date" ? Ago(10) : Ago(12))}}","contact":"1001",
                 "campaign":{{(last == "in, no sequence" ? "null" : "\"7\"")}},"message":null,"bounce":null}
                """)!.ToJsonString(),
            only.ToJsonString());
    }

    /// <summary>
    /// A bounce is held to the end of a span by the thread's place in the feed, its last activity, as every item is, and
    /// never by the notice's own moment, in either direction: a notice before the end on a thread whose last activity is
    /// past it ends the span, and a notice Reply dates past the end on a thread whose last activity is before it does not.
    /// </summary>
    [Theory]
    [InlineData(30, 10, true)]
    [InlineData(10, 30, false)]
    public async Task A_bounce_ends_a_span_by_the_thread_s_last_activity_and_never_by_the_notice_s_moment(int notice, int activity, bool ends)
    {
        var item = new JsonObject
        {
            ["threadId"] = 501, ["contactId"] = 1001, ["channel"] = "email", ["lastActivityDate"] = Ago(activity),
            ["flags"] = new JsonObject { ["hasBounce"] = true, ["hasReply"] = false },
            ["lastMessage"] = new JsonObject { ["messageId"] = "<b501@mailer.example.com>", ["isOutbound"] = false, ["date"] = Ago(notice) },
        };
        using var account = new ReplyAccount().Returns(Feed, new JsonObject { ["items"] = new JsonArray(item), ["has_more"] = true, ["cursor"] = "AAAA" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);
        var until = Moment(Now.AddMinutes(-20));

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), until: until), Ct));

        var read = page["events"]!.AsArray().Select(found => ((string?)found!["kind"], (string?)found["occurred_at"])).ToList();
        Assert.Equal(ends ? [] : [("bounce", Utc(Ago(notice)))], read);
        Assert.Equal(!ends, (bool)page["has_more"]!);
        Assert.Equal("u|" + until + "|" + (ends ? "" : "AAAA"), (string)page["cursor"]!);
    }

    /// <summary>
    /// A bounce Reply names no contact for is left out of the page: no bounce, since there is nobody to say it of, and no
    /// moved thread, which is the false reply a bounce is read to avoid. The package says so in one line of its own on
    /// stderr, carrying the thread's and the sequence's identifiers and the item's moment and nothing anybody wrote: no
    /// subject, no preview, no address, no name. No other item of the page, a bounce with its contact or a move by either
    /// side, writes a line.
    /// </summary>
    [Fact]
    public async Task A_bounce_that_names_no_contact_is_left_out_and_says_so_by_the_thread_s_identifier_alone()
    {
        var item = new JsonObject
        {
            ["threadId"] = 501, ["channel"] = "email", ["lastActivityDate"] = Ago(10), ["sequenceId"] = 7,
            ["flags"] = new JsonObject { ["hasBounce"] = true, ["hasReply"] = false },
            ["lastMessage"] = new JsonObject
            {
                ["messageId"] = "<b501@mailer.example.com>", ["isOutbound"] = false, ["date"] = Ago(12),
                ["subject"] = "Undelivered Mail Returned to Sender", ["preview"] = "The address was not found.",
                ["fromAddress"] = "mailer-daemon@mailer.example.com", ["fromName"] = "Mail Delivery System",
            },
        };
        JsonObject Other(int thread, int? contact, bool outbound, JsonObject? flags, int minutes)
        {
            var other = new JsonObject
            {
                ["threadId"] = thread, ["channel"] = "email", ["lastActivityDate"] = Ago(minutes), ["sequenceId"] = 7,
                ["lastMessage"] = new JsonObject { ["isOutbound"] = outbound, ["date"] = Ago(minutes) },
            };
            if (contact is not null)
            {
                other["contactId"] = contact;
            }

            if (flags is not null)
            {
                other["flags"] = flags;
            }

            return other;
        }

        var items = new JsonArray(
            item,
            Other(502, 1002, outbound: false, new JsonObject { ["hasBounce"] = true, ["hasReply"] = false }, 9),
            Other(503, 1003, outbound: true, null, 8),
            Other(504, 1004, outbound: false, null, 7));
        using var account = new ReplyAccount().Returns(Feed, new JsonObject { ["items"] = items, ["has_more"] = false, ["cursor"] = "AAAA" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct);

        Assert.Equal(
            [("bounce", "1002"), ("message_sent", "1003"), ("thread_moved", "1004")],
            Page(result)["events"]!.AsArray().Select(read => ((string?)read!["kind"], (string?)read["contact"])));
        var line = Assert.Single(PluginLines(result));
        Assert.Equal(
            ("info", "Reply flags a bounce on a thread it names no contact for, so no event was read from it."),
            ((string?)line["level"], (string?)line["message"]));
        Assert.Equal($$"""{"thread":"501","campaign":"7","at":"{{Utc(Ago(10))}}"}""", line["data"]!.ToJsonString());
        var stderr = Stderr(result);
        Assert.All(
            ["Undelivered", "The address was not found", "mailer-daemon", "mailer.example.com", "Mail Delivery System"],
            written => Assert.DoesNotContain(written, stderr, StringComparison.Ordinal));
    }

    /// <summary>
    /// A page of nothing but the account's moves is a page of sends, and still says where the read goes on from and
    /// whether more follows, so the next page is read from past them.
    /// </summary>
    [Fact]
    public async Task A_page_of_the_account_s_moves_alone_is_a_page_of_sends_and_goes_on()
    {
        using var account = new ReplyAccount()
            .WithThread(ReplyConversationListTests.Thread(501, Ago(30), inbound: false))
            .WithThread(ReplyConversationListTests.Thread(502, Ago(20), inbound: false))
            .WithThread(ReplyConversationListTests.Thread(503, Ago(10)));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1)), limit: 2), Ct));
        var second = Page(await ReadAsync(api, Input(cursor: (string)first["cursor"]!, limit: 2), Ct));

        Assert.Equal(["message_sent", "message_sent"], first["events"]!.AsArray().Select(read => (string?)read!["kind"]));
        Assert.Equal(["501", "502"], Threads(first));
        Assert.True((bool)first["has_more"]!);
        var going = (string)first["cursor"]!;
        Assert.StartsWith("c|", going, StringComparison.Ordinal);
        Assert.Equal(going[2..], account.Calls[1].Option("--cursor"));
        Assert.Equal(["503"], Threads(second));
    }

    /// <summary>
    /// An account's send is held to the end of a span like any other move: one at or past the moment a span ends at ends it,
    /// and is not kept, so nothing after it is read and the span is spent.
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

    /// <summary>
    /// The account's message on a thread that holds no reply is one <c>message_sent</c>: the person it went to, the
    /// sequence it belongs to as the campaign, the thread, the message's own identifier and moment, and the channel, and
    /// nothing else. The thread's last activity is a little later than the message, and the message's moment is the one
    /// kept.
    /// </summary>
    [Fact]
    public async Task A_sequence_s_send_is_one_message_sent_carrying_the_identifiers_the_feed_gives()
    {
        using var account = new ReplyAccount().Returns(Feed, Answer(Send(501, contact: 1001, sequence: 7, message: "m_88", sent: Ago(12), activity: Ago(10))));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var page = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));

        Assert.Equal(
            JsonNode.Parse($$"""
                [{"kind":"message_sent","id":null,"occurred_at":"{{Utc(Ago(12))}}","contact":"1001","campaign":"7","thread":"501",
                  "message":"m_88","channel":"email"}]
                """)!.ToJsonString(),
            page["events"]!.ToJsonString());
    }

    /// <summary>A message sent on a thread outside every sequence names no campaign: a direct send's.</summary>
    [Fact]
    public async Task A_direct_send_names_no_campaign()
    {
        using var account = new ReplyAccount().Returns(Feed, Answer(Send(501, contact: 1001, sequence: null, message: "m_89", sent: Ago(12), activity: Ago(12), channel: "linkedIn")));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = Assert.Single(Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct))["events"]!.AsArray())!;

        Assert.Equal(("message_sent", null, "m_89", "linkedin"), ((string?)read["kind"], (string?)read["campaign"], (string?)read["message"], (string?)read["channel"]));
    }

    /// <summary>
    /// The hour a read after a finished one reads again brings the same item back with the same last message, and the
    /// same moment: the same send, which the runtime keeps once by its key.
    /// </summary>
    [Fact]
    public async Task A_send_read_twice_through_the_overlap_hour_is_the_same_event()
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, Ago(12), inbound: false));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct));
        var again = Page(await ReadAsync(api, Input(cursor: (string)first["cursor"]!), Ct));

        Assert.StartsWith("m|", (string)first["cursor"]!, StringComparison.Ordinal);
        Assert.NotNull(account.Calls[1].Option("--since"));
        Assert.Single(first["events"]!.AsArray());
        Assert.Equal(first["events"]!.ToJsonString(), again["events"]!.ToJsonString());
    }

    /// <summary>
    /// A last message with no identifier is a send with no message, and one with no moment of its own takes the thread's
    /// last activity, which is the moment the thread moved.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task A_send_with_no_message_identifier_or_no_moment_still_is_one(bool hasMessage, bool hasDate)
    {
        var item = Send(501, contact: 1001, sequence: 7, message: hasMessage ? "m_88" : null, sent: hasDate ? Ago(12) : null, activity: Ago(10));
        using var account = new ReplyAccount().Returns(Feed, Answer(item));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = Assert.Single(Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct))["events"]!.AsArray())!;

        Assert.Equal("message_sent", (string?)read["kind"]);
        Assert.Equal(hasMessage ? "m_88" : null, (string?)read["message"]);
        Assert.Equal(Utc(hasDate ? Ago(12) : Ago(10)), (string?)read["occurred_at"]);
    }

    /// <summary>
    /// A send to nobody counts nobody, so it is left out of the page, and the package says so in one line of its own by
    /// the thread's and the sequence's identifiers and the thread's moment alone: no subject, no address, no name.
    /// </summary>
    [Fact]
    public async Task A_send_with_no_contact_is_left_out_and_says_so_by_identifiers_alone()
    {
        var item = Send(501, contact: null, sequence: 7, message: "m_88", sent: Ago(12), activity: Ago(10));
        item["lastMessage"]!["subject"] = "Q3 pilot for Marta";
        using var account = new ReplyAccount().Returns(Feed, Answer(item));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct);

        Assert.Empty(Page(result)["events"]!.AsArray());
        var line = Assert.Single(PluginLines(result));
        Assert.Equal("info", (string?)line["level"]);
        Assert.Equal($$"""{"thread":"501","campaign":"7","at":"{{Utc(Ago(10))}}"}""", line["data"]!.ToJsonString());
        Assert.DoesNotContain("Marta", Stderr(result), StringComparison.Ordinal);
    }

    /// <summary>A send carries the contract's eight members and no other: nothing anybody wrote is in it.</summary>
    [Fact]
    public async Task A_send_has_no_member_beyond_the_eight()
    {
        var item = Send(501, contact: 1001, sequence: 7, message: "m_88", sent: Ago(12), activity: Ago(10));
        item["lastMessage"]!["subject"] = "Q3 pilot";
        item["lastMessage"]!["preview"] = "Hello Marta";
        using var account = new ReplyAccount().Returns(Feed, Answer(item));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var read = Assert.Single(Page(await ReadAsync(api, Input(since: Moment(Now.AddHours(-1))), Ct))["events"]!.AsArray())!.AsObject();

        Assert.Equal(
            ["campaign", "channel", "contact", "id", "kind", "message", "occurred_at", "thread"],
            read.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Marta", read.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>One feed item: the account's last message on a thread that holds no reply.</summary>
    private static JsonObject Send(int thread, int? contact, int? sequence, string? message, string? sent, string activity, string channel = "email")
    {
        var last = new JsonObject { ["isOutbound"] = true };
        if (message is not null)
        {
            last["messageId"] = message;
        }

        if (sent is not null)
        {
            last["date"] = sent;
        }

        var item = new JsonObject { ["threadId"] = thread, ["channel"] = channel, ["lastActivityDate"] = activity, ["lastMessage"] = last };
        if (contact is not null)
        {
            item["contactId"] = contact;
        }

        if (sequence is not null)
        {
            item["sequenceId"] = sequence;
        }

        return item;
    }

    /// <summary>The feed's whole answer holding one item.</summary>
    private static JsonObject Answer(JsonObject item) => new() { ["items"] = new JsonArray(item), ["has_more"] = false, ["cursor"] = "AAAA" };

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

    /// <summary>The lines the package itself wrote with host.log, from the invocation's own stderr.log.</summary>
    private static IReadOnlyList<JsonObject> PluginLines(PluginInvocationResult result) =>
        [.. Stderr(result).Split('\n')
            .Where(line => line.TrimStart().StartsWith('{'))
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(line => (string?)line["source"] == "plugin")];

    /// <summary>The invocation's whole stderr, the host's lines and the package's alike.</summary>
    private static string Stderr(PluginInvocationResult result)
    {
        var log = Path.Combine(result.Launch!.WorkDir, "stderr.log");
        return File.Exists(log) ? File.ReadAllText(log) : string.Empty;
    }

    private static string[] Threads(JsonObject page) => [.. page["events"]!.AsArray().Select(item => (string)item!["thread"]!)];
}
