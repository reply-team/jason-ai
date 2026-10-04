using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>enrollment.stop</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. One test per line of the document's conformance list,
/// and then the ways Reply answers a person who takes no part. A stop creates nobody: the person is found by the pin
/// or by looking the address up, exactly and whatever its case, and a lookup that cannot be finished is refused.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyEnrollmentStopTests
{
    private const string Operation = "enrollment.stop";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRN";
    private const int Sequence = ReplyOperations.Sequence;
    private const string Address = "marta@example.com";
    private const string FirstPage = "/v3/contacts/filter?top=100&skip=0";
    private const string Note = "replied that the timing is wrong";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SequencePath = "/v3/sequences/7";

    private static string Participation(int contact) => $"/v3/sequences/{Sequence}/contacts/{contact}";

    private static string Removal(int contact) => $"/v3/sequences/{Sequence}/contact-links/{contact}";

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list, every entry of it covered
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_contact_is_worked_by_the_pin_and_never_matched_by_the_address_again()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(
            $$$"""{"items":[{"contact_id":"{{{ContactId}}}","status":"stopped","external_ids":{"contact":"1001"}}]}""",
            answer.ToJsonString());
        Assert.Equal("""{"contact":"1001","campaign":"7"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal([$"GET {Participation(1001)}", $"DELETE {Removal(1001)}"], Paths(account));
        Assert.DoesNotContain(Address, Everything(account), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// A person Reply has never held takes part in nothing, which is a completed stop — and nobody is made to find
    /// that out. The lookup is the only call.
    /// </summary>
    [Fact]
    public async Task A_stop_creates_nobody_and_a_person_the_provider_never_held_answers_already_stopped()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input());

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal($$"""{"items":[{"contact_id":"{{ContactId}}","status":"already_stopped"}]}""", answer.ToJsonString());
        Assert.Equal("""{"campaign":"7"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["POST " + FirstPage], Paths(account));
        Assert.Empty(account.Contacts);
    }

    /// <summary>
    /// The person Reply holds under another address is another contact there, and the lookup compares this one
    /// exactly: it finds nobody, answers `already_stopped`, and the participation under the other address stays —
    /// which `enrollment.list` shows.
    /// </summary>
    [Fact]
    public async Task A_person_enrolled_under_another_address_is_not_found_and_answers_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, "marta.work@example.com", "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task More_than_one_contact_under_the_address_is_refused_as_not_unique_and_nothing_is_removed()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithDuplicate(1002, of: 1001).WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("contact_not_unique", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(["POST " + FirstPage], Paths(account));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// The person is on the first page and five hundred addresses that hold theirs fill the rest, so the end is never
    /// reached. A stop that took that for nobody would answer `already_stopped` over a person still being written to;
    /// it is refused instead, and nothing is removed. The message, which is all that reaches the work item's last error,
    /// says what a person does instead.
    /// </summary>
    [Fact]
    public async Task A_lookup_that_cannot_be_read_to_its_end_is_refused_and_never_answers_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        for (var at = 0; at < 500; at++)
        {
            account.WithContact(2000 + at, $"x{at}.{Address}", "Near" + at);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("take the person out at Reply by hand, then check with enrollment.list", error.Message, StringComparison.Ordinal);
        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(page => $"POST /v3/contacts/filter?top=100&skip={page * 100}")],
            Paths(account));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("finished")]
    public async Task A_participation_that_is_absent_or_finished_answers_already_stopped_and_nothing_is_removed(string? state)
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta");
        if (state is not null)
        {
            account.WithEnrollment(Sequence, 1001, statusInSequence: state);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal("1001", (string?)answer["items"]![0]!["external_ids"]!["contact"]);
        Assert.Equal([$"GET {Participation(1001)}"], Paths(account));
    }

    /// <summary>
    /// Reply answers a contact it no longer holds and a contact that takes no part with the same 404, so a well-formed
    /// pin that resolves to nobody reads as the completed stop it cannot be told apart from.
    /// </summary>
    [Fact]
    public async Task A_pin_the_provider_no_longer_holds_reads_as_already_stopped()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "4242")));

        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal([$"GET {Participation(4242)}"], Paths(account));
    }

    /// <summary>The same pin, where Reply words the 404 as a contact it does not hold: the same completed stop.</summary>
    [Fact]
    public async Task A_pin_reply_says_it_does_not_hold_reads_as_already_stopped()
    {
        using var account = Planted();
        account.Answers("GET", Participation(4242), 404, """{"type":"about:blank","title":"Not Found","status":404,"code":"contact.notFound"}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "4242")));

        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
    }

    /// <summary>
    /// The person left between the read and the removal, and the removal says they take no part: a stop all the same.
    /// </summary>
    [Fact]
    public async Task A_removal_that_finds_the_person_already_out_answers_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        account.Answers("DELETE", Removal(1001), 404, """{"type":"about:blank","title":"Not Found","status":404,"code":"sequenceContact.notInSequence"}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
    }

    [Fact]
    public async Task The_person_is_taken_out_and_never_marked_as_having_finished()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        // The one write is the removal of the participation itself; nothing sets a state on it, so no count of the
        // people who completed the campaign ever includes this person.
        var writes = account.Calls.Where(call => call.Method != "GET").ToList();
        Assert.Equal($"DELETE {Removal(1001)}", Assert.Single(writes).Method + " " + writes[0].Path);
        Assert.Null(writes[0].Body);
        Assert.DoesNotContain(account.Enrollments, entry => (int)entry!["contactId"]! == 1001);
    }

    [Fact]
    public async Task The_reason_never_reaches_the_provider()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal(["POST " + FirstPage, $"GET {Participation(1001)}", $"DELETE {Removal(1001)}"], Paths(account));
        var everything = Everything(account);
        foreach (var word in new[] { Note, "not_interested", "timing", "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRE" })
        {
            Assert.DoesNotContain(word, everything, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // Finding the person, and what Reply answers
    // -------------------------------------------------------------------------------------------------------

    /// <summary>The person Reply holds is found whatever the case of the address, and the pin travels out.</summary>
    [Fact]
    public async Task A_person_found_by_the_address_whatever_its_case_is_taken_out_and_pinned()
    {
        using var account = Planted();
        account.WithContact(1001, "MARTA@example.com", "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input());

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal("""{"contact":"1001","campaign":"7"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        var search = Assert.Single(account.Calls, call => call.Path == FirstPage);
        Assert.Equal($$"""{"searchTerm":"{{Address}}"}""", JsonNode.Parse(search.Body!)!.ToJsonString());
        Assert.DoesNotContain(Address, string.Join(" ", search.Args), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// The first page holding no exact match is not an answer: a hundred addresses that hold this one fill it, and the
    /// person is on the second. The walk reads on, finds them, and takes them out.
    /// </summary>
    [Fact]
    public async Task A_person_on_the_second_page_of_the_lookup_is_found_and_taken_out()
    {
        using var account = Planted();
        for (var at = 0; at < 100; at++)
        {
            account.WithContact(2000 + at, $"x{at}.{Address}", "Near" + at);
        }

        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal("1001", (string?)answer["items"]![0]!["external_ids"]!["contact"]);
        Assert.Equal(
            ["POST " + FirstPage, "POST /v3/contacts/filter?top=100&skip=100", $"GET {Participation(1001)}", $"DELETE {Removal(1001)}"],
            Paths(account));
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// A page shorter than asked for is not the end: only <c>hasMore</c> says that. Here the provider answers a first page
    /// of three with more to come, and the person is on the next.
    /// </summary>
    [Fact]
    public async Task A_short_page_that_says_there_is_more_is_not_the_end_of_the_lookup()
    {
        using var account = Planted();
        for (var at = 0; at < 100; at++)
        {
            account.WithContact(2000 + at, $"x{at}.{Address}", "Near" + at);
        }

        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        account.Answers(
            "POST",
            FirstPage,
            200,
            $$"""{"items":[{"id":2000,"email":"x0.{{Address}}"},{"id":2001,"email":"x1.{{Address}}"},{"id":2002,"email":"x2.{{Address}}"}],"hasMore":true}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Contains("POST /v3/contacts/filter?top=100&skip=100", Paths(account));
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// A stop takes out exactly the people the register would refuse to reach, so Reply's own opt-out is not read and
    /// does not stand in the way.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_as_opted_out_is_taken_out_all_the_same()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta", optedOut: true).WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("stopped", (string?)answer["items"]![0]!["status"]);
        Assert.DoesNotContain(account.Calls, call => call.Path.EndsWith("/statuses", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pin_that_is_not_a_reply_contact_number_resolves_to_nobody_and_nothing_is_asked()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "p_88421")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Equal("7", (string?)error.ExternalIds!["campaign"]);
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// Reply answers a participation read with one 404 for a sequence it does not hold, a contact it does not hold and a
    /// person who takes no part. So a campaign named for the first time is read before anything else: an identifier
    /// Reply does not hold is not found, nothing is removed, and no pin is recorded for it — where the lumped 404 would
    /// have read as a stop and pinned the wrong campaign for good.
    /// </summary>
    [Fact]
    public async Task A_campaign_named_the_first_time_that_reply_does_not_hold_is_not_found_and_nothing_is_pinned()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001", campaign: null, supplied: "8")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.False(error.ExternalIds?.ContainsKey("campaign") ?? false);
        Assert.Equal(["GET /v3/sequences/8"], Paths(account));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// The same first link, with nobody to find by the address: the campaign is read before the lookup, so a stop that
    /// would have answered `already_stopped` against a campaign Reply does not hold is not found instead.
    /// </summary>
    [Fact]
    public async Task A_first_link_reply_does_not_hold_is_not_found_before_anybody_is_looked_up()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(campaign: null, supplied: "8")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.Null(error.ExternalIds);
        Assert.Equal(["GET /v3/sequences/8"], Paths(account));
    }

    /// <summary>A campaign named the first time and held by Reply is read once, then worked and pinned.</summary>
    [Fact]
    public async Task A_campaign_named_the_first_time_is_read_once_then_worked_and_pinned()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001", campaign: null, supplied: "7"));

        Assert.Equal("stopped", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["status"]);
        Assert.Equal("""{"contact":"1001","campaign":"7"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal([$"GET {SequencePath}", $"GET {Participation(1001)}", $"DELETE {Removal(1001)}"], Paths(account));
    }

    /// <summary>
    /// A campaign Jason pinned earlier is not read again. If Reply no longer holds it, the participation read answers
    /// its one lumped 404, and a person who takes part in nothing there reads as the completed stop it is.
    /// </summary>
    [Fact]
    public async Task A_pinned_campaign_reply_no_longer_holds_reads_as_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001", campaign: "8")));

        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Equal(["GET /v3/sequences/8/contacts/1001"], Paths(account));
    }

    /// <summary>A 404 in a word this version does not know is reported as itself, never read as a person who is out.</summary>
    [Fact]
    public async Task A_404_in_a_word_this_version_does_not_know_is_reported_and_never_read_as_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        account.Answers("GET", Participation(1001), 404, """{"type":"about:blank","title":"Not Found","status":404,"code":"sequenceContact.somethingElse"}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal("sequenceContact.somethingElse", (string?)error.Details!["provider_code"]);
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// A removal whose answer was lost may already have removed. The next attempt reads the participation first and
    /// answers `already_stopped` when the person is out, without a second removal.
    /// </summary>
    [Fact]
    public async Task A_removal_whose_answer_was_lost_is_read_first_and_answers_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        account.LosesTheAnswerAfterWriting("DELETE", Removal(1001));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);
        Assert.Equal("1001", (string?)lost.ExternalIds!["contact"]);
        Assert.Equal("7", (string?)lost.ExternalIds["campaign"]);

        var mark = account.Mark();
        var repeated = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));
        Assert.Equal("already_stopped", (string?)repeated["items"]![0]!["status"]);
        Assert.Equal([$"GET {Participation(1001)}"], account.CallsSince(mark).Select(call => call.Method + " " + call.Path));
    }

    // -------------------------------------------------------------------------------------------------------

    private static ReplyAccount Planted() =>
        new ReplyAccount().WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11, 12);

    /// <summary>
    /// The stop's input: the person pinned or not, the campaign by its pin (<paramref name="campaign"/>) or named for the
    /// first time in the arguments (<paramref name="supplied"/>, with no pin).
    /// </summary>
    private static JsonObject Input(string? pinned = null, string? campaign = "7", string? supplied = null)
    {
        var input = Arguments(pinned, campaign);
        if (supplied is not null)
        {
            input["args"]!["campaign"] = new JsonObject { ["external_id"] = supplied };
        }

        return input;
    }

    private static JsonObject Arguments(string? pinned, string? campaign) => new()
    {
        ["args"] = new JsonObject
        {
            ["channel"] = "email",
            ["reason"] = new JsonObject
            {
                ["code"] = "not_interested",
                ["note"] = Note,
                ["caused_by_contact_id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
            },
        },
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = ContactId,
            ["first_name"] = "Marta",
            ["last_name"] = "Alvarez",
            ["company"] = null,
            ["title"] = null,
            ["time_zone"] = null,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address, ["primary"] = true }),
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
        }),
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = campaign is null ? new JsonObject() : new JsonObject { ["campaign"] = campaign },
        },
        ["idempotency_key"] = WorkItemId,
    };

    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Method + " " + call.Path)];

    /// <summary>Every path, argument and body the plugin sent, as one string to search.</summary>
    private static string Everything(ReplyAccount account) =>
        string.Join("\n", account.Calls.Select(call => call.Path + " " + string.Join(" ", call.Args) + " " + call.Body));

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
