using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
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
/// The contact proxy rule, held over every operation the catalog publishes about a person, the way each document's
/// <c>preflight.provider_contact</c> says it is found: the real plugin JavaScript in a real plugin-host child, against
/// the stand-in CLI. A way that finds the person finds Reply's contact and pins it without importing anybody; only
/// <c>find_or_create</c> imports a person Reply does not hold; <c>find_only</c> creates nobody; a pin is worked by and a
/// second attempt looks nobody up; a provider that answers another identifier than the pin has it recorded beside the
/// pin while the attempt succeeds; a LinkedIn URL is found by its lowercased slug; and <c>pinned_only</c> asks nothing
/// for a person nobody pinned. Each case reads which operations it covers from the documents, so a document published
/// under one of these ways fails here until it has a driver.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyContactProxyRuleTests
{
    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK";
    private const string Address = "marta@example.com";

    /// <summary>A profile as Jason keys it — its lowercased slug — and the same profile as somebody typed it at Reply.</summary>
    private const string Profile = "https://www.linkedin.com/in/marta-example";

    private const string ProfileAsTyped = "https://www.linkedin.com/in/Marta-Example";

    private const int Thread = 502;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What drives one operation against the stand-in: the command it runs once the person is named, the world it needs, and how Reply answers it by another identifier.</summary>
    private sealed record Driver(string Command, Func<ReplyAccount, ReplyAccount> Plant, Func<int, JsonObject>? AnsweredAs, bool ThroughTheThread = false);

    private static readonly IReadOnlyDictionary<string, Driver> Drivers = new Dictionary<string, Driver>(StringComparer.Ordinal)
    {
        ["campaign.enroll"] = new(
            "sequence add-contact 7",
            ReplyOperations.Plant,
            id => new JsonObject { ["status"] = "added", ["sequence_id"] = 7, ["contact_id"] = id, ["contact_created"] = false, ["sequence_active"] = true }),
        ["list_membership.add"] = new(
            "contact-list add-contact 9",
            ReplyOperations.Plant,
            id => new JsonObject { ["status"] = "added", ["list_id"] = 9, ["contact_id"] = id, ["contact_created"] = false }),
        ["optout.record"] = new(
            "contact opt-out",
            account => account,
            id => new JsonObject { ["status"] = "opted_out", ["contact_id"] = id, ["contact_created"] = false }),
        ["enrollment.stop"] = new(
            "sequence remove-contact 7",
            account => ReplyOperations.Plant(account).WithEnrollment(ReplyOperations.Sequence, 1001),
            id => new JsonObject { ["status"] = "removed", ["sequence_id"] = 7, ["contact_id"] = id }),
        ["conversation.reply"] = new(
            $"inbox reply {Thread}",
            account => account.WithThread(ReplyConversationListTests.Thread(Thread, "2026-10-03T07:41:09Z")),
            AnsweredAs: null,
            ThroughTheThread: true),
    };

    public static TheoryData<string> Finding => Of(ProviderContact.FindOrCreate, ProviderContact.FindOnly);

    public static TheoryData<string> Creating => Of(ProviderContact.FindOrCreate);

    public static TheoryData<string> FindingOnly => Of(ProviderContact.FindOnly);

    /// <summary>The ways that find the person by the consumed value itself, every one but the reply, which finds them through its thread.</summary>
    public static TheoryData<string> ByTheValue => Data(Ids(ProviderContact.FindOrCreate, ProviderContact.FindOnly).Where(id => !Drivers[id].ThroughTheThread));

    public static TheoryData<string> PinnedOnly => Of(ProviderContact.PinnedOnly);

    [Fact]
    public void Every_operation_that_finds_a_person_has_a_driver_and_every_driver_is_one_of_them()
    {
        Assert.Equal(
            Ids(ProviderContact.FindOrCreate, ProviderContact.FindOnly).Order(StringComparer.Ordinal).ToList(),
            Drivers.Keys.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["conversation.list"], Ids(ProviderContact.PinnedOnly));
    }

    /// <summary>
    /// Reply holds the person: the exact lookup finds them, the command names them by that identifier, nothing is
    /// imported and their record is left as it was — this one has no first name at Reply, and still has none — and the
    /// identifier is pinned. The reply finds them through its thread instead, and looks nobody up.
    /// </summary>
    [Theory]
    [MemberData(nameof(Finding))]
    public async Task A_person_reply_holds_is_found_and_pinned_and_nothing_is_imported(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount()).WithContact(1001, Address);
        var before = account.Contacts.ToJsonString();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "email", Address), Ct);

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal("1001", PinnedContact(result));
        Assert.Equal(before, account.Contacts.ToJsonString());
        if (driver.ThroughTheThread)
        {
            Assert.Equal([$"inbox get {Thread}", driver.Command], Lines(account));
            return;
        }

        Assert.Equal(["contact find", driver.Command], Lines(account));
        Assert.Equal($$"""{"email":"{{Address}}"}""", account.Calls[0].Body);
        Assert.Equal("1001", account.Calls[1].Option("--contact-id"));
        Assert.Null(account.Calls[1].Body);
    }

    /// <summary>
    /// Reply holds nobody under the value, and the way is <c>find_or_create</c>: the lookup finds nobody, the command
    /// imports the person from stdin, and the identifier Reply gave them is pinned.
    /// </summary>
    [Theory]
    [MemberData(nameof(Creating))]
    public async Task A_person_reply_holds_nobody_for_is_imported_and_pinned(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "email", Address), Ct);

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal("1001", PinnedContact(result));
        Assert.Equal(["contact find", driver.Command], Lines(account));
        Assert.Equal("-", account.Calls[1].Option("--contact"));
        Assert.Contains(Address, account.Calls[1].Body!, StringComparison.Ordinal);
        var made = Assert.Single(account.Contacts)!;
        Assert.Equal((1001, Address), ((int)made["id"]!, (string?)made["email"]));
    }

    /// <summary>
    /// The way is <c>find_only</c>: Reply holding nobody creates nobody. The stop answers that the person takes part in
    /// nothing, and the reply, whose thread names nobody Reply still holds, sends nothing — and neither pins anybody.
    /// </summary>
    [Theory]
    [MemberData(nameof(FindingOnly))]
    public async Task A_way_that_finds_only_creates_nobody_and_pins_nobody(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.ThroughTheThread
            ? new ReplyAccount().WithThread(ReplyConversationListTests.Thread(Thread, "2026-10-03T07:41:09Z", deleted: true))
            : driver.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "email", Address), Ct);

        Assert.Empty(account.Contacts);
        if (driver.ThroughTheThread)
        {
            var error = ReplyOperations.Failed(operation, result);
            Assert.Equal("thread_contact_mismatch", error.Code);
            Assert.Null(error.ExternalIds);
            Assert.Equal([$"inbox get {Thread}"], Lines(account));
            Assert.Empty(account.OutboundIn(Thread));
            return;
        }

        var answer = ReplyOperations.Succeeded(operation, result);
        Assert.Equal("already_stopped", (string?)answer["items"]![0]!["status"]);
        Assert.Null(PinnedContact(result));
        Assert.Equal(["contact find"], Lines(account));
    }

    /// <summary>
    /// A pin is worked by: a second attempt carries it, nobody is looked up by the address, and the command names the
    /// person by it — the reply reads its thread for the channel and holds the thread's person to the pin.
    /// </summary>
    [Theory]
    [MemberData(nameof(Finding))]
    public async Task A_second_attempt_works_by_the_pin_and_looks_nobody_up(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount()).WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "email", Address, pin: "1001"), Ct, attempt: 2);

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal("1001", PinnedContact(result));
        Assert.DoesNotContain(account.Calls, call => call.Command == "contact find");
        if (driver.ThroughTheThread)
        {
            Assert.Equal([$"inbox get {Thread}", driver.Command], Lines(account));
            return;
        }

        var command = Assert.Single(account.Calls);
        Assert.Equal(driver.Command, command.Line);
        Assert.Equal("1001", command.Option("--contact-id"));
        Assert.Null(command.Body);
    }

    /// <summary>
    /// The pin is 1001 and Reply answers the command about 1002. The plugin returns what Reply answered, and the runtime
    /// writes it beside the pin, which stands: the attempt succeeds, 1001 is still the person's identifier here, 1002 is
    /// recorded as the divergence, and the chronicle says so.
    /// </summary>
    [Theory]
    [MemberData(nameof(ByTheValue))]
    public async Task An_answer_about_another_identifier_than_the_pin_is_recorded_beside_it_and_the_attempt_succeeds(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount()).WithContact(1001, Address, "Marta");
        account.Returns(driver.Command.Split(' ')[0] + " " + driver.Command.Split(' ')[1], driver.AnsweredAs!(1002));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db, operation, pinned: "1001");

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "email", Address, pin: "1001", contactId: scene.Contact.PublicId), Ct, attempt: 2);
        Assert.Equal("1002", PinnedContact(result));

        await scene.Recorder.RecordAsync(db, scene.Item, scene.Attempt, OperationCatalog.Find(operation)!, ReplyPlugins.PluginId, result, Ct);
        await db.SaveChangesAsync(Ct);

        Assert.True(scene.Attempt.Status == AttemptStatus.Succeeded, $"The attempt ended {scene.Attempt.Status}: {scene.Attempt.Error?.Code} {scene.Attempt.Error?.Message}");
        var pin = await db.ExternalIds.AsNoTracking().SingleAsync(entry => entry.ContactId == scene.Contact.Id && entry.Kind == "contact", Ct);
        Assert.Equal(("1001", "1002"), (pin.Value, pin.DivergedValue));
        Assert.Contains(await db.Journal.AsNoTracking().ToListAsync(Ct), entry => entry.Kind == JournalKinds.ExternalIdDiverged);
    }

    /// <summary>
    /// The rule's "on <c>error.external_ids</c> too", for a person the plugin found itself: Reply holds them, the
    /// lookup finds them, and the command that follows is refused with nobody named in the refusal. The identifier the
    /// lookup found is still what the failure pins, so an attempt that ends there leaves the trace of who it was about.
    /// </summary>
    [Theory]
    [MemberData(nameof(ByTheValue))]
    public async Task A_person_the_lookup_found_is_pinned_on_the_failure_of_the_command_that_follows(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount()).WithContact(1001, Address, "Marta");
        account.Refuses(driver.Command.Split(' ')[0] + " " + driver.Command.Split(' ')[1], "rate_limited", status: 429, retryAfter: 30);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "email", Address), Ct);

        var error = ReplyOperations.Failed(operation, result);
        Assert.Equal(("rate_limited", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal("1001", PinnedContact(result));
        Assert.Equal(["contact find", driver.Command], Lines(account));
    }

    /// <summary>
    /// Reply's exact lookup reads an email address or a LinkedIn URL and nothing else, so a person reached on any other
    /// channel is <c>invalid_channel_value</c> before anything is called: nobody is looked up under a value Reply
    /// cannot compare, and nobody is found or created by it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ByTheValue))]
    public async Task A_person_on_a_channel_reply_looks_nobody_up_by_is_refused_before_anything_is_called(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount()).WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "phone", "+15550100123"), Ct);

        Assert.Equal("invalid_channel_value", ReplyOperations.Failed(operation, result).Code);
        Assert.Empty(account.Calls);
        Assert.Null(PinnedContact(result));
    }

    /// <summary>
    /// A LinkedIn URL identifies a person by its lowercased slug. Somebody typed the profile at Reply in mixed case, and
    /// Jason holds its key: the lookup asks by the URL on stdin and finds them, the command names them by that
    /// identifier, and they are pinned — and the reply's thread person, held under the mixed-case spelling, is the same
    /// person, so the reply goes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Finding))]
    public async Task A_person_reply_holds_under_a_mixed_case_linkedin_url_is_found_by_its_lowercased_slug(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.ThroughTheThread
            ? new ReplyAccount().WithThread(ThreadOnLinkedIn(ProfileAsTyped))
            : driver.Plant(new ReplyAccount()).WithContact(1001, null, linkedIn: ProfileAsTyped);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "linkedin", Profile), Ct);

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal("1001", PinnedContact(result));
        if (driver.ThroughTheThread)
        {
            Assert.Equal([$"inbox get {Thread}", driver.Command], Lines(account));
            Assert.Single(account.OutboundIn(Thread));
            return;
        }

        Assert.Equal(["contact find", driver.Command], Lines(account));
        Assert.Equal($$"""{"linkedin_url":"{{Profile}}"}""", account.Calls[0].Body);
        Assert.Equal("1001", account.Calls[1].Option("--contact-id"));
        Assert.Single(account.Contacts);
    }

    /// <summary>
    /// Reply holds nobody under the profile, and the way is <c>find_or_create</c>: the person is imported keyed by the
    /// LinkedIn URL, which the CLI writes as its lowercased slug, and pinned.
    /// </summary>
    [Theory]
    [MemberData(nameof(Creating))]
    public async Task A_person_given_by_a_linkedin_url_reply_holds_nobody_for_is_imported_by_it_and_pinned(string operation)
    {
        var driver = Drivers[operation];
        using var account = driver.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await ReplyOperations.InvokeAsync(api, operation, Input(operation, "linkedin", Profile), Ct);

        ReplyOperations.Succeeded(operation, result);
        Assert.Equal("1001", PinnedContact(result));
        Assert.Equal(["contact find", driver.Command], Lines(account));
        var person = JsonNode.Parse(account.Calls[1].Body!)!.AsObject();
        Assert.Equal((Profile, "Marta"), ((string?)person["linkedin_url"], (string?)person["first_name"]));
        Assert.False(person.ContainsKey("email"));
        var made = Assert.Single(account.Contacts)!;
        Assert.Equal((1001, Profile), ((int)made["id"]!, (string?)made["linkedInUrl"]));
    }

    /// <summary>
    /// The way is <c>pinned_only</c>: a read about a person works by the pin alone, so a person nobody pinned fails
    /// <c>contact_not_linked</c> before anything is called — nobody is looked up and nobody is created.
    /// </summary>
    [Theory]
    [MemberData(nameof(PinnedOnly))]
    public async Task A_way_that_works_by_the_pin_alone_asks_nothing_for_a_person_nobody_pinned(string operation)
    {
        using var account = new ReplyAccount().WithContact(1001, Address, "Marta");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var input = new JsonObject
        {
            ["args"] = new JsonObject { ["limit"] = 20, ["offset"] = 0, ["source"] = "all" },
            ["contacts"] = new JsonArray(new JsonObject { ["id"] = ContactId, ["external_ids"] = new JsonObject() }),
            ["campaign"] = Campaign(),
        };
        var error = ReplyOperations.Failed(operation, await ReplyOperations.InvokeAsync(api, operation, input, Ct));

        Assert.Equal(("contact_not_linked", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    // -------------------------------------------------------------------------------------------------------

    /// <summary>The composed input each operation's document accepts, about one person on one channel.</summary>
    private static JsonObject Input(string operation, string channel, string value, string? pin = null, string contactId = ContactId)
    {
        var person = new JsonObject { ["id"] = contactId };
        if (operation != "conversation.reply")
        {
            person["first_name"] = "Marta";
            person["last_name"] = "Alvarez";
            person["company"] = "Puerto Analytics";
            person["title"] = "Head of Growth";
            person["time_zone"] = null;
        }

        person["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = value, ["primary"] = true });
        person["external_ids"] = pin is null ? new JsonObject() : new JsonObject { ["contact"] = pin };

        var input = new JsonObject
        {
            ["args"] = operation switch
            {
                "campaign.enroll" => new JsonObject
                {
                    ["campaign"] = new JsonObject { ["external_id"] = "7" },
                    ["channel"] = channel,
                    ["collision"] = "skip",
                    ["start"] = new JsonObject { ["position"] = "first_step" },
                    ["first_touch"] = "authored_delay",
                },
                "list_membership.add" => new JsonObject { ["list"] = new JsonObject { ["external_id"] = "9" }, ["channel"] = channel },
                "optout.record" => new JsonObject
                {
                    ["channel"] = channel,
                    ["statement"] = "please take me off your list",
                    ["arrived_via"] = "social",
                    ["said_at"] = "2026-10-03T08:14:00Z",
                    ["learned_at"] = "2026-10-03T08:20:00Z",
                },
                "enrollment.stop" => new JsonObject { ["channel"] = channel, ["reason"] = new JsonObject { ["code"] = "not_interested" } },
                "conversation.reply" => new JsonObject
                {
                    ["channel"] = channel,
                    ["thread"] = new JsonObject { ["external_id"] = Thread.ToString(CultureInfo.InvariantCulture) },
                    ["body"] = "Thursday at 10 works.",
                },
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "No input for this operation."),
            },
            ["contacts"] = new JsonArray(person),
            ["campaign"] = Campaign(),
        };

        if (OperationCatalog.Find(operation)!.IdempotencyKey.Value == "required")
        {
            input["idempotency_key"] = WorkItemId;
        }

        return input;
    }

    private static JsonObject Campaign() => new()
    {
        ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        ["name"] = "Q3 LatAm founders",
        ["status"] = "active",
        ["external_ids"] = new JsonObject { ["campaign"] = "7" },
    };

    /// <summary>The planted LinkedIn thread, its person's profile spelled as given.</summary>
    private static JsonObject ThreadOnLinkedIn(string profile)
    {
        var thread = ReplyConversationListTests.Thread(Thread, "2026-10-03T07:41:09Z", channel: "linkedIn");
        thread["contact"]!["linkedInProfileUrl"] = profile;
        return thread;
    }

    /// <summary>The contact identifier the answer pins — beside a result, or on a failure — or null where it pins none.</summary>
    private static string? PinnedContact(PluginInvocationResult result) => result.Outcome switch
    {
        InvocationOutcome.Succeeded succeeded => (string?)succeeded.ExternalIds?["contact"],
        InvocationOutcome.Failed failed => (string?)failed.Error.ExternalIds?["contact"],
        _ => null,
    };

    private static IReadOnlyList<string> Lines(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];

    private static IEnumerable<string> Ids(params ProviderContact[] ways) =>
        OperationCatalog.All.Where(contract => ways.Contains(contract.Preflight.ProviderContact)).Select(contract => contract.Id);

    private static TheoryData<string> Of(params ProviderContact[] ways) => Data(Ids(ways));

    private static TheoryData<string> Data(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        foreach (var value in values)
        {
            data.Add(value);
        }

        return data;
    }

    // -------------------------------------------------------------------------------------------------------
    // The runtime's half: what it writes down from the plugin's answer
    // -------------------------------------------------------------------------------------------------------

    private static readonly DateTime Noon = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Scene(ProviderOutcomeRecorder Recorder, Contact Contact, WorkItem Item, Attempt Attempt);

    /// <summary>
    /// One claimed item of the operation, about one person whom an earlier attempt pinned for the official package; the
    /// second attempt is out at the plugin.
    /// </summary>
    private static async Task<Scene> SeedAsync(JasonDbContext db, string operation, string pinned)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), CreatedAt = Noon, UpdatedAt = Noon };
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon);
        item.Contact = contact;
        item.Status = WorkItemStatus.Processing;
        var first = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon);
        var second = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Running, Noon);
        second.Provenance = new AttemptProvenanceDto(
            ReplyPlugins.PluginId, "0.4.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            operation, 1, PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC", "rts_01K5B7Q2WE5X3M9T0YH4C6RDND",
            RouteScope.GlobalDefault, "sha256:1", CorrelationId: second.PublicId);

        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.WorkItems.Add(item);
        db.Attempts.Add(first);
        db.Attempts.Add(second);
        await db.SaveChangesAsync(Ct);

        var store = new ExternalIdStore(new JournalWriter(clock), clock);
        store.Apply(db, OperationCatalog.Find(operation)!, ReplyPlugins.PluginId, item, campaign, contact, first, new JsonObject { ["contact"] = pinned });
        await db.SaveChangesAsync(Ct);

        var recorder = new ProviderOutcomeRecorder(
            new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(o => o.RetryDelaySeconds = 0)),
            store,
            TestOptions.PluginSettings(_ => { }));
        return new Scene(recorder, contact, item, second);
    }
}
