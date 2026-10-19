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
    /// The update changes a record Reply holds and makes none: a person Reply holds nobody for under the address is not
    /// found, nothing is imported, and nobody is pinned. They are enrolled or put on a list first, then updated.
    /// </summary>
    [Fact]
    public async Task A_person_reply_holds_nobody_for_is_not_found_and_nobody_is_created()
    {
        using var account = new ReplyAccount().WithContact(1001, "ana@example.com", "Ana");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(Fields(("title", "CTO")))));

        Assert.Equal(("contact_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact find"], Lines(account));
        Assert.Single(account.Contacts);
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
