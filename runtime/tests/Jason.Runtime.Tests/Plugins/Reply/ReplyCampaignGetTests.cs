using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>campaign.get</c> as the official package performs it: the real plugin JavaScript, loaded by a real
/// plugin-host child, driving the stand-in vendor CLI against an account a test planted. One test per entry of
/// the document's own <c>conformance</c> list, and then the mapping decisions this provider forces — Reply has
/// no campaign resource at all, so a campaign is a sequence; a sequence's state is two fields there and one
/// here; and a sequence reports no people counts, which is a fact rather than a gap.
/// </summary>
/// <remarks>
/// The class writes a process-wide variable — the one the CLI finds its account through — so it belongs to the
/// collection that never runs two such classes at once. Every fixture starts through
/// <see cref="ReplyPlugins.StartAsync"/>, which holds the resolved program to the test tree before anything
/// runs: a resolution that escaped would not be a wrong answer, it would be a call to somebody's real account.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyCampaignGetTests
{
    private const string Operation = "campaign.get";
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";

    /// <summary>The sequence every test plants, by an identifier of the shape Reply actually issues.</summary>
    private const int Sequence = 7;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list, every entry of it covered
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_linked_campaign_is_read_by_its_pin_without_the_planner_repeating_the_identifier()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "7"), Ct);

        // A link exists, so nobody has to name the provider's identifier a second time — which is the whole
        // reason the first read pins it.
        var answer = Succeeded(result);
        Assert.Equal("7", answer["campaign"]!["external_id"]!.GetValue<string>());
        Assert.Equal("Q3 outbound", answer["campaign"]!["name"]!.GetValue<string>());
        AssertTheSequenceWasRead(account);
    }

    [Fact]
    public async Task A_first_read_links_the_campaign_by_the_identifier_the_planner_supplied_and_pins_it()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "new");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "7"), Ct);

        var answer = Succeeded(result);
        Assert.Equal("7", answer["campaign"]!["external_id"]!.GetValue<string>());

        // The pin travels on the outcome's own `external_ids`, which is what the runtime records, and never
        // inside `result.campaign`, whose schema is closed and would make the whole answer `result_invalid`.
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("7", outcome.ExternalIds!["campaign"]!.GetValue<string>());
        Assert.DoesNotContain("external_ids", answer["campaign"]!.AsObject().Select(member => member.Key));
        AssertTheSequenceWasRead(account);
    }

    [Fact]
    public async Task A_sequence_this_account_does_not_hold_fails_permanently_rather_than_answering_empty()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "7"), Ct);

        // Nothing was planted, so Reply answers 404 with its own `sequence.notFound`. An empty answer would be
        // read by a planner as a campaign with nothing in it.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("campaign_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("sequence.notFound", failed.Error.Details!["provider_code"]!.GetValue<string>());

        // And the identifier is not pinned: the provider has just said it holds nothing under it, so recording
        // it as a link would record a link the provider denies.
        Assert.Null(failed.Error.ExternalIds);
        AssertTheSequenceWasRead(account);
    }

    [Fact]
    public async Task A_provider_state_this_vocabulary_has_no_word_for_is_reported_as_other()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "queued");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        // Guessing the closest-looking word is exactly what the catch-all exists to prevent: a planner reasons
        // over `status`, and a wrong word there is acted on.
        Assert.Equal("other", answer["campaign"]!["status"]!.GetValue<string>());
        Assert.Equal("queued", answer["vendor"]!["status"]!.GetValue<string>());
    }

    /// <summary>A provider state naming a property every JavaScript object has is still a state with no word here.</summary>
    [Fact]
    public async Task A_provider_state_that_names_an_object_property_is_still_reported_as_other()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "constructor");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        Assert.Equal("other", answer["campaign"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_result_carries_no_field_the_contract_does_not_declare_apart_from_vendor()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "paused");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        // `Succeeded` has already held the whole answer to the published output schema, which is closed. This
        // says the same thing in the form a reader can check by eye: two members at the top, four below, and
        // everything Reply's own is under the one bag that is allowed to be free-form.
        Assert.Equal(["campaign", "vendor"], answer.Select(member => member.Key).Order());
        Assert.Equal(
            ["counts", "external_id", "name", "status"],
            answer["campaign"]!.AsObject().Select(member => member.Key).Order());
    }

    // -------------------------------------------------------------------------------------------------------
    // The state mapping, which is two fields at Reply and one here
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("new", false, "draft")]
    [InlineData("active", false, "live")]
    [InlineData("paused", false, "paused")]
    [InlineData("sunsetting", false, "other")]
    [InlineData("new", true, "archived")]
    [InlineData("active", true, "archived")]
    [InlineData("paused", true, "archived")]
    [InlineData("sunsetting", true, "archived")]
    public async Task Every_state_a_sequence_can_be_in_is_answered_in_the_vocabulary_the_contract_publishes(
        string state,
        bool archived,
        string expected)
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", state, archived);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        // Reply says `new | active | paused` and carries `isArchived` beside it, so being archived is a boolean
        // rather than a state and decides first. The word this asserts is the output schema's, never Reply's:
        // `active` becomes `live` here, and the word `active` appears in no answer at all.
        Assert.Equal(expected, answer["campaign"]!["status"]!.GetValue<string>());
        Assert.NotEqual("active", answer["campaign"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Replys_own_words_travel_untranslated_under_vendor()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active", archived: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        // Two fields of Reply's collapse into one word here, so both have to survive somewhere: `archived` is
        // the neutral answer, and the state the sequence would be in if it were not archived is not lost.
        // `health` has no word in this vocabulary at all, which is the other reason the bag exists.
        Assert.Equal("archived", answer["campaign"]!["status"]!.GetValue<string>());
        Assert.Equal("active", answer["vendor"]!["status"]!.GetValue<string>());
        Assert.True(answer["vendor"]!["is_archived"]!.GetValue<bool>());
        Assert.Equal("good", answer["vendor"]!["health"]!.GetValue<string>());
    }

    [Fact]
    public async Task No_contact_is_paged_to_put_a_number_in_counts()
    {
        // An empty `counts` is a real answer: it says the provider reports none, which is not the same as
        // reporting zero, and a caller plans differently on each. A Reply sequence carries no people counts at
        // all — the one count endpoint in the published description still says it is coming — so this package
        // answers the empty object unconditionally, and that half of it cannot fail while it does.
        //
        // What can fail, and is the reason this test exists, is the other half: synthesising a number would mean
        // paging every contact in the sequence inside a sixty-second budget, and nothing here does. One call is
        // made, and it is the sequence read.
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        Assert.Empty(answer["campaign"]!["counts"]!.AsObject());
        AssertTheSequenceWasRead(account);
    }

    [Fact]
    public async Task A_name_longer_than_the_contract_allows_is_answered_rather_than_refused()
    {
        // The contract allows five hundred characters and Reply publishes no limit at all, so the two can
        // disagree about a real sequence. Passing a longer name straight through would make the whole answer
        // result_invalid — final, never repeated — and cost an operator the read of that campaign for good,
        // over a display string. The neutral half carries what it can hold; the provider's own name survives
        // beside it, whole, which is what `vendor` is for.
        var long_name = new string('a', 640);
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, long_name, "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        // Succeeded holds the answer to the output schema, so a name that did not fit would fail here.
        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        Assert.Equal(500, answer["campaign"]!["name"]!.GetValue<string>().Length);
        Assert.Equal(long_name, answer["vendor"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_name_cut_at_the_limit_is_cut_between_characters_and_never_through_one()
    {
        // A name is counted in UTF-16 units and an emoji is two of them, so a cut at the five-hundredth unit
        // can land inside one character. Half a character cannot be written as JSON: the answer would not be a
        // shortened name, it would be a protocol failure, and the operator would lose the read of that campaign
        // over a character nobody chose to put there. The name is built here rather than written out, so the
        // pair really does straddle the limit.
        var straddles = new string('a', 499) + char.ConvertFromUtf32(0x1F600) + " and then some more of it";
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, straddles, "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        // One unit short of the limit, because the character at it was not whole, and no lone half left behind.
        var name = answer["campaign"]!["name"]!.GetValue<string>();
        Assert.Equal(499, name.Length);
        Assert.DoesNotContain(name, character => char.IsSurrogate(character));
        Assert.Equal(straddles, answer["vendor"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_name_that_fits_is_not_repeated_beside_itself()
    {
        // The provider's name travels under `vendor` only where the two halves really differ. Repeating it
        // unchanged would say they disagree when they agree.
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        Assert.Equal("Q3 outbound", answer["campaign"]!["name"]!.GetValue<string>());
        Assert.Null(answer["vendor"]!["name"]);
    }

    // -------------------------------------------------------------------------------------------------------
    // The failure rows this operation declares, and only those
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_identifier_that_is_not_a_reply_number_is_refused_without_a_call_being_made()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "cmp_42"), Ct);

        // Reply identifies a sequence with a positive integer. A value that is not one names no sequence this
        // account could hold under any circumstance, so it is the permanent absence it is — and there is
        // nothing to ask the provider about, which is why no call is made at all.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("campaign_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_credential_that_may_not_read_campaigns_fails_permanently()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        account.Refuses("sequence get", "access.denied", status: 401);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "7"), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("unauthorized", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal(401, failed.Error.Details!["status"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_read_the_provider_asked_to_be_made_later_is_transient_rather_than_final()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        account.Refuses("sequence get", "rate_limited", status: 429, retryAfter: 30);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "7"), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("rate_limited", failed.Error.Code);
        Assert.Equal(FailureClass.Transient, failed.Error.Class);
        Assert.Equal(30, (int)failed.Error.Details!["retry_after"]!);
    }

    [Fact]
    public async Task A_failure_on_the_providers_own_side_is_transient_because_this_call_is_a_read()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        account.Refuses("sequence get", "reply.unavailable", status: 503);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "7"), Ct);

        // The read/write mark in the package's own table is what decides this. The same ending on a write would
        // be `provider_answer_lost` and ambiguous, because the provider may already have acted; a read that
        // failed changed nothing and cost nothing, so it is simply asked again.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_unavailable", failed.Error.Code);
        Assert.Equal(FailureClass.Transient, failed.Error.Class);
    }

    [Fact]
    public async Task A_failure_after_the_sequence_was_resolved_leaves_the_link_behind_just_as_a_success_does()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        account.Refuses("sequence get", "rate_limited", status: 429, retryAfter: 30);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var refused = await InvokeAsync(api, Input(supplied: "7"), Ct);
        var read = await InvokeAsync(api, Input(supplied: "7"), Ct);

        // A failure's identifiers go on `error.external_ids` and nowhere else — a top-level `external_ids`
        // beside a failure is accepted by the validator and then silently dropped. So the link the planner
        // supplied survives the attempt that could not confirm it, and the attempt that could says the same
        // thing in the place a success says it.
        var failed = Assert.IsType<InvocationOutcome.Failed>(refused.Outcome);
        Assert.Equal("7", failed.Error.ExternalIds!["campaign"]!.GetValue<string>());

        Succeeded(read);
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(read.Outcome);
        Assert.Equal("7", outcome.ExternalIds!["campaign"]!.GetValue<string>());
    }

    /// <summary>reply-cli's verdict is its one error line on stderr; Node may print a warning before it or after it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_cli_s_verdict_is_found_among_other_lines_on_stderr(bool warningAfter)
    {
        const string Warning = "(node:4242) ExperimentalWarning: something unrelated";
        const string Verdict = """{"error":{"code":"sequence.not_found","title":"This account has no sequence 7.","status":404}}""";
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence get", 1, string.Empty, warningAfter ? Verdict + "\n" + Warning : Warning + "\n" + Verdict);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(supplied: "7"), Ct));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.Null(error.ExternalIds);
    }

    /// <summary>An exit the CLI did not explain — a crash and its stack trace — is a read that did not answer: asked again.</summary>
    [Fact]
    public async Task An_exit_the_cli_did_not_explain_is_transient_on_a_read()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("sequence get", 1, string.Empty, "TypeError: Cannot read properties of undefined\n    at main (index.js:1:1)");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(supplied: "7"), Ct));

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.Equal(1, (int)error.Details!["exit_code"]!);
    }

    // -------------------------------------------------------------------------------------------------------
    // What the call log has to show
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_whole_operation_is_one_read_of_the_sequence_and_nothing_else()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 outbound", "active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(supplied: "7"), Ct));

        // The whole command line, read out of the stand-in's own log rather than inferred: the global prefix the
        // package promises, the command and the sequence — and nothing on stdin, because a read carries no person. A
        // route with no binding names neither a profile nor a team.
        var call = Assert.Single(account.Calls);
        Assert.Equal(["--json", "-q", "sequence", "get", "7"], call.Args);
        Assert.Null(call.Body);
    }

    // -------------------------------------------------------------------------------------------------------
    // Composing the input, and checking both halves against the published document
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The canonical input: a campaign is named either by the identifier Jason already holds for it or, the
    /// first time, by one the planner supplied.
    /// </summary>
    private static JsonObject Input(string? supplied = null, string? pinned = null) => new()
    {
        ["args"] = supplied is null
            ? new JsonObject()
            : new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = supplied } },
        ["campaign"] = new JsonObject
        {
            ["id"] = CampaignId,
            ["name"] = "Q3 outbound",
            ["status"] = "draft",
            ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
        },
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

    /// <summary>One call and one only: the sequence this campaign is, read by its identifier.</summary>
    private static void AssertTheSequenceWasRead(ReplyAccount account)
    {
        var call = Assert.Single(account.Calls);
        Assert.Equal($"sequence get {Sequence}", call.Line);
        Assert.Null(call.Body);
    }

    private static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        JsonObject input,
        CancellationToken kill)
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
                CorrelationId: "att_01K0REPLYCAMPAIGNGET",
                AttemptId: "att_01K0REPLYCAMPAIGNGET",
                AttemptNumber: 1,
                WorkItemId: WorkItemId,
                CampaignId: CampaignId),
            kill);
    }
}
