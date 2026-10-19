using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// Answers no provider would send, given to the package to see what it does with them. Every one is built here
/// rather than parsed out of a string a test wrote, so nothing can pass because a parser refused the input for
/// an unrelated reason, and every one has to end the same way: a failure the package declared, under a code the
/// operation's own document publishes, in the class that document gives it.
/// </summary>
/// <remarks>
/// Two endings are failures of this file rather than passes. A protocol failure means the runtime never got an
/// answer at all, and <c>plugin_exception</c> means the package threw something of its own — neither says
/// anything about whether the provider acted, which is the one thing an operator needs from a failure. Both are
/// caught by <see cref="ReplyOperations.Failed"/>, which will not accept a code the document does not declare.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyHostileAnswerTests
{
    /// <summary>The exact lookup every write about a person with no pin makes before anything is written.</summary>
    private const string Find = "contact find";

    /// <summary>A profile nobody has signed into, which is how an account with no credential is asked for.</summary>
    private const string SignedOut = "signed-out";

    /// <summary>
    /// The CLI's own refusal of a profile it does not hold, in the envelope <c>--json</c> prints it in — built
    /// here rather than written out, like every other input these tests hand the package. The exit code is the
    /// same one a missing credential ends with; the code beside it is what separates them.
    /// </summary>
    private static string UsageRefusal => new JsonObject
    {
        ["error"] = new JsonObject
        {
            ["code"] = "usage.profile",
            ["title"] = "Unknown profile 'acme'.",
            ["hint"] = "Create it with `profile add acme`.",
        },
    }.ToJsonString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stdout_that_is_not_json_at_all_is_treated_as_no_answer()
    {
        // A CLI that fell over prints whatever it prints. There is no envelope to read, so there is no answer —
        // and this call changes nothing, so asking again costs nothing.
        var noise = string.Join('\n', Enumerable.Range(0, 20).Select(line => "reply: unhandled exception at frame " + line));
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence get", 0, noise);

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Fact]
    public async Task A_result_that_is_null_is_no_answer()
    {
        // The command exited 0 and its result says nothing. Reading a campaign out of null would mean inventing one,
        // so the read is reported as one that did not arrive.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence get", 0, "null");

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Fact]
    public async Task A_result_that_leaves_out_the_field_the_mapping_needs_is_a_lost_write()
    {
        // The add went out, and its result does not say whom it took. Guessing a contact would list somebody else, so
        // it is the lost write it may be, and what came back travels as evidence.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Returns("contact-list add-contact", new JsonObject { ["status"] = "added", ["list_id"] = 9 });

        var error = await FailedAsync(account, ReplyOperations.MembershipAdd, ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        var item = error.Details!["provider_item"]!.GetValue<string>();
        Assert.DoesNotContain(ReplyOperations.Address, item, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A list add whose result names the contact by an identifier nobody can work by took somebody: a lost write.</summary>
    [Fact]
    public async Task A_list_add_whose_result_names_a_contact_nobody_can_work_by_is_a_lost_write()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Returns("contact-list add-contact", new JsonObject { ["status"] = "added", ["list_id"] = 9, ["contact_id"] = "abc" });

        var error = await FailedAsync(account, ReplyOperations.MembershipAdd, ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Contains("abc", error.Details!["provider_item"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact lookup answers with an identifier this package cannot work by, or with nothing it can read as one: that
    /// is no answer to a read, so the read is asked again and nothing is added. The failure names what came back by the
    /// identifier alone, because anything else an answer held could be part of a person's record.
    /// </summary>
    [Theory]
    [InlineData("\"c-88\"")]
    [InlineData("0")]
    [InlineData("1001.5")]
    [InlineData(null)]
    public async Task A_lookup_that_answers_no_identifier_this_package_can_work_by_is_asked_again(string? id)
    {
        var answer = new JsonObject
        {
            ["email"] = ReplyOperations.Address,
            ["first_name"] = "Recorded-marker",
            ["phone"] = "+15550100",
        };
        if (id is not null)
        {
            answer["contact_id"] = JsonNode.Parse(id);
        }

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Returns(Find, answer);

        var error = await FailedAsync(account, ReplyOperations.MembershipAdd, ReplyOperations.MembershipAddInput());

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        var item = error.Details!["provider_item"]!.GetValue<string>();
        foreach (var field in new[] { ReplyOperations.Address, "Recorded-marker", "+15550100" })
        {
            Assert.DoesNotContain(field, item, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal([Find], account.Calls.Select(call => call.Line));
        Assert.Empty(account.MembersOf(ReplyOperations.List));
    }

    /// <summary>
    /// The exec capture is bounded, and a body past that bound arrives cut in half: valid JSON up to the cut and
    /// nothing after it. On a read that is not an answer that went missing — asking again would be cut at the same
    /// byte — so it is the one permanent ending a read has for it, and it says which setting is the bound.
    /// </summary>
    [Fact]
    public async Task A_read_whose_answer_outgrows_the_capture_fails_permanently_as_too_large()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence get", 0, Enormous().ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, paths => Capping(paths, Capture));

        var result = await ReplyOperations.InvokeAsync(
            api,
            ReplyOperations.CampaignGet,
            ReplyOperations.CampaignGetInput(),
            Ct);
        var error = ReplyOperations.Failed(ReplyOperations.CampaignGet, result);

        Assert.Equal("provider_answer_too_large", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("Plugins:Exec:OutputBytes", error.Message, StringComparison.Ordinal);

        // And it really was the cut, said by the host that made it rather than inferred from the package having
        // failed: the `exec` diagnostic records that what it captured was not all there was to capture.
        var diagnostics = await File.ReadAllTextAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
        Assert.Contains("\"truncated\":true", diagnostics, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same cut on a write is still the expensive ending. The request went out and its answer did not come
    /// back whole, so the write may have happened, and nothing about the answer's size changes that.
    /// </summary>
    [Fact]
    public async Task A_write_whose_answer_outgrows_the_capture_is_still_a_lost_write()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("contact-list add-contact", 0, Enormous().ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, paths => Capping(paths, Capture));

        var error = ReplyOperations.Failed(
            ReplyOperations.MembershipAdd,
            await ReplyOperations.InvokeAsync(api, ReplyOperations.MembershipAdd, ReplyOperations.MembershipAddInput(), Ct));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
    }

    /// <summary>
    /// A read made inside a write keeps the ending it always had. The exact lookup a list add makes for a person with
    /// no pin is a read, and the write declares no too-large code, so a cut there is reported as the read going
    /// unanswered, which the next attempt simply asks again.
    /// </summary>
    [Fact]
    public async Task A_read_inside_a_write_whose_answer_outgrows_the_capture_keeps_the_ending_it_had()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints(Find, 0, Enormous().ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, paths => Capping(paths, Capture));
        var input = ReplyOperations.MembershipAddInput();

        var error = ReplyOperations.Failed(
            ReplyOperations.MembershipAdd,
            await ReplyOperations.InvokeAsync(api, ReplyOperations.MembershipAdd, input, Ct));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.Empty(account.MembersOf(ReplyOperations.List));
    }

    [Fact]
    public async Task An_answer_that_arrives_after_the_timeout_is_transient_on_a_read()
    {
        // The call was cut off, and this one changes nothing at the provider: there is nothing to recover and
        // nothing a repeat could do twice.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Hangs("sequence get", 20_000);

        // Wide enough for the package's reserve to apply nearly in full — it keeps back five seconds, never more than half of
        // what is left — so the call that is cut off has seconds to spare on a busy machine before the host's own
        // timeout would answer in its place.
        var timeout = TimeSpan.FromSeconds(10);
        var error = await FailedAsync(
            account,
            ReplyOperations.CampaignGet,
            ReplyOperations.CampaignGetInput(),
            timeout: timeout,
            inspect: result => AssertCutOffWithTimeToAnswerAsync(result, timeout));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.True(error.Details!["timed_out"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_answer_that_arrives_after_the_timeout_is_ambiguous_on_a_write()
    {
        // The same ending on the enrollment, which may already have sent somebody an email. Nothing about the
        // attempt can say whether it did, and with consumption pricing a repeat is a real charge, so the work
        // item stops for a person instead of being retried.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Hangs("sequence add-contact", 20_000);

        // Wide enough for the package's reserve to apply in full, as in the read above.
        var timeout = TimeSpan.FromSeconds(16);
        var error = await FailedAsync(
            account,
            ReplyOperations.Enroll,
            ReplyOperations.EnrollInput(pinned: ReplyOperations.Ensured),
            timeout: timeout,
            inspect: result => AssertCutOffWithTimeToAnswerAsync(result, timeout));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.True(error.Details!["timed_out"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_refused_credential_is_unauthorized()
    {
        // The CLI says a refused credential in its own word, `access.denied`, with Reply's status beside it: the
        // profile has to be signed in again, and no repeat can help.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Refuses("sequence get", "access.denied", status: 401);

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        Assert.Equal("unauthorized", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(401, error.Details!["status"]!.GetValue<int>());
        Assert.Null(error.Details!["provider_code"]);
    }

    [Fact]
    public async Task A_two_hundred_carrying_an_array_where_an_object_was_expected_is_no_answer()
    {
        var listed = new JsonArray(new JsonObject { ["id"] = ReplyOperations.Sequence });
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence get", 0, listed.ToJsonString());

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        // A page where one sequence was asked for is not a sequence, whatever the status said, and taking the
        // first element would be reading a campaign out of something that never named one.
        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Fact]
    public async Task A_two_hundred_carrying_an_array_where_a_page_was_expected_is_no_answer_either()
    {
        // A page's items that are not a list say nothing about who the sequence holds, and reading them as an empty
        // page would answer a page nobody gave. This read changes nothing, so it is asked again.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence contacts", 0, """{"items":{},"has_more":false,"total":0}""");

        // A read carries no idempotency key, so the campaign-scope input is built without one.
        var input = CampaignOnly(new JsonObject { ["limit"] = 10, ["offset"] = 0 });
        input.Remove("idempotency_key");
        var error = await FailedAsync(account, "enrollment.list", input);

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Theory]
    [InlineData(ReplyOperations.MembershipAdd)]
    [InlineData(ReplyOperations.Enroll)]
    public async Task A_write_the_cli_refused_for_want_of_a_credential_is_not_a_write_that_may_have_happened(string operation)
    {
        // The day-one failure: the operator has not signed in. The CLI decides that before it builds a request,
        // so nothing reached the account — and calling it a lost answer would stop a work item for a person over
        // a write nobody made, on writes where that verdict is the expensive one. The add and the enrollment are
        // driven here, because the read/write mark is what the old reading turned on.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);

        var error = await FailedAsync(
            account,
            operation,
            ReplyOperations.Input(operation),
            binding: new JsonObject { ["profile"] = SignedOut });

        Assert.Equal("unauthorized", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);

        // Read from the CLI's own refusal rather than from its exit code, which says only that the call was
        // refused and not what an operator has to do about it.
        Assert.Equal("auth.required", error.Details!["cli_code"]!.GetValue<string>());
        Assert.Equal(2, error.Details!["exit_code"]!.GetValue<int>());

        // The account this test planted was never touched: the calls this run made went to the profile nobody
        // signed into, and that profile answered before any of them was sent.
        Assert.Empty(account.Calls);
        Assert.Empty(account.MembersOf(ReplyOperations.List));
    }

    [Fact]
    public async Task A_refusal_the_cli_makes_for_any_other_reason_stays_what_the_exit_code_says()
    {
        // The other half of the same reading, or the rule above would be "any stderr at all means unauthorized".
        // An unknown profile is refused with the same exit code and a code outside the authentication namespace,
        // and it is a call this package built wrong rather than an account nobody signed into.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Prints("contact-list add-contact", 2, string.Empty, stderr: UsageRefusal);

        var error = await FailedAsync(
            account,
            ReplyOperations.MembershipAdd,
            ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal("usage.profile", error.Details!["cli_code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_lost_answer_with_nothing_the_cli_said_is_still_a_lost_write()
    {
        // And the third: exit 1, nothing on stdout, nothing of the CLI's own on stderr. That is the ending the
        // read/write mark was written for — the request may have gone out and been cut off — and it stays what
        // it was.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Prints("contact-list add-contact", 1, string.Empty, stderr: "reply: connection reset while reading the response");

        var error = await FailedAsync(
            account,
            ReplyOperations.MembershipAdd,
            ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Null(error.Details!["provider_code"]);
        Assert.Null(error.Details!["cli_code"]);
    }

    // -------------------------------------------------------------------------------------------------------
    // The four writes: a shape no provider documents, on the read each one decides by, ends in a declared code
    // -------------------------------------------------------------------------------------------------------

    /// <summary>A pause whose result does not say `paused` went out and says nothing of what it did: a lost write.</summary>
    [Fact]
    public async Task A_pause_whose_result_is_not_paused_is_a_lost_write()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Returns("sequence pause", new JsonObject { ["status"] = "weird", ["sequence_id"] = 7 });

        var error = await FailedAsync(account, "campaign.pause", CampaignOnly(new JsonObject()));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
    }

    /// <summary>A live sequence counted in words is no count: the answer would have no `enrolled`, so it is asked again.</summary>
    [Fact]
    public async Task An_activation_whose_count_is_not_a_number_is_asked_again()
    {
        using var account = new ReplyAccount().WithSequence(ReplyOperations.Sequence, "Q3 LatAm founders", "active", false, 11, 12);
        account.Returns("sequence start", new JsonObject
        {
            ["status"] = "already_active",
            ["sequence_id"] = 7,
            ["previous_status"] = "active",
            ["contacts"] = "two",
        });

        var error = await FailedAsync(account, "campaign.activate", CampaignOnly(new JsonObject { ["expected"] = new JsonObject { ["enrolled"] = 2 } }));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    /// <summary>
    /// An opt-out whose result is an array says nothing about whom it took, and the write went out, so it is the lost
    /// write it may be.
    /// </summary>
    [Fact]
    public async Task An_opt_out_whose_write_answers_an_array_is_a_lost_write()
    {
        using var account = new ReplyAccount().WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Prints("contact opt-out", 0, "[]");

        var error = await FailedAsync(account, "optout.record", Person(
            new JsonObject
            {
                ["channel"] = "email",
                ["statement"] = "please take me off your list",
                ["arrived_via"] = "reply",
                ["said_at"] = "2026-10-03T08:14:00Z",
                ["learned_at"] = "2026-10-03T08:20:00Z",
            },
            pinned: "1001"));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Equal("1001", (string?)error.ExternalIds!["contact"]);
    }

    /// <summary>A stop whose result is neither `removed` nor `not_in_sequence` says nothing of what it did: a lost write.</summary>
    [Fact]
    public async Task A_stop_whose_result_is_not_one_it_knows_is_a_lost_write()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName).WithEnrollment(ReplyOperations.Sequence, 1001);
        account.Returns("sequence remove-contact", new JsonObject { ["status"] = "gone", ["sequence_id"] = 7, ["contact_id"] = 1001 });

        var error = await FailedAsync(account, "enrollment.stop", Person(
            new JsonObject { ["channel"] = "email", ["reason"] = new JsonObject { ["code"] = "other" } },
            pinned: null,
            campaign: "7"));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Equal([1001], account.EnrolledIn(ReplyOperations.Sequence));
    }

    /// <summary>An input naming the campaign by its pin and nobody else, as a campaign-scope write receives it.</summary>
    private static JsonObject CampaignOnly(JsonObject arguments) => new()
    {
        ["args"] = arguments,
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = ReplyOperations.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        },
        ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRT",
    };

    /// <summary>An input about one person, pinned or not, in a campaign pinned or not.</summary>
    private static JsonObject Person(JsonObject arguments, string? pinned, string? campaign = null) => new()
    {
        ["args"] = arguments,
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["first_name"] = ReplyOperations.FirstName,
            ["last_name"] = null,
            ["company"] = null,
            ["title"] = null,
            ["time_zone"] = null,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = ReplyOperations.Address, ["primary"] = true }),
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["contact"] = pinned },
        }),
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = campaign is null ? new JsonObject() : new JsonObject { ["campaign"] = campaign },
        },
        ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRV",
    };

    /// <summary>
    /// Drives one operation against an account that has already been told what to answer, and holds the ending
    /// to the operation's own document: a failure the package declared, under a code that document publishes,
    /// in the class it gives it.
    /// </summary>
    private static async Task<OutcomeError> FailedAsync(
        ReplyAccount account,
        string operation,
        JsonObject input,
        int attempt = 1,
        TimeSpan? timeout = null,
        Action<JasonPaths>? prepare = null,
        JsonObject? binding = null,
        Func<PluginInvocationResult, Task>? inspect = null)
    {
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, prepare);

        var result = await ReplyOperations.InvokeAsync(
            api,
            operation,
            input,
            Ct,
            binding: binding,
            attempt: attempt,
            timeout: timeout);

        var error = ReplyOperations.Failed(operation, result);
        if (inspect is not null)
        {
            await inspect(result);
        }

        return error;
    }

    /// <summary>
    /// A call that ran out of time is ended with time left for the package to say so. The host ends the whole
    /// invocation when its budget is spent, and before the package hears that a call was cut off the host has
    /// ended the program's process tree and waited for its pipes — work that takes longer on a busy machine. A
    /// call given everything that was left leaves nothing for any of it, so the host's own timeout answers
    /// instead of the package's: <c>plugin_timeout</c>, with the identifiers the attempt had learned lost. The
    /// length the host recorded for the call that was cut off is how long it was given, and it has to leave a
    /// quarter of the budget.
    /// </summary>
    private static async Task AssertCutOffWithTimeToAnswerAsync(PluginInvocationResult result, TimeSpan budget)
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
        var cutOff = lines
            .Where(line => line.Trim().Length > 0)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(line => (string?)line["message"] == "exec" && line["data"]!["timed_out"]!.GetValue<bool>())
            .ToList();

        var ranMs = Assert.Single(cutOff)["data"]!["duration_ms"]!.GetValue<long>();
        var allowedMs = budget.TotalMilliseconds * 3 / 4;
        Assert.True(
            ranMs <= allowedMs,
            $"The call that was cut off ran for {ranMs} ms of a {budget.TotalMilliseconds} ms budget; it may run for {allowedMs} ms at most.");
    }

    /// <summary>The smallest exec capture an installation may configure, which a test can outgrow cheaply.</summary>
    private const int Capture = 65_536;

    /// <summary>A sequence whose name alone is three times the capture: a well-formed answer nobody can read whole.</summary>
    private static JsonObject Enormous() => new()
    {
        ["id"] = ReplyOperations.Sequence,
        ["name"] = new string('a', Capture * 3),
        ["status"] = "active",
        ["isArchived"] = false,
    };

    /// <summary>
    /// The exec capture, lowered to its smallest allowed size so an answer can be built that outgrows it
    /// without a test writing megabytes. The grant is already in the file by the time this runs, so it is
    /// edited rather than replaced.
    /// </summary>
    private static void Capping(JasonPaths paths, int outputBytes)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        var plugins = settings["Plugins"]!.AsObject();
        plugins["Exec"] = new JsonObject { ["OutputBytes"] = outputBytes };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
    }
}
