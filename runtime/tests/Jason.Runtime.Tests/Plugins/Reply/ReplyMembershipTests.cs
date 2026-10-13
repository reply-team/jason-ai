using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>list_membership.add</c> as the official package performs it: the real plugin JavaScript, loaded by a real
/// plugin-host child, driving the stand-in vendor CLI against an account a test planted. Every entry of the
/// document's own <c>conformance</c> list has a test, some of them more than one, and then come the decisions
/// this provider forces — Reply's import
/// refuses an item that carries no first name, its create path publishes no word for a duplicate address, and
/// its list add answers per-item failure in a shape its own documentation describes two incompatible ways.
/// </summary>
/// <remarks>
/// The class writes a process-wide variable — the one the CLI finds its account through — so it belongs to the
/// collection that never runs two such classes at once. Every fixture starts through
/// <see cref="ReplyPlugins.StartAsync"/>, which holds the resolved program to the test tree before anything
/// runs: a resolution that escaped would not be a wrong answer, it would be a call to somebody's real account.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyMembershipTests
{
    private const string Operation = "list_membership.add";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRF";

    /// <summary>The list every test plants, by an identifier of the shape Reply actually issues.</summary>
    private const int List = 9;

    /// <summary>The identifier Reply gives the first contact an account creates.</summary>
    private const string Ensured = "1001";

    private const string Address = "marta@example.com";
    private const string FirstName = "Marta";
    private const string LastName = "Alvarez";
    private const string Company = "Puerto Analytics";
    private const string Title = "Head of Growth";
    private const string TimeZone = "America/Bogota";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list, every entry of it covered
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_contact_is_worked_by_the_pin_and_never_matched_by_the_address_again()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // The pin is the whole of it: nothing is imported, nothing is created, and the address this person
        // carries reaches Reply nowhere at all — which is what stops two spellings of one address ever becoming
        // two people.
        Assert.Equal("added", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(["contact-list add-contact 9"], Paths(account));
        Assert.Equal(Ensured, account.Calls[0].Option("--contact-id"));
        Assert.Contains("--refuse-opted-out", account.Calls[0].Args);
        Assert.Null(account.Calls[0].Body);
        Assert.DoesNotContain(Address, Everything(account), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([1001], account.MembersOf(List));
    }

    [Fact]
    public async Task A_contact_with_no_pin_is_created_from_the_channel_value_and_its_identifier_comes_back()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(), Ct);

        // The identifier comes back in both places the contract names, and they are not copies of each other:
        // the one beside the result is what the runtime pins, the one inside the item is the record of which
        // person this identifier belongs to.
        var answer = Succeeded(result);
        Assert.Equal(Ensured, answer["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>());
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal(Ensured, outcome.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal([1001], account.MembersOf(List));

        // One command, and the person on stdin for the CLI to import.
        var call = Assert.Single(account.Calls);
        Assert.Equal("-", call.Option("--contact"));
        Assert.Contains(Address, call.Body!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reply keeps one membership however often a person is added, and does not say whether they were there already,
    /// so a second call answers `added` again: the effect at Reply is the same.
    /// </summary>
    [Fact]
    public async Task A_second_call_answers_added_and_leaves_one_membership()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var first = Succeeded(await InvokeAsync(api, Input(), Ct));
        var second = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        Assert.Equal("added", (string?)first["items"]![0]!["status"]);
        Assert.Equal("added", (string?)second["items"]![0]!["status"]);
        Assert.Equal([1001], account.MembersOf(List));
    }

    [Fact]
    public async Task An_attempt_after_a_lost_answer_runs_the_add_again_and_leaves_one_membership()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.LosesTheOutcome("contact-list add-contact");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(), Ct));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);
        Assert.Equal(Ensured, (string?)lost.ExternalIds!["contact"]);

        var repeat = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));
        Assert.Equal("added", (string?)repeat["items"]![0]!["status"]);
        Assert.Equal([1001], account.MembersOf(List));
    }

    /// <summary>
    /// The add refuses a person Reply holds as opted out, so a person who opts out after a first attempt's add landed is
    /// reported suppressed by the second. That is the one known edge of a single command, and one membership stays.
    /// </summary>
    [Fact]
    public async Task A_person_who_opted_out_between_two_attempts_is_reported_suppressed()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.LosesTheOutcome("contact-list add-contact");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Assert.Equal("provider_answer_lost", ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(), Ct)).Code);
        account.WithContact(1001, Address, FirstName, optedOut: true);

        var repeat = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));
        Assert.Equal("suppressed", repeat.Code);
        Assert.Equal([1001], account.MembersOf(List));
    }

    [Fact]
    public async Task A_person_without_a_first_name_whom_reply_holds_is_found_by_address_and_added_by_id()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, Address.ToUpperInvariant(), FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(firstName: null), Ct));

        Assert.Equal("2001", (string?)answer["items"]![0]!["external_ids"]!["contact"]);
        Assert.Equal(["POST /v3/contacts/filter?top=100&skip=0", "contact-list add-contact 9"], account.Calls.Select(call => call.Line));
        Assert.Equal("2001", account.Calls[1].Option("--contact-id"));
        Assert.Null(account.Calls[1].Body);
        Assert.Equal([2001], account.MembersOf(List));
    }

    [Fact]
    public async Task A_person_without_a_first_name_whom_reply_does_not_hold_is_refused_before_any_add()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(firstName: null), Ct));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("first name", error.Message, StringComparison.Ordinal);
        Assert.Equal(["POST /v3/contacts/filter?top=100&skip=0"], account.Calls.Select(call => call.Line));
        Assert.Empty(account.Contacts);
    }

    /// <summary>Review focus 1: Reply's search matches by containment; only an exact address is the person.</summary>
    [Fact]
    public async Task A_person_without_a_first_name_is_never_added_as_somebody_whose_address_only_contains_theirs()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, "jo" + Address, "Jo");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(firstName: null), Ct));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Contains("first name", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Calls, call => call.Command == "contact-list add-contact");
        Assert.Empty(account.MembersOf(List));
    }

    /// <summary>Two contacts under the address: picking one would be guessing whom the work is about, so nobody is added.</summary>
    [Fact]
    public async Task A_person_without_a_first_name_held_twice_under_the_address_is_refused_and_nobody_is_added()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, Address, FirstName).WithDuplicate(2002, of: 2001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(firstName: null), Ct));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("more than one contact under this address", error.Message, StringComparison.Ordinal);
        Assert.Equal(["POST /v3/contacts/filter?top=100&skip=0"], account.Calls.Select(call => call.Line));
        Assert.Empty(account.MembersOf(List));
    }

    /// <summary>
    /// The search matches by containment and the lookup reads five pages of a hundred. Five hundred addresses that
    /// hold this one fill them, so the end is never reached and one match could not be told from two: nobody is added.
    /// </summary>
    [Fact]
    public async Task A_lookup_that_cannot_be_read_to_its_end_adds_nobody()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, Address, FirstName);
        for (var at = 0; at < 500; at++)
        {
            account.WithContact(3000 + at, $"x{at}.{Address}", "Near" + at);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(firstName: null), Ct));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(page => $"POST /v3/contacts/filter?top=100&skip={page * 100}")],
            account.Calls.Select(call => call.Line));
        Assert.Empty(account.MembersOf(List));
    }

    /// <summary>
    /// The CLI says `outcome.unknown` for any POST under `reply api` whose answer was lost, this search included. The
    /// search changes nothing, so the package's own mark for it decides: asked again, never stopped for a person.
    /// </summary>
    [Fact]
    public async Task A_lookup_whose_outcome_the_cli_could_not_see_is_asked_again()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, Address, FirstName);
        account.Prints(
            "POST",
            "/v3/contacts/filter?top=100&skip=0",
            3,
            string.Empty,
            """{"error":{"code":"outcome.unknown","title":"The request may have reached Reply, but no answer came back."}}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(firstName: null), Ct));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.Empty(account.MembersOf(List));
    }

    [Fact]
    public async Task The_person_on_stdin_carries_only_what_the_projection_gives_and_the_cli_takes()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(), Ct));

        Assert.Equal(
            """{"email":"marta@example.com","first_name":"Marta","last_name":"Alvarez","company":"Puerto Analytics","title":"Head of Growth"}""",
            Assert.Single(account.Calls).Body);
    }

    [Theory]
    [InlineData("contact.invalid_email", null)]
    [InlineData("reply.invalid_request", "email")]
    public async Task An_address_the_provider_will_not_accept_is_a_validation_failure_rather_than_a_refusal(string code, string? field)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Refuses(
            "contact-list add-contact", code, status: 400,
            details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(), Ct));

        Assert.Equal("invalid_channel_value", error.Code);
        Assert.Equal(FailureClass.Validation, error.Class);
    }

    [Fact]
    public async Task A_field_reply_will_not_accept_that_is_not_the_address_is_a_refusal_this_version_has_no_word_for()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Refuses("contact-list add-contact", "reply.invalid_request", status: 400, details: new JsonObject { ["fields"] = new JsonArray("firstName") });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(), Ct));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public async Task A_person_the_list_would_not_take_is_a_refusal_carrying_reply_s_own_word()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Refuses(
            "contact-list add-contact", "reply.refused", status: 200, replyCode: "contactNotProcessed",
            detail: "Contact was not added to the list", ids: new JsonObject { ["list_id"] = List, ["contact_id"] = 1001 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(), Ct));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal("contactNotProcessed", (string?)error.Details!["provider_code"]);
        Assert.Equal("Contact was not added to the list", (string?)error.Details!["provider_detail"]);
        Assert.Equal(Ensured, (string?)error.ExternalIds!["contact"]);
    }

    [Fact]
    public async Task An_address_the_provider_suppresses_fails_permanently_rather_than_being_added()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName, optedOut: true).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The provider knows something the runtime cannot: a person suppressed between the claim and the act is
        // the ordinary case. The plugin recognises the refusal and reports it, and the list is left alone.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("suppressed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal(["contact-list add-contact 9"], Paths(account));
        Assert.Empty(account.MembersOf(List));
    }

    [Fact]
    public async Task The_result_carries_exactly_one_item_naming_the_contact_the_call_was_given()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // The identifier the call was given, so the caller can match the answer to the person — never Reply's
        // own, which travels beside it under `external_ids`. And nothing else: this operation's output schema
        // is closed, so unlike `campaign.get` there is no `vendor` bag for anything of Reply's to ride along in.
        var item = Assert.Single(answer["items"]!.AsArray())!.AsObject();
        Assert.Equal(["items"], answer.Select(member => member.Key));
        Assert.Equal(ContactId, item["contact_id"]!.GetValue<string>());
        Assert.NotEqual(ContactId, item["external_ids"]!["contact"]!.GetValue<string>());
    }

    // -------------------------------------------------------------------------------------------------------
    // The two ways a provider's contact is ensured, and why there are two
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_person_with_a_first_name_is_matched_or_created_by_email_in_one_call()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(), Ct));

        // The CLI imports the person from stdin, and its import deduplicates by email: one command both matches the
        // person this account already holds and would have created them.
        Assert.Equal(["contact-list add-contact 9"], Paths(account));
        Assert.Equal("-", account.Calls[0].Option("--contact"));
        Assert.Equal(Ensured, answer["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>());
        Assert.Single(account.Contacts);
    }

    // -------------------------------------------------------------------------------------------------------
    // The failure rows this operation declares, and only those
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pin_that_no_longer_resolves_is_reported_rather_than_the_person_being_matched_again()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "4242"), Ct);

        // Working by the pin is the rule, so a pin the provider no longer resolves is the end of this call and
        // not the beginning of a search: re-matching by address is exactly what the pin exists to stop.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("contact_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("contact.notFound", failed.Error.Details!["provider_code"]!.GetValue<string>());
        Assert.Equal(["contact-list add-contact 9"], Paths(account));
    }

    [Fact]
    public async Task A_list_this_account_does_not_hold_fails_permanently()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("list_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("contact-list.not_found", failed.Error.Details!["cli_code"]!.GetValue<string>());

        // The contact was ensured before the list refused it, so the pin travels out: the next attempt works by it.
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_identifier_that_is_not_a_reply_list_number_is_refused_without_a_call_being_made()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured, list: "L-1129"), Ct);

        // Reply numbers its contact lists. A value that is not one names no list this account could hold under
        // any circumstance, so there is nothing to ask the provider about — and asking anyway would spend a
        // write's worth of trust on a question already answered.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("list_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_pin_that_is_not_a_reply_contact_number_is_refused_without_a_call_being_made()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "p_88421"), Ct);

        // A pin is worked by, never interpreted: one that is not a Reply contact identifier resolves to nobody,
        // and answering that is better than falling back to the address the pin was recorded to replace.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("contact_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task An_account_that_will_hold_no_further_contact_says_so_in_the_word_the_contract_has_for_it()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        account.Refuses("contact-list add-contact", "account.contact_limit_reached", status: 400, replyCode: "contact.limitExceeded");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(), Ct);

        // The account's own contact cap is the one capacity Reply publishes a code for. The list's own is not,
        // so a refusal there arrives as an ordinary business 400 and is reported as a refusal this version has
        // no word for — named in the package's table rather than guessed into this row.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("limit_reached", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
    }

    [Fact]
    public async Task A_failure_on_the_providers_own_side_during_the_add_is_ambiguous_because_the_call_is_a_write()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        account.LosesTheOutcome("contact-list add-contact");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The CLI could not say whether the add happened. On a write that is ambiguous, and the contact the CLI had
        // already learned travels out with the failure.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_answer_lost", failed.Error.Code);
        Assert.Equal(FailureClass.Ambiguous, failed.Error.Class);
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
    }

    // -------------------------------------------------------------------------------------------------------
    // What may never appear on a command line
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task No_call_this_operation_makes_carries_a_persons_own_data_in_an_argument()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(), Ct));

        // The `exec` diagnostic records every argument and the runtime's logs may never hold contact data, so a
        // request body travels on stdin and nowhere else. The second assertion is what makes the first one mean
        // something: the address really did reach Reply, and it reached it the only way it may.
        foreach (var call in account.Calls)
        {
            var line = string.Join(' ', call.Args);
            foreach (var personal in new[] { Address, FirstName, LastName, Company, Title, TimeZone })
            {
                Assert.DoesNotContain(personal, line, StringComparison.OrdinalIgnoreCase);
            }
        }

        var added = Assert.Single(account.Calls);
        Assert.Contains(Address, added.Body!, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------------
    // The recovery read, and the endpoint it is not made with
    // -------------------------------------------------------------------------------------------------------

    // -------------------------------------------------------------------------------------------------------
    // Composing the input, and checking both halves against the published document
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The canonical input: one person, one list, one key, and the one channel this operation consumes. The pin
    /// is present once the runtime has recorded one, which is the difference between the two ensure paths.
    /// </summary>
    private static JsonObject Input(string? pinned = null, string? firstName = FirstName, string? list = null) => new()
    {
        ["args"] = new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = list ?? List.ToString(CultureInfo.InvariantCulture) },
            ["channel"] = "email",
        },
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = ContactId,
            ["first_name"] = firstName,
            ["last_name"] = LastName,
            ["company"] = Company,
            ["title"] = Title,
            ["time_zone"] = TimeZone,
            ["channels"] = new JsonArray(new JsonObject
            {
                ["channel"] = "email",
                ["value"] = Address,
                ["primary"] = true,
            }),
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
        }),
        ["campaign"] = new JsonObject
        {
            ["id"] = CampaignId,
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject(),
        },
        ["idempotency_key"] = WorkItemId,
    };

    /// <summary>The result the plugin answered with, having first been held to the operation's own document.</summary>
    private static JsonObject Succeeded(PluginInvocationResult result)
    {
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        var contract = OperationCatalog.Find(Operation)!;

        Assert.Empty(OutcomeContract.CheckResult(contract, outcome.Result));
        Assert.Empty(OutcomeContract.CheckExternalIds(contract, outcome.ExternalIds));
        return outcome.Result!.AsObject();
    }

    /// <summary>Every call the account was asked to make, as "METHOD /path", in the order it was asked.</summary>
    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Line)];

    /// <summary>Everything that left this runtime: every argument of every call, and every body.</summary>
    private static string Everything(ReplyAccount account) =>
        string.Join('\n', account.Calls.Select(call => string.Join(' ', call.Args) + " " + call.Body));

    private static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        JsonObject input,
        CancellationToken kill,
        int attempt = 1)
    {
        // The input is what the document says it is before anything runs: a test that sent nonsense would prove
        // nothing about whether the operation is implementable.
        Assert.Empty(SchemaValidator.Validate(input, OperationCatalog.Find(Operation)!.InputSchema));

        return await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(
                ReplyPlugins.PluginId,
                Operation,
                input,
                Binding: null,
                CorrelationId: "att_01K0REPLYMEMBERSHIPADD",
                AttemptId: "att_01K0REPLYMEMBERSHIPADD",
                AttemptNumber: attempt,
                WorkItemId: WorkItemId,
                CampaignId: CampaignId),
            kill);
    }
}
