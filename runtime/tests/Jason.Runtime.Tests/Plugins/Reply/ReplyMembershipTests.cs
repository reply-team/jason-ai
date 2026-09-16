using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>list_membership.add</c> as the official package performs it: the real plugin JavaScript, loaded by a real
/// plugin-host child, driving the stand-in vendor CLI against an account a test planted. One test per entry of
/// the document's own <c>conformance</c> list, and then the decisions this provider forces — Reply's import
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
    // The document's conformance list, one test each
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
        Assert.Equal([$"GET /v3/contacts/{Ensured}/statuses", $"POST /v3/contact-lists/{List}/add-contacts"], Paths(account));
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
    }

    [Fact]
    public async Task A_second_call_under_the_same_key_answers_already_member_and_writes_nothing()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));
        var mark = account.Mark();

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        // From a mark taken after the first attempt, or the assertion would run over that attempt's own write.
        // Two reads and no write: the membership the provider already holds is the answer, and answering it is
        // what makes one crash cost one effect rather than two.
        Assert.Equal("already_member", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(
            [$"GET /v3/contacts/{Ensured}/statuses", $"GET /v3/contacts/{Ensured}/lists"],
            PathsSince(account, mark));
    }

    [Fact]
    public async Task An_attempt_after_a_lost_answer_reads_the_membership_before_it_writes()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders")
            .LosesTheAnswerAfterWriting("POST", $"/v3/contact-lists/{List}/add-contacts");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = await InvokeAsync(api, Input(), Ct);

        // The write landed and the answer did not. Nothing about the attempt can say which, so it ends
        // ambiguous — and it carries out the identifier of the contact it ensured, because after a lost answer
        // that pin is the only trace that anything happened at all.
        var failed = Assert.IsType<InvocationOutcome.Failed>(lost.Outcome);
        Assert.Equal("provider_answer_lost", failed.Error.Code);
        Assert.Equal(FailureClass.Ambiguous, failed.Error.Class);
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal([1001], account.MembersOf(List));

        var mark = account.Mark();
        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        Assert.Equal("already_member", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(
            [$"GET /v3/contacts/{Ensured}/statuses", $"GET /v3/contacts/{Ensured}/lists"],
            PathsSince(account, mark));
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
        Assert.Equal([$"GET /v3/contacts/{Ensured}/statuses"], Paths(account));
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

        // Reply's import deduplicates by email and answers the identifier either way, so one call both matches
        // the person this account already holds and would have created them — which is why no address ever has
        // to travel in a query string to find somebody.
        Assert.Contains("POST /v3/contacts/import", Paths(account));
        Assert.DoesNotContain("POST /v3/contacts", Paths(account));
        Assert.Equal(Ensured, answer["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>());
        Assert.Single(account.Contacts);
    }

    [Fact]
    public async Task A_person_with_no_first_name_is_created_rather_than_imported()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(firstName: null), Ct));

        // Reply's import refuses an item that carries no first name, and Jason's own projection allows none.
        // Inventing one would write data nobody supplied into a customer's own records, so the person is
        // created instead — that path needs only an address.
        Assert.Contains("POST /v3/contacts", Paths(account));
        Assert.DoesNotContain("POST /v3/contacts/import", Paths(account));
        Assert.Equal(Ensured, answer["items"]![0]!["external_ids"]!["contact"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_address_this_account_already_holds_is_reported_on_the_create_path_rather_than_guessed_at()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address).WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(firstName: null), Ct);

        // Reply publishes no code for a duplicate address on this path, so the stand-in answers one it invented
        // and the plugin does not read it: a refusal is reported as the refusal it is, with Reply's own word in
        // the details for whoever has to act on it. That is this version's documented limitation, and mapping a
        // code nobody publishes would be a mapping of a fiction.
        Assert.Contains("POST /v3/contacts", Paths(account));
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_call_failed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("contact.duplicate", failed.Error.Details!["provider_code"]!.GetValue<string>());
        Assert.Empty(account.MembersOf(List));
    }

    [Fact]
    public async Task The_import_carries_only_what_the_projection_gives_and_reply_publishes_a_word_for()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(), Ct));

        // The person's own time zone is projected and deliberately not sent: Reply's `timeZoneId` accepts a
        // vocabulary published nowhere, and an unrecognised value fails the whole import item — so sending it
        // would cost the import rather than improve it.
        var imported = Assert.Single(account.Calls, call => call.Path == "/v3/contacts/import");
        var item = JsonNode.Parse(imported.Body!)!["items"]![0]!.AsObject();
        Assert.Equal(["company", "email", "firstName", "lastName", "title"], item.Select(member => member.Key).Order());
        Assert.DoesNotContain(TimeZone, imported.Body!, StringComparison.Ordinal);
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
        Assert.Equal(["GET /v3/contacts/4242/statuses"], Paths(account));
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
        Assert.Equal("contactList.notFound", failed.Error.Details!["provider_code"]!.GetValue<string>());

        // The contact was ensured before the list refused it, so the pin travels out: the next attempt has
        // something to read the membership by even though this one wrote nothing.
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
        account.Answers("POST", "/v3/contacts/import", 400, Refusal("contactLimitExceeded").ToJsonString());
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
    public async Task An_address_the_provider_will_not_accept_is_a_validation_failure_rather_than_a_refusal()
    {
        using var account = new ReplyAccount();
        account.WithList(List, "Q3 LatAm founders");
        account.Answers("POST", "/v3/contacts/import", 400, Invalid("/items/0/email").ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(), Ct);

        // Reply's 400 is two shapes and which member is present says which: a validation problem carries
        // `errors[]` and no `code`, a business rejection the other way round. A pointer naming the address is
        // the provider saying the channel value itself is wrong, which no later attempt would accept either.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("invalid_channel_value", failed.Error.Code);
        Assert.Equal(FailureClass.Validation, failed.Error.Class);
    }

    [Theory]
    [InlineData("\"contactNotProcessed\"")]
    [InlineData("8")]
    public async Task A_person_the_list_would_not_take_is_read_from_the_key_and_never_from_the_value(string value)
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        account.Answers("POST", $"/v3/contact-lists/{List}/add-contacts", 200, $$"""{"{{Ensured}}": {{value}}}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The answer is a failures-only dictionary: an identifier that is absent succeeded. What the value is,
        // is documented twice and incompatibly — a prose table says `8 | ContactNotProcessed`, the schema says a
        // camelCase string — so the key's presence is the failure and the value is only ever reported. Both
        // spellings therefore end the same way, which is the whole claim.
        Assert.Contains($"POST /v3/contact-lists/{List}/add-contacts", Paths(account));
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_call_failed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);

        var seen = failed.Error.Details?["provider_item"]?.GetValue<string>();
        Assert.NotNull(seen);
        Assert.Contains(value.Trim('"'), seen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_on_the_providers_own_side_during_the_add_is_ambiguous_because_the_call_is_a_write()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName).WithList(List, "Q3 LatAm founders");
        account.Answers("POST", $"/v3/contact-lists/{List}/add-contacts", 503, Refusal("server.unavailable", 503).ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The read/write mark in the package's own table is what decides this. The same ending on a read is
        // transient and simply asked again; here the request may have landed before Reply's own side failed, so
        // the work item stops for a person rather than repeating a write nobody can say did not happen.
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

        var imported = Assert.Single(account.Calls, call => call.Path == "/v3/contacts/import");
        Assert.Contains(Address, imported.Body!, StringComparison.Ordinal);
    }

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

    /// <summary>A business rejection of the shape Reply answers one in: a code, and no <c>errors[]</c>.</summary>
    private static JsonObject Refusal(string code, int status = 400) => new()
    {
        ["type"] = "about:blank",
        ["title"] = "Bad Request",
        ["status"] = status,
        ["detail"] = "The stand-in was told to answer this.",
        ["code"] = code,
    };

    /// <summary>A validation problem of the shape Reply answers one in: <c>errors[]</c>, and no code.</summary>
    private static JsonObject Invalid(string pointer) => new()
    {
        ["type"] = "about:blank",
        ["title"] = "Bad Request",
        ["status"] = 400,
        ["errors"] = new JsonArray(new JsonObject
        {
            ["pointer"] = pointer,
            ["detail"] = "The stand-in was told to answer this.",
        }),
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
        [.. account.Calls.Select(call => call.Method + " " + call.Path)];

    private static IReadOnlyList<string> PathsSince(ReplyAccount account, int mark) =>
        [.. account.CallsSince(mark).Select(call => call.Method + " " + call.Path)];

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
