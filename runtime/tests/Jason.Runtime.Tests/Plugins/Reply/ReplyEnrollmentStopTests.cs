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
    private const string Note = "replied that the timing is wrong";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
        Assert.Equal("1001", account.Calls[0].Option("--contact-id"));
        Assert.Null(account.Calls[0].Body);
        Assert.DoesNotContain(Address, Everything(account), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// A person Reply has never held takes part in nothing, which is a completed stop — and nobody is made to find
    /// that out. The one command is the only call.
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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    /// <summary>
    /// The CLI's lookup could not be read to its end. A stop that took that for nobody would answer `already_stopped`
    /// over a person still being written to; it is refused instead, and nothing is removed. The message, which is all
    /// that reaches the work item's last error, says what a person does instead.
    /// </summary>
    [Fact]
    public async Task A_lookup_that_cannot_be_read_to_its_end_is_refused_and_never_answers_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        account.Refuses("sequence remove-contact", "contact.lookup_incomplete", status: 200);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("take the person out at Reply by hand, then check with enrollment.list", error.Message, StringComparison.Ordinal);
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
    }

    /// <summary>
    /// The CLI answers a contact Reply no longer holds and a contact that takes no part the same way, so a well-formed
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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
    }

    [Fact]
    public async Task The_person_is_taken_out_and_never_marked_as_having_finished()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        // The one write takes the participation out; nothing sets a state on it, so no count of the people who
        // completed the campaign ever includes this person.
        var call = Assert.Single(account.Calls);
        Assert.Equal("sequence remove-contact 7", call.Line);
        Assert.Null(call.Body);
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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
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
        var call = Assert.Single(account.Calls);
        Assert.Equal("-", call.Option("--contact"));
        Assert.Equal(Address, (string?)JsonNode.Parse(call.Body!)!["email"]);
        Assert.DoesNotContain(Address, string.Join(" ", call.Args), StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(["sequence remove-contact 7"], Paths(account));
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
    /// A pinned person's part in the sequence is read by the CLI, which tells a sequence Reply does not hold apart
    /// itself, so a campaign named for the first time needs no read of its own: it is not found, nothing is removed,
    /// and no pin is recorded for it.
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
        Assert.Equal(["sequence remove-contact 8"], Paths(account));
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
        Assert.Equal(["sequence get 8"], Paths(account));
    }

    /// <summary>
    /// A person named by address may be somebody Reply does not hold, which the CLI answers without checking the
    /// sequence, so a campaign named the first time is read once first, then worked and pinned.
    /// </summary>
    [Fact]
    public async Task A_campaign_named_the_first_time_is_read_once_then_worked_and_pinned()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(campaign: null, supplied: "7"));

        Assert.Equal("stopped", (string?)ReplyOperations.Succeeded(Operation, result)["items"]![0]!["status"]);
        Assert.Equal("""{"contact":"1001","campaign":"7"}""", Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds!.ToJsonString());
        Assert.Equal(["sequence get 7", "sequence remove-contact 7"], Paths(account));
    }

    /// <summary>
    /// reply-cli checks the sequence when a person it found takes no part, so a pinned campaign Reply no longer holds is
    /// not found rather than read as a completed stop.
    /// </summary>
    [Fact]
    public async Task A_pinned_campaign_reply_no_longer_holds_is_not_found_once_the_person_is_found()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(campaign: "8")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.False(error.ExternalIds?.ContainsKey("campaign") ?? false);
    }

    [Fact]
    public async Task A_refusal_this_version_has_no_word_for_is_reported_and_never_read_as_already_stopped()
    {
        using var account = Planted();
        account.Refuses("sequence remove-contact", "reply.refused", status: 404, replyCode: "sequenceContact.somethingNew");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal("sequenceContact.somethingNew", (string?)error.Details!["provider_code"]);
    }

    [Fact]
    public async Task A_removal_whose_answer_was_lost_is_run_again_and_answers_already_stopped()
    {
        using var account = Planted();
        account.WithContact(1001, Address, "Marta").WithEnrollment(Sequence, 1001);
        account.LosesTheOutcome("sequence remove-contact");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);

        var repeat = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));
        Assert.Equal("already_stopped", (string?)repeat["items"]![0]!["status"]);
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
        [.. account.Calls.Select(call => call.Line)];

    /// <summary>Every path, argument and body the plugin sent, as one string to search.</summary>
    private static string Everything(ReplyAccount account) =>
        string.Join("\n", account.Calls.Select(call => string.Join(" ", call.Args) + " " + call.Body));

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
