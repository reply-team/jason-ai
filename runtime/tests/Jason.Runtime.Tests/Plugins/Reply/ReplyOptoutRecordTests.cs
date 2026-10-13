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
        Assert.Equal(["contact opt-out"], Paths(account));
        Assert.Equal("1001", account.Calls[0].Option("--contact-id"));
        Assert.Null(account.Calls[0].Body);
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

        // The whole command ran — the CLI's lookup, the create and the write — and none of the four facts reached
        // any of it: the provider holds a yes or a no.
        Assert.Equal("recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal(["contact opt-out"], Paths(account));
        var everything = Everything(account);
        foreach (var fact in new[] { Statement, ArrivedVia, SaidAt, LearnedAt, "take me off" })
        {
            Assert.DoesNotContain(fact, everything, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Setting a flag that is set already changes nothing at Reply, so the answer is `recorded` either way.</summary>
    [Fact]
    public async Task A_person_already_opted_out_answers_recorded()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal([1001], OptedOut(account));
    }

    [Fact]
    public async Task The_address_travels_on_stdin_and_nowhere_else()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input()));

        var call = Assert.Single(account.Calls);
        Assert.Equal("-", call.Option("--contact"));
        Assert.Contains(Address, call.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(call.Args, argument => argument.Contains(Address, StringComparison.Ordinal));
    }

    /// <summary>The write has happened and its answer names nobody this package can work by: a lost write, reported by that value alone.</summary>
    [Fact]
    public async Task A_result_naming_a_contact_nobody_can_work_by_is_a_lost_write()
    {
        using var account = new ReplyAccount();
        account.Returns("contact opt-out", new JsonObject { ["status"] = "opted_out", ["contact_id"] = "abc", ["contact_created"] = false });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Contains("abc", (string?)error.Details!["provider_item"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_opt_out_whose_answer_was_lost_is_run_again_and_answers_recorded()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.LosesTheOutcome("contact opt-out");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal("1001", (string?)lost.ExternalIds!["contact"]);

        var repeat = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));
        Assert.Equal("recorded", (string?)repeat["items"]![0]!["status"]);
        Assert.Equal([1001], OptedOut(account));
    }

    /// <summary>Reply creates nobody without a first name, so an opt-out for an address it does not hold is its refusal, with its words.</summary>
    [Fact]
    public async Task A_person_reply_does_not_hold_and_cannot_create_without_a_first_name_is_reply_s_refusal()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(firstName: null)));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal("Invalid data", (string?)error.Details!["provider_detail"]);
        Assert.Empty(OptedOut(account));
    }

    [Fact]
    public async Task The_flag_is_only_ever_set_and_never_cleared()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        // The command sets the flag and has no way to clear it: its whole argument vector names the person and nothing
        // else.
        Assert.Equal(["--json", "-q", "contact", "opt-out", "--contact-id", "1001"], Assert.Single(account.Calls).Args);
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
        Assert.Equal(["contact opt-out"], Paths(account));
        Assert.Empty(account.OptOuts);
    }

    /// <summary>
    /// The CLI's lookup reads at most five pages of a hundred; past that one match proves nothing about a second, and
    /// the opt-out is refused — never a pin, never a create. The message, which is all that reaches the work item's
    /// last error, says what a person does instead.
    /// </summary>
    [Fact]
    public async Task A_lookup_that_cannot_be_read_to_its_end_is_refused_and_never_taken_for_nobody_or_for_one()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        account.Refuses("contact opt-out", "contact.lookup_incomplete", status: 200);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input()));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.ExternalIds);
        Assert.Contains("record the opt-out at Reply by hand", error.Message, StringComparison.Ordinal);
        Assert.Contains("Jason's own register already stops Jason's own reach", error.Message, StringComparison.Ordinal);
        Assert.Equal(["contact opt-out"], Paths(account));
        Assert.Single(account.Contacts);
        Assert.Empty(account.OptOuts);
    }

    // -------------------------------------------------------------------------------------------------------
    // The lookup, and what a search that matches by containment means for it
    // -------------------------------------------------------------------------------------------------------

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
        Assert.Equal(["contact opt-out"], Paths(account));
        Assert.Empty(account.OptOuts);
    }

    /// <summary>A contact the CLI says Reply does not hold is not found, and the pin it was given travels out.</summary>
    [Fact]
    public async Task A_contact_the_write_says_it_does_not_hold_is_not_found()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, "Marta");
        account.Refuses("contact opt-out", "contact.not_found", status: 200, replyCode: "notFound");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Equal("1001", (string?)error.ExternalIds!["contact"]);
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
        [.. account.Calls.Select(call => call.Line)];

    private static IReadOnlyList<int> OptedOut(ReplyAccount account) =>
        [.. account.OptOuts.Select(entry => entry!.GetValue<int>()).Order()];

    /// <summary>Every path, argument and body the plugin sent, as one string to search.</summary>
    private static string Everything(ReplyAccount account) =>
        string.Join("\n", account.Calls.Select(call => string.Join(" ", call.Args) + " " + call.Body));

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
