using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>contact.delete</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. One test per line of the document's conformance list,
/// then the endings that are the plugin's own — a read that does not say whether the person is opted out, a result it
/// does not know — and last the runtime's half: a delete changes nothing of Jason's.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyContactDeleteTests
{
    private const string Operation = "contact.delete";
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRT";
    private const string Address = "marta@example.com";
    private const string Profile = "https://www.linkedin.com/in/marta-example";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -------------------------------------------------------------------------------------------------------
    // The document's conformance list
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pinned_contact_is_read_by_the_pin_then_deleted_by_it_and_nothing_is_pinned()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001"));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal($$"""{"items":[{"contact_id":"{{ContactId}}","status":"deleted"}]}""", answer.ToJsonString());
        Assert.True(((InvocationOutcome.Succeeded)result.Outcome).ExternalIds is null or { Count: 0 }, "A delete pinned an identifier.");
        Assert.Equal(["contact get 1001", "contact delete 1001"], Lines(account));
        Assert.Null(account.ContactOf(1001));
    }

    [Fact]
    public async Task A_person_nobody_pinned_is_not_linked_and_nobody_is_looked_up_by_the_address()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: null)));

        Assert.Equal(("contact_not_linked", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Empty(account.Calls);
        Assert.NotNull(account.ContactOf(1001));
    }

    /// <summary>Reply's own stop lives on the contact, so deleting it would erase that stop: the person is refused, and their record stays.</summary>
    [Fact]
    [Critical(Family.S1, "an opt-out is never deleted away")]
    public async Task A_contact_the_provider_holds_as_opted_out_is_refused_and_not_deleted()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("contact_opted_out", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact get 1001"], Lines(account));
        Assert.NotNull(account.ContactOf(1001));
        Assert.Equal([1001], account.OptOuts.Select(entry => (int)entry!));
    }

    [Fact]
    public async Task On_a_first_attempt_a_pin_the_provider_does_not_hold_is_not_found_and_nothing_is_deleted()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "4242")));

        Assert.Equal(("contact_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact get 4242"], Lines(account));
        Assert.Single(account.Contacts);
    }

    /// <summary>
    /// The delete went out and its answer was lost; the next attempt reads by the pin first, finds nobody, and that is
    /// the delete already done — nothing is deleted a second time.
    /// </summary>
    [Fact]
    public async Task After_a_lost_answer_a_contact_the_provider_no_longer_holds_is_already_deleted()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.LosesTheOutcome("contact delete");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));
        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (lost.Code, lost.Class));
        Assert.Null(lost.ExternalIds);
        Assert.Null(account.ContactOf(1001));

        var mark = account.Mark();
        var repeat = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));

        Assert.Equal("already_deleted", (string?)repeat["items"]![0]!["status"]);
        Assert.Equal(["contact get 1001"], [.. account.CallsSince(mark).Select(call => call.Line)]);
    }

    /// <summary>
    /// The opt-out is read on every attempt, a repeat's included: a contact Reply holds as opted out by the time the work
    /// is repeated is refused, and its record — the only one Reply keeps of the stop — stays.
    /// </summary>
    [Fact]
    public async Task A_repeat_that_finds_the_contact_now_held_as_opted_out_is_refused_and_not_deleted()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta", optedOut: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));

        Assert.Equal(("contact_opted_out", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact get 1001"], Lines(account));
        Assert.NotNull(account.ContactOf(1001));
        Assert.Equal([1001], account.OptOuts.Select(entry => (int)entry!));
    }

    /// <summary>A repeat that finds the contact still there deletes it then, after asking about the stop again.</summary>
    [Fact]
    public async Task After_a_lost_answer_a_contact_still_held_is_read_again_and_deleted()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var repeat = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001"), attempt: 2));

        Assert.Equal("deleted", (string?)repeat["items"]![0]!["status"]);
        Assert.Equal(["contact get 1001", "contact delete 1001"], Lines(account));
        Assert.Null(account.ContactOf(1001));
    }

    /// <summary>The read found the contact, and the delete then says Reply holds it no longer: whoever removed it, it is gone, which is what was asked.</summary>
    [Fact]
    public async Task A_delete_answered_as_missing_after_the_read_found_the_contact_is_already_deleted()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Refuses("contact delete", "contact.not_found", status: 404, replyCode: "contact.notFound", ids: new JsonObject { ["contact_id"] = 1001 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal("already_deleted", (string?)answer["items"]![0]!["status"]);
        Assert.Equal(["contact get 1001", "contact delete 1001"], Lines(account));
    }

    /// <summary>
    /// The two command lines are the pin and nothing else: no address, no body, no other contact, list or campaign —
    /// and everything else the account holds is as it was.
    /// </summary>
    [Fact]
    public async Task The_call_names_nothing_but_the_pinned_contact()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount())
            .WithContact(1001, Address, "Marta")
            .WithContact(1002, "ana@example.com", "Ana")
            .WithEnrollment(ReplyOperations.Sequence, 1002)
            .WithList(ReplyOperations.List, "Q3 LatAm founders", 1001, 1002);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(["--json", "-q", "contact", "get", "1001"], account.Calls[0].Args);
        Assert.Equal(["--json", "-q", "contact", "delete", "1001"], account.Calls[1].Args);
        Assert.All(account.Calls, call => Assert.Null(call.Body));
        Assert.NotNull(account.ContactOf(1002));
        Assert.Equal([1002], account.EnrolledIn(ReplyOperations.Sequence));
        Assert.Equal([1002], account.MembersOf(ReplyOperations.List));
    }

    // -------------------------------------------------------------------------------------------------------
    // The plugin's own endings
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pin_that_is_not_a_reply_contact_number_names_nobody_and_nothing_is_asked()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "p_88421")));

        Assert.Equal("contact_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    /// <summary>A read that does not say whether the person is opted out is no answer: nothing is deleted on a guess, and the read is asked again.</summary>
    [Theory]
    [InlineData("\"no\"")]
    [InlineData("null")]
    public async Task A_read_that_does_not_say_whether_the_person_is_opted_out_deletes_nothing(string optedOut)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Returns("contact get", JsonNode.Parse($$"""{"id":1001,"email":"{{Address}}","isOptedOut":{{optedOut}}}""")!.AsObject());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal(["contact get 1001"], Lines(account));
        Assert.NotNull(account.ContactOf(1001));
        Assert.DoesNotContain(Address, error.Details?.ToJsonString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_about_another_contact_than_the_pin_deletes_nothing()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Returns("contact get", new JsonObject { ["id"] = 1002, ["isOptedOut"] = false });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal(["contact get 1001"], Lines(account));
    }

    [Fact]
    public async Task A_delete_whose_result_is_not_one_it_knows_is_a_lost_write()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Returns("contact delete", new JsonObject { ["status"] = "removed", ["contact_id"] = 1001 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
    }

    /// <summary>
    /// The delete went out and its answer names another contact than the one the call named: that is no answer about this
    /// delete, so it is a lost write — ambiguous, repeated only through the read — and nothing is pinned.
    /// </summary>
    [Fact]
    public async Task A_delete_whose_answer_names_another_contact_is_a_lost_write()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Returns("contact delete", new JsonObject { ["status"] = "deleted", ["contact_id"] = 1002 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
        Assert.Equal(["contact get 1001", "contact delete 1001"], Lines(account));
    }

    /// <summary>A refusal the CLI says it learned the contact's identifier on carries none out: a delete pins nothing, on a failure either.</summary>
    [Fact]
    public async Task A_refusal_carries_no_identifier_out()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Refuses("contact delete", "rate_limited", status: 429, retryAfter: 30, ids: new JsonObject { ["contact_id"] = 1001 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("rate_limited", FailureClass.Transient), (error.Code, error.Class));
        Assert.Null(error.ExternalIds);
    }

    [Fact]
    public async Task An_account_whose_plan_does_not_include_it_is_told_so()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        account.Refuses("contact get", "access.feature_unavailable", status: 403);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "1001")));

        Assert.Equal(("feature_unavailable", FailureClass.Permanent), (error.Code, error.Class));
    }

    // -------------------------------------------------------------------------------------------------------
    // The runtime's half: a delete changes nothing of Jason's
    // -------------------------------------------------------------------------------------------------------

    private static readonly DateTime Noon = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The provider's contact is gone, and Jason's is exactly as it was: the person, their channels, the pin — which now
    /// names nobody, so a later operation about them fails rather than finding them again by address — and the
    /// do-not-contact register, whose stop on another of their channels stays a stop.
    /// </summary>
    [Fact]
    [Critical(Family.S1, "the register survives a delete")]
    public async Task The_runtime_keeps_the_person_their_pin_and_the_register_after_a_delete()
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Marta", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = Address, IsPrimary = true });
        contact.Channels.Add(new ContactChannel { Channel = "linkedin", Value = Profile });
        var earlier = WorkItemFactory.NewProviderOp(campaign, "list_membership.add", Noon);
        earlier.Contact = contact;
        var linked = WorkItemFactory.NewAttempt(earlier, 1, AttemptStatus.Succeeded, Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, Operation, Noon);
        item.Contact = contact;
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        attempt.Provenance = new AttemptProvenanceDto(
            ReplyPlugins.PluginId, "0.4.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            Operation, 1, PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC", "rts_01K5B7Q2WE5X3M9T0YH4C6RDND",
            RouteScope.GlobalDefault, "sha256:1", CorrelationId: attempt.PublicId);
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.WorkItems.AddRange(earlier, item);
        db.Attempts.AddRange(linked, attempt);
        db.Suppressions.Add(new Suppression { PublicId = PublicId.New("sup"), Channel = "linkedin", Value = Profile, Reason = "asked to stop", CreatedAt = Noon });
        await db.SaveChangesAsync(Ct);

        // The pin an earlier list add wrote, which is how a person comes to be pinned at all.
        var store = new ExternalIdStore(new JournalWriter(clock), clock);
        store.Apply(db, OperationCatalog.Find("list_membership.add")!, ReplyPlugins.PluginId, earlier, campaign, contact, linked, new JsonObject { ["contact"] = "1001" });
        await db.SaveChangesAsync(Ct);

        var result = await InvokeAsync(api, Input(pinned: "1001", contactId: contact.PublicId));
        ReplyOperations.Succeeded(Operation, result);
        var recorder = new ProviderOutcomeRecorder(
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(o => o.RetryDelaySeconds = 0)),
            store,
            new SentFacts(new JournalWriter(clock)),
            TestOptions.PluginSettings(_ => { }));
        await recorder.RecordAsync(db, item, attempt, OperationCatalog.Find(Operation)!, ReplyPlugins.PluginId, ReplyPlugins.SettlesByKey(Operation), result, Ct);
        await db.SaveChangesAsync(Ct);

        Assert.True(attempt.Status == AttemptStatus.Succeeded, $"The attempt ended {attempt.Status}: {attempt.Error?.Code} {attempt.Error?.Message}");
        Assert.Null(account.ContactOf(1001));
        var kept = await db.Contacts.AsNoTracking().Include(entry => entry.Channels).SingleAsync(entry => entry.Id == contact.Id, Ct);
        Assert.Null(kept.ArchivedAt);
        Assert.Equal(["email", "linkedin"], kept.Channels.Select(channel => channel.Channel).Order(StringComparer.Ordinal));
        var pin = await db.ExternalIds.AsNoTracking().SingleAsync(entry => entry.ContactId == contact.Id && entry.Kind == "contact", Ct);
        Assert.Equal(("1001", null), (pin.Value, pin.DivergedValue));
        var stop = Assert.Single(await db.Suppressions.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(("linkedin", Profile), (stop.Channel, stop.Value));
    }

    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Input(string? pinned, string channel = "email", string value = Address, string contactId = ContactId) => new()
    {
        ["args"] = new JsonObject { ["channel"] = channel },
        ["contacts"] = new JsonArray(new JsonObject
        {
            ["id"] = contactId,
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

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
