using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>campaign.enroll</c> as the official package performs it: the real plugin JavaScript, loaded by a real
/// plugin-host child, driving the stand-in vendor CLI against an account a test planted. One test per entry of
/// the document's own <c>conformance</c> list, and then the decisions this provider forces — an enrollment into a
/// live sequence is a send, so the two declared recovery reads are proven by the ordered call log rather than by
/// the state left behind; a Reply sequence carries no step ordering at all, so a step position it cannot answer
/// is refused rather than guessed; and <c>immediately</c> and <c>next_open_window</c> are the same request here,
/// because Reply always sends inside the sequence's own schedule.
/// </summary>
/// <remarks>
/// The class writes a process-wide variable — the one the CLI finds its account through — so it belongs to the
/// collection that never runs two such classes at once. Every fixture starts through
/// <see cref="ReplyPlugins.StartAsync"/>, which holds the resolved program to the test tree before anything
/// runs: a resolution that escaped would not be a wrong answer, it would be a call to somebody's real account.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyEnrollTests
{
    private const string Operation = "campaign.enroll";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG";

    /// <summary>The sequence every test plants, by an identifier of the shape Reply actually issues.</summary>
    private const int Sequence = 7;

    /// <summary>A second sequence, for the participations this call is not allowed to disturb.</summary>
    private const int Elsewhere = 8;

    /// <summary>The identifier Reply gives the first contact an account creates.</summary>
    private const string Ensured = "1001";

    private const string Address = "marta@example.com";
    private const string FirstName = "Marta";
    private const string LastName = "Alvarez";
    private const string Company = "Puerto Analytics";
    private const string Title = "Head of Growth";
    private const string TimeZone = "America/Bogota";

    private static string Bulk => $"POST /v3/sequences/{Sequence}/contact-links/bulk";

    private static string BulkPath => $"/v3/sequences/{Sequence}/contact-links/bulk";

    private static string Participation => $"GET /v3/sequences/{Sequence}/contacts/{Ensured}";

    private static string LiveState => $"GET /v3/sequences/{Sequence}";

    private static string Statuses => $"GET /v3/contacts/{Ensured}/statuses";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list, one test each
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_contact_is_worked_by_the_pin_and_never_matched_by_the_address_again()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11, 12)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // The pin is the whole of it: nothing is imported, nothing is created, and the address this person
        // carries reaches Reply nowhere at all — which is what stops two spellings of one address ever becoming
        // two people, and what keeps an enrollment from going to somebody else of the same name.
        Assert.Equal("enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal([LiveState, Statuses, Bulk], Paths(account));
        Assert.DoesNotContain(Address, Everything(account), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task An_enrollment_into_a_campaign_that_is_not_live_reports_that_it_was_not_a_send()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "paused", false, 11)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // Bookkeeping, not a send: the enrollment happened and nobody was written to. That is the difference a
        // caller cannot see from the status alone, and it is why the field is required on every answer.
        Assert.Equal("enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.False(answer["campaign_live"]!.GetValue<bool>());
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task An_enrollment_into_a_live_campaign_says_that_it_was_a_send()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // Live is two fields at Reply and one word here: running, and not archived. The pair is read from the
        // same sequence read the rest of the call is built from, so the answer is about the state the
        // enrollment met rather than one a later reader happens to find.
        Assert.True(answer["campaign_live"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_second_call_under_the_same_key_answers_already_enrolled_and_enrols_nobody_twice()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));
        var mark = account.Mark();

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        // From a mark taken after the first attempt, or the assertion would run over that attempt's own write.
        // Two reads and no write: the participation the provider already holds is the answer, and answering it
        // is what makes one crash cost one enrollment rather than two — which, in a live campaign, is one email
        // to a person rather than two.
        Assert.Equal("already_enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal([Participation, LiveState], PathsSince(account, mark));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task An_attempt_after_a_lost_answer_reads_the_prior_run_first_so_one_crash_leaves_one_enrollment()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .LosesTheAnswerAfterWriting("POST", BulkPath);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The enrollment landed and the answer did not. Nothing about the attempt can say which, so it ends
        // ambiguous and carries out both identifiers: after a lost answer they are the only trace of what was
        // done, and without the contact's one the next attempt has nobody to read the prior outcome for.
        var failed = Assert.IsType<InvocationOutcome.Failed>(lost.Outcome);
        Assert.Equal("provider_answer_lost", failed.Error.Code);
        Assert.Equal(FailureClass.Ambiguous, failed.Error.Class);
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal("7", failed.Error.ExternalIds["campaign"]!.GetValue<string>());
        Assert.Equal([1001], account.EnrolledIn(Sequence));

        var mark = account.Mark();
        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        Assert.Equal("already_enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.DoesNotContain(Bulk, PathsSince(account, mark));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task Collision_refuse_against_an_existing_participation_fails_rather_than_deciding()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured, collision: "refuse"), Ct);

        // The caller asked to be refused rather than to have the decision taken for them, and Reply says which
        // person it would not take and why inside a 200. Deciding here — enrolling again, or reporting success
        // — would be the plugin answering a question the call deliberately left to a person.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("collision_refused", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Contains(Bulk, Paths(account));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task Collision_skip_against_an_existing_participation_answers_already_enrolled()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured, collision: "skip"), Ct));

        // The same per-item word, the other policy: leave the participation alone and say so. It is a success
        // because the caller asked for exactly this outcome, and the status distinguishes it from an enrollment
        // this call made — which is what a caller counting sends has to know.
        Assert.Equal("already_enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Single(account.Enrollments);
    }

    [Fact]
    public async Task A_repeat_answers_from_the_recovery_read_whatever_the_collision_policy_asked()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured, collision: "refuse"), Ct, attempt: 2));

        // The same account and the same policy as the test above it, on a repeated attempt instead of a first
        // one — and the answer is the other one. The document's instruction is to answer from the recovery read
        // when the effect already happened, and it has to win here: Reply has no per-run ledger, so after a lost
        // answer the participation this read finds may well be the one this work item's own earlier attempt
        // made, and reporting that as a collision would call the runtime's own write somebody else's.
        Assert.Equal("already_enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.DoesNotContain(Bulk, Paths(account));
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public void A_call_missing_the_start_or_the_first_touch_is_refused_before_the_plugin_sees_it()
    {
        var contract = OperationCatalog.Find(Operation);
        Assert.NotNull(contract);

        // The reader first: a whole input validates, so a refusal below is about the member that was taken out
        // and not about a schema check that refuses everything it is shown.
        Assert.Empty(SchemaValidator.Validate(Input(pinned: Ensured), contract.InputSchema));

        // Neither `start` nor `first_touch` may be defaulted: a default here is a silent decision about what a
        // real person receives and when. Both are `required`, so the composition check refuses the call at
        // `workitem.create` and again at the claim — the plugin is never reached, and there is nothing for it
        // to be careful about.
        foreach (var absent in new[] { "start", "first_touch" })
        {
            var input = Input(pinned: Ensured);
            input["args"]!.AsObject().Remove(absent);
            Assert.NotEmpty(SchemaValidator.Validate(input, contract.InputSchema));
        }

        // And `step` without a number is refused the same way, which is the third thing the page says about
        // these two fields.
        var numberless = Input(pinned: Ensured, start: new JsonObject { ["position"] = "step" });
        Assert.NotEmpty(SchemaValidator.Validate(numberless, contract.InputSchema));
    }

    // -------------------------------------------------------------------------------------------------------
    // The two recovery reads, in the document's order, and what "before the write" means with and without a pin
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Both_recovery_reads_happen_in_the_documents_order_before_the_enrollment_is_written()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .Answers("POST", BulkPath, 503, Refusal("server.unavailable", 503).ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        // The first attempt is told the provider failed on its own side. Nothing was enrolled, but no answer
        // came back either, so the ending is ambiguous and the next attempt is the one under obligation.
        var lost = await InvokeAsync(api, Input(pinned: Ensured), Ct);
        Assert.Equal("provider_answer_lost", Assert.IsType<InvocationOutcome.Failed>(lost.Outcome).Error.Code);
        Assert.Empty(account.EnrolledIn(Sequence));

        // From a mark taken after that attempt: over the whole lifetime the assertion would pick up attempt
        // one's own calls, and its POST alone would make the last line below false.
        var mark = account.Mark();
        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        // The contract names two reads and an order: the prior run's outcome for this person, then the
        // campaign's live state. The call log is the evidence — a plugin that wrote blind would leave exactly
        // the same state behind, so the state proves nothing and the order proves everything.
        var since = PathsSince(account, mark).ToList();
        Assert.Equal([Participation, LiveState], since.Take(2));
        Assert.True(since.IndexOf(Bulk) > 1, $"the enrollment was written at position {since.IndexOf(Bulk)} of [{string.Join(", ", since)}].");
        Assert.Equal("enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task With_no_pin_the_contact_is_ensured_first_and_both_reads_still_precede_the_enrollment()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .Answers("POST", BulkPath, 503, Refusal("server.unavailable", 503).ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = await InvokeAsync(api, Input(), Ct);
        Assert.Equal("provider_answer_lost", Assert.IsType<InvocationOutcome.Failed>(lost.Outcome).Error.Code);

        var mark = account.Mark();
        var answer = Succeeded(await InvokeAsync(api, Input(), Ct, attempt: 2));

        // Without a pin the participation cannot be read at all until the person has an identifier, so
        // ensure-contact comes first — and it is itself a write by this package's own table. The claim the
        // package makes is therefore that both recovery reads precede the *enrollment*, never that they precede
        // anything being written, which in this order would be plainly false.
        Assert.Equal(["POST /v3/contacts/import", Participation, LiveState, Statuses, Bulk], PathsSince(account, mark));
        Assert.Equal("enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal([1001], account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task A_person_who_opted_out_after_the_enrollment_landed_is_not_told_it_failed()
    {
        // The document says to answer from the recovery read when the effect already happened, so that reading
        // comes before the opt-out check on a repeated attempt. A person can opt out between two attempts, and
        // reporting `suppressed` for an enrollment that already landed would say the send did not happen when
        // it did — sending an operator after an item that is finished.
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName, optedOut: true)
            .WithEnrollment(Sequence, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct, attempt: 2));

        Assert.Equal("already_enrolled", answer["items"]![0]!["status"]!.GetValue<string>());
        Assert.Equal([Participation, LiveState], Paths(account));
    }

    // -------------------------------------------------------------------------------------------------------
    // The request this provider is actually sent
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Nobody_is_removed_from_another_sequence_by_an_enrollment_that_asked_for_nothing_of_the_kind()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithSequence(Elsewhere, "Nurture", "active", false, 21)
            .WithContact(1001, Address, FirstName)
            .WithEnrollment(Elsewhere, 1001);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // Reply's own switch would take this person out of every other sequence they are in. Nothing in the
        // contract asked for that, and doing it silently would be an external effect nobody requested — so the
        // member is written out as `false` rather than left off, and the participation elsewhere survives.
        var body = JsonNode.Parse(Assert.Single(account.Calls, call => call.Path == BulkPath).Body!)!.AsObject();
        Assert.False(body["removeFromExisting"]!.GetValue<bool>());
        Assert.Equal([1001], account.EnrolledIn(Elsewhere));
    }

    [Fact]
    public async Task Immediately_and_the_next_open_window_are_one_request_because_reply_cannot_bypass_a_window()
    {
        var bodies = new List<JsonObject>();
        foreach (var timing in new[] { "immediately", "next_open_window", "authored_delay" })
        {
            using var account = new ReplyAccount();
            account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
                .WithContact(1001, Address, FirstName);
            using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
            await using var api = await ReplyPlugins.StartAsync(Ct);

            Succeeded(await InvokeAsync(api, Input(pinned: Ensured, firstTouch: timing), Ct));
            bodies.Add(JsonNode.Parse(Assert.Single(account.Calls, call => call.Path == BulkPath).Body!)!.AsObject());
        }

        // Reply always sends inside the sequence's own schedule and has no way to bypass a sending window, so
        // the two timings that ask for one are the same request here. Pretending otherwise would be the plugin
        // claiming a precision the provider does not have; the package says they are the same instead.
        Assert.Equal(bodies[0].ToJsonString(), bodies[1].ToJsonString());
        Assert.True(bodies[0]["ignoreStepDelay"]!.GetValue<bool>());

        // And the one timing Reply can honour is honoured: the step's authored delay is left in place.
        Assert.False(bodies[2]["ignoreStepDelay"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_step_position_is_resolved_by_walking_the_chain_a_sequence_actually_has()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11, 12, 13)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(pinned: Ensured, start: Step(3)), Ct));

        // A Reply step carries no ordering field whatever — the `parentId` graph is the only order there is —
        // so the third step is the third link of the chain from the step nobody is the parent of, and never the
        // third element of whatever order the array happened to arrive in.
        var body = JsonNode.Parse(Assert.Single(account.Calls, call => call.Path == BulkPath).Body!)!.AsObject();
        Assert.Equal(13, body["startStepId"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_first_step_is_asked_for_by_saying_nothing_about_which_step_to_start_at()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11, 12)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        Succeeded(await InvokeAsync(api, Input(pinned: Ensured), Ct));

        // `first_step` is Reply's own default, so it is asked for by leaving the member out rather than by
        // naming the step the chain walk would have found: the two can differ, and the provider's own idea of
        // where its sequence begins is the one that should win.
        var body = JsonNode.Parse(Assert.Single(account.Calls, call => call.Path == BulkPath).Body!)!.AsObject();
        Assert.DoesNotContain("startStepId", body.Select(member => member.Key));
    }

    [Fact]
    public async Task A_step_position_a_branching_sequence_cannot_answer_is_refused_rather_than_guessed()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithSteps(Sequence, (11, null, "email"), (12, 11, "condition"), (13, 12, "email"))
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured, start: Step(3)), Ct);

        // Reply numbers a branch "2A" and gives a step no ordering field at all, so "the third step" has no
        // sound meaning once a condition step is in the way. Picking the third element of the array would be a
        // guess that enrolled a person at the wrong step — and in a live campaign that is the wrong email.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_call_failed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Contains("condition", failed.Error.Details!["reason"]!.GetValue<string>(), StringComparison.Ordinal);

        // The sequence was read and nothing else happened: the call could not be built, so it was not made.
        Assert.Equal([LiveState], Paths(account));
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task A_step_with_two_ways_on_from_it_names_no_single_next_step_and_is_refused()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithSteps(Sequence, (11, null, "email"), (12, 11, "email"), (13, 11, "email"))
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured, start: Step(2)), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_call_failed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal([LiveState], Paths(account));
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task A_step_position_past_the_end_of_the_chain_is_refused_rather_than_rounded_down()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11, 12)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured, start: Step(4)), Ct);

        // Starting at the last step instead would enrol a person at a step nobody asked for, and starting at
        // the first would be worse: the call names a position this sequence does not have, and saying so is the
        // only answer that does not act on a guess.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_call_failed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal([LiveState], Paths(account));
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    // -------------------------------------------------------------------------------------------------------
    // The per-item words Reply answers with, and the open half of that set
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("contactNotFound", "contact_not_found", "permanent")]
    [InlineData("contactLimitExceeded", "limit_reached", "permanent")]
    [InlineData("forbidden", "unauthorized", "permanent")]
    [InlineData("invalidInput", "provider_call_failed", "permanent")]
    public async Task A_per_item_word_the_published_table_names_is_reported_in_the_contracts_own_vocabulary(
        string slug,
        string code,
        string failureClass)
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .Answers("POST", BulkPath, 200, NotProcessed(slug).ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // A 200 that refuses one person inside it: the status says the call was taken, not that the work was
        // done. Unlike the list add — whose per-item value its own documentation describes two incompatible
        // ways — this table is published, so the word is read and mapped rather than merely reported.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal(code, failed.Error.Code);
        Assert.Equal(Enum.Parse<FailureClass>(failureClass, ignoreCase: true), failed.Error.Class);
        Assert.Contains(slug, failed.Error.Details!["provider_item"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_per_item_word_the_published_table_does_not_name_is_reported_rather_than_guessed_at()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .Answers("POST", BulkPath, 200, NotProcessed("sequenceHasNoSendingAccount").ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The type behind these words is named in Reply's description and defined nowhere, and the table that
        // lists them is labelled "Common" — so it is an open set and there will be words like this one. A
        // refusal nobody published is reported as itself, with Reply's own word in the details for whoever has
        // to act on it, rather than mapped to whichever known row looks nearest.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_call_failed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Contains("sequenceHasNoSendingAccount", failed.Error.Details!["provider_item"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    // -------------------------------------------------------------------------------------------------------
    // The failure rows this operation declares, and only those
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_sequence_this_account_does_not_hold_fails_permanently_and_pins_nothing()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("campaign_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("sequence.notFound", failed.Error.Details!["provider_code"]!.GetValue<string>());

        // The campaign identifier does not travel out: the provider has just said it holds nothing under it, so
        // recording a link it denies would be worse than recording none. What was learned about the person
        // still does, because that much the provider did not deny.
        Assert.Null(failed.Error.ExternalIds?["campaign"]);
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_archived_sequence_will_take_no_enrollment_and_is_not_asked_to()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "paused", true, 11)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The live-state read already says so, so the enrollment is never attempted: un-archiving the sequence
        // at Reply is what makes this work item runnable, and no number of attempts will do it.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("campaign_not_enrollable", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal([LiveState], Paths(account));
    }

    [Fact]
    public async Task A_sequence_with_no_steps_to_send_will_take_no_enrollment_either()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .Answers("POST", BulkPath, 400, Refusal("sequenceContact.noStepsInSequence").ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // A sequence with nothing to send is not a sequence a person can be put into, whichever call says so.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("campaign_not_enrollable", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task An_address_the_provider_suppresses_fails_permanently_rather_than_being_enrolled()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName, optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // An enrollment into a live campaign is a send, so the person who asked not to be contacted must not
        // reach one. The provider knows what the runtime cannot, and the plugin's part is to recognise the
        // refusal and report it — with both identifiers, because both were learned before it was met.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("suppressed", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal(Ensured, failed.Error.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal("7", failed.Error.ExternalIds["campaign"]!.GetValue<string>());
        Assert.Equal([LiveState, Statuses], Paths(account));
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task A_pin_that_no_longer_resolves_is_reported_rather_than_the_person_being_matched_again()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "4242"), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("contact_not_found", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("contact.notFound", failed.Error.Details!["provider_code"]!.GetValue<string>());
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    [Fact]
    public async Task A_failure_on_the_providers_own_side_during_the_enrollment_is_ambiguous_because_it_is_a_write()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .Answers("POST", BulkPath, 503, Refusal("server.unavailable", 503).ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The read/write mark in the package's own table is what decides this. The same ending on the live-state
        // read is transient and simply asked again; here the enrollment may have landed before Reply's own side
        // failed, and into a live campaign that is an email already sent.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_answer_lost", failed.Error.Code);
        Assert.Equal(FailureClass.Ambiguous, failed.Error.Class);
    }

    [Fact]
    public async Task A_failure_on_the_providers_own_side_during_the_live_state_read_is_only_transient()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName)
            .Answers("GET", $"/v3/sequences/{Sequence}", 503, Refusal("server.unavailable", 503).ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);

        // The other side of the same mark: nothing was written, and asking again costs nothing, so the attempt
        // is simply repeated rather than stopping a person's work item for a human to look at.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("provider_unavailable", failed.Error.Code);
        Assert.Equal(FailureClass.Transient, failed.Error.Class);
        Assert.Empty(account.EnrolledIn(Sequence));
    }

    // -------------------------------------------------------------------------------------------------------
    // The answer, and what may never appear on a command line
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_result_carries_one_item_the_live_state_and_nothing_else()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(1001, Address, FirstName);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: Ensured), Ct);
        var answer = Succeeded(result);

        // This operation's output schema is closed, so unlike `campaign.get` there is no `vendor` bag for
        // anything of Reply's to ride along in: one item naming the contact the call was given, Reply's own
        // identifier beside it, and the live state. The pins travel on the outcome envelope, which is what the
        // runtime records.
        Assert.Equal(["campaign_live", "items"], answer.Select(member => member.Key).Order());
        var item = Assert.Single(answer["items"]!.AsArray())!.AsObject();
        Assert.Equal(["contact_id", "external_ids", "status"], item.Select(member => member.Key).Order());
        Assert.Equal(ContactId, item["contact_id"]!.GetValue<string>());
        Assert.Equal(Ensured, item["external_ids"]!["contact"]!.GetValue<string>());

        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal(Ensured, outcome.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal("7", outcome.ExternalIds["campaign"]!.GetValue<string>());
    }

    [Fact]
    public async Task No_call_this_operation_makes_carries_a_persons_own_data_in_an_argument()
    {
        using var account = new ReplyAccount();
        account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11);
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
    /// The canonical input: one person, one sequence, one key, and the three decisions this operation refuses to
    /// take for the caller. The pin is present once the runtime has recorded one, which is the difference
    /// between the two ensure paths — and the one that decides where the recovery reads can start.
    /// </summary>
    private static JsonObject Input(
        string? pinned = null,
        string collision = "skip",
        JsonObject? start = null,
        string firstTouch = "authored_delay",
        string? firstName = FirstName)
        => new()
        {
            ["args"] = new JsonObject
            {
                ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(CultureInfo.InvariantCulture) },
                ["channel"] = "email",
                ["collision"] = collision,
                ["start"] = start ?? new JsonObject { ["position"] = "first_step" },
                ["first_touch"] = firstTouch,
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

    private static JsonObject Step(int position) => new() { ["position"] = "step", ["step"] = position };

    /// <summary>A bulk enrolment that took the call and refused this one person, in the shape Reply uses.</summary>
    private static JsonObject NotProcessed(string slug) => new()
    {
        ["added"] = new JsonArray(),
        ["notProcessed"] = new JsonObject
        {
            [Ensured] = new JsonObject
            {
                ["error"] = slug,
                ["errorDetails"] = "The stand-in was told to answer this.",
            },
        },
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
                CorrelationId: "att_01K0REPLYCAMPAIGNENROLL",
                AttemptId: "att_01K0REPLYCAMPAIGNENROLL",
                AttemptNumber: attempt,
                WorkItemId: WorkItemId,
                CampaignId: CampaignId),
            kill);
    }
}
