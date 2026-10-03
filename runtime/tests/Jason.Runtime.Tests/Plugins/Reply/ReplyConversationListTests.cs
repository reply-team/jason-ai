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
    private const string FilterPath = "/v3/inbox/threads/filter";
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

        var call = Assert.Single(account.Calls);
        Assert.Equal($"{FilterPath}?top=20&skip=0", call.Path);
        Assert.Equal("""{"contactIds":[1001]}""", call.Body);
        Assert.Equal(["--json", "-q", "api", $"{FilterPath}?top=20&skip=0", "--method", "POST", "--body", "-"], call.Args);
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
        Assert.All(account.Calls, call => Assert.Equal("""{"sequenceIds":[7]}""", call.Body));
    }

    [Fact]
    public async Task Replies_only_and_since_travel_as_the_provider_s_own_filter()
    {
        using var account = Planted();
        account.WithThread(Thread(501, "2026-10-03T07:41:09Z"));
        account.WithThread(Thread(502, "2026-10-02T18:02:44Z", inbound: false));
        account.WithThread(Thread(503, "2026-09-20T10:00:00Z"));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(
            Operation,
            await InvokeAsync(api, Input(limit: 20, offset: 0, pinned: "7", source: "inbound", since: "2026-10-01T00:00:00Z")));

        Assert.Equal(["501"], answer["items"]!.AsArray().Select(thread => (string)thread!["external_id"]!));
        Assert.Equal("""{"sequenceIds":[7],"source":"inbox","from":"2026-10-01T00:00:00Z"}""", Assert.Single(account.Calls).Body);
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
    // Refusals, and a read that writes nothing
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("inboxThread.forbidden", "feature_unavailable")]
    [InlineData("auth.scopeMissing", "unauthorized")]
    public async Task A_403_is_a_feature_the_account_lacks_only_where_reply_says_so(string providerCode, string expected)
    {
        using var account = Planted();
        account.Answers("POST", $"{FilterPath}?top=20&skip=0", 403, new JsonObject
        {
            ["title"] = "Forbidden",
            ["status"] = 403,
            ["detail"] = "Inbox feature is not available for this user.",
            ["code"] = providerCode,
        }.ToJsonString());
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
