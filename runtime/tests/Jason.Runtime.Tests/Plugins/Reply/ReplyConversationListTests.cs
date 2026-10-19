using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>conversation.list</c> as the official package performs it, against a stand-in holding threads in the shape
/// Reply holds them — the person on each with everything Reply knows of them, address and phone included. One test
/// per line of the document's conformance list, and the refusals Reply has a word for.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyConversationListTests
{
    private const string Operation = "conversation.list";
    private const string Phone = "+5715550100";
    private const string Profile = "https://www.linkedin.com/in/marta-alvarez";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // Whose threads
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_person_s_threads_are_read_by_the_pin_across_the_account_and_the_read_pins_nothing()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        account.WithThread(Thread(502, "2026-10-02T18:02:44Z", sequence: 8));
        account.WithThread(Thread(503, "2026-10-01T10:00:00Z"));
        account.WithThread(Thread(504, "2026-10-04T10:00:00Z", contactId: 1002, name: "Lena Okafor"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(limit: 20, offset: 0, pin: "1001", pinned: "7"));

        // That person's threads, wherever they are — the one in another sequence included — the last to move first.
        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(["501", "502", "503"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.Equal("last_activity_desc", (string?)answer["vendor"]!["ordering"]);

        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Null(outcome.ExternalIds);

        // Every thread is two of Reply's buckets, the inbox and the sent threads, each read from its first thread.
        Assert.Equal(["inbox", "sent"], account.Calls.Select(call => call.Option("--source")));
        Assert.All(account.Calls, call => Assert.Null(call.Body));
        Assert.Equal(
            ["--json", "-q", "inbox", "list", "--contact-id", "1001", "--source", "inbox", "--limit", "20", "--offset", "0"],
            account.Calls[0].Args);
    }

    [Fact]
    public async Task A_person_with_no_pin_fails_contact_not_linked_and_nothing_is_asked()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pin: null, person: true, pinned: "7")));

        Assert.Equal("contact_not_linked", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_pin_that_is_not_a_reply_number_is_refused_without_a_call()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pin: "p-1001")));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Contains("contact", (string?)error.Details!["reason"], StringComparison.Ordinal);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task Without_a_person_the_campaign_s_threads_are_read_and_only_a_first_link_returns_the_campaign()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        account.WithThread(Thread(502, "2026-10-02T18:02:44Z", sequence: 8));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var linking = await InvokeAsync(api, Input(limit: 20, offset: 0, supplied: "7"));
        var linked = await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7"));

        Assert.Equal(["501"], ReplyOperations.Succeeded(Operation, linking)["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.Equal("""{"campaign":"7"}""", Assert.IsType<InvocationOutcome.Succeeded>(linking.Outcome).ExternalIds!.ToJsonString());
        Assert.Null(Assert.IsType<InvocationOutcome.Succeeded>(linked.Outcome).ExternalIds);
        Assert.All(account.Calls, call => Assert.Equal("7", call.Option("--sequence-id")));
        Assert.All(account.Calls, call => Assert.Null(call.Option("--contact-id")));
    }

    /// <summary>`inbound` is Reply's inbox bucket, and `since` is passed as given: the CLI keeps a thread that moved at or after it.</summary>
    [Fact]
    public async Task Inbound_and_since_travel_as_the_cli_s_own_flags()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(
            api, Input(limit: 20, offset: 0, pin: "1001", source: "inbound", since: "2026-10-01T09:00:00Z")));

        var call = Assert.Single(account.Calls);
        Assert.Equal("inbox list", call.Line);
        Assert.Equal("1001", call.Option("--contact-id"));
        Assert.Equal("inbox", call.Option("--source"));
        Assert.Equal("2026-10-01T09:00:00Z", call.Option("--since"));
        Assert.Equal("20", call.Option("--limit"));
        Assert.Equal("0", call.Option("--offset"));
        Assert.Null(call.Body);
    }

    /// <summary>
    /// The contract keeps the threads that moved at or after <c>since</c>, and so does the CLI's <c>--since</c>: the
    /// package passes the moment as given, and a thread whose last activity is stamped exactly at it is listed.
    /// </summary>
    [Theory]
    [InlineData("2026-10-01T00:00:00Z", "2026-10-01T00:00:00Z")]
    [InlineData("2026-10-01T07:41:09.250Z", "2026-10-01T07:41:09.250Z")]
    public async Task A_thread_that_moved_exactly_at_since_is_listed(string moved, string since)
    {
        using var account = Planted();
        account.WithThread(Thread(501, moved));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(
            Operation,
            await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7", source: "inbound", since: since)));

        Assert.Equal(["501"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.Equal(since, Assert.Single(account.Calls).Option("--since"));
    }

    // -------------------------------------------------------------------------------------------------------
    // What a thread is, and what of its person never travels
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_thread_reads_in_the_published_vocabulary_and_no_address_of_its_person_travels()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T10:41:09+03:00", read: false, state: "needsAttention", category: (3, "Interested"), meetingIntent: true));
        account.WithThread(Thread(502, "2026-10-02T18:02:44Z", channel: "linkedIn", read: true, subject: null, deleted: true));
        account.WithThread(Thread(503, "2026-10-01T10:00:00Z", channel: "unknown", contactId: null, name: null));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7")));

        var threads = answer["items"]!.AsArray();
        var expected = JsonNode.Parse(
            """{"external_id":"501","channel":"email","subject":"Re: Q3 pilot","preview":"Thanks — could we talk on Thursday?","last_activity_at":"2026-10-03T07:41:09.000Z","unread":true,"contact":{"external_id":"1001","name":"Marta Alvarez"},"campaign_external_id":"7","has_meeting_intent":true,"vendor":{"is_last_message_planned":false,"state":"needs_attention","category":{"id":"3","name":"Interested"}}}""");
        Assert.True(JsonNode.DeepEquals(expected, threads[0]), threads[0]!.ToJsonString());

        // A person Reply no longer holds keeps the name it shows and loses the identifier; another channel is `other`.
        Assert.Equal("linkedin", (string?)threads[1]!["channel"]);
        Assert.Null(threads[1]!["subject"]);
        Assert.False((bool)threads[1]!["unread"]!);
        Assert.Equal("""{"external_id":null,"name":"Marta Alvarez"}""", threads[1]!["contact"]!.ToJsonString());
        Assert.Equal("other", (string?)threads[2]!["channel"]);

        var text = answer.ToJsonString();
        foreach (var never in new[] { ReplyOperations.Address, Phone, Profile, ReplyOperations.Company, ReplyOperations.Title })
        {
            Assert.DoesNotContain(never, text, StringComparison.Ordinal);
        }
    }

    /// <summary>A provider word naming a property every JavaScript object has is still a word this vocabulary lacks.</summary>
    [Fact]
    public async Task A_provider_word_that_names_an_object_property_maps_to_nothing()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z", channel: "constructor", state: "__proto__"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var thread = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7")))["items"]![0]!;

        Assert.Equal("other", (string?)thread["channel"]);
        Assert.Null(thread["vendor"]!["state"]);
    }

    [Fact]
    public async Task A_preview_longer_than_the_document_allows_is_cut_where_a_character_ends()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z", preview: new string('a', 499) + "😀" + " and the rest"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7")));

        // Five hundred characters, as the document counts them: the emoji is the five-hundredth, and it is kept whole.
        Assert.Equal(new string('a', 499) + "😀", (string?)answer["items"]![0]!["preview"]);
    }

    /// <summary>
    /// Threads whose subjects and previews are as long as the document allows, in a script the runtime counts at six
    /// bytes a character, make a page of a hundred too large for one result: the page comes back short with more to
    /// follow, and the pages after it, each started at the offset plus what came back, read every thread once.
    /// </summary>
    [Fact]
    public async Task A_page_of_threads_too_large_for_one_result_comes_back_short_and_the_next_one_carries_on()
    {
        const int Threads = 120;
        using var account = Planted();
        for (var at = 0; at < Threads; at++)
        {
            account.WithThread(Thread(
                5000 + at,
                DateTimeOffset.UnixEpoch.AddMinutes(at).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                subject: new string('Ж', 1000),
                preview: new string('Ж', 500)));
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var pages = new List<int>();
        var offset = 0;
        var more = true;
        while (more)
        {
            var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 100, offset: offset, pinned: "7")));
            var items = answer["items"]!.AsArray();
            seen.AddRange(items.Select(thread => (string)thread!["external_id"]!));
            pages.Add(items.Count);
            more = (bool)answer["has_more"]!;
            offset += items.Count;
            Assert.True(pages.Count <= 5, "the pages never ended");
        }

        Assert.True(pages[0] < 100, $"the first page held {pages[0]} threads, so nothing was cut");
        Assert.Equal(Enumerable.Range(0, Threads).Reverse().Select(at => (5000 + at).ToString(CultureInfo.InvariantCulture)), seen);
    }

    // -------------------------------------------------------------------------------------------------------
    // Every thread: Reply's inbox and its sent threads, merged
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reply's filter has no bucket for every thread: with no source it answers the inbox, threads with an inbound
    /// message. A thread whose messages are all outbound is in the sent bucket only, and `all` lists it.
    /// </summary>
    [Fact]
    public async Task Every_thread_lists_the_sent_ones_too_in_one_order_of_last_activity()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        account.WithThread(Thread(502, "2026-10-02T18:02:44Z", inbound: false));
        account.WithThread(Thread(503, "2026-10-01T10:00:00Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7")));

        Assert.Equal(["501", "502", "503"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.False((bool)answer["has_more"]!);
    }

    /// <summary>A thread with an inbound message and an outbound reply after it is in both buckets, and listed once.</summary>
    [Fact]
    public async Task A_thread_in_both_buckets_is_listed_once()
    {
        using var account = Planted();
        account.WithThread(Answered(Thread(501, "2026-10-03T07:41:09Z"), "2026-10-03T09:00:00Z"));
        account.WithThread(Thread(502, "2026-10-02T18:02:44Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7")));

        Assert.Equal(["501", "502"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
    }

    /// <summary>
    /// A page of every thread is a slice of the merged order, so both buckets are read from their first thread up to the
    /// page's end: the offset counts the merged threads, not either bucket's.
    /// </summary>
    [Fact]
    public async Task A_page_of_every_thread_is_a_slice_of_the_merged_order()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-04T10:00:00Z"));
        account.WithThread(Thread(502, "2026-10-03T10:00:00Z", inbound: false));
        account.WithThread(Thread(503, "2026-10-02T10:00:00Z"));
        account.WithThread(Thread(504, "2026-10-01T10:00:00Z", inbound: false));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var middle = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 2, offset: 1, pinned: "7")));
        Assert.Equal(["502", "503"], middle["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.True((bool)middle["has_more"]!);
        Assert.All(account.Calls, call => Assert.Equal(("3", "0"), (call.Option("--limit"), call.Option("--offset"))));

        var last = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 2, offset: 3, pinned: "7")));
        Assert.Equal(["504"], last["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.False((bool)last["has_more"]!);
    }

    /// <summary>
    /// Every thread is merged from both buckets, each read a hundred threads at a time from its first, as far as a
    /// thousand; a page of every thread that ends past the first thousand is refused before anything is asked. One that
    /// ends at it is read.
    /// </summary>
    [Theory]
    [InlineData(990, 20, true)]
    [InlineData(980, 20, false)]
    public async Task A_page_of_every_thread_past_the_first_thousand_is_refused_before_anything_is_asked(int offset, int limit, bool refused)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(limit: limit, offset: offset, pinned: "7"));

        if (!refused)
        {
            ReplyOperations.Succeeded(Operation, result);
            Assert.All(account.Calls, call => Assert.Equal(("100", "0"), (call.Option("--limit"), call.Option("--offset"))));
            return;
        }

        var error = ReplyOperations.Failed(Operation, result);
        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("1000", (string?)error.Details!["reason"], StringComparison.Ordinal);
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// One bucket holds more than the page reaches and the other holds nothing: the merge is then exactly the page's
    /// reach long, and only that bucket's own word says there is more.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_bucket_with_more_than_the_page_says_so_though_the_other_is_empty(bool inbox)
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T10:00:00Z", inbound: inbox));
        account.WithThread(Thread(502, "2026-10-02T10:00:00Z", inbound: inbox));
        account.WithThread(Thread(503, "2026-10-01T10:00:00Z", inbound: inbox));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 2, offset: 0, pinned: "7")));

        Assert.Equal(["501", "502"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.True((bool)answer["has_more"]!);
    }

    /// <summary>
    /// A page further in than one CLI page reaches: each bucket is read in steps of a hundred from its first thread, as
    /// far as the page reaches or the bucket ends, and the page is a slice of the merged order.
    /// </summary>
    [Fact]
    public async Task A_deep_page_of_every_thread_reads_each_bucket_in_steps_of_a_hundred()
    {
        using var account = Planted();
        for (var at = 0; at < 260; at++)
        {
            account.WithThread(Thread(10_000 + at, Moment(at)));
        }

        for (var at = 0; at < 120; at++)
        {
            account.WithThread(Thread(20_000 + at, Moment(at), inbound: false));
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 100, offset: 250, pinned: "7")));

        // The page reaches 350: the inbox ends at 260, so it is read at 0, 100 and 200; the sent bucket ends at 120.
        Assert.Equal(
            ["inbox 0 100", "inbox 100 100", "inbox 200 100", "sent 0 100", "sent 100 100"],
            account.Calls.Select(call => $"{call.Option("--source")} {call.Option("--offset")} {call.Option("--limit")}"));
        Assert.Equal(100, answer["items"]!.AsArray().Count);
        Assert.True((bool)answer["has_more"]!);
    }

    private static string Moment(int minutes) =>
        new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minutes)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A planted thread with an outbound reply after its inbound message, so its latest message is outbound.</summary>
    private static JsonObject Answered(JsonObject thread, string at)
    {
        thread["lastActivityDate"] = at;
        thread["messages"]!.AsArray().Add(new JsonObject
        {
            ["channel"] = thread["channel"]!.DeepClone(),
            ["messageId"] = $"<r{thread["id"]}@mail.example.com>",
            ["isOutbound"] = true,
            ["date"] = at,
            ["fromAddress"] = "sdr@vendor.example",
            ["subject"] = thread["subject"]?.DeepClone(),
            ["body"] = "Thursday works.",
        });
        return thread;
    }

    // -------------------------------------------------------------------------------------------------------
    // Refusals, and a read that writes nothing
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("access.feature_unavailable", "inboxThread.forbidden", "feature_unavailable")]
    [InlineData("access.denied", null, "unauthorized")]
    public async Task A_403_is_a_feature_the_account_lacks_only_where_reply_says_so(string cliCode, string? providerCode, string expected)
    {
        using var account = Planted();
        account.Refuses("inbox list", cliCode, status: 403, replyCode: providerCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7")));

        Assert.Equal(expected, error.Code);
    }

    /// <summary>
    /// A contact identifier Reply does not hold is answered with a validation problem that names no code — the shape
    /// a live account answered — so this version has no word for it, and it is not a person "not found": the
    /// document declares no such code for a read that answers an empty page for a person with no threads.
    /// </summary>
    [Fact]
    public async Task A_contact_reply_does_not_hold_is_a_refusal_this_version_has_no_word_for()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pin: "999999")));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(400, (int)error.Details!["status"]!);
    }

    [Fact]
    public async Task A_read_creates_nothing()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        var before = File.ReadAllText(Path.Combine(account.Root, ReplyAccount.ThreadsFile)) + account.Contacts.ToJsonString();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(limit: 20, offset: 0, pin: "1001")));

        Assert.Equal(before, File.ReadAllText(Path.Combine(account.Root, ReplyAccount.ThreadsFile)) + account.Contacts.ToJsonString());
    }

    // -------------------------------------------------------------------------------------------------------
    // Planting, composing and invoking
    // -------------------------------------------------------------------------------------------------------

    // -------------------------------------------------------------------------------------------------------
    // The thread's channel and the provider's category
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Both filters are Reply's to apply, before it pages: they travel on every page this read asks for — each of the
    /// two buckets of every thread included — so the page holds only threads that pass them and `offset` counts those.
    /// </summary>
    [Fact]
    public async Task The_thread_s_channel_and_its_category_travel_on_every_page_read_and_only_their_threads_come_back()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        account.WithThread(Thread(502, "2026-10-02T07:41:09Z", channel: "linkedIn", category: (3, "Interested")));
        account.WithThread(Thread(503, "2026-10-01T07:41:09Z", channel: "linkedIn"));
        account.WithThread(Thread(504, "2026-09-30T07:41:09Z", category: (3, "Interested")));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(limit: 20, offset: 0, pinned: "7");
        input["args"]!["thread_channel"] = "linkedin";
        input["args"]!["category"] = new JsonObject { ["external_id"] = "3" };
        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, input));

        Assert.Equal(["502"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.Equal(["inbox", "sent"], account.Calls.Select(call => call.Option("--source")));
        Assert.All(account.Calls, call => Assert.Equal(("linkedin", "3"), (call.Option("--channel"), call.Option("--category-id"))));
    }

    [Fact]
    public async Task An_inbound_page_on_one_channel_is_one_call_with_the_channel_in_its_place()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        account.WithThread(Thread(502, "2026-10-02T07:41:09Z", channel: "linkedIn"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(limit: 20, offset: 0, pinned: "7", source: "inbound");
        input["args"]!["thread_channel"] = "email";
        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, input));

        Assert.Equal(["501"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.Equal(
            ["--json", "-q", "inbox", "list", "--sequence-id", "7", "--source", "inbox", "--channel", "email", "--limit", "20", "--offset", "0"],
            Assert.Single(account.Calls).Args);
    }

    [Fact]
    public async Task A_category_that_is_not_a_reply_number_names_nothing_and_nothing_is_asked()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(limit: 20, offset: 0, pinned: "7");
        input["args"]!["category"] = new JsonObject { ["external_id"] = "interested" };
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    private static ReplyAccount Planted() =>
        ReplyOperations.Plant(new ReplyAccount())
            .WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName, lastName: ReplyOperations.LastName)
            .WithContact(1002, "lena@example.com", "Lena", lastName: "Okafor");

    /// <summary>
    /// One thread as Reply holds it: the list's fields, the whole party with the address, phone and profile URL a
    /// read must never carry on, and a message history whose direction decides whether the thread counts as a reply.
    /// </summary>
    internal static JsonObject Thread(
        int id,
        string lastActivity,
        int? contactId = 1001,
        string? name = "Marta Alvarez",
        bool deleted = false,
        string channel = "email",
        bool read = false,
        string? subject = "Re: Q3 pilot",
        string? preview = "Thanks — could we talk on Thursday?",
        int sequence = 7,
        bool inbound = true,
        string state = "ok",
        (int Id, string Name)? category = null,
        bool meetingIntent = false) => new()
    {
        ["id"] = id,
        ["channel"] = channel,
        ["isRead"] = read,
        ["subject"] = subject,
        ["bodyPreview"] = preview,
        ["lastActivityDate"] = lastActivity,
        ["isLastMessagePlanned"] = false,
        ["contact"] = new JsonObject
        {
            ["id"] = deleted ? null : contactId,
            ["ownerId"] = deleted ? null : 42,
            ["fullName"] = name,
            ["email"] = ReplyOperations.Address,
            ["linkedInProfileUrl"] = Profile,
            ["phone"] = Phone,
            ["companyName"] = ReplyOperations.Company,
            ["title"] = ReplyOperations.Title,
            ["isDeleted"] = deleted,
        },
        ["sequence"] = new JsonObject { ["id"] = sequence, ["name"] = "Q3 LatAm founders" },
        ["category"] = category is { } named ? new JsonObject { ["id"] = named.Id, ["name"] = named.Name } : null,
        ["hasMeetingIntent"] = meetingIntent,
        ["status"] = new JsonObject { ["state"] = state },
        ["emailAccountId"] = 77,
        ["linkedInAccountId"] = null,
        ["aiDraft"] = null,
        ["messages"] = new JsonArray(new JsonObject
        {
            ["channel"] = channel,
            ["messageId"] = $"<m{id}@mail.example.com>",
            ["isOutbound"] = !inbound,
            ["date"] = lastActivity,
            ["fromAddress"] = inbound ? ReplyOperations.Address : "sdr@vendor.example",
            ["subject"] = subject,
            ["body"] = preview,
        }),
    };

    private static JsonObject Input(
        int limit,
        int offset,
        string? pin = null,
        bool person = false,
        string? supplied = null,
        string? pinned = null,
        string source = "all",
        string? since = null)
    {
        var args = new JsonObject { ["limit"] = limit, ["offset"] = offset, ["source"] = source };
        if (supplied is not null)
        {
            args["campaign"] = new JsonObject { ["external_id"] = supplied };
        }

        if (since is not null)
        {
            args["since"] = since;
        }

        var input = new JsonObject
        {
            ["args"] = args,
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
            },
        };

        if (pin is not null || person)
        {
            input["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["external_ids"] = pin is null ? new JsonObject() : new JsonObject { ["contact"] = pin },
            });
        }

        return input;
    }

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct);
}
