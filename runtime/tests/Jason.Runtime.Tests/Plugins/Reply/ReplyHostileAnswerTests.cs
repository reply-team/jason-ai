using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;

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
    private const string SequencePath = "/v3/sequences/7";
    private const string ImportPath = "/v3/contacts/import";
    private const string ListsPath = "/v3/contacts/1001/lists";
    private const string AddPath = "/v3/contact-lists/9/add-contacts";
    private const string BulkPath = "/v3/sequences/7/contact-links/bulk";

    /// <summary>A profile nobody has signed into, which is how an account with no credential is asked for.</summary>
    private const string SignedOut = "signed-out";

    /// <summary>
    /// The CLI's own refusal of a profile it does not hold, in the envelope <c>--json</c> prints it in. The
    /// exit code is the same one a missing credential ends with; the code beside it is what separates them.
    /// </summary>
    private const string UsageRefusal =
        """{"error":{"code":"usage.profile","title":"Unknown profile 'acme'.","hint":"Create it with `profile add acme`."}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stdout_that_is_not_json_at_all_is_treated_as_no_answer()
    {
        // A CLI that fell over prints whatever it prints. There is no envelope to read, so there is no answer —
        // and this call changes nothing, so asking again costs nothing.
        var noise = string.Join('\n', Enumerable.Range(0, 20).Select(line => "reply: unhandled exception at frame " + line));
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("GET", SequencePath, 1, noise);

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Fact]
    public async Task An_envelope_whose_data_is_null_where_an_object_was_expected_is_no_answer_either()
    {
        // The envelope parses and the status says success; the body it carries says nothing. Reading a campaign
        // out of null would mean inventing one, so the read is reported as one that did not arrive.
        var envelope = new JsonObject { ["code"] = 200, ["data"] = null };
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Prints("GET", SequencePath, 0, envelope.ToJsonString());

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Fact]
    public async Task A_two_hundred_that_leaves_out_the_field_the_mapping_needs_is_reported_rather_than_guessed()
    {
        // The import answers per item, and an item with no identifier is a person this call cannot work with.
        // Guessing one — the first contact in the account, say — would enrol or list somebody else.
        var answered = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["status"] = "created" }),
        };

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Answers("POST", ImportPath, 200, answered.ToJsonString());

        var error = await FailedAsync(account, ReplyOperations.MembershipAdd, ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);

        // What came back travels as evidence rather than as a reading of it: the item is reported, never mapped.
        Assert.Contains("created", error.Details!["provider_item"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_larger_than_the_capture_that_has_to_hold_it_is_no_answer()
    {
        // The exec capture is bounded, and a body past that bound arrives cut in half: valid JSON up to the cut
        // and nothing after it. Reading what survived would mean acting on half an answer, so the ending is the
        // one every unreadable answer to a read gets.
        const int Capture = 65_536;
        var enormous = new JsonObject
        {
            ["id"] = ReplyOperations.Sequence,
            ["name"] = new string('a', Capture * 3),
            ["status"] = "active",
            ["isArchived"] = false,
        };

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Answers("GET", SequencePath, 200, enormous.ToJsonString());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct, paths => Capping(paths, Capture));

        var result = await ReplyOperations.InvokeAsync(
            api,
            ReplyOperations.CampaignGet,
            ReplyOperations.CampaignGetInput(),
            Ct);
        var error = ReplyOperations.Failed(ReplyOperations.CampaignGet, result);

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);

        // And it really was the cut that made it unreadable, said by the host that made it rather than inferred
        // from the package having failed: the `exec` diagnostic records that what it captured was not all there
        // was to capture.
        var diagnostics = await File.ReadAllTextAsync(Path.Combine(result.Launch!.WorkDir, "stderr.log"), Ct);
        Assert.Contains("\"truncated\":true", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_arrives_after_the_timeout_is_transient_on_a_read()
    {
        // The call was cut off, and this one changes nothing at the provider: there is nothing to recover and
        // nothing a repeat could do twice.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Hangs("GET", SequencePath, 20_000);

        var error = await FailedAsync(
            account,
            ReplyOperations.CampaignGet,
            ReplyOperations.CampaignGetInput(),
            timeout: TimeSpan.FromSeconds(4));

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
        account.Hangs("POST", BulkPath, 20_000);

        var error = await FailedAsync(
            account,
            ReplyOperations.Enroll,
            ReplyOperations.EnrollInput(pinned: ReplyOperations.Ensured),
            timeout: TimeSpan.FromSeconds(8));

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.True(error.Details!["timed_out"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_empty_bodied_refusal_of_the_credential_is_never_parsed()
    {
        // Reply's 401 carries no body at all, which is the answer a plugin is likeliest to meet: a package that
        // parsed one would fail on its most ordinary bad day, and it would fail with the wrong word.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Answers("POST", ImportPath, 401, string.Empty);

        var error = await FailedAsync(account, ReplyOperations.MembershipAdd, ReplyOperations.MembershipAddInput());

        Assert.Equal("unauthorized", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(401, error.Details!["status"]!.GetValue<int>());

        // Nothing was read out of the body, because there was none: no provider code is claimed.
        Assert.Null(error.Details!["provider_code"]);
    }

    [Fact]
    public async Task A_two_hundred_carrying_an_array_where_an_object_was_expected_is_no_answer()
    {
        var listed = new JsonArray(new JsonObject { ["id"] = ReplyOperations.Sequence });
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.Answers("GET", SequencePath, 200, listed.ToJsonString());

        var error = await FailedAsync(account, ReplyOperations.CampaignGet, ReplyOperations.CampaignGetInput());

        // A page where one sequence was asked for is not a sequence, whatever the status said, and taking the
        // first element would be reading a campaign out of something that never named one.
        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
    }

    [Fact]
    public async Task A_two_hundred_carrying_an_object_where_an_array_was_expected_is_no_answer_either()
    {
        // The recovery read answers a bare array of the lists this person is on. An object is not an empty
        // array, and reading it as one would turn a reading into a second write.
        var notAnArray = new JsonObject { ["items"] = new JsonArray() };
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Answers("GET", ListsPath, 200, notAnArray.ToJsonString());

        var error = await FailedAsync(
            account,
            ReplyOperations.MembershipAdd,
            ReplyOperations.MembershipAddInput(pinned: ReplyOperations.Ensured),
            attempt: 2);

        Assert.Equal("provider_unavailable", error.Code);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.Empty(account.MembersOf(ReplyOperations.List));
    }

    [Fact]
    public async Task A_per_item_failure_of_the_list_add_whose_value_is_neither_documented_shape_is_reported()
    {
        // The add answers a failures-only dictionary, and what the value against a present key is, Reply's own
        // description gives two incompatible answers to. So the key's presence is the failure and the value is
        // only ever evidence — which has to hold for a value that is neither of the two spellings.
        var refused = new JsonObject
        {
            [ReplyOperations.Ensured] = new JsonObject { ["unexpected"] = new JsonArray(1, 2, 3) },
        };

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Answers("POST", AddPath, 200, refused.ToJsonString());

        var error = await FailedAsync(
            account,
            ReplyOperations.MembershipAdd,
            ReplyOperations.MembershipAddInput(pinned: ReplyOperations.Ensured));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("unexpected", error.Details!["provider_item"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_per_item_failure_of_the_enrollment_whose_value_is_neither_documented_shape_is_reported()
    {
        // The same rule on the other write, whose per-item set is published as open: a value with no word in it
        // at all falls to the default branch rather than being read as the nearest word that fits.
        var refused = new JsonObject
        {
            ["added"] = new JsonArray(),
            ["notProcessed"] = new JsonObject { [ReplyOperations.Ensured] = 8 },
        };

        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Answers("POST", BulkPath, 200, refused.ToJsonString());

        var error = await FailedAsync(
            account,
            ReplyOperations.Enroll,
            ReplyOperations.EnrollInput(pinned: ReplyOperations.Ensured));

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("8", error.Details!["provider_item"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReplyOperations.MembershipAdd)]
    [InlineData(ReplyOperations.Enroll)]
    public async Task A_write_the_cli_refused_for_want_of_a_credential_is_not_a_write_that_may_have_happened(string operation)
    {
        // The day-one failure: the operator has not signed in. The CLI decides that before it builds a request,
        // so nothing reached the account — and calling it a lost answer would stop a work item for a person over
        // a write nobody made, on the two operations where that verdict is the expensive one. Both are driven
        // here, because the read/write mark is what the old reading turned on.
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
        Assert.Equal("auth.required", error.Details!["provider_code"]!.GetValue<string>());
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
        account.Prints("POST", ImportPath, 2, string.Empty, stderr: UsageRefusal);

        var error = await FailedAsync(
            account,
            ReplyOperations.MembershipAdd,
            ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_call_failed", error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal("usage.profile", error.Details!["provider_code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_lost_answer_with_nothing_the_cli_said_is_still_a_lost_write()
    {
        // And the third: exit 1, nothing on stdout, nothing of the CLI's own on stderr. That is the ending the
        // read/write mark was written for — the request may have gone out and been cut off — and it stays what
        // it was.
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithContact(1001, ReplyOperations.Address, ReplyOperations.FirstName);
        account.Prints("POST", ImportPath, 1, string.Empty, stderr: "reply: connection reset while reading the response");

        var error = await FailedAsync(
            account,
            ReplyOperations.MembershipAdd,
            ReplyOperations.MembershipAddInput());

        Assert.Equal("provider_answer_lost", error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Null(error.Details!["provider_code"]);
    }

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
        JsonObject? binding = null)
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

        return ReplyOperations.Failed(operation, result);
    }

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
