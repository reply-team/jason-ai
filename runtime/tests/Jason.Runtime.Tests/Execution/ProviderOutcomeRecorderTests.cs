using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// One answer becoming one outcome. Everything a plugin can hand back — a result, a declared failure, or nothing
/// at all because the protocol broke — ends the attempt through the same routine every other kind of work ends
/// through, and the only thing that varies is what the operation's own contract says about repeating it.
/// </summary>
public class ProviderOutcomeRecorderTests
{
    private static readonly DateTime Noon = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OperationContract Add => OperationCatalog.Find("list_membership.add")!;

    /// <summary>A published operation read under a rule none of them uses, so <c>never</c> has something to prove it.</summary>
    private static OperationContract Never => Add with { RepeatAfterAmbiguous = RepeatAfterAmbiguous.Never };

    private static JsonNode Added(string contact) => new JsonObject
    {
        ["items"] = new JsonArray(new JsonObject
        {
            ["contact_id"] = contact,
            ["status"] = "added",
            ["external_ids"] = new JsonObject { ["contact"] = "p_1001" },
        }),
    };

    [Fact]
    public async Task An_answer_the_operation_accepts_becomes_the_item_s_result_and_its_identifiers_are_pinned()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Succeeded(Added(scene.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }));

        Assert.Equal(AttemptStatus.Succeeded, scene.Attempt.Status);
        Assert.Equal(WorkItemStatus.Succeeded, scene.Item.Status);
        Assert.Equal("added", (string?)scene.Item.Result!["items"]![0]!["status"]);

        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("p_1001", pin.Value);
        Assert.Equal(scene.Attempt.PublicId, pin.RecordedByAttemptId);

        // What the plugin answered with, kept on the attempt beside what ran, exactly as it returned it.
        var provenance = await scene.ProvenanceAsync(db);
        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p_1001" }, provenance.ExternalIdsReturned);
        Assert.Null(provenance.RejectedResult);
    }

    /// <summary>
    /// A2. A shape error is deterministic: the next attempt would run the same code over the same answer, so it
    /// can only spend budget while a manager waits. The operation's own rule does not get a vote, which is why
    /// the contract under test here is the one that would otherwise allow the repeat.
    /// </summary>
    [Fact]
    public async Task A_result_the_operation_refuses_is_final_whatever_the_operation_s_rule_says()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var refused = JsonNode.Parse("""{"items":[{"contact_id":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","status":"joined"}]}""")!;

        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, Add.RepeatAfterAmbiguous);
        await scene.RecordAsync(db, Add, Succeeded(refused, null));

        Assert.Equal(AttemptStatus.Failed, scene.Attempt.Status);
        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        var error = scene.Attempt.Error!;
        Assert.Equal(AttemptErrors.ResultInvalid, error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.False(error.Retriable);

        // PA6: the pointers say where, in the words the validator used; the answer itself is too big for a list
        // of details, so it is kept where a document belongs — on the attempt's provenance.
        var detail = Assert.Single(error.Details!);
        Assert.Equal("/items/0/status", detail.Field);
        Assert.Equal("enum", detail.Code);

        var provenance = await scene.ProvenanceAsync(db);
        Assert.Equal(refused.ToJsonString(), provenance.RejectedResult!.ToJsonString());
    }

    /// <summary>
    /// The pin is Jason's record; the answer is the plugin's. When they disagree the disagreement is written
    /// down beside the pin — and the work still happened, so the attempt is still a success. Failing it would
    /// throw away work really done at the provider, and deciding which value is right is reconciliation, which
    /// this version deliberately does not have.
    /// </summary>
    [Fact]
    public async Task A_disagreement_about_an_identifier_does_not_cost_the_attempt_its_success()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var first = await SeedAsync(db);
        await first.RecordAsync(db, Add, Succeeded(Added(first.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }));

        var second = await SeedAsync(db, after: first);
        await second.RecordAsync(db, Add, Succeeded(Added(second.Contact.PublicId), new JsonObject { ["contact"] = "p_2002" }));

        Assert.Equal(AttemptStatus.Succeeded, second.Attempt.Status);
        Assert.Equal(WorkItemStatus.Succeeded, second.Item.Status);
        Assert.Null(second.Attempt.Error);

        var pin = await db.ExternalIds.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("p_1001", pin.Value);
        Assert.Equal("p_2002", pin.DivergedValue);
        Assert.Equal(second.Attempt.PublicId, pin.DivergedByAttemptId);
    }

    [Fact]
    public async Task An_identifier_of_a_kind_the_operation_never_declared_refuses_the_whole_answer()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(
            db,
            Add,
            Succeeded(Added(scene.Contact.PublicId), new JsonObject { ["contact"] = "p_1001", ["membership"] = "m_5512" }));

        Assert.Equal(AttemptErrors.ResultInvalid, scene.Attempt.Error!.Code);
        Assert.Equal("/external_ids/membership", Assert.Single(scene.Attempt.Error.Details!).Field);
        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));

        // The same answer as any other shape error, asserted rather than inherited from a shared code path:
        // ambiguous, and final however generously the operation's own rule reads. `list_membership.add` says
        // after_recovery_read, so a rule-driven answer here would have been a repeat.
        Assert.Equal(FailureClass.Ambiguous, scene.Attempt.Error.Class);
        Assert.False(scene.Attempt.Error.Retriable);
        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);

        // And the answer itself is kept, for the same reason it is kept when the result is the wrong shape:
        // the author has to see what was actually sent. What is kept is the result the plugin returned — the
        // identifiers it refused travel as the pointer above, not as part of the document.
        var provenance = await scene.ProvenanceAsync(db);
        Assert.Contains(scene.Contact.PublicId, provenance.RejectedResult!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The plugin's own verdict, in the plugin's own vocabulary, under the class the runtime reads — and the
    /// repeat decided by the contract rather than by the code table, which knows nothing of operations.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Transient, true, WorkItemStatus.Created)]
    [InlineData(FailureClass.Permanent, false, WorkItemStatus.Failed)]
    [InlineData(FailureClass.Validation, false, WorkItemStatus.Failed)]
    [InlineData(FailureClass.Ambiguous, true, WorkItemStatus.Created)]
    public async Task A_declared_failure_carries_its_class_and_the_operation_decides_the_repeat(
        FailureClass failureClass,
        bool retriable,
        WorkItemStatus expected)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(failureClass, "provider_answer_lost", "no answer", null, null)));

        Assert.Equal(AttemptStatus.Failed, scene.Attempt.Status);
        Assert.Equal(expected, scene.Item.Status);
        Assert.Equal("provider_answer_lost", scene.Attempt.Error!.Code);
        Assert.Equal(failureClass, scene.Attempt.Error.Class);
        Assert.Equal(retriable, scene.Attempt.Error.Retriable);
    }

    /// <summary>
    /// The same ambiguous failure under an operation that may never be repeated: the item waits for a person
    /// instead of being handed out again, which is the whole reason a contract states the rule.
    /// </summary>
    [Fact]
    public async Task An_ambiguous_failure_of_an_operation_that_may_never_be_repeated_waits_for_a_person()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Never, Failed(new OutcomeError(FailureClass.Ambiguous, "provider_answer_lost", "no answer", null, null)));

        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        Assert.False(scene.Item.LastError!.Retriable);
        Assert.Equal(FailureClass.Ambiguous, scene.Item.LastError.Class);
    }

    /// <summary>
    /// N13. The answer-lost case is where a pin matters most: the effect happened, the answer did not come back,
    /// and the identifier the plugin managed to return is the only trace of what was done.
    /// </summary>
    [Fact]
    public async Task An_identifier_returned_with_a_failure_is_pinned_because_that_is_where_it_matters_most()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var externalIds = new JsonObject { ["contact"] = "p_1001" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Ambiguous, "provider_answer_lost", "no answer", null, externalIds)));

        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("p_1001", pin.Value);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p_1001" },
            (await scene.ProvenanceAsync(db)).ExternalIdsReturned);
    }

    /// <summary>
    /// The asymmetry with the success path, and it is deliberate: replacing the failure the plugin reported with
    /// a shape error would hide the reason the operation failed. The undeclared pin is refused, not recorded,
    /// and the attempt says so beside the failure it kept.
    /// </summary>
    [Fact]
    public async Task An_undeclared_identifier_on_a_failure_refuses_the_pin_and_keeps_the_failure()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var externalIds = new JsonObject { ["contact"] = "p_1001", ["membership"] = "m_5512" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Permanent, "list_not_found", "no such list", null, externalIds)));

        Assert.Equal("list_not_found", scene.Attempt.Error!.Code);
        Assert.Equal(FailureClass.Permanent, scene.Attempt.Error.Class);

        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("contact", pin.Kind);

        var detail = Assert.Single(scene.Attempt.Error.Details!);
        Assert.Equal("/error/external_ids/membership", detail.Field);
        Assert.Equal("additional_properties", detail.Code);
    }

    /// <summary>
    /// What the attempt kept says it kept the identifiers "as it returned them", and an undeclared kind is the
    /// one case where that matters: nothing else in Jason will ever mention that key again. A provider that
    /// spells its kind `contactId` has to be readable beside the pointer that refuses it, which names that same
    /// spelling — so the key is written down as the plugin wrote it, not as Jason spells its own fields.
    /// </summary>
    [Fact]
    public async Task The_key_a_plugin_returned_is_recorded_exactly_as_it_spelled_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var externalIds = new JsonObject { ["contactId"] = "p_1001" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Permanent, "list_not_found", "no such list", null, externalIds)));

        var returned = Assert.Single((await scene.ProvenanceAsync(db)).ExternalIdsReturned!);
        Assert.Equal("contactId", returned.Key);
        Assert.Equal("p_1001", returned.Value);
        Assert.Equal("/error/external_ids/contactId", Assert.Single(scene.Attempt.Error!.Details!).Field);
    }

    /// <summary>
    /// The rejected answer is evidence, and evidence is bounded: an attempt row is not where an unbounded
    /// document belongs, so past the setting that bounds what the invoker reads back it is replaced by a note
    /// saying so rather than by a truncation nothing could parse.
    /// </summary>
    [Fact]
    public async Task A_rejected_answer_too_large_to_keep_is_replaced_by_a_note_saying_so()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, invokerOutcomeBytes: 64);

        await scene.RecordAsync(db, Add, Succeeded(new JsonObject { ["items"] = new string('x', 200) }, null));

        var rejected = (await scene.ProvenanceAsync(db)).RejectedResult!.AsObject();
        Assert.Equal(64, (int)rejected["limit"]!);
        Assert.True((int)rejected["bytes"]! > 64);
        Assert.Contains("Plugins:Invoker:OutcomeBytes", (string?)rejected["omitted"], StringComparison.Ordinal);
    }

    /// <summary>
    /// A null inside the rejected answer is a value the plugin wrote, and it has to survive being written down.
    /// The record is completed by merging into what the claim wrote, and a merge patch reads a null as "remove
    /// this key" — so an answer full of nulls would come back as evidence of something nobody sent.
    /// </summary>
    [Fact]
    public async Task A_null_inside_a_rejected_answer_survives_being_written_down()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var refused = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["contact_id"] = null, ["status"] = "added" }),
            ["note"] = null,
        };

        await scene.RecordAsync(db, Add, Succeeded(refused, null));

        Assert.Equal(AttemptErrors.ResultInvalid, scene.Attempt.Error!.Code);
        Assert.Equal(refused.ToJsonString(), (await scene.ProvenanceAsync(db)).RejectedResult!.ToJsonString());
    }

    /// <summary>
    /// A1's first end. A protocol failure is not the plugin's answer — the runtime never got one — so its class
    /// comes from how far the invocation got, and the repeat from the operation's rule all the same.
    /// </summary>
    [Theory]
    [InlineData(ProtocolCodes.PluginTimeout, FailureClass.Ambiguous, true)]
    [InlineData(ProtocolCodes.PluginLaunchFailed, FailureClass.Transient, true)]
    [InlineData(ProtocolCodes.PluginNotLoaded, FailureClass.Permanent, false)]
    public async Task A_protocol_failure_is_classified_by_how_far_it_got_and_repeated_by_the_operation(
        string code,
        FailureClass expected,
        bool retriable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, new PluginInvocationResult(
            new InvocationOutcome.ProtocolFailure(code, "the child did not answer", 4, "tail"),
            Provenance,
            Launch: null));

        Assert.Equal(code, scene.Attempt.Error!.Code);
        Assert.Equal(expected, scene.Attempt.Error.Class);
        Assert.Equal(retriable, scene.Attempt.Error.Retriable);
        Assert.Equal("tail", scene.Attempt.Error.Trace);
    }

    [Fact]
    public async Task A_timeout_of_an_operation_that_may_never_be_repeated_ends_the_item()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Never, new PluginInvocationResult(
            new InvocationOutcome.ProtocolFailure(ProtocolCodes.PluginTimeout, "the budget ran out", null, null),
            Provenance,
            Launch: null));

        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        Assert.False(scene.Item.LastError!.Retriable);
    }

    private static readonly InvocationProvenance Provenance = new(
        TestPlugins.FakeProviderId,
        "1.0.0",
        "sha256:0",
        PluginProtocol.CurrentVersion,
        PluginProtocol.OperationContractVersion,
        PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
        "att_01K5B7Q2WE5X3M9T0YH4C6RDNB",
        PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC");

    private static PluginInvocationResult Succeeded(JsonNode? result, JsonObject? externalIds) => new(
        new InvocationOutcome.Succeeded(result, externalIds, new OutcomeDiagnostics(12, 2, 0, 1)),
        Provenance,
        Launch: null);

    private static PluginInvocationResult Failed(OutcomeError error) => new(
        new InvocationOutcome.Failed(error, new OutcomeDiagnostics(12, 1, 0, 1)),
        Provenance,
        Launch: null);

    /// <summary>
    /// One claimed provider item, mid-run: the campaign, the person, and the attempt that is out at the plugin.
    /// The claim wrote the provenance, so the completion this task adds has a record to merge into.
    /// </summary>
    /// <summary>
    /// A scene is one provider item mid-flight. Passing a previous scene reuses its campaign, contact and item
    /// and adds the next attempt to them, which is what a second answer about the same person needs.
    /// </summary>
    private static async Task<Scene> SeedAsync(JasonDbContext db, int? invokerOutcomeBytes = null, Scene? after = null)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaign = after?.Campaign ?? WorkItemFactory.NewCampaign(now: Noon);
        var contact = after?.Contact ?? new Contact { PublicId = PublicId.New("cnt"), CreatedAt = Noon, UpdatedAt = Noon };
        var item = after?.Item ?? WorkItemFactory.NewProviderOp(campaign, "list_membership.add", Noon);
        item.Contact = contact;
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, (after?.Attempt.Number ?? 0) + 1, AttemptStatus.Running, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            TestPlugins.FakeProviderId, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            "list_membership.add", 1, Provenance.SnapshotId, RouteSnapshotId, RouteScope.GlobalDefault, "sha256:1",
            CorrelationId: attempt.PublicId);

        if (after is null)
        {
            db.Campaigns.Add(campaign);
            db.Contacts.Add(contact);
            db.WorkItems.Add(item);
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        var options = TestOptions.Plugins(o => o.Invoker.OutcomeBytes = invokerOutcomeBytes ?? o.Invoker.OutcomeBytes);
        var recorder = new ProviderOutcomeRecorder(
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Dispatcher(o => o.RetryDelaySeconds = 0)),
            new ExternalIdStore(new JournalWriter(clock), clock),
            options);
        return new Scene(recorder, campaign, contact, item, attempt);
    }

    private const string RouteSnapshotId = "rts_01K5B7Q2WE5X3M9T0YH4C6RDND";

    private sealed record Scene(ProviderOutcomeRecorder Recorder, Campaign Campaign, Contact Contact, WorkItem Item, Attempt Attempt)
    {
        public async Task RecordAsync(JasonDbContext db, OperationContract contract, PluginInvocationResult result)
        {
            await Recorder.RecordAsync(db, Item, Attempt, contract, TestPlugins.FakeProviderId, result, Ct);
            await db.SaveChangesAsync(Ct);
        }

        public async Task<AttemptProvenanceDto> ProvenanceAsync(JasonDbContext db) =>
            (await db.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == Attempt.PublicId, Ct)).Provenance!;
    }
}
