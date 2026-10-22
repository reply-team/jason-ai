using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>contact.update</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. One test per line of the document's conformance list that
/// the person found covers, then the refusals the package decides before anything is asked and the answers it does not
/// take for one.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyContactUpdateTests
{
    private const string Operation = "contact.update";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRV";
    private const string Address = "marta@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list, for a person Reply holds
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_contact_is_changed_by_the_pin_and_never_looked_up_by_the_address()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", title: "Head of Growth");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(Fields(("title", "Chief Revenue Officer")), pinned: "1001")));

        Assert.Equal(
            $$$"""{"items":[{"contact_id":"{{{ContactId}}}","status":"updated","external_ids":{"contact":"1001"}}]}""",
            answer.ToJsonString());
        var call = Assert.Single(account.Calls);
        Assert.Equal(["--json", "-q", "contact", "update", "1001", "--fields", "-"], call.Args);
        Assert.Equal("""{"title":"Chief Revenue Officer"}""", call.Body);
        Assert.Equal("Chief Revenue Officer", (string?)account.ContactOf(1001)!["title"]);
    }

    /// <summary>Reply holds the person: the exact lookup finds them, they are changed by that identifier, nobody is imported, and the identifier is pinned.</summary>
    [Fact]
    public async Task A_person_reply_holds_is_found_by_the_address_changed_by_that_identifier_and_pinned()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(Fields(("company", "Puerto Analytics Group"))));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("updated", (string?)answer["items"]![0]!["status"]);
        Assert.Equal("1001", (string?)((InvocationOutcome.Succeeded)result.Outcome).ExternalIds!["contact"]);
        Assert.Equal(["contact find", "contact update 1001"], Lines(account));
        Assert.Equal($$"""{"email":"{{Address}}"}""", account.Calls[0].Body);
        Assert.Single(account.Contacts);
        Assert.Equal("Puerto Analytics Group", (string?)account.ContactOf(1001)!["company"]);
    }

    /// <summary>
    /// A person Reply holds nobody for under the address is created by the update's own command: the person's fields
    /// name and import them, the arguments' fields are then set on the contact made — the one pinned — and the person
    /// and the fields travel as one object on stdin, with no identifier on the command line.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_nobody_for_is_created_by_the_update_and_given_its_fields()
    {
        using var account = new ReplyAccount().WithContact(1001, "ana@example.com", "Ana");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(Fields(("title", "CTO"))));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("1002", (string?)((InvocationOutcome.Succeeded)result.Outcome).ExternalIds!["contact"]);
        Assert.Equal(["contact find", "contact update"], Lines(account));
        var update = account.Calls[^1];
        Assert.Equal(["--json", "-q", "contact", "update", "--contact", "-", "--fields", "-"], update.Args);
        Assert.Equal(
            $$$"""{"contact":{"email":"{{{Address}}}","first_name":"Marta","last_name":"Alvarez","company":"Puerto Analytics","title":"Head of Growth"},"fields":{"title":"CTO"}}""",
            JsonNode.Parse(update.Body!)!.ToJsonString());
        var made = account.ContactOf(1002)!;
        Assert.Equal((Address, "Marta", "CTO"), ((string?)made["email"], (string?)made["firstName"], (string?)made["title"]));
        Assert.Equal("Ana", (string?)account.ContactOf(1001)!["firstName"]);
    }

    /// <summary>
    /// The command reads the account's custom field names before it imports anybody, so a name the account defines none of
    /// creates nobody and changes nothing.
    /// </summary>
    [Fact]
    public async Task A_custom_field_the_account_defines_none_of_creates_nobody()
    {
        using var account = new ReplyAccount().WithCustomField(1, "leadSource");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var custom = new JsonArray(new JsonObject { ["name"] = "favouriteColour", ["value"] = "teal" });
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(null, custom: custom)));

        Assert.Equal(("field_not_found", FailureClass.Validation), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Contacts);
    }

    /// <summary>
    /// A person with no first name, whom Reply holds nobody for, is created under the name an add would derive; a route that
    /// forbids deriving refuses the update before anything is written; and an account that holds no further contact is
    /// <c>limit_reached</c>.
    /// </summary>
    [Theory]
    [InlineData("derived")]
    [InlineData("forbidden")]
    [InlineData("limit")]
    public async Task A_person_reply_holds_nobody_for_is_created_as_an_add_creates_them(string way)
    {
        using var account = new ReplyAccount();
        if (way == "limit")
        {
            account.Refuses("contact update", "account.contact_limit_reached", status: 400, replyCode: "contact.limitExceeded");
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(Fields(("title", "CTO")));
        input["contacts"]![0]!["first_name"] = null;
        var binding = way == "forbidden" ? new JsonObject { ["derive_first_name"] = false } : null;
        var result = await ReplyOperations.InvokeAsync(api, Operation, input, Ct, binding: binding);

        switch (way)
        {
            case "derived":
                ReplyOperations.Succeeded(Operation, result);
                Assert.Equal(("Alvarez", "CTO"), ((string?)account.ContactOf(1001)!["firstName"], (string?)account.ContactOf(1001)!["title"]));
                break;
            case "forbidden":
                var refused = ReplyOperations.Failed(Operation, result);
                Assert.Equal(("provider_call_failed", "the route forbids deriving a first name"), (refused.Code, (string?)refused.Details!["reason"]));
                Assert.Equal(["contact find"], Lines(account));
                Assert.Empty(account.Contacts);
                break;
            default:
                var full = ReplyOperations.Failed(Operation, result);
                Assert.Equal(("limit_reached", FailureClass.Permanent), (full.Code, full.Class));
                Assert.Empty(account.Contacts);
                break;
        }
    }

    [Fact]
    public async Task A_field_left_out_stays_as_reply_holds_it_and_a_null_clears_one()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", lastName: "Alvarez", company: "Puerto Analytics", title: "Head of Growth");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var fields = new JsonObject { ["last_name"] = null, ["title"] = "CTO" };
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(fields, pinned: "1001")));

        Assert.Equal("""{"last_name":null,"title":"CTO"}""", Assert.Single(account.Calls).Body);
        var contact = account.ContactOf(1001)!;
        Assert.Equal((null, "CTO", "Puerto Analytics", "Marta"), ((string?)contact["lastName"], (string?)contact["title"], (string?)contact["company"], (string?)contact["firstName"]));
    }

    /// <summary>A custom field is named as the account names it, whatever the case the arguments wrote it in; a null clears one.</summary>
    [Fact]
    public async Task A_custom_field_is_set_under_the_name_the_account_gives_it()
    {
        using var account = new ReplyAccount()
            .WithContact(1001, Address, "Marta")
            .WithCustomValue(1001, "Region", "LatAm")
            .WithCustomField(1, "lead_source")
            .WithCustomField(2, "Region");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var custom = new JsonArray(
            new JsonObject { ["name"] = "LEAD_SOURCE", ["value"] = "Webinar" },
            new JsonObject { ["name"] = "region", ["value"] = null });
        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(fields: null, pinned: "1001", custom: custom)));

        Assert.Equal("""{"custom_fields":[{"name":"LEAD_SOURCE","value":"Webinar"},{"name":"region","value":null}]}""", Assert.Single(account.Calls).Body);
        Assert.Equal("""[{"key":"lead_source","value":"Webinar"}]""", account.ContactOf(1001)!["customFields"]!.ToJsonString());
    }

    [Fact]
    public async Task A_custom_field_the_account_defines_none_of_is_not_found_and_nothing_is_changed()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", title: "Head of Growth").WithCustomField(1, "lead_source");
        var before = account.Contacts.ToJsonString();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var custom = new JsonArray(new JsonObject { ["name"] = "budget", ["value"] = "100k" });
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), pinned: "1001", custom: custom)));

        Assert.Equal(("field_not_found", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal("1001", (string?)error.ExternalIds!["contact"]);
        Assert.Equal(before, account.Contacts.ToJsonString());
    }

    /// <summary>
    /// The update ran and its answer was lost. The pin it learned travels out with the failure, and the repeat works by it:
    /// nobody is looked up again, and setting the same values again lands on the same record.
    /// </summary>
    [Fact]
    public async Task A_lost_update_is_repeated_by_the_contact_pinned_on_the_failure()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.LosesTheOutcome("contact update");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")))));
        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (lost.Code, lost.Class));
        Assert.Equal("1001", (string?)lost.ExternalIds!["contact"]);

        var mark = account.Mark();
        var repeat = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), pinned: "1001"), attempt: 2));

        Assert.Equal("updated", (string?)repeat["items"]![0]!["status"]);
        Assert.Equal(["contact update 1001"], [.. account.CallsSince(mark).Select(call => call.Line)]);
        Assert.Single(account.Contacts);
        Assert.Equal("CTO", (string?)account.ContactOf(1001)!["title"]);
    }

    // -------------------------------------------------------------------------------------------------------
    // Refused before anything is asked, and refusals Reply makes
    // -------------------------------------------------------------------------------------------------------

    /// <summary>reply-cli refuses a custom field named twice as a usage error; the package refuses it first, in the operation's own word, and asks nothing.</summary>
    [Fact]
    public async Task One_custom_field_named_twice_is_an_invalid_value_and_nothing_is_asked()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithCustomField(1, "lead_source");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var custom = new JsonArray(
            new JsonObject { ["name"] = "lead_source", ["value"] = "Webinar" },
            new JsonObject { ["name"] = " Lead_Source ", ["value"] = "Event" });
        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(fields: null, custom: custom)));

        Assert.Equal(("invalid_field_value", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// The command's refusals of the import of a person Reply holds nobody for, in the words the add operations report them
    /// in: an address or a profile Reply will not take, and an invalid request naming the email, which is the person's
    /// address rather than a field the arguments set. Nothing is changed, and nobody is pinned.
    /// </summary>
    [Theory]
    [InlineData("email", Address, "contact.invalid_email", null)]
    [InlineData("linkedin", "https://www.linkedin.com/in/marta-example", "contact.invalid_linkedin_url", null)]
    [InlineData("email", Address, "reply.invalid_request", "email")]
    public async Task A_refusal_of_the_import_is_reported_as_an_add_reports_it(string channel, string value, string code, string? field)
    {
        using var account = new ReplyAccount();
        account.Refuses("contact update", code, status: 400, details: field is null ? null : new JsonObject { ["fields"] = new JsonArray(field) });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), channel: channel, value: value)));

        Assert.Equal(("invalid_channel_value", FailureClass.Validation), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact find", "contact update"], Lines(account));
        Assert.Empty(account.Contacts);
    }

    /// <summary>
    /// The lookup found nobody and the command's own import matched a contact Reply already held: the update changes that
    /// contact, as an add acts on a contact it matched, and pins it.
    /// </summary>
    [Fact]
    public async Task A_person_the_import_matched_though_the_lookup_missed_is_changed_and_pinned()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").Unlisted(1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(Fields(("title", "CTO"))));

        ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("1001", (string?)((InvocationOutcome.Succeeded)result.Outcome).ExternalIds!["contact"]);
        Assert.Equal(["contact find", "contact update"], Lines(account));
        Assert.Single(account.Contacts);
        Assert.Equal("CTO", (string?)account.ContactOf(1001)!["title"]);
    }

    /// <summary>
    /// A contact this attempt was not pinned to — one the command's own import made or matched, or one the lookup found —
    /// which Reply then answers it holds no longer is no stale pin: the attempt fails without calling it one, and nobody is
    /// pinned.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_contact_reply_named_and_then_denies_is_no_stale_pin_and_is_pinned_nowhere(bool foundByTheLookup)
    {
        using var account = foundByTheLookup ? new ReplyAccount().WithContact(1001, Address, "Marta") : new ReplyAccount();
        account.Refuses("contact update", "contact.not_found", status: 404, replyCode: "contact.notFound", ids: new JsonObject { ["contact_id"] = foundByTheLookup ? 1001 : 1009 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")))));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal("contact.not_found", (string?)error.Details!["cli_code"]);
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact find", foundByTheLookup ? "contact update 1001" : "contact update"], Lines(account));
    }

    /// <summary>
    /// An input composed without the person's own fields — absent, not null — is refused before anything is called: a
    /// person Reply holds nobody for could not be created under the name Jason holds.
    /// </summary>
    [Fact]
    public async Task An_input_without_the_person_s_projected_fields_is_refused_before_anything_is_called()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = Input(Fields(("title", "CTO")), pinned: "1001");
        foreach (var field in new[] { "first_name", "company" })
        {
            input["contacts"]![0]!.AsObject().Remove(field);
        }

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, input));

        Assert.Equal(ReplyOperations.ProjectionRefused, (error.Code, error.Class));
        Assert.Equal(ReplyOperations.ProjectionReason("first_name,company"), (string?)error.Details!["reason"]);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_value_reply_will_not_store_is_an_invalid_field_value()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Refuses("contact update", "reply.invalid_request", status: 400, details: new JsonObject { ["fields"] = new JsonArray("firstName") }, ids: new JsonObject { ["contact_id"] = 1001 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["first_name"] = null }, pinned: "1001")));

        Assert.Equal(("invalid_field_value", FailureClass.Validation), (error.Code, error.Class));
        Assert.Equal("1001", (string?)error.ExternalIds!["contact"]);
    }

    [Fact]
    public async Task More_than_one_contact_under_the_address_is_not_unique_and_nothing_is_changed()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta").WithDuplicate(1002, of: 1001);
        var before = account.Contacts.ToJsonString();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")))));

        Assert.Equal(("contact_not_unique", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact find"], Lines(account));
        Assert.Equal(before, account.Contacts.ToJsonString());
    }

    [Fact]
    public async Task A_lookup_that_cannot_be_read_to_its_end_is_asked_again_and_nothing_is_changed()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Refuses("contact find", "reply.unavailable", status: 200);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")))));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact find"], Lines(account));
    }

    [Fact]
    public async Task A_pin_that_is_not_a_reply_contact_number_resolves_to_nobody_and_nothing_is_asked()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), pinned: "p_88421")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_pin_reply_no_longer_holds_is_not_found_and_never_matched_again_by_the_address()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), pinned: "4242")));

        Assert.Equal(("contact_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["contact update 4242"], Lines(account));
        Assert.Null((string?)account.ContactOf(1001)!["title"]);
    }

    /// <summary>A channel Reply looks nobody up by names no value any attempt could find the person by, so nothing is asked.</summary>
    [Fact]
    public async Task A_channel_reply_looks_nobody_up_by_is_an_invalid_channel_value()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), channel: "phone", value: "+5491100000000")));

        Assert.Equal(("invalid_channel_value", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_result_that_does_not_say_whom_it_changed_is_a_lost_write()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Returns("contact update", new JsonObject { ["status"] = "updated", ["contact_id"] = "abc" });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal("1001", (string?)error.ExternalIds!["contact"]);
        Assert.Contains("abc", (string?)error.Details!["provider_item"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_account_whose_plan_does_not_include_it_is_told_so()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Refuses("contact update", "access.feature_unavailable", status: 403);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")), pinned: "1001")));

        Assert.Equal(("feature_unavailable", FailureClass.Permanent), (error.Code, error.Class));
    }

    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Fields(params (string Name, string? Value)[] fields)
    {
        var named = new JsonObject();
        foreach (var (name, value) in fields)
        {
            named[name] = value;
        }

        return named;
    }

    private static JsonObject Input(JsonObject? fields, string? pinned = null, JsonArray? custom = null, string channel = "email", string value = Address)
    {
        var args = new JsonObject { ["channel"] = channel };
        if (fields is not null)
        {
            args["fields"] = fields;
        }

        if (custom is not null)
        {
            args["custom_fields"] = custom;
        }

        return new JsonObject
        {
            ["args"] = args,
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = ContactId,
                ["first_name"] = "Marta",
                ["last_name"] = "Alvarez",
                ["company"] = "Puerto Analytics",
                ["title"] = "Head of Growth",
                ["time_zone"] = null,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = value, ["primary"] = true }),
                ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
            }),
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = new JsonObject(),
            },
            ["idempotency_key"] = WorkItemId,
        };
    }

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
