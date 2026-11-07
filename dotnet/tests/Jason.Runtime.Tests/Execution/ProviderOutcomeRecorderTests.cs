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
using Jason.Runtime.Tests.Dispatch;
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

    /// <summary>
    /// The add read under <c>never</c>, so the recorder is shown reading the rule rather than the operation: everything
    /// else about the scene stays the add's, and only the repeat rule differs.
    /// </summary>
    private static OperationContract Never => Add with { RepeatAfterAmbiguous = RepeatAfterAmbiguous.Never };

    private static OperationContract Pause => OperationCatalog.Find("campaign.pause")!;

    private static OperationContract Send => OperationCatalog.Find("message.send")!;

    private static OperationContract Cancel => OperationCatalog.Find("message.cancel")!;

    private static JsonNode Taken(string contact, string? reference) => new JsonObject
    {
        ["items"] = new JsonArray(new JsonObject
        {
            ["contact_id"] = contact,
            ["status"] = "accepted",
            ["accepted_for"] = "2026-11-03T14:00:00Z",
            ["reference"] = reference,
            ["cancelable"] = true,
            ["channel"] = "email",
            ["format"] = "email",
            ["sender_external_id"] = "s_12",
            ["message"] = new JsonObject { ["external_id"] = null, ["sent_at"] = null },
            ["external_ids"] = reference is null
                ? new JsonObject { ["contact"] = "p_1001" }
                : new JsonObject { ["contact"] = "p_1001", ["send"] = reference },
        }),
    };

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
    /// The reference of the send a work item made the provider take is an identifier of the item itself: one item is one
    /// send. It is kept where the item's own record is — the attempt's provenance, as returned, and the frozen result, as
    /// answered — and pinned to no contact and no campaign, so no later call of another item receives it back.
    /// </summary>
    [Fact]
    public async Task A_send_s_reference_is_kept_on_the_attempt_and_in_the_result_and_pins_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: "message.send");

        await scene.RecordAsync(db, Send, Succeeded(Taken(scene.Contact.PublicId, "email-1001"), new JsonObject { ["contact"] = "p_1001", ["send"] = "email-1001" }));

        Assert.Equal(WorkItemStatus.Succeeded, scene.Item.Status);
        Assert.Equal("email-1001", (string?)scene.Item.Result!["items"]![0]!["reference"]);
        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(("contact", "p_1001"), (pin.Kind, pin.Value));
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p_1001", ["send"] = "email-1001" },
            (await scene.ProvenanceAsync(db)).ExternalIdsReturned);
    }

    /// <summary>
    /// A read of a send that finds it gone says so about the work item that made the send, in the change set that ends the
    /// read: the line and the read's success commit together. The send's own result stays as it was frozen.
    /// </summary>
    [Fact]
    public async Task A_read_that_finds_the_send_gone_says_so_about_the_send_as_it_succeeds()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: "message.status");
        var send = WorkItemFactory.NewProviderOp(scene.Campaign, "message.send", Noon);
        db.WorkItems.Add(send);
        await db.SaveChangesAsync(Ct);

        await scene.Recorder.RecordAsync(db, scene.Item, scene.Attempt, Status, TestPlugins.FakeProviderId, false, Succeeded(Read("sent"), null), Ct, ReadOf(send));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Succeeded, scene.Item.Status);
        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct);
        Assert.Equal((send.PublicId, send.PublicId), (line.WorkItemId, line.Key));
        Assert.Equal((TestPlugins.FakeProviderId, "email-1001", scene.Item.PublicId), ((string?)line.New!["plugin"], (string?)line.New["reference"], (string?)line.New["read_work_item"]));
        Assert.Null((await db.WorkItems.AsNoTracking().SingleAsync(item => item.Id == send.Id, Ct)).Result);
    }

    /// <summary>
    /// A read that finds the send still held, or one the provider lost track of and that may have gone, says nothing of it:
    /// the line is only ever about a send that went.
    /// </summary>
    [Theory]
    [InlineData("queued")]
    [InlineData("unknown")]
    public async Task A_read_that_finds_the_send_still_held_or_unknown_says_nothing_of_it(string state)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: "message.status");
        var send = WorkItemFactory.NewProviderOp(scene.Campaign, "message.send", Noon);
        db.WorkItems.Add(send);
        await db.SaveChangesAsync(Ct);

        await scene.Recorder.RecordAsync(db, scene.Item, scene.Attempt, Status, TestPlugins.FakeProviderId, false, Succeeded(Read(state), null), Ct, ReadOf(send));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Succeeded, scene.Item.Status);
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct));
    }

    private static OperationContract Status => OperationCatalog.Find("message.status")!;

    /// <summary>What a read of a send answers, in one of the document's words.</summary>
    private static JsonNode Read(string state) => new JsonObject
    {
        ["state"] = state,
        ["channel"] = "email",
        ["cancelable"] = state == "queued",
        ["accepted_for"] = "2026-09-15T11:55:00Z",
        ["sent_at"] = state == "sent" ? "2026-09-15T11:58:00Z" : null,
        ["message_id"] = state == "sent" ? "msg-7001" : null,
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = null,
        ["failure_code"] = null,
        ["cancelled_at"] = null,
    };

    /// <summary>The input the claim composes for a read of <paramref name="send"/>, with the reference its attempt returned.</summary>
    private static JsonObject ReadOf(WorkItem send) => new()
    {
        ["args"] = new JsonObject { ["send"] = send.PublicId },
        ["send"] = new JsonObject { ["work_item"] = send.PublicId, ["operation"] = send.Operation, ["reference"] = "email-1001" },
    };

    /// <summary>
    /// The reference is said beside the result and in it, and the two are one value. An answer that says it in one place
    /// only would leave a person reading the result and a later call working by the identifier on two different things, so
    /// it is a shape error like any other, and nothing in it is written down.
    /// </summary>
    [Fact]
    public async Task A_send_s_reference_said_in_the_result_alone_is_a_result_the_operation_refuses()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: "message.send");

        await scene.RecordAsync(db, Send, Succeeded(Taken(scene.Contact.PublicId, "email-1001"), new JsonObject { ["contact"] = "p_1001" }));

        var error = scene.Attempt.Error!;
        Assert.Equal((AttemptErrors.ResultInvalid, FailureClass.Ambiguous, false), (error.Code, error.Class, error.Retriable));
        Assert.Equal(("/external_ids/send", "const"), (Assert.Single(error.Details!).Field, error.Details![0].Code));
        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A send that lost its answer is repeated only under a package that says it settles it by the key: such a package
    /// makes every attempt's send under the work item's idempotency key, so the repeat is answered with the first send's
    /// own answer where the provider took it, and the item goes back to be claimed. Under any other package nothing
    /// says the first send carried a key, so a repeat could reach the person twice and the end is final. Either way the
    /// contact the plugin learned is still written down.
    /// </summary>
    [Theory]
    [InlineData(true, WorkItemStatus.Created)]
    // Critical: a send no package settles by its key is never repeated.
    [InlineData(false, WorkItemStatus.Failed, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W5)])]
    public async Task A_send_that_lost_its_answer_is_repeated_only_under_a_package_that_settles_it_by_the_key(bool settlesByKey, WorkItemStatus expected)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: "message.send");

        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, Send.RepeatAfterAmbiguous);
        await scene.RecordAsync(db, Send, Failed(new OutcomeError(FailureClass.Ambiguous, "provider_answer_lost", "no answer", null, new JsonObject { ["contact"] = "p_1001" })), settlesByKey);

        Assert.Equal(expected, scene.Item.Status);
        Assert.Equal((FailureClass.Ambiguous, settlesByKey), (scene.Attempt.Error!.Class, scene.Attempt.Error.Retriable));
        Assert.Equal("p_1001", Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct)).Value);
    }

    /// <summary>
    /// The marker is asked only where the contract rests the repeat on the key: an operation whose recovery read stands
    /// on its own is repeated after a lost answer under a package that lists nothing, exactly as before.
    /// </summary>
    [Theory]
    [InlineData("contact.update")]
    [InlineData("campaign.create")]
    public async Task An_operation_whose_repeat_does_not_rest_on_the_key_is_repeated_under_a_package_that_lists_nothing(string operation)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: operation);
        var contract = OperationCatalog.Find(operation)!;

        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, contract.RepeatAfterAmbiguous);
        Assert.False(OutcomeContract.SettlesByKey(contract));
        await scene.RecordAsync(db, contract, Failed(new OutcomeError(FailureClass.Ambiguous, "provider_answer_lost", "no answer", null, null)), settlesByKey: false);

        Assert.Equal(WorkItemStatus.Created, scene.Item.Status);
        Assert.Equal((FailureClass.Ambiguous, true), (scene.Attempt.Error!.Class, scene.Attempt.Error.Retriable));
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
    /// An answer arrived and a pattern of the output schema could not finish measuring it. The plugin has run and its
    /// effect may have landed, so this is a lost answer — ambiguous — and the operation's own repeat rule decides:
    /// calling it transient would run a completed write again blind, the one thing that rule exists to prevent. The
    /// answer is kept, unmeasured, on the attempt's trace for a person, and nothing in it is written down.
    /// </summary>
    [Fact]
    public async Task An_answer_a_pattern_could_not_measure_is_a_lost_answer_the_operation_s_rule_decides()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, timing: TimingOut.Always());
        var answer = Added(scene.Contact.PublicId);

        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, Add.RepeatAfterAmbiguous);
        await scene.RecordAsync(db, Add, Succeeded(answer, new JsonObject { ["contact"] = "p_1001" }));

        var error = scene.Attempt.Error!;
        Assert.Equal((AttemptErrors.ValidationTimedOut, FailureClass.Ambiguous, true), (error.Code, error.Class, error.Retriable));
        Assert.Equal(WorkItemStatus.Created, scene.Item.Status);
        Assert.Null(scene.Item.Result);
        Assert.Equal(answer.ToJsonString(), error.Trace);
        Assert.Equal("/items/0/contact_id", Assert.Single(error.Details!).Field);
        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A pin is written once and never overwritten, so it is written only from an answer that was checked — and an
    /// answer whose validation timed out never was. What it said is kept where a person can read it — the result on the
    /// attempt's trace, the identifiers it returned in the provenance — and none of it becomes Jason's record of what
    /// the provider calls anybody. A later attempt that measures its answer pins as any answer does.
    /// </summary>
    [Fact]
    [Critical(Family.X1, "a timed-out validation pins nothing")]
    public async Task An_answer_whose_validation_timed_out_pins_nothing_and_stays_on_the_trace()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, timing: TimingOut.Always());

        // The result names no identifier itself, so the only place p_1001 can be read afterwards is where the
        // identifiers the plugin returned are kept.
        var answer = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["contact_id"] = scene.Contact.PublicId, ["status"] = "added" }) };

        await scene.RecordAsync(db, Add, Succeeded(answer, new JsonObject { ["contact"] = "p_1001" }));

        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(answer.ToJsonString(), scene.Attempt.Error!.Trace);
        Assert.DoesNotContain("p_1001", scene.Attempt.Error.Trace, StringComparison.Ordinal);

        // What the plugin said is still recorded as said — the provenance's account of the answer, which is not a pin.
        var provenance = await scene.ProvenanceAsync(db);
        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p_1001" }, provenance.ExternalIdsReturned);
        Assert.Null(provenance.RejectedResult);
    }

    [Fact]
    public async Task An_unmeasured_answer_to_an_operation_that_is_never_repeated_ends_for_a_person()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, timing: TimingOut.Always());

        await scene.RecordAsync(db, Never, Succeeded(Added(scene.Contact.PublicId), null));

        var error = scene.Attempt.Error!;
        Assert.Equal((AttemptErrors.ValidationTimedOut, FailureClass.Ambiguous, false), (error.Code, error.Class, error.Retriable));
        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
    }

    [Fact]
    public async Task An_answer_whose_pattern_times_out_once_is_measured_warm_and_becomes_the_result()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, timing: new TimingOut(1));

        await scene.RecordAsync(db, Add, Succeeded(Added(scene.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }));

        Assert.Equal(WorkItemStatus.Succeeded, scene.Item.Status);
        Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>An answer wrong somewhere else is a shape error whatever the unanswered pattern would have said.</summary>
    [Fact]
    public async Task An_answer_wrong_elsewhere_is_result_invalid_for_what_is_wrong()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, timing: TimingOut.Always());
        var refused = JsonNode.Parse($$"""{"items":[{"contact_id":"{{scene.Contact.PublicId}}","status":"joined"}]}""")!;

        await scene.RecordAsync(db, Add, Succeeded(refused, null));

        Assert.Equal(AttemptErrors.ResultInvalid, scene.Attempt.Error!.Code);
        Assert.Equal("/items/0/status", Assert.Single(scene.Attempt.Error.Details!).Field);
    }

    /// <summary>
    /// The pin is Jason's record; the answer is the plugin's. When they disagree the disagreement is written
    /// down beside the pin — and the work still happened, so the attempt is still a success. Failing it would
    /// throw away work really done at the provider, and deciding which value is right is reconciliation, which
    /// this version deliberately does not have.
    /// </summary>
    [Fact]
    [Critical(Family.W10, "divergence beside the pin")]
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

    /// <summary>
    /// The plugin says the provider created the person: the pin is written, and right after it a line saying so, under the
    /// pin's key, work item and attempt, by the attempt — and the attempt's record keeps what the plugin said.
    /// </summary>
    [Fact]
    [Critical(Family.W10, "the origin follows the pin")]
    public async Task A_contact_the_provider_created_is_said_to_be_created_right_after_its_pin()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Succeeded(Added(scene.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }, Origins("created")));

        var lines = await IdentifierLinesAsync(db);
        Assert.Equal([JournalKinds.ExternalIdPinned, JournalKinds.ExternalIdOrigin], lines.Select(line => line.Kind));
        var origin = lines[1];
        Assert.Equal("created", (string?)origin.New);
        Assert.Null(origin.Old);
        Assert.Equal(lines[0].Key, origin.Key);
        Assert.Equal((scene.Item.PublicId, scene.Attempt.PublicId), (origin.WorkItemId, origin.AttemptId));
        Assert.Equal((ActorType.Attempt, scene.Attempt.PublicId), (origin.ActorType, origin.ActorId));

        var provenance = await scene.ProvenanceAsync(db);
        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "created" }, provenance.ExternalIdOrigins);
    }

    [Fact]
    public async Task A_contact_the_provider_already_held_is_said_to_be_found()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Succeeded(Added(scene.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }, Origins("found")));

        var origin = Assert.Single(await IdentifierLinesAsync(db), line => line.Kind == JournalKinds.ExternalIdOrigin);
        Assert.Equal("found", (string?)origin.New);
    }

    /// <summary>
    /// The plugin says nothing about how the provider came to hold the person: that is unknown, and it stays unknown —
    /// no line says found or created, and the attempt's record names no origin.
    /// </summary>
    [Fact]
    [Critical(Family.W10, "an unsaid origin is never guessed")]
    public async Task A_plugin_that_says_nothing_about_the_origin_leaves_it_unknown()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Succeeded(Added(scene.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }));

        Assert.Equal([JournalKinds.ExternalIdPinned], (await IdentifierLinesAsync(db)).Select(line => line.Kind));
        Assert.Null((await scene.ProvenanceAsync(db)).ExternalIdOrigins);
    }

    /// <summary>
    /// An answer naming a kind the operation never declared is refused whole, so an origin beside it is written nowhere:
    /// no pin, and no line saying how the provider came to hold anything.
    /// </summary>
    [Fact]
    public async Task An_origin_beside_an_undeclared_kind_leaves_no_line()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Succeeded(
            Added(scene.Contact.PublicId),
            new JsonObject { ["contact"] = "p_1001", ["mystery"] = "m_1" },
            new JsonObject { ["contact"] = "created", ["mystery"] = "created" }));

        Assert.Equal(AttemptErrors.ResultInvalid, scene.Attempt.Error!.Code);
        Assert.Empty(await IdentifierLinesAsync(db));
    }

    [Fact]
    public async Task An_unchanged_pin_says_nothing_about_its_origin()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var first = await SeedAsync(db);
        await first.RecordAsync(db, Add, Succeeded(Added(first.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }));

        var second = await SeedAsync(db, after: first);
        await second.RecordAsync(db, Add, Succeeded(Added(second.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }, Origins("created")));

        Assert.Equal([JournalKinds.ExternalIdPinned], (await IdentifierLinesAsync(db)).Select(line => line.Kind));
    }

    /// <summary>
    /// A divergence the provider says it created is a second record of one person made at the provider — the line saying
    /// so follows the divergence, under the same key, by the attempt that returned the value it is about.
    /// </summary>
    [Fact]
    public async Task A_divergence_the_provider_created_is_said_to_be_created_right_after_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var first = await SeedAsync(db);
        await first.RecordAsync(db, Add, Succeeded(Added(first.Contact.PublicId), new JsonObject { ["contact"] = "p_1001" }));

        var second = await SeedAsync(db, after: first);
        await second.RecordAsync(db, Add, Succeeded(Added(second.Contact.PublicId), new JsonObject { ["contact"] = "p_2002" }, Origins("created")));

        var lines = await IdentifierLinesAsync(db);
        Assert.Equal(
            [JournalKinds.ExternalIdPinned, JournalKinds.ExternalIdDiverged, JournalKinds.ExternalIdOrigin],
            lines.Select(line => line.Kind));
        Assert.Equal(("created", lines[1].Key, second.Attempt.PublicId), ((string?)lines[2].New, lines[2].Key, lines[2].AttemptId));
        Assert.Equal("p_2002", (string?)lines[1].New);
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
    [InlineData("rate_limited", FailureClass.Transient, true, WorkItemStatus.Created)]
    [InlineData("provider_call_failed", FailureClass.Permanent, false, WorkItemStatus.Failed)]
    [InlineData("invalid_channel_value", FailureClass.Validation, false, WorkItemStatus.Failed)]
    [InlineData("provider_answer_lost", FailureClass.Ambiguous, true, WorkItemStatus.Created)]
    public async Task A_declared_failure_carries_its_class_and_the_operation_decides_the_repeat(
        string code,
        FailureClass failureClass,
        bool retriable,
        WorkItemStatus expected)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(failureClass, code, "no answer", null, null)));

        Assert.Equal(AttemptStatus.Failed, scene.Attempt.Status);
        Assert.Equal(expected, scene.Item.Status);
        Assert.Equal(code, scene.Attempt.Error!.Code);
        Assert.Equal(failureClass, scene.Attempt.Error.Class);
        Assert.Equal(retriable, scene.Attempt.Error.Retriable);
    }

    /// <summary>
    /// A code the operation does not declare is an answer outside the contract, whatever class it came with: no planner
    /// was written for it, so the class it carries cannot be trusted to decide anything. The attempt ends
    /// <c>result_invalid</c>, ambiguous, and is never repeated, because the next attempt would run the same code over
    /// the same answer; the pointer says it is the code that is wrong.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Permanent)]
    [InlineData(FailureClass.Transient)]
    [InlineData(FailureClass.Ambiguous)]
    public async Task A_code_the_operation_does_not_declare_ends_the_attempt_outside_the_contract(FailureClass failureClass)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(failureClass, "quota_exceeded", "the account is out of quota", null, null)));

        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        Assert.Equal((AttemptErrors.ResultInvalid, (FailureClass?)FailureClass.Ambiguous, false), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class, scene.Attempt.Error.Retriable));
        var detail = Assert.Single(scene.Attempt.Error.Details!);
        Assert.Equal(("/error/code", "enum"), (detail.Field, detail.Code));
        Assert.Contains("quota_exceeded", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A declared code under a class its operation does not give it is outside the contract the same way. A rate limit
    /// called permanent strands work that would pass, a lost answer called transient repeats a write blind, and a code
    /// called blocked would hold back every item of an account on a word the operation never meant that way — so the
    /// block it carries is not kept either. The pointer says it is the class that is wrong.
    /// </summary>
    [Theory]
    [InlineData("rate_limited", FailureClass.Permanent)]
    [InlineData("provider_answer_lost", FailureClass.Transient)]
    [InlineData("provider_call_failed", FailureClass.Blocked)]
    public async Task A_declared_code_under_another_class_ends_the_attempt_outside_the_contract(string code, FailureClass failureClass)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var block = failureClass == FailureClass.Blocked ? new JsonObject { ["scope"] = "account", ["reason"] = "billing" } : null;

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(failureClass, code, "the provider said no", null, null, block)));

        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        Assert.Equal((AttemptErrors.ResultInvalid, (FailureClass?)FailureClass.Ambiguous, false), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class, scene.Attempt.Error.Retriable));
        Assert.Null(scene.Attempt.Error.Block);
        Assert.Null(scene.Item.LastError!.Block);
        var detail = Assert.Single(scene.Attempt.Error.Details!);
        Assert.Equal(("/error/class", "enum"), (detail.Field, detail.Code));
        Assert.Contains(code, detail.Message, StringComparison.Ordinal);
        Assert.Equal(
            "The plugin reported a failure outside what 'list_membership.add' declares, so nothing can read its class; what it said is on this attempt's trace.",
            scene.Attempt.Error.Message);
    }

    /// <summary>
    /// What the plugin said is not lost by being refused: its code, class, message and details are kept on the attempt's
    /// trace, where its author reads what was actually sent. An identifier beside it is not pinned — an answer outside
    /// the contract is written down nowhere — but the attempt's record still says the plugin returned it.
    /// </summary>
    [Fact]
    public async Task A_failure_outside_the_contract_keeps_what_the_plugin_said_and_pins_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var externalIds = new JsonObject { ["contact"] = "p_1001" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Permanent, "quota_exceeded", "the account is out of quota", new JsonObject { ["quota"] = "daily" }, externalIds)));

        var trace = JsonNode.Parse(scene.Attempt.Error!.Trace!)!;
        Assert.Equal("quota_exceeded", (string?)trace["code"]);
        Assert.Equal("permanent", (string?)trace["class"]);
        Assert.Equal("the account is out of quota", (string?)trace["message"]);
        Assert.Equal("daily", (string?)trace["details"]!["quota"]);

        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p_1001" },
            (await scene.ProvenanceAsync(db)).ExternalIdsReturned);
    }

    /// <summary>
    /// The host's own endings are declared by no operation: they say what became of the plugin, not what the provider
    /// answered. Under the class the host writes each with they stand as the plugin host reported them.
    /// </summary>
    [Theory]
    [InlineData(HostOutcomeCodes.PluginException, FailureClass.Permanent)]
    [InlineData(HostOutcomeCodes.CapabilityNotGranted, FailureClass.Permanent)]
    [InlineData(HostOutcomeCodes.PluginTimeout, FailureClass.Ambiguous)]
    [InlineData(HostOutcomeCodes.FilePathNotAllowed, FailureClass.Permanent)]
    [InlineData(HostOutcomeCodes.FileWriteFailed, FailureClass.Transient)]
    public async Task A_host_ending_under_the_class_the_host_gives_it_stands(string code, FailureClass failureClass)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(failureClass, code, "the host stopped the plugin", null, null)));

        Assert.Equal((code, (FailureClass?)failureClass), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class));
        Assert.Null(scene.Attempt.Error.Details);
    }

    public static TheoryData<string> EveryHostCode() => new(HostOutcomeCodes.All.Order(StringComparer.Ordinal));

    /// <summary>Every code the host writes stands under <c>permanent</c>, the class it writes them all with.</summary>
    [Theory]
    [MemberData(nameof(EveryHostCode))]
    public async Task Every_host_code_under_permanent_stands(string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Permanent, code, "the host stopped the plugin", null, null)));

        Assert.Equal((code, (FailureClass?)FailureClass.Permanent), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class));
        Assert.Null(scene.Attempt.Error.Details);
    }

    /// <summary>
    /// A plugin can write a host code itself, under any class. Under a class the host never gives it, it is the plugin's
    /// word and no operation declares it, so it is outside the contract like any other undeclared code.
    /// </summary>
    [Fact]
    public async Task A_host_code_under_a_class_the_host_never_gives_it_ends_the_attempt_outside_the_contract()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var block = new JsonObject { ["scope"] = "account", ["reason"] = "billing" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Blocked, HostOutcomeCodes.PluginException, "blocked, it says", null, null, block)));

        Assert.Equal((AttemptErrors.ResultInvalid, (FailureClass?)FailureClass.Ambiguous, false), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class, scene.Attempt.Error.Retriable));
        Assert.Null(scene.Attempt.Error.Block);
        Assert.Equal("/error/code", Assert.Single(scene.Attempt.Error.Details!).Field);
    }

    /// <summary>
    /// The same ambiguous failure under an operation that may never be repeated: the item waits for a person
    /// instead of being handed out again, which is the whole reason a contract states the rule.
    /// </summary>
    [Fact]
    [Critical(Family.W5, "never repeated after ambiguous")]
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
    [Critical(Family.W10, "pins on failure")]
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
    /// A block the published contract accepts is final, and it is kept on the attempt and on the item, where a person
    /// reads what is blocked, why and where it is cleared.
    /// </summary>
    [Fact]
    public async Task A_provider_block_is_final_and_kept_where_a_person_reads_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var block = new JsonObject { ["scope"] = "sender", ["sender_external_id"] = "li_5531", ["reason"] = "limit", ["clears_at"] = "2026-09-16T00:00:00Z" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Blocked, "provider_blocked", "the sender is at its limit", null, null, block)));

        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        Assert.Equal((FailureClass?)FailureClass.Blocked, scene.Attempt.Error!.Class);
        Assert.False(scene.Attempt.Error.Retriable);
        Assert.Null(scene.Attempt.Error.Details);
        Assert.Equal("li_5531", (string?)scene.Attempt.Error.Block!["sender_external_id"]);
        Assert.Equal("limit", (string?)scene.Item.LastError!.Block!["reason"]);
    }

    /// <summary>
    /// The same answer to work a block holds — here protective work — is not a failure: the work has not been tried, so
    /// the attempt ends blocked, the item waits on the block the answer names, and the block is opened by this attempt
    /// with the link the provider gave, which the campaign's line never carries.
    /// </summary>
    [Fact]
    public async Task Held_work_that_meets_a_block_waits_on_the_block_its_attempt_opened()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: Pause.Id);
        var block = new JsonObject { ["scope"] = "account", ["reason"] = "billing", ["link"] = "https://billing.example.com/pay" };

        await scene.RecordAsync(db, Pause, Failed(new OutcomeError(FailureClass.Blocked, "provider_blocked", "the plan has lapsed", null, null, block)));

        Assert.Equal((WorkItemStatus.Waiting, AttemptStatus.Blocked), (scene.Item.Status, scene.Attempt.Status));
        Assert.Equal(("provider_blocked", (FailureClass?)FailureClass.Blocked), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class));
        var opened = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(
            (ProviderBlockStatus.Open, ProviderBlockScope.Account, "billing", "https://billing.example.com/pay"),
            (opened.Status, opened.Scope, opened.Reason, opened.Link));
        Assert.Equal((TestPlugins.FakeProviderId, "sha256:1", scene.Attempt.Id), (opened.PluginId, opened.BindingIdentity, opened.OpenedByAttemptId));
        Assert.Equal(opened.Id, (await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == scene.Item.Id, Ct)).ProviderBlockId);
        var said = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).ToListAsync(Ct));
        Assert.DoesNotContain("billing.example.com", said.New!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The take-back of a send is not held by a block: the claim lets it through an open one, and where the provider
    /// refuses it for the account, that answer ends it final, as a failed protective action a person is told about. It is
    /// not parked and not repeated behind the block it ignores, the open block is left exactly as it was, and no
    /// campaign is told the block holds its work.
    /// </summary>
    [Fact]
    public async Task A_cancel_runs_through_an_open_block_and_its_blocked_answer_ends_it_final()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: Cancel.Id);
        var opener = WorkItemFactory.NewProviderOp(scene.Campaign, Send.Id, Noon);
        opener.Status = WorkItemStatus.Failed;
        db.WorkItems.Add(opener);
        var open = BlockRows.Block(opener, Noon, Noon.AddMinutes(10), binding: "sha256:1");
        open.PluginId = TestPlugins.FakeProviderId;
        db.ProviderBlocks.Add(open);
        await db.SaveChangesAsync(Ct);
        var block = new JsonObject { ["scope"] = "account", ["reason"] = "billing" };

        await scene.RecordAsync(db, Cancel, Failed(new OutcomeError(FailureClass.Blocked, "provider_blocked", "the plan has lapsed", null, null, block)));

        Assert.Equal((WorkItemStatus.Failed, AttemptStatus.Failed), (scene.Item.Status, scene.Attempt.Status));
        Assert.Equal(("provider_blocked", (FailureClass?)FailureClass.Blocked, false), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class, scene.Attempt.Error.Retriable));
        Assert.False(await db.WorkItemWaits.AsNoTracking().AnyAsync(w => w.WorkItemId == scene.Item.Id, Ct));
        var row = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(
            (ProviderBlockStatus.Open, (int?)null, (int?)null, Noon, Noon.AddMinutes(10)),
            (row.Status, row.ClearedByAttemptId, row.TrialWorkItemId, row.LastSeenAt, row.NextTrialAt));
        Assert.False(await db.Journal.AsNoTracking().AnyAsync(e => e.Kind == JournalKinds.ProviderBlocked || e.Kind == JournalKinds.ProviderBlockCleared, Ct));
    }

    public static TheoryData<string, bool> WhatAnAnswerSaysOfAnOpenBlock() => new()
    {
        { "succeeded", true },
        { "contact_not_found", true },
        { "message_invalid", true },
        { HostOutcomeCodes.PluginException, false },
        { UnjudgedOutcomeCodes.ProviderInputInvalid, false },
        { UnjudgedOutcomeCodes.CapabilityNotOffered, false },
        { "rate_limited", false },
        { "provider_answer_lost", false },
    };

    /// <summary>
    /// A call to held work that reaches a person — here a send — that the provider judged on the work — it succeeded, or
    /// failed permanent or validation — got past the block, which clears; a transient or ambiguous failure, the host's own ending, the package's refusal of
    /// the provider's object, answered before any call, or the provider's answer that it does not offer the operation yet,
    /// which judges nothing of the work, says nothing about it. Each failure is recorded as it was given:
    /// one refused as an answer outside the contract would leave the block open too, for another reason.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhatAnAnswerSaysOfAnOpenBlock))]
    public async Task Held_work_the_provider_judged_on_the_work_clears_the_open_block_and_nothing_else_does(string answer, bool clears)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: Send.Id);
        var opener = WorkItemFactory.NewProviderOp(scene.Campaign, Send.Id, Noon);
        opener.Status = WorkItemStatus.Failed;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, Noon, Noon, binding: "sha256:1");
        block.PluginId = TestPlugins.FakeProviderId;
        db.ProviderBlocks.Add(block);
        await db.SaveChangesAsync(Ct);

        var given = answer switch
        {
            "succeeded" => null,
            HostOutcomeCodes.PluginException => new OutcomeError(FailureClass.Permanent, answer, "the host stopped the plugin", null, null),
            UnjudgedOutcomeCodes.ProviderInputInvalid => new OutcomeError(FailureClass.Validation, answer, "args.provider is read as an object with a schedule; the object given is not that shape", null, null),
            UnjudgedOutcomeCodes.CapabilityNotOffered => new OutcomeError(FailureClass.Permanent, answer, "message.send: the provider does not offer this operation yet", null, null),
            _ => new OutcomeError(Send.FailureCodes.Single(c => c.Code == answer).Class, answer, "the provider said so", null, null),
        };
        await scene.RecordAsync(
            db,
            Send,
            given is null
                ? Succeeded(Taken(scene.Contact.PublicId, "email-1001"), new JsonObject { ["contact"] = "p_1001", ["send"] = "email-1001" })
                : Failed(given));

        var row = await db.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == block.Id, Ct);
        Assert.True(
            (row.Status == ProviderBlockStatus.Cleared) == clears,
            $"After '{answer}' the block is {row.Status}, cleared by attempt {row.ClearedByAttemptId}.");
        Assert.Equal(clears ? (int?)scene.Attempt.Id : null, row.ClearedByAttemptId);
        Assert.NotEqual(WorkItemStatus.Waiting, scene.Item.Status);
        if (given is not null)
        {
            var error = scene.Attempt.Error!;
            Assert.Equal(
                (given.Code, (FailureClass?)given.Class, OutcomeContract.Retriable(given.Class, Send, packageSettlesByKey: false)),
                (error.Code, error.Class, error.Retriable));
        }
    }

    /// <summary>
    /// A held control is not the block's judge: the pause that succeeds while an account block of its binding is open
    /// got past nothing that reaches a person. The block stays open, cleared by nobody, and no line says it cleared. Where
    /// the pause was the block's trial, the next one is a cadence off rather than never; where it was not — claimed
    /// before the block opened, or while another item is the trial — the block's trial is left as it was. The
    /// provider's judgement of the pause itself decides no more than its success does.
    /// </summary>
    [Theory]
    [InlineData("succeeded", "this")]
    [InlineData("campaign_not_found", "this")]
    [InlineData("succeeded", "none")]
    [InlineData("succeeded", "another")]
    public async Task A_held_control_that_succeeds_under_an_open_block_leaves_it_as_it_was(string answer, string trial)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: Pause.Id);
        var opener = WorkItemFactory.NewProviderOp(scene.Campaign, Send.Id, Noon);
        opener.Status = WorkItemStatus.Failed;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, Noon.AddHours(-1), Noon, binding: "sha256:1");
        block.PluginId = TestPlugins.FakeProviderId;
        db.ProviderBlocks.Add(block);
        await db.SaveChangesAsync(Ct);
        block.TrialWorkItemId = trial switch
        {
            "this" => scene.Item.Id,
            "another" => opener.Id,
            _ => null,
        };
        await db.SaveChangesAsync(Ct);

        await scene.RecordAsync(
            db,
            Pause,
            answer == "succeeded"
                ? Succeeded(new JsonObject { ["items"] = new JsonArray(new JsonObject { ["campaign_external_id"] = "sq-1129", ["status"] = "paused" }) }, null)
                : Failed(new OutcomeError(Pause.FailureCodes.Single(c => c.Code == answer).Class, answer, "the provider said so", null, null)));

        Assert.Equal(answer == "succeeded" ? WorkItemStatus.Succeeded : WorkItemStatus.Failed, scene.Item.Status);
        var row = await db.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == block.Id, Ct);
        Assert.Equal(
            (ProviderBlockStatus.Open, (int?)null, (DateTime?)null, trial == "another" ? opener.Id : (int?)null),
            (row.Status, row.ClearedByAttemptId, row.ClearedAt, row.TrialWorkItemId));
        Assert.Equal(trial == "this" ? Noon.AddSeconds(TestOptions.Settings().Current.Blocks.TrialSeconds) : Noon, row.NextTrialAt);
        Assert.False(await db.Journal.AsNoTracking().AnyAsync(e => e.Kind == JournalKinds.ProviderBlockCleared, Ct));
    }

    /// <summary>
    /// A held control that is an account block's trial and meets a sender's block has not got past the account: what
    /// it says is that a sender is blocked, which decides nothing about the account. The account's block stays open,
    /// cleared by nobody, with its next trial a cadence off; the pause parks on the sender's block its answer opened.
    /// </summary>
    [Fact]
    public async Task A_held_control_that_is_the_account_s_trial_and_meets_a_sender_s_block_leaves_the_account_open()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation: Pause.Id);
        var opener = WorkItemFactory.NewProviderOp(scene.Campaign, Send.Id, Noon);
        opener.Status = WorkItemStatus.Failed;
        db.WorkItems.Add(opener);
        var account = BlockRows.Block(opener, Noon.AddHours(-1), Noon, binding: "sha256:1");
        account.PluginId = TestPlugins.FakeProviderId;
        account.TrialWorkItemId = scene.Item.Id;
        db.ProviderBlocks.Add(account);
        await db.SaveChangesAsync(Ct);
        var said = new JsonObject { ["scope"] = "sender", ["sender_external_id"] = "li_5531", ["reason"] = "limit" };

        await scene.RecordAsync(db, Pause, Failed(new OutcomeError(FailureClass.Blocked, "provider_blocked", "the sender is at its limit", null, null, said)));

        var row = await db.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == account.Id, Ct);
        Assert.Equal(
            (ProviderBlockStatus.Open, (int?)null, (int?)null),
            (row.Status, row.ClearedByAttemptId, row.TrialWorkItemId));
        Assert.Equal(Noon.AddSeconds(TestOptions.Settings().Current.Blocks.TrialSeconds), row.NextTrialAt);
        var sender = await db.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id != account.Id, Ct);
        Assert.Equal(
            (ProviderBlockStatus.Open, ProviderBlockScope.Sender, "li_5531", scene.Attempt.Id),
            (sender.Status, sender.Scope, sender.SenderExternalId, sender.OpenedByAttemptId));
        Assert.Equal((WorkItemStatus.Waiting, AttemptStatus.Blocked), (scene.Item.Status, scene.Attempt.Status));
        Assert.Equal(sender.Id, (await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == scene.Item.Id, Ct)).ProviderBlockId);
        Assert.False(await db.Journal.AsNoTracking().AnyAsync(e => e.Kind == JournalKinds.ProviderBlockCleared, Ct));
    }

    /// <summary>
    /// A block outside its contract is refused the way an undeclared identifier is: the plugin's failure stands, recorded
    /// permanent, with what is wrong with the block named beside it, and the block itself is not kept.
    /// </summary>
    [Fact]
    public async Task A_block_outside_its_contract_is_recorded_permanent_with_its_problem_named()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        var block = new JsonObject { ["scope"] = "account", ["reason"] = "insufficient_credits" };

        await scene.RecordAsync(db, Add, Failed(new OutcomeError(FailureClass.Blocked, "provider_blocked", "out of credits", null, null, block)));

        Assert.Equal(WorkItemStatus.Failed, scene.Item.Status);
        Assert.Equal(("provider_blocked", (FailureClass?)FailureClass.Permanent, false), (scene.Attempt.Error!.Code, scene.Attempt.Error.Class, scene.Attempt.Error.Retriable));
        Assert.Null(scene.Attempt.Error.Block);
        Assert.Null(scene.Item.LastError!.Block);
        var detail = Assert.Single(scene.Attempt.Error.Details!);
        Assert.Equal(("/error/block/reason", "enum"), (detail.Field, detail.Code));
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
    [Critical(Family.W5, "a timed-out send ends")]
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

    private static PluginInvocationResult Succeeded(JsonNode? result, JsonObject? externalIds, JsonObject? origins = null) => new(
        new InvocationOutcome.Succeeded(result, externalIds, new OutcomeDiagnostics(12, 2, 0, 1), origins),
        Provenance,
        Launch: null);

    private static JsonObject Origins(string contact) => new() { ["contact"] = contact };

    /// <summary>The chronicle's lines about identifiers, in the order they were written.</summary>
    private static async Task<List<JournalEntry>> IdentifierLinesAsync(JasonDbContext db) =>
        await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind.StartsWith("external_id_"))
            .OrderBy(entry => entry.Id)
            .ToListAsync(Ct);

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
    private static async Task<Scene> SeedAsync(
        JasonDbContext db,
        int? invokerOutcomeBytes = null,
        Scene? after = null,
        PatternTiming? timing = null,
        string operation = "list_membership.add")
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaign = after?.Campaign ?? WorkItemFactory.NewCampaign(now: Noon);
        var contact = after?.Contact ?? new Contact { PublicId = PublicId.New("cnt"), CreatedAt = Noon, UpdatedAt = Noon };
        var item = after?.Item ?? WorkItemFactory.NewProviderOp(campaign, operation, Noon);
        item.Contact = contact;
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, (after?.Attempt.Number ?? 0) + 1, AttemptStatus.Running, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            TestPlugins.FakeProviderId, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            operation, 1, Provenance.SnapshotId, RouteSnapshotId, RouteScope.GlobalDefault, "sha256:1",
            CorrelationId: attempt.PublicId);

        if (after is null)
        {
            db.Campaigns.Add(campaign);
            db.Contacts.Add(contact);
            db.WorkItems.Add(item);
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        var options = TestOptions.PluginSettings(o => o.Invoker.OutcomeBytes = invokerOutcomeBytes ?? o.Invoker.OutcomeBytes);
        var recorder = new ProviderOutcomeRecorder(
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(o => o.RetryDelaySeconds = 0)),
            new ExternalIdStore(new JournalWriter(clock), clock),
            new SentFacts(new JournalWriter(clock)),
            options,
            timing);
        return new Scene(recorder, campaign, contact, item, attempt);
    }

    private const string RouteSnapshotId = "rts_01K5B7Q2WE5X3M9T0YH4C6RDND";

    private sealed record Scene(ProviderOutcomeRecorder Recorder, Campaign Campaign, Contact Contact, WorkItem Item, Attempt Attempt)
    {
        public async Task RecordAsync(JasonDbContext db, OperationContract contract, PluginInvocationResult result, bool settlesByKey = false)
        {
            await Recorder.RecordAsync(db, Item, Attempt, contract, TestPlugins.FakeProviderId, settlesByKey, result, Ct);
            await db.SaveChangesAsync(Ct);
        }

        public async Task<AttemptProvenanceDto> ProvenanceAsync(JasonDbContext db) =>
            (await db.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == Attempt.PublicId, Ct)).Provenance!;
    }
}
