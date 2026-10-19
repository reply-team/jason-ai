using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A person with no pin and no first name, in the three operations that may create somebody at Reply, and in the one
/// that never does. Reply requires a first name and Jason does not; the plugin looks the address up first, works a
/// person Reply holds by the identifier found, and otherwise imports them under a first name derived from what Jason
/// holds — or refuses, when the route forbids it for an add or there is nothing to derive from. Every fact reads the
/// stand-in's call log and the invocation's own stderr, because "nothing was written" and "no value was logged" are the
/// claims.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyFirstNameTests
{
    private const string MembershipAdd = "list_membership.add";
    private const string Enroll = "campaign.enroll";
    private const string Optout = "optout.record";
    private const string Stop = "enrollment.stop";

    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRQ";

    // Made-up data. The local part keeps its case, a dot and a plus, so "as given" is something a test can see.
    private const string Address = "Marta.Alvarez+q3@example.com";
    private const string LocalPart = "Marta.Alvarez+q3";
    private const string LastName = "Alvarez";
    private const string Company = "Puerto Analytics";
    private const string Title = "Head of Growth";

    private const string Lookup = "contact find";
    private const string CannotDerive = "a first name could not be derived";
    private const string Forbidden = "the route forbids deriving a first name";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Creating => [MembershipAdd, Enroll, Optout];

    // The writes the route's switch governs. An opt-out is protective and derives whatever the binding says.
    public static TheoryData<string> Adding => [MembershipAdd, Enroll];

    // A LinkedIn URL is no name, so a person reached by one, with no first and no last name, has nothing to derive from.
    public static TheoryData<string, string> CreatingWithNothingToDeriveFrom => new()
    {
        { MembershipAdd, "https://www.linkedin.com/in/marta-example" },
        { MembershipAdd, "https://example.com/profiles/m.alvarez@q3" },
        { Enroll, "https://www.linkedin.com/in/marta-example" },
        { Enroll, "https://example.com/profiles/m.alvarez@q3" },
        { Optout, "https://www.linkedin.com/in/marta-example" },
        { Optout, "https://example.com/profiles/m.alvarez@q3" },
    };

    public static TheoryData<string, bool> CreatingEitherWay => new()
    {
        { MembershipAdd, true },
        { MembershipAdd, false },
        { Enroll, true },
        { Enroll, false },
        { Optout, true },
        { Optout, false },
    };

    // -------------------------------------------------------------------------------------------------------
    // Reply holds nobody: the derivation, in its order
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Creating))]
    public async Task A_person_reply_does_not_hold_with_no_first_name_is_imported_under_their_last_name(string operation)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, operation, Input(operation));

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal([Lookup, CommandLine(operation)], Paths(account));

        // The one name the person has is their first at Reply, and it is not sent a second time as a last name.
        var sent = JsonNode.Parse(account.Calls[1].Body!)!.AsObject();
        Assert.Equal(LastName, (string?)sent["first_name"]);
        Assert.False(sent.ContainsKey("last_name"));

        // Everything else Jason holds about the person travels as it would with a first name given: deriving one
        // must not cost the company or the title.
        Assert.Equal(Company, (string?)sent["company"]);
        Assert.Equal(Title, (string?)sent["title"]);

        var stored = Assert.Single(account.Contacts)!.AsObject();
        Assert.Equal(LastName, (string?)stored["firstName"]);
        Assert.Null((string?)stored["lastName"]);
        Assert.Equal(Address, (string?)stored["email"]);
        Assert.Equal(Company, (string?)stored["company"]);
        Assert.Equal(Title, (string?)stored["title"]);

        Assert.Equal("last_name", (string?)Assert.Single(PluginLines(result))["data"]!["first_name_from"]);
        if (operation == Optout)
        {
            Assert.Equal([1001], OptedOut(account));
        }
    }

    [Theory]
    [MemberData(nameof(Creating))]
    public async Task A_person_reply_does_not_hold_with_neither_a_first_nor_a_last_name_is_imported_under_their_address_s_local_part(string operation)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, operation, Input(operation, lastName: null));

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal([Lookup, CommandLine(operation)], Paths(account));

        // As spelled: case, the dot and the plus all kept.
        var sent = JsonNode.Parse(account.Calls[1].Body!)!.AsObject();
        Assert.Equal(LocalPart, (string?)sent["first_name"]);
        Assert.Equal(Company, (string?)sent["company"]);
        Assert.Equal(Title, (string?)sent["title"]);

        var stored = Assert.Single(account.Contacts)!.AsObject();
        Assert.Equal(LocalPart, (string?)stored["firstName"]);
        Assert.Equal(Company, (string?)stored["company"]);
        Assert.Equal(Title, (string?)stored["title"]);
        Assert.Equal("email_local_part", (string?)Assert.Single(PluginLines(result))["data"]!["first_name_from"]);
    }

    /// <summary>
    /// A first name of white space is no first name: the CLI trims it to nothing and Reply never receives it. The rows
    /// are characters JavaScript's own trim takes away, which is what decides it in the plugin and in the CLI alike.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\uFEFF")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public async Task A_blank_first_name_is_no_first_name(string blank)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, MembershipAdd, Input(MembershipAdd, firstName: blank));

        ReplyOperations.Succeeded(MembershipAdd, result);
        Assert.Equal([Lookup, CommandLine(MembershipAdd)], Paths(account));
        Assert.Equal(LastName, (string?)JsonNode.Parse(account.Calls[1].Body!)!["first_name"]);
    }

    /// <summary>The same blank first name for a person Reply holds: found by the lookup, added by the identifier.</summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("\uFEFF")]
    public async Task A_blank_first_name_of_a_person_reply_holds_is_added_by_the_identifier_found(string blank)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(MembershipAdd, await InvokeAsync(api, MembershipAdd, Input(MembershipAdd, firstName: blank)));

        Assert.Equal([Lookup, CommandLine(MembershipAdd)], Paths(account));
        Assert.Equal("2001", account.Calls[1].Option("--contact-id"));
        Assert.Null(account.Calls[1].Body);
        Assert.Equal([2001], account.MembersOf(ReplyOperations.List));
    }

    [Fact]
    public async Task A_blank_last_name_is_no_last_name()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, MembershipAdd, Input(MembershipAdd, lastName: "  "));

        ReplyOperations.Succeeded(MembershipAdd, result);
        Assert.Equal(LocalPart, (string?)JsonNode.Parse(account.Calls[1].Body!)!["first_name"]);
        Assert.Equal("email_local_part", (string?)Assert.Single(PluginLines(result))["data"]!["first_name_from"]);
    }

    [Theory]
    [MemberData(nameof(CreatingWithNothingToDeriveFrom))]
    public async Task A_person_with_nothing_to_derive_a_first_name_from_is_refused_and_nothing_is_written(string operation, string value)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, operation, Input(operation, lastName: null, address: value, channel: "linkedin"));

        var error = ReplyOperations.Failed(operation, result);
        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(CannotDerive, (string?)error.Details!["reason"]);
        Assert.Equal([Lookup], Paths(account));
        Assert.Empty(account.Contacts);
        Assert.Empty(PluginLines(result));
        if (operation == Enroll)
        {
            // The campaign the enrolment learned is still reported, so the runtime keeps its pin.
            Assert.Equal("7", (string?)error.ExternalIds!["campaign"]);
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // The route's switch
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Adding))]
    public async Task A_route_that_forbids_deriving_refuses_a_person_reply_does_not_hold(string operation)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, operation, Input(operation), binding: Off());

        var error = ReplyOperations.Failed(operation, result);
        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(Forbidden, (string?)error.Details!["reason"]);
        Assert.Contains("derive_first_name", error.Message, StringComparison.Ordinal);
        Assert.Equal([Lookup], Paths(account));
        Assert.Empty(account.Contacts);
        Assert.Empty(PluginLines(result));
    }

    /// <summary>
    /// An opt-out is protective and greets nobody. Refusing it would leave the person unflagged at Reply and protect
    /// nobody from a made-up greeting, so it derives whatever the route's binding says.
    /// </summary>
    [Fact]
    public async Task A_route_that_forbids_deriving_still_records_the_opt_out_of_a_person_reply_does_not_hold()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Optout, Input(Optout, lastName: null), binding: Off());

        var answer = ReplyOperations.Succeeded(Optout, result);
        Assert.Equal("recorded", (string?)answer["items"]![0]!["status"]);
        Assert.Equal([Lookup, CommandLine(Optout)], Paths(account));
        Assert.Equal(LocalPart, (string?)JsonNode.Parse(account.Calls[1].Body!)!["first_name"]);
        Assert.Equal([1001], OptedOut(account));
        Assert.Equal("email_local_part", (string?)Assert.Single(PluginLines(result))["data"]!["first_name_from"]);
    }

    /// <summary>On the floor, an opt-out's refusal names the floor, never the switch: the switch never applies to it.</summary>
    [Fact]
    public async Task An_opt_out_with_nothing_to_derive_from_is_refused_for_that_reason_even_where_the_route_forbids_deriving()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Optout, Input(Optout, lastName: null, address: "https://www.linkedin.com/in/marta-example", channel: "linkedin"), binding: Off());

        var error = ReplyOperations.Failed(Optout, result);
        Assert.Equal(CannotDerive, (string?)error.Details!["reason"]);
        Assert.Empty(account.OptOuts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"derive_first_name":true}""")]
    public async Task A_route_that_says_nothing_or_yes_derives(string? binding)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(
            api,
            MembershipAdd,
            Input(MembershipAdd),
            binding: binding is null ? null : JsonNode.Parse(binding)!.AsObject());

        ReplyOperations.Succeeded(MembershipAdd, result);
        Assert.Equal(LastName, (string?)Assert.Single(account.Contacts)!["firstName"]);
    }

    // -------------------------------------------------------------------------------------------------------
    // Reply holds the person, a repeat, and what never leaves the runtime
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(CreatingEitherWay))]
    public async Task A_person_reply_holds_is_worked_by_the_identifier_found_and_their_record_is_untouched(string operation, bool derive)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithContact(2001, Address.ToLowerInvariant(), "Marta");
        var before = account.Contacts.ToJsonString();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, operation, Input(operation), binding: derive ? null : Off());

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal([Lookup, CommandLine(operation)], Paths(account));
        Assert.Equal("2001", account.Calls[1].Option("--contact-id"));
        Assert.Null(account.Calls[1].Body);
        Assert.Equal(before, account.Contacts.ToJsonString());
        Assert.Empty(PluginLines(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_repeat_after_the_answer_to_a_derived_import_was_lost_makes_no_second_contact(bool pinned)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.LosesTheOutcome("contact-list add-contact");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(MembershipAdd, await InvokeAsync(api, MembershipAdd, Input(MembershipAdd)));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal("1001", (string?)lost.ExternalIds!["contact"]);

        var repeat = await InvokeAsync(api, MembershipAdd, Input(MembershipAdd, pinned: pinned ? "1001" : null), attempt: 2);

        ReplyOperations.Succeeded(MembershipAdd, repeat);
        Assert.Single(account.Contacts);
        Assert.Equal([1001], account.MembersOf(ReplyOperations.List));
        Assert.Empty(PluginLines(repeat));
    }

    [Theory]
    [InlineData(MembershipAdd, true)]
    [InlineData(MembershipAdd, false)]
    [InlineData(Enroll, true)]
    [InlineData(Enroll, false)]
    [InlineData(Optout, true)]
    [InlineData(Optout, false)]
    public async Task No_value_of_the_person_reaches_an_argument_or_the_log(string operation, bool withLastName)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, operation, Input(operation, lastName: withLastName ? LastName : null));

        ReplyOperations.Succeeded(operation, result);
        var arguments = string.Join("\n", account.Calls.Select(call => string.Join(" ", call.Args)));
        var stderr = Stderr(result);
        foreach (var personal in new[] { Address, LocalPart, LastName, Company, Title })
        {
            Assert.DoesNotContain(personal, arguments, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(personal, stderr, StringComparison.OrdinalIgnoreCase);
        }

        // What makes the two assertions above mean something: a name was derived, and the line saying so was read.
        Assert.Single(PluginLines(result));
    }

    [Fact]
    public async Task The_stop_of_a_person_without_a_first_name_derives_nothing()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Stop, Input(Stop, lastName: null));

        var answer = ReplyOperations.Succeeded(Stop, result);
        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
        var removal = Assert.Single(account.Calls);
        Assert.False(JsonNode.Parse(removal.Body!)!.AsObject().ContainsKey("first_name"));
        Assert.Empty(PluginLines(result));
    }

    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Off() => new() { ["derive_first_name"] = false };

    private static string CommandLine(string operation) => operation switch
    {
        MembershipAdd => "contact-list add-contact 9",
        Enroll => "sequence add-contact 7",
        Optout => "contact opt-out",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static JsonObject Input(
        string operation,
        string? firstName = null,
        string? lastName = LastName,
        string address = Address,
        string? pinned = null,
        string channel = "email") => new()
    {
        ["args"] = operation switch
        {
            MembershipAdd => new JsonObject { ["list"] = new JsonObject { ["external_id"] = "9" }, ["channel"] = channel },
            Enroll => new JsonObject
            {
                ["campaign"] = new JsonObject { ["external_id"] = "7" },
                ["channel"] = channel,
                ["collision"] = "skip",
                ["start"] = new JsonObject { ["position"] = "first_step" },
                ["first_touch"] = "authored_delay",
            },
            Optout => new JsonObject
            {
                ["channel"] = channel,
                ["statement"] = "please take me off your list",
                ["arrived_via"] = "social",
                ["said_at"] = "2026-10-03T08:14:00Z",
                ["learned_at"] = "2026-10-03T08:20:00Z",
            },
            Stop => new JsonObject
            {
                ["channel"] = channel,
                ["reason"] = new JsonObject
                {
                    ["code"] = "not_interested",
                    ["note"] = "asked to stop",
                    ["caused_by_contact_id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        },
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = ContactId,
            ["first_name"] = firstName,
            ["last_name"] = lastName,
            ["company"] = Company,
            ["title"] = Title,
            ["time_zone"] = null,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = address, ["primary"] = true }),
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
        }),
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = operation == Stop ? new JsonObject { ["campaign"] = "7" } : new JsonObject(),
        },
        ["idempotency_key"] = WorkItemId,
    };

    private static Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        string operation,
        JsonObject input,
        JsonObject? binding = null,
        int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, operation, input, Ct, binding: binding, attempt: attempt);

    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Line)];

    private static IReadOnlyList<int> OptedOut(ReplyAccount account) =>
        [.. account.OptOuts.Select(entry => entry!.GetValue<int>()).Order()];

    /// <summary>The lines the plugin itself wrote with host.log, from the invocation's own stderr.log.</summary>
    private static IReadOnlyList<JsonObject> PluginLines(PluginInvocationResult result) =>
        [.. Stderr(result).Split('\n')
            .Where(line => line.TrimStart().StartsWith('{'))
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(line => (string?)line["source"] == "plugin")];

    /// <summary>The invocation's whole stderr, host lines and plugin lines alike.</summary>
    private static string Stderr(PluginInvocationResult result)
    {
        var log = Path.Combine(result.Launch!.WorkDir, "stderr.log");
        return File.Exists(log) ? File.ReadAllText(log) : string.Empty;
    }
}
