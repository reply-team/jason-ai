using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>conversation.get</c> as the official package performs it, against a stand-in holding a thread as Reply holds it:
/// no subject of its own, the whole history inline with the header, and a drafted answer whose text must never travel.
/// One test per line of the document's conformance list, and the refusals Reply has a word for.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyConversationGetTests
{
    private const string Operation = "conversation.get";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thread_reads_first_message_first_with_who_wrote_each_and_to_whom()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithThread(Thread(501, [
            Email("<a@x>", outbound: true, "2026-09-29T11:00:00+03:00", "Q3 pilot", "<p>Hi Marta</p>", to: ["marta@example.com"], cc: ["boss@example.com"]),
            Email("<b@x>", outbound: false, "2026-10-03T07:41:09Z", "Re: Q3 pilot", "<p>Thursday?</p>", to: ["sdr@vendor.example"]),
        ]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input("501", limit: 20, offset: 0));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(
            """{"external_id":"501","channel":"email","subject":"Re: Q3 pilot","contact":{"external_id":"1001","name":"Marta Alvarez"},"campaign_external_id":"7","unread":true,"vendor":{"category":null,"has_ai_draft":false}}""",
            answer["thread"]!.ToJsonString());

        var messages = answer["messages"]!.AsArray();
        Assert.Equal(
            """{"external_id":"<a@x>","direction":"outbound","sent_at":"2026-09-29T08:00:00.000Z","from":"sdr@vendor.example","to":["marta@example.com"],"cc":["boss@example.com"],"bcc":null,"subject":"Q3 pilot","body":"<p>Hi Marta</p>","truncated":false,"vendor":{"state":"ok","status_code":null,"ai_sent_type":"ai_generated_edited"}}""",
            messages[0]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Equal("inbound", (string?)messages[1]!["direction"]);
        Assert.Equal("oldest_first", (string?)answer["vendor"]!["ordering"]);

        // The thread named itself, so nothing is pinned; two reads, the header and then the page, with no body.
        Assert.Null(Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds);
        var call = Assert.Single(account.Calls);
        Assert.Equal("inbox get 501", call.Line);
        Assert.Equal(("20", "0"), (call.Option("--limit"), call.Option("--offset")));
        Assert.All(account.Calls, call => Assert.Null(call.Body));
    }

    [Fact]
    public async Task A_linkedin_thread_has_no_subject_and_its_writer_is_the_name_reply_shows()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var thread = Thread(501, [LinkedIn(outbound: false, "2026-10-02T18:02:44Z", "Marta Alvarez", "Happy to connect.")]);
        thread["channel"] = "linkedIn";
        account.WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)));

        Assert.Equal("linkedin", (string?)answer["thread"]!["channel"]);
        Assert.Null(answer["thread"]!["subject"]);
        var message = answer["messages"]![0]!;
        Assert.Equal("Marta Alvarez", (string?)message["from"]);
        Assert.Null(message["to"]);
        Assert.Null(message["external_id"]);
    }

    [Fact]
    public async Task A_body_longer_than_the_document_allows_is_cut_where_a_character_ends_and_says_so()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var body = new string('Б', 11999) + "😀" + "the rest";
        account.WithThread(Thread(501, [Email("<a@x>", outbound: false, "2026-10-03T07:41:09Z", "Re: Q3 pilot", body, to: ["sdr@vendor.example"])]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)));

        var message = answer["messages"]![0]!;
        Assert.Equal(new string('Б', 11999) + "😀", (string?)message["body"]);
        Assert.True((bool)message["truncated"]!);
    }

    /// <summary>
    /// The document counts a body in characters, as its validator does, and an emoji is one character though
    /// JavaScript counts it as two. Seven thousand of them are within the 12000 a body may hold, so nothing is cut.
    /// </summary>
    [Fact]
    public async Task A_body_within_its_bound_in_characters_is_not_cut_however_many_units_javascript_counts()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var body = string.Concat(Enumerable.Repeat("😀", 7000));
        account.WithThread(Thread(501, [Email("<a@x>", outbound: false, "2026-10-03T07:41:09Z", "Re: Q3 pilot", body, to: ["sdr@vendor.example"])]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var message = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)))["messages"]![0]!;

        Assert.Equal(body, (string?)message["body"]);
        Assert.False((bool)message["truncated"]!);
    }

    [Fact]
    public async Task A_drafted_answer_is_reported_and_its_text_never_returned()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var thread = Thread(501, [Email("<a@x>", outbound: false, "2026-10-03T07:41:09Z", "Re: Q3 pilot", "Thursday?", to: ["sdr@vendor.example"])]);
        thread["aiDraft"] = new JsonObject { ["message"] = "Sure, Thursday at ten works.", ["cc"] = new JsonArray(), ["bcc"] = new JsonArray() };
        account.WithThread(thread);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)));

        Assert.True((bool)answer["thread"]!["vendor"]!["has_ai_draft"]!);
        Assert.DoesNotContain("Thursday at ten", answer.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>A provider word naming a property every JavaScript object has is still a word this vocabulary lacks.</summary>
    [Fact]
    public async Task A_provider_word_that_names_an_object_property_maps_to_nothing()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var message = Email("<a@x>", outbound: true, "2026-10-03T07:41:09Z", "Q3 pilot", "Hello", to: ["x@example.com"]);
        message["aiSentType"] = "constructor";
        message["status"] = new JsonObject { ["state"] = "toString", ["code"] = null, ["occurredAt"] = null };
        account.WithThread(Thread(501, [message]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var vendor = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)))["messages"]![0]!["vendor"]!;

        Assert.Null(vendor["ai_sent_type"]);
        Assert.Null(vendor["state"]);
    }

    [Fact]
    public async Task Recipients_are_capped_at_ten_each()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        var many = Enumerable.Range(0, 14).Select(at => $"r{at}@example.com").ToArray();
        account.WithThread(Thread(501, [Email("<a@x>", outbound: true, "2026-10-03T07:41:09Z", "All hands", "Hello", to: many, cc: many, bcc: many)]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var message = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)))["messages"]![0]!;

        Assert.Equal(10, message["to"]!.AsArray().Count);
        Assert.Equal(10, message["cc"]!.AsArray().Count);
        Assert.Equal(10, message["bcc"]!.AsArray().Count);
    }

    [Fact]
    public async Task Pages_follow_on_by_the_number_of_messages_returned()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithThread(Thread(501, [.. Enumerable.Range(0, 25).Select(at =>
            Email($"<m{at}@x>", outbound: at % 2 == 0, DateTimeOffset.UnixEpoch.AddMinutes(at).ToString("O", CultureInfo.InvariantCulture), "Re: Q3", "message " + at, to: ["x@example.com"]))]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)));
        var second = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 20)));

        Assert.True((bool)first["has_more"]!);
        Assert.False((bool)second["has_more"]!);
        Assert.Equal(
            Enumerable.Range(0, 25).Select(at => $"<m{at}@x>"),
            first["messages"]!.AsArray().Concat(second["messages"]!.AsArray()).Select(message => (string)message!["external_id"]!));
    }

    /// <summary>
    /// The short page this read exists for: twenty bodies of 12000 characters of Cyrillic are a third past what the
    /// runtime accepts as one result, so the page comes back short with more to follow, and the pages after it read
    /// every message once.
    /// </summary>
    [Fact]
    public async Task Twenty_long_bodies_in_a_script_the_runtime_counts_at_six_bytes_come_back_over_several_pages()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithThread(Thread(501, [.. Enumerable.Range(0, 20).Select(at =>
            Email($"<m{at}@x>", outbound: false, DateTimeOffset.UnixEpoch.AddMinutes(at).ToString("O", CultureInfo.InvariantCulture), "Re: Q3", new string('Ж', 12000), to: ["x@example.com"]))]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var pages = new List<int>();
        var offset = 0;
        var more = true;
        while (more)
        {
            var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: offset)));
            var messages = answer["messages"]!.AsArray();
            seen.AddRange(messages.Select(message => (string)message!["external_id"]!));
            pages.Add(messages.Count);
            more = (bool)answer["has_more"]!;
            offset += messages.Count;
            Assert.True(pages.Count <= 5, "the pages never ended");
        }

        Assert.True(pages[0] < 20, $"the first page held {pages[0]} messages, so nothing was cut");
        Assert.Equal(Enumerable.Range(0, 20).Select(at => $"<m{at}@x>"), seen);
    }

    /// <summary>
    /// DEL is ASCII, so JavaScript's JSON writes it as itself, while the runtime's measure escapes it at six bytes.
    /// A sender controls a message's body, so twenty bodies of it are a page the plugin must count as the runtime
    /// does — or the page passes the cap and the answer is refused for good.
    /// </summary>
    [Fact]
    public async Task Twenty_bodies_of_a_character_the_runtime_escapes_and_javascript_does_not_still_come_back_in_pages()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithThread(Thread(501, [.. Enumerable.Range(0, 20).Select(at =>
            Email($"<m{at}@x>", outbound: false, DateTimeOffset.UnixEpoch.AddMinutes(at).ToString("O", CultureInfo.InvariantCulture), "Re: Q3", new string((char)0x7F, 12000), to: ["x@example.com"]))]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var offset = 0;
        var more = true;
        while (more)
        {
            var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: offset)));
            var messages = answer["messages"]!.AsArray();
            Assert.True(seen.Count > 0 || messages.Count < 20, "the first page held all twenty bodies, past what one result may be");
            seen.AddRange(messages.Select(message => (string)message!["external_id"]!));
            more = (bool)answer["has_more"]!;
            offset += messages.Count;
            Assert.True(offset <= 20 && seen.Count <= 20, "the pages never ended");
        }

        Assert.Equal(Enumerable.Range(0, 20).Select(at => $"<m{at}@x>"), seen);
    }

    [Fact]
    public async Task A_thread_reply_does_not_hold_is_not_found_permanently()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)));

        Assert.Equal("conversation_not_found", error.Code);
        Assert.Equal("inboxThread.notFound", (string?)error.Details!["provider_code"]);
        Assert.Single(account.Calls);
    }

    [Fact]
    public async Task An_identifier_that_is_not_a_thread_number_is_refused_before_anything_is_asked()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("t_99812", limit: 20, offset: 0)));

        Assert.Equal("conversation_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    [Theory]
    [InlineData("access.feature_unavailable", "inboxThread.forbidden", "feature_unavailable")]
    [InlineData("access.denied", null, "unauthorized")]
    public async Task A_403_is_a_feature_the_account_lacks_only_where_reply_says_so(string cliCode, string? providerCode, string expected)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Refuses("inbox get", cliCode, status: 403, replyCode: providerCode);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input("501", limit: 20, offset: 0)));

        Assert.Equal(expected, error.Code);
    }

    /// <summary>
    /// The CLI reads a thread without its inline history, so a history far longer than the exec capture is read a page
    /// at a time, however long it grows.
    /// </summary>
    [Fact]
    public async Task A_thread_with_a_long_history_is_read_a_page_at_a_time()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithThread(Thread(501, [.. Enumerable.Range(0, 30).Select(at =>
            Email($"<m{at}@x>", outbound: false, "2026-10-03T07:41:09Z", "Re: Q3", new string('a', 5000), to: ["x@example.com"]))]));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, paths => Capping(paths, 65_536));

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input("501", limit: 1, offset: 0)));

        Assert.Single(answer["messages"]!.AsArray());
        Assert.True((bool)answer["has_more"]!);
    }

    // -------------------------------------------------------------------------------------------------------
    // Planting, composing and invoking
    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Thread(int id, JsonObject[] messages)
    {
        var thread = ReplyConversationListTests.Thread(id, "2026-10-03T07:41:09Z");
        thread["messages"] = new JsonArray([.. messages]);
        return thread;
    }

    private static JsonObject Email(string id, bool outbound, string at, string subject, string body, string[] to, string[]? cc = null, string[]? bcc = null) => new()
    {
        ["channel"] = "email",
        ["messageId"] = id,
        ["date"] = at,
        ["body"] = body,
        ["fromName"] = outbound ? "SDR" : "Marta Alvarez",
        ["isOutbound"] = outbound,
        ["status"] = new JsonObject { ["state"] = "ok", ["code"] = null, ["occurredAt"] = null },
        ["aiSentType"] = outbound ? "aiGeneratedEdited" : null,
        ["subject"] = subject,
        ["fromAddress"] = outbound ? "sdr@vendor.example" : ReplyOperations.Address,
        ["to"] = new JsonArray([.. to.Select(address => (JsonNode)address)]),
        ["cc"] = cc is null ? null : new JsonArray([.. cc.Select(address => (JsonNode)address)]),
        ["bcc"] = bcc is null ? null : new JsonArray([.. bcc.Select(address => (JsonNode)address)]),
    };

    private static JsonObject LinkedIn(bool outbound, string at, string name, string body) => new()
    {
        ["channel"] = "linkedIn",
        ["messageId"] = null,
        ["date"] = at,
        ["body"] = body,
        ["fromName"] = name,
        ["isOutbound"] = outbound,
        ["status"] = null,
        ["aiSentType"] = null,
    };

    private static JsonObject Input(string thread, int limit, int offset) => new()
    {
        ["args"] = new JsonObject
        {
            ["thread"] = new JsonObject { ["external_id"] = thread },
            ["limit"] = limit,
            ["offset"] = offset,
        },
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "7" },
        },
    };

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct);

    private static void Capping(JasonPaths paths, int outputBytes)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!.AsObject()["Exec"] = new JsonObject { ["OutputBytes"] = outputBytes };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
    }
}
