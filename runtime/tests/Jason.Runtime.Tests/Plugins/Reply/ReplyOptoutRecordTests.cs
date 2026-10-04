using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>optout.record</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. One test per line of the document's conformance list,
/// and then the lookup that finds the person by address before anything is created — exact, whatever the case, and
/// refused rather than guessed wherever it cannot be finished.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyOptoutRecordTests
{
    private const string Operation = "optout.record";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRM";
    private const string Address = "marta@example.com";
    private const string SetPath = "/v3/contacts/set-opted-out";
    private const string FirstPage = "/v3/contacts/filter?top=100&skip=0";

    // What they said, where it arrived and when: Jason's record, every word of which a test looks for at the provider.
    private const string Statement = "please take me off your list";
    private const string ArrivedVia = "social";
    private const string SaidAt = "2026-10-03T08:14:00Z";
    private const string LearnedAt = "2026-10-03T08:20:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list, every entry of it covered
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_contact_is_worked_by_the_pin_and_never_matched_by_the_address_again()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(
            $$$"""{"items":[{"contact_id":"{{{ContactId}}}","status":"recorded","external_ids":{"contact":"1001"}}]}""",
            answer.ToJsonString());
        Assert.Equal(["GET /v3/contacts/1001/statuses", $"POST {SetPath}"], Paths(account));
        Assert.DoesNotContain(Address, Everything(account), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([1001], OptedOut(account));
    }

    [Fact]
    public async Task What_they_said_where_and_when_never_reaches_the_provider()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        // The whole path ran — the lookup, the create, the flag read and the write — and none of the four facts
        // reached any of it: the provider holds a yes or a no.
        Assert.Equal("recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal(["POST " + FirstPage, "POST /v3/contacts/import", "GET /v3/contacts/1001/statuses", $"POST {SetPath}"], Paths(account));
        var everything = Everything(account);
        foreach (var fact in new[] { Statement, ArrivedVia, SaidAt, LearnedAt, "take me off" })
        {
            Assert.DoesNotContain(fact, everything, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_person_the_provider_already_holds_as_opted_out_answers_already_recorded_and_nothing_is_written()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta", optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("already_recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal(["GET /v3/contacts/1001/statuses"], Paths(account));
        Assert.Equal([1001], OptedOut(account));
    }

    [Fact]
    public async Task The_flag_is_only_ever_set_and_never_cleared()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        var write = Assert.Single(account.Calls, call => call.Path == SetPath);
        Assert.Equal("""{"contactIds":[1001],"isOptedOut":true}""", JsonNode.Parse(write.Body!)!.ToJsonString());
    }

    /// <summary>
    /// The person Reply already holds is the one who said stop, found by the address whatever its case — so the
    /// opt-out lands on them rather than on a second contact made for the same address. Without a first name the
    /// only way to make one is the create path, which has no word for a duplicate: this is the case the lookup is for.
    /// </summary>
    [Fact]
    public async Task An_opt_out_never_creates_a_second_contact_for_an_address_the_provider_already_holds()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "Marta@Example.COM");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(firstName: null));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal("1001", (string?)answer["items"]![0]!["external_ids"]!["contact"]);
        Assert.Equal("""{"contact":"1001"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Single(account.Contacts);
        Assert.Equal(["POST " + FirstPage, "GET /v3/contacts/1001/statuses", $"POST {SetPath}"], Paths(account));
        Assert.Equal([1001], OptedOut(account));
    }

    [Fact]
    public async Task More_than_one_contact_under_the_address_is_refused_as_not_unique_and_nothing_is_done()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta").WithDuplicate(1002, of: 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("contact_not_unique", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Equal(["POST " + FirstPage], Paths(account));
        Assert.Empty(account.OptOuts);
    }

    /// <summary>
    /// The search matches by containment and the lookup reads at most five pages of a hundred. Here the person is on
    /// the first page and five hundred others whose addresses hold theirs fill the rest: the end is never reached, so
    /// one match proves nothing about a second, and the lookup is refused — never a pin, never a create. The message,
    /// which is all that reaches the work item's last error, says what a person does instead.
    /// </summary>
    [Fact]
    public async Task A_lookup_that_cannot_be_read_to_its_end_is_refused_and_never_taken_for_nobody_or_for_one()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        for (var at = 0; at < 500; at++)
        {
            account.WithContact(2000 + at, $"x{at}.{Address}", "Near" + at);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Contains("record the opt-out at Reply by hand", error.Message, StringComparison.Ordinal);
        Assert.Contains("Jason's own register already stops Jason's own reach", error.Message, StringComparison.Ordinal);
        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(page => $"POST /v3/contacts/filter?top=100&skip={page * 100}")],
            Paths(account));
        Assert.Equal(501, account.Contacts.Count);
        Assert.Empty(account.OptOuts);
    }

    // -------------------------------------------------------------------------------------------------------
    // The lookup, and what a search that matches by containment means for it
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The address goes to the search on stdin and nowhere else, and nothing but the term is sent: a rule set is
    /// ignored by Reply and would answer a page of everybody.
    /// </summary>
    [Fact]
    public async Task The_lookup_sends_the_address_as_a_search_term_on_stdin_and_nowhere_else()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        var search = Assert.Single(account.Calls, call => call.Path.StartsWith("/v3/contacts/filter", StringComparison.Ordinal));
        Assert.Equal($$"""{"searchTerm":"{{Address}}"}""", JsonNode.Parse(search.Body!)!.ToJsonString());
        Assert.DoesNotContain(Address, string.Join(" ", search.Args), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A contact whose address merely holds this one is answered by the search and is not this person. Nobody
    /// matches exactly, so the provider has never held them: the contact is made from the address, and the near
    /// match is left as it was.
    /// </summary>
    [Fact]
    public async Task A_near_match_is_not_the_person_and_the_contact_is_made_from_the_address()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "ana." + Address, "Ana");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        var made = (string?)answer["items"]![0]!["external_ids"]!["contact"];
        Assert.NotEqual("1001", made);
        Assert.Equal(["POST " + FirstPage, "POST /v3/contacts/import", $"GET /v3/contacts/{made}/statuses", $"POST {SetPath}"], Paths(account));
        Assert.Equal([int.Parse(made!, System.Globalization.CultureInfo.InvariantCulture)], OptedOut(account));
    }

    /// <summary>
    /// The first page holding no exact match is not an answer: a hundred addresses that hold this one fill it, and the
    /// person is on the second. The opt-out is recorded on that person, and nobody is made.
    /// </summary>
    [Fact]
    public async Task A_person_on_the_second_page_of_the_lookup_is_found_and_nobody_is_made()
    {
        using var account = new ReplyAccount();
        for (var at = 0; at < 100; at++)
        {
            account.WithContact(2000 + at, $"x{at}.{Address}", "Near" + at);
        }

        account.WithContact(1001, Address);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(firstName: null)));

        Assert.Equal("recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal("1001", (string?)answer["items"]![0]!["external_ids"]!["contact"]);
        Assert.Equal(
            ["POST " + FirstPage, "POST /v3/contacts/filter?top=100&skip=100", "GET /v3/contacts/1001/statuses", $"POST {SetPath}"],
            Paths(account));
        Assert.Equal(101, account.Contacts.Count);
        Assert.Equal([1001], OptedOut(account));
    }

    /// <summary>
    /// A match under an identifier this package cannot work by is reported, and what travels into the attempt's record is
    /// the identifier alone: the rest of a search answer is a person's record, and none of it belongs in a failure.
    /// </summary>
    [Fact]
    public async Task A_match_under_an_identifier_nobody_can_work_by_is_reported_by_that_identifier_alone()
    {
        using var account = new ReplyAccount();
        account.Answers(
            "POST",
            FirstPage,
            200,
            $$"""{"items":[{"id":"c-88","email":"{{Address}}","firstName":"Recorded-marker","lastName":"Elsewhere-marker","phone":"+15550100"}],"hasMore":false}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        var item = (string?)error.Details!["provider_item"];
        Assert.Contains("c-88", item, StringComparison.Ordinal);
        foreach (var field in new[] { "Recorded-marker", "Elsewhere-marker", "+15550100", Address })
        {
            Assert.DoesNotContain(field, item, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_pin_that_is_not_a_reply_contact_number_resolves_to_nobody_and_nothing_is_asked()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "p_88421")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_pin_the_provider_no_longer_holds_is_not_found_and_not_matched_again_by_address()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "4242")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Equal(["GET /v3/contacts/4242/statuses"], Paths(account));
        Assert.Empty(account.OptOuts);
    }

    /// <summary>The write answers per contact inside a 200; a contact it says it does not hold is not found.</summary>
    [Fact]
    public async Task A_contact_the_write_says_it_does_not_hold_is_not_found()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        account.Answers("POST", SetPath, 200, """{"1001":{"error":"notFound","errorDetails":"Contact 1001 does not exist."}}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Equal("1001", (string?)error.ExternalIds!["contact"]);
    }

    /// <summary>
    /// An opt-out whose answer was lost may already be recorded. The failure carries the contact it was about, so the
    /// next attempt has a pin to read the flag of, and a flag already set answers `already_recorded` with no second
    /// write.
    /// </summary>
    [Fact]
    public async Task An_opt_out_whose_answer_was_lost_is_read_first_and_answers_already_recorded()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        account.LosesTheAnswerAfterWriting("POST", SetPath);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);
        Assert.Equal("1001", (string?)lost.ExternalIds!["contact"]);

        var mark = account.Mark();
        var repeated = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));
        Assert.Equal("already_recorded", (string?)repeated["items"]![0]!["status"]);
        Assert.Equal(["GET /v3/contacts/1001/statuses"], account.CallsSince(mark).Select(call => call.Method + " " + call.Path));
    }

    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Input(string? pinned = null, string? firstName = "Marta")
    {
        var contact = new JsonObject
        {
            ["id"] = ContactId,
            ["first_name"] = firstName,
            ["last_name"] = null,
            ["company"] = null,
            ["title"] = null,
            ["time_zone"] = null,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address, ["primary"] = true }),
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
        };

        return new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = "email",
                ["statement"] = Statement,
                ["arrived_via"] = ArrivedVia,
                ["said_at"] = SaidAt,
                ["learned_at"] = LearnedAt,
            },
            ["contacts"] = new JsonArray(contact),
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "paused",
                ["external_ids"] = new JsonObject(),
            },
            ["idempotency_key"] = WorkItemId,
        };
    }

    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Method + " " + call.Path)];

    private static IReadOnlyList<int> OptedOut(ReplyAccount account) =>
        [.. account.OptOuts.Select(entry => entry!.GetValue<int>()).Order()];

    /// <summary>Every path, argument and body the plugin sent, as one string to search.</summary>
    private static string Everything(ReplyAccount account) =>
        string.Join("\n", account.Calls.Select(call => call.Path + " " + string.Join(" ", call.Args) + " " + call.Body));

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
