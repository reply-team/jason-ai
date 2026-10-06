using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in vendor CLI's own contract. Every later Reply test trusts this program to behave as the real one
/// was measured to behave, so what it must do is asserted here rather than assumed: <c>{code, data}</c> on stdout
/// for any status, exit 1 from 400 up, exit 2 for a usage error before anything is called, exit 1 with nothing
/// parseable when it holds no credential, and a call log written before the call is answered.
/// </summary>
public class ReplyStubTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The global prefix the package always passes, so each test says only what it is about.</summary>
    private static string[] Prefix => ["--json", "-q"];

    // -------------------------------------------------------------------------------------------------------
    // Nothing a Reply test resolves may be a program the machine installed
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_reply_search_path_never_carries_the_machine_path()
    {
        var machine = Environment.GetEnvironmentVariable("PATH");
        var path = ReplyPlugins.SearchPath.Path!;

        // A workstation can have a real `reply` installed by npm and signed into a real account. The search path
        // a Reply test resolves on is built here, so nothing on the machine can answer for the name.
        if (!string.IsNullOrEmpty(machine))
        {
            Assert.DoesNotContain(machine, path, StringComparison.Ordinal);
        }

        var host = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // Two kinds of directory and no third: the tree the tests were built into, and a .NET install root,
            // which a program of ours needs to start at all.
            Assert.True(
                ReplyPlugins.IsInsideTheTestTree(Path.Combine(directory, "anything")) || File.Exists(Path.Combine(directory, host)),
                $"'{directory}' is neither the test tree nor a .NET install root.");
        }
    }

    [Fact]
    public void The_declared_program_resolves_to_the_stand_in_inside_the_test_tree()
    {
        var resolver = new ExecutableResolver(ReplyPlugins.SearchPath);

        var resolved = resolver.Resolve(new ExecutableRequest(ReplyPlugins.ExecutableName, null, null), 0);

        Assert.Null(resolved.Problem);
        ReplyPlugins.AssertInsideTheTestTree(resolved.Path);
        Assert.Equal(ReplyPlugins.ExecutablePath, resolved.Path);
    }

    // -------------------------------------------------------------------------------------------------------
    // How it answers
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_stand_in_prints_code_and_data_and_exits_one_when_the_status_is_four_hundred()
    {
        using var account = new ReplyAccount();
        account.Answers("GET", "/v3/sequences/7", 404, Problem(404, "sequence.notFound").ToJsonString());

        var (exit, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        // The real CLI prints {code, data} for any status and exits 1 from 400 up; a stand-in that exited 0 would
        // let a plugin pass a test it would fail against the real thing.
        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(404, code);
        Assert.Equal("sequence.notFound", data!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_status_below_four_hundred_is_a_success()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", false, 11, 12);

        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal("Nurture", data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_empty_bodied_four_hundred_and_one_answers_with_no_data_at_all()
    {
        using var account = new ReplyAccount();
        account.Answers("GET", "/v3/sequences/7", 401, string.Empty);

        var (exit, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        // Reply's 401 has an empty body. A plugin that parsed one would fail on the one answer it is most likely
        // to meet, so the stand-in reproduces it rather than inventing a problem document.
        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(401, code);
        Assert.Null(data);
    }

    // -------------------------------------------------------------------------------------------------------
    // Usage: the command surface, and nothing beyond it
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_path_that_does_not_start_with_a_slash_is_a_usage_error_and_exits_two()
    {
        using var account = new ReplyAccount();

        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, "api", "v3/sequences/7"], Ct);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("error:", stderr, StringComparison.Ordinal);

        // A usage error is decided before anything is called, so nothing was called.
        Assert.Empty(account.Calls);
    }

    [Theory]
    [InlineData("-k")]
    [InlineData("--api-key")]
    [InlineData("--verbose")]
    [InlineData("--user-id")]
    [InlineData("--user-email")]
    [InlineData("--pretty")]
    [InlineData("--insecure")]
    public async Task An_unknown_flag_is_a_usage_error_and_exits_two(string flag)
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");

        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, flag, "value", "api", "/v3/sequences/7"], Ct);

        // This is what turns the forbidden-flag rule into something the stand-in itself enforces: a plugin that
        // reached for one of these fails here, before any assertion about the argument vector runs.
        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains(flag, stderr, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_body_that_is_not_read_from_stdin_is_a_usage_error()
    {
        using var account = new ReplyAccount();
        var body = new JsonObject { ["email"] = "written@example.test" };

        var (exit, _, stderr) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts", "--method", "POST", "--body", body.ToJsonString()],
            Ct);

        // A body on the command line would be a person's address in the runtime's log. The CLI takes '-' and
        // nothing else, so the rule holds even against a plugin that tried.
        Assert.Equal(2, exit);
        Assert.Contains("--body", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_version_command_a_manifest_may_name_is_answered_before_anything_else()
    {
        using var account = new ReplyAccount();

        var (exit, stdout, stderr) = await account.RunAsync(["--version"], Ct);

        // A reload runs this on whatever it resolved, for a plugin that declares a minimum version — so a
        // stand-in that could not answer it would make the package unavailable and nothing else could be proven
        // about it. It is answered before the account is looked for, because a version is not account business.
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        Assert.Matches(@"\d+\.\d+\.\d+", stdout);
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task A_command_the_package_does_not_use_is_a_usage_error()
    {
        using var account = new ReplyAccount();

        var (exit, _, stderr) = await account.RunAsync([.. Prefix, "auth", "login"], Ct);

        Assert.Equal(2, exit);
        Assert.Contains("auth", stderr, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------------
    // The account, found the way the real CLI finds its store
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_store_this_stand_in_did_not_plant_is_refused_before_anything_is_written()
    {
        // The safety rule this program is held to. Which directory it reads is decided by a variable a test
        // sets, and a test that forgot would find the operator's own Reply configuration — where it would
        // create profiles, plant accounts and write a log of every call. So the store has to carry a file that
        // says a test made it, and without that file nothing at all happens: no directory, no record, no
        // answer.
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");

        // A directory that is not one of this stand-in's, modelled by taking the mark off one that is.
        File.Delete(Path.Combine(Directory.GetParent(account.Root)!.FullName, ".jason-stand-in"));

        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal("usage.store", JsonNode.Parse(stderr.Trim())!["error"]!["code"]!.GetValue<string>());

        // Nothing was read and nothing was written: the call is not even in the log, which is the first thing
        // this program does for a call it will answer.
        Assert.Empty(account.Calls);
    }

    [Fact]
    public async Task An_unauthenticated_account_is_refused_the_way_the_real_cli_refuses_one()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");

        // A profile nobody signed into: the store is there, the credential is not, and the CLI decides that
        // before it has built a request, let alone received an HTTP status. reply-cli 0.5.1 calls that a usage
        // error — exit 2, nothing on stdout — and says why in the error envelope `--json` prints on stderr. The
        // code in that envelope is the only thing that separates this from an unknown flag, which ends the same
        // way, so it is what the package reads.
        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, "--profile", "nobody", "api", "/v3/sequences/7"], Ct);

        Assert.Equal(2, exit);
        Assert.Null(Parsed(stdout));
        Assert.Equal("auth.required", JsonNode.Parse(stderr.Trim())!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_account_is_found_through_the_environment_rather_than_an_argument()
    {
        using var first = new ReplyAccount();
        using var second = new ReplyAccount();
        first.WithSequence(7, "Nurture", "active");
        second.WithSequence(7, "Winback", "paused");

        string[] arguments = [.. Prefix, "api", "/v3/sequences/7"];
        var (_, fromFirst, _) = await first.RunAsync(arguments, Ct);
        var (_, fromSecond, _) = await second.RunAsync(arguments, Ct);

        // The same argument vector, two accounts, two answers: nothing on the command line says which account
        // this is. The child's environment is built from nothing and the package declares no `env`, so this is
        // the only way a test could choose one — and it is the way the real CLI finds its store.
        Assert.Equal("Nurture", Answer(fromFirst).Data!["name"]!.GetValue<string>());
        Assert.Equal("Winback", Answer(fromSecond).Data!["name"]!.GetValue<string>());
        Assert.DoesNotContain(first.Root, string.Join(' ', Assert.Single(first.Calls).Args), StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------------------------------------
    // The call log
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The inbox filter's <c>from</c> is exclusive, as Reply's was measured to be: a <c>from</c> one millisecond
    /// before a thread's last activity lists it, and a <c>from</c> equal to it does not.
    /// </summary>
    [Theory]
    [InlineData("2026-10-01T07:41:08.999Z", 1)]
    [InlineData("2026-10-01T07:41:09.000Z", 0)]
    public async Task The_inbox_filter_s_from_is_exclusive_as_reply_s_is(string from, int listed)
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithThread(ReplyConversationListTests.Thread(501, "2026-10-01T07:41:09Z"));
        var body = new JsonObject { ["sequenceIds"] = new JsonArray(7), ["source"] = "inbox", ["from"] = from };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/inbox/threads/filter?top=10&skip=0", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        Assert.Equal(0, exit);
        Assert.Equal(listed, JsonNode.Parse(stdout)!["data"]!["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Every_call_is_recorded_before_it_runs()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");
        account.Hangs("GET", "/v3/sequences/7", 3000);

        var run = account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (account.Calls.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        // Written before the answer, not after it: the entry is readable while the program is still working. The
        // state left behind could never show that, because a provider answers the same either way.
        var recorded = Assert.Single(account.Calls);
        Assert.False(run.IsCompleted);
        Assert.Equal("GET", recorded.Method);
        Assert.Equal("/v3/sequences/7", recorded.Path);
        Assert.Null(recorded.Body);
        Assert.Equal([.. Prefix, "api", "/v3/sequences/7"], recorded.Args);

        var (exit, _, _) = await run;
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task The_body_is_read_from_stdin_when_the_arguments_say_so()
    {
        using var account = new ReplyAccount();
        var body = new JsonObject { ["email"] = "sent@example.test", ["firstName"] = "Sent" };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        Assert.Equal(0, exit);
        Assert.Equal(201, Answer(stdout).Code);

        var recorded = Assert.Single(account.Calls);
        Assert.Equal("POST", recorded.Method);
        Assert.Equal(body.ToJsonString(), recorded.Body);

        // The address travelled on stdin. Arguments are written to the runtime's log, and a contact's address may
        // never be in one.
        Assert.DoesNotContain("sent@example.test", string.Join(' ', recorded.Args), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calls_since_a_mark_are_only_the_ones_that_followed_it()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithList(5, "Warm", 1001);

        await account.RunAsync([.. Prefix, "api", "/v3/contacts/1001/lists"], Ct);
        var mark = account.Mark();
        await account.RunAsync([.. Prefix, "api", "/v3/contacts/1001/statuses"], Ct);

        // An assertion about one attempt has to be able to start where that attempt started; over a whole
        // lifetime, "it never wrote before it read" is answered by the attempt before it.
        Assert.Equal(2, account.Calls.Count);
        var since = Assert.Single(account.CallsSince(mark));
        Assert.Equal("/v3/contacts/1001/statuses", since.Path);
    }

    // -------------------------------------------------------------------------------------------------------
    // The shapes Reply answers in
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_collection_read_answers_the_paged_envelope()
    {
        using var account = new ReplyAccount();
        account.WithList(5, "Warm").WithList(6, "Cold");

        var (_, all, _) = await account.RunAsync([.. Prefix, "api", "/v3/contact-lists"], Ct);
        var (_, page, _) = await account.RunAsync([.. Prefix, "api", "/v3/contact-lists?limit=1"], Ct);

        Assert.Equal(2, Answer(all).Data!["items"]!.AsArray().Count);
        Assert.False(Answer(all).Data!["hasMore"]!.GetValue<bool>());
        Assert.Single(Answer(page).Data!["items"]!.AsArray());
        Assert.True(Answer(page).Data!["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_validation_four_hundred_carries_errors_and_no_code()
    {
        using var account = new ReplyAccount();
        var body = new JsonObject();

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(400, code);

        // 400 is two shapes, and which member is present is the only way to tell them apart.
        Assert.Null(data!["code"]);
        var problem = Assert.Single(data["errors"]!.AsArray());
        Assert.Equal("/email", problem!["pointer"]!.GetValue<string>());
        Assert.NotNull(problem["detail"]);
    }

    [Fact]
    public async Task A_business_four_hundred_carries_a_code_and_no_errors()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithSequence(7, "Nurture", "paused", true, 11);
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001) };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/sequences/7/contact-links/bulk", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(400, code);
        Assert.Equal("sequence.archived", data!["code"]!.GetValue<string>());
        Assert.Null(data["errors"]);
    }

    [Fact]
    public async Task A_bulk_enrolment_reports_per_item_failure_inside_a_two_hundred()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithContact(1002, "gone@example.test", "Gone", optedOut: true);
        account.WithSequence(7, "Nurture", "active", false, 11);
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001, 1002, 4242) };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/sequences/7/contact-links/bulk", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        // A 200 that contains failures: the status says the call was taken, not that the work was done.
        Assert.Equal(0, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal([1001], data!["added"]!.AsArray().Select(added => added!.GetValue<int>()));
        var notProcessed = data["notProcessed"]!.AsObject();
        Assert.Equal(["1002", "4242"], notProcessed.Select(entry => entry.Key).Order());
        Assert.NotNull(notProcessed["4242"]!["error"]);
        Assert.NotNull(notProcessed["4242"]!["errorDetails"]);
        Assert.Equal([1001], account.EnrolledIn(7));
    }

    [Fact]
    public async Task A_list_add_reports_per_item_failure_as_a_bare_dictionary()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithList(5, "Warm");
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001, 4242) };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contact-lists/5/add-contacts", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        Assert.Equal(0, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);

        // The failures-only dictionary, whose values the documentation contradicts itself about: the presence of
        // the key is the failure, and nothing here is asked to branch on what it holds.
        Assert.Equal(["4242"], data!.AsObject().Select(entry => entry.Key));
        Assert.Equal([1001], account.MembersOf(5));
    }

    [Fact]
    public async Task Adding_a_contact_that_is_already_on_the_list_says_nothing_at_all()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithList(5, "Warm", 1001);
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001) };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contact-lists/5/add-contacts", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        // Re-adding is documented nowhere, and the answer does not distinguish it. That is why a recovery read is
        // the only sound way to tell an add from an add that had already happened.
        Assert.Equal(0, exit);
        Assert.Empty(Answer(stdout).Data!.AsObject());
        Assert.Equal([1001], account.MembersOf(5));
    }

    [Fact]
    public async Task A_sequence_answers_its_steps_as_a_parent_chain_with_no_ordering_field()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", false, 11, 12, 13);

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        var sequence = Answer(stdout).Data!;
        Assert.False(sequence["isArchived"]!.GetValue<bool>());
        var steps = sequence["steps"]!.AsArray();
        Assert.Equal(3, steps.Count);
        Assert.Null(steps[0]!["parentId"]);
        Assert.Equal(11, steps[1]!["parentId"]!.GetValue<int>());
        Assert.NotNull(steps[1]!["delayInMinutes"]);
        Assert.NotNull(steps[1]!["type"]);

        // There is no ordering field on a Reply step, and a stand-in that invented one would let a plugin ship a
        // step resolution that cannot work against the real thing.
        Assert.All(steps, step => Assert.Equal(["delayInMinutes", "id", "parentId", "type"], step!.AsObject().Select(member => member.Key).Order()));
    }

    [Fact]
    public async Task A_participation_read_answers_not_in_sequence_when_there_is_none()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithSequence(7, "Nurture", "active", false, 11);

        var (missing, before, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/contacts/1001"], Ct);
        account.WithEnrollment(7, 1001);
        var (present, after, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/contacts/1001"], Ct);

        Assert.Equal(1, missing);
        Assert.Equal(404, Answer(before).Code);
        Assert.Equal("sequenceContact.notInSequence", Answer(before).Data!["code"]!.GetValue<string>());
        Assert.Equal(0, present);
        Assert.Equal("active", Answer(after).Data!["statusInSequence"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_contact_s_statuses_answer_the_opt_out_register_and_a_pin_that_no_longer_resolves()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "gone@example.test", "Gone", optedOut: true);
        account.WithSequence(7, "Nurture", "active", false, 11).WithEnrollment(7, 1001);

        var (_, held, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/1001/statuses"], Ct);
        var (exit, unknown, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/4242/statuses"], Ct);

        var statuses = Answer(held).Data!;
        Assert.Equal(1001, statuses["contactId"]!.GetValue<int>());
        Assert.True(statuses["isOptedOut"]!.GetValue<bool>());
        Assert.NotNull(statuses["callStatus"]);
        Assert.NotNull(statuses["meetingStatus"]);
        Assert.Equal(7, Assert.Single(statuses["sequences"]!.AsArray())!["id"]!.GetValue<int>());

        // The same call answers the pin-validity question: a pin that no longer resolves is a 404, not silence.
        Assert.Equal(1, exit);
        Assert.Equal(404, Answer(unknown).Code);
        Assert.Equal("contact.notFound", Answer(unknown).Data!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_contact_s_lists_are_a_bare_array_and_hold_only_the_shared_ones()
    {
        // What a live account did, modelled here because the package's design turns on it: this endpoint
        // answered `[]` for a contact provably on a list that was not shared — checked twice a minute apart,
        // and confirmed from the other side by the search below. A stand-in that answered for private lists
        // would let an offline test prove a recovery read the provider does not perform.
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithList(5, "Warm", shared: true, 1001).WithList(6, "Private", 1001);

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/1001/lists"], Ct);

        var lists = Answer(stdout).Data!.AsArray();
        var only = Assert.Single(lists);
        Assert.Equal(5, only!["id"]!.GetValue<int>());
        Assert.Equal("Warm", only["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_search_answers_for_a_private_list_that_the_contacts_own_lists_do_not_mention()
    {
        // The other half of the same discovery, and the reason the recovery read is made from the list's side:
        // the search scoped by `listId` returns the person at once for the very list the endpoint above will
        // not mention.
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithList(6, "Private", 1001);

        var (_, lists, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/1001/lists"], Ct);
        var (_, searched, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/filter?top=1000", "--method", "POST", "--body", "-"],
            """{"listId":6}""",
            Ct);

        Assert.Empty(Answer(lists).Data!.AsArray());
        var page = Answer(searched).Data!.AsObject();
        Assert.Equal(1001, Assert.Single(page["items"]!.AsArray())!["id"]!.GetValue<int>());
        Assert.False(page["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_search_pages_with_top_and_skip_and_says_whether_the_list_goes_on()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "first@example.test", "First");
        account.WithContact(1002, "second@example.test", "Second");
        account.WithList(6, "Private", 1001, 1002);

        var (_, first, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/filter?top=1&skip=0", "--method", "POST", "--body", "-"],
            """{"listId":6}""",
            Ct);
        var (_, second, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/filter?top=1&skip=1", "--method", "POST", "--body", "-"],
            """{"listId":6}""",
            Ct);

        // The page says there is more where it came from, and the one after it says there is not — which is the
        // difference the package reads to know whether it saw the whole list.
        Assert.True(Answer(first).Data!["hasMore"]!.GetValue<bool>());
        Assert.Equal(1001, Answer(first).Data!["items"]![0]!["id"]!.GetValue<int>());
        Assert.False(Answer(second).Data!["hasMore"]!.GetValue<bool>());
        Assert.Equal(1002, Answer(second).Data!["items"]![0]!["id"]!.GetValue<int>());
    }

    /// <summary>
    /// The free-text search as a live account answered it: every contact whose address contains the term, whatever
    /// the case of either — so a term finds the person who carries it and anybody whose address merely holds it.
    /// Telling the two apart is the caller's business.
    /// </summary>
    [Fact]
    public async Task A_search_term_finds_every_contact_whose_address_contains_it_whatever_the_case()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "marta@example.com", "Marta");
        account.WithContact(1002, "ana.marta@example.com", "Ana");
        account.WithContact(1003, "someone@example.org", "Someone");

        var (_, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/filter?top=5&skip=0", "--method", "POST", "--body", "-"],
            """{"searchTerm":"MARTA@EXAMPLE.COM"}""",
            Ct);

        var page = Answer(stdout).Data!.AsObject();
        Assert.Equal([1001, 1002], page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<int>()));
        Assert.False(page["hasMore"]!.GetValue<bool>());
        Assert.Equal(["hasMore", "items"], page.Select(member => member.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The search pages the same way whatever it searches by, and says nothing of a total: the only word for the end
    /// is <c>hasMore</c>, and a page past the end is empty.
    /// </summary>
    [Fact]
    public async Task A_search_term_pages_with_top_and_skip_and_says_where_it_ends()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "first@example.com", "First");
        account.WithContact(1002, "second@example.com", "Second");
        account.WithContact(1003, "third@example.com", "Third");

        async Task<JsonObject> PageAsync(int skip)
        {
            var (_, stdout, _) = await account.RunAsync(
                [.. Prefix, "api", $"/v3/contacts/filter?top=2&skip={skip}", "--method", "POST", "--body", "-"],
                """{"searchTerm":"example.com"}""",
                Ct);
            return Answer(stdout).Data!.AsObject();
        }

        var first = await PageAsync(0);
        var second = await PageAsync(2);
        var past = await PageAsync(1000000);

        Assert.Equal([1001, 1002], first["items"]!.AsArray().Select(item => item!["id"]!.GetValue<int>()));
        Assert.True(first["hasMore"]!.GetValue<bool>());
        Assert.Equal([1003], second["items"]!.AsArray().Select(item => item!["id"]!.GetValue<int>()));
        Assert.False(second["hasMore"]!.GetValue<bool>());
        Assert.Empty(past["items"]!.AsArray());
        Assert.False(past["hasMore"]!.GetValue<bool>());
        Assert.Null(first["total"]);
    }

    /// <summary>
    /// A live account ignored a rule set entirely and answered a page of everybody, which is why the package never
    /// sends one. The stand-in answers the same way, so a package that did would find the wrong person here too.
    /// </summary>
    [Fact]
    public async Task A_rule_set_is_ignored_and_the_search_answers_everybody()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "marta@example.com", "Marta");
        account.WithContact(1002, "someone@example.org", "Someone");

        var (_, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/filter?top=5&skip=0", "--method", "POST", "--body", "-"],
            """{"rules":[{"property":"email","condition":"equals","value":"marta@example.com"}]}""",
            Ct);

        var page = Answer(stdout).Data!.AsObject();
        Assert.Equal([1001, 1002], page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<int>()));
    }

    /// <summary>
    /// Reply's create path publishes no word for an address it already holds, so an account can hold two contacts
    /// under one address, and a search for it answers both.
    /// </summary>
    [Fact]
    public async Task An_account_can_hold_two_contacts_under_one_address_and_the_search_answers_both()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "marta@example.com", "Marta").WithDuplicate(1002, of: 1001);

        var (_, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/filter?top=5&skip=0", "--method", "POST", "--body", "-"],
            """{"searchTerm":"marta@example.com"}""",
            Ct);

        var items = Answer(stdout).Data!["items"]!.AsArray();
        Assert.Equal([1001, 1002], items.Select(item => item!["id"]!.GetValue<int>()));
        Assert.All(items, item => Assert.Equal("marta@example.com", (string?)item!["email"]));
    }

    // -------------------------------------------------------------------------------------------------------
    // The import, whose first-name rule decides how a contact is ensured
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_imported_item_without_a_first_name_is_not_imported()
    {
        using var account = new ReplyAccount();
        var item = new JsonObject { ["email"] = "nameless@example.test" };
        var body = new JsonObject { ["items"] = new JsonArray(item) };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/import", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        // Reply refuses an item with no first name, and Jason's contact projection allows none. That single rule
        // is why ensuring a contact is two paths rather than one.
        Assert.Equal(0, exit);
        var answered = Answer(stdout).Data!;
        var only = Assert.Single(answered["items"]!.AsArray());
        Assert.Equal("failed", only!["status"]!.GetValue<string>());
        Assert.Equal("Invalid data", only["error"]!.GetValue<string>());
        Assert.Null(only["id"]);
        Assert.Equal(1, answered["failed"]!.GetValue<int>());
        Assert.Equal(0, answered["added"]!.GetValue<int>());
        Assert.Empty(account.Contacts);
    }

    [Fact]
    public async Task An_import_answers_synchronously_and_deduplicates_by_email()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        var body = new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["email"] = "held@example.test", ["firstName"] = "Held" },
                new JsonObject { ["email"] = "fresh@example.test", ["firstName"] = "Fresh" },
                new JsonObject { ["email"] = "fresh@example.test", ["firstName"] = "Fresh" }),
        };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts/import", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        Assert.Equal(0, exit);
        var answered = Answer(stdout).Data!;
        var items = answered["items"]!.AsArray();

        // One call that creates or matches, with the identifiers positionally aligned to the request: that is
        // what makes ensuring a contact a single call with no address on the command line.
        Assert.Equal(["updated", "created", "skipped"], items.Select(item => item!["status"]!.GetValue<string>()));
        Assert.Equal(1001, items[0]!["id"]!.GetValue<int>());
        Assert.Equal(items[1]!["id"]!.GetValue<int>(), items[2]!["id"]!.GetValue<int>());
        Assert.Equal(1, answered["added"]!.GetValue<int>());
        Assert.Equal(1, answered["updated"]!.GetValue<int>());
        Assert.Equal(1, answered["skipped"]!.GetValue<int>());
        Assert.Equal(2, account.Contacts.Count);
    }

    [Fact]
    public async Task A_contact_created_on_its_own_answers_two_hundred_and_one()
    {
        using var account = new ReplyAccount();
        var body = new JsonObject { ["email"] = "fresh@example.test" };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contacts", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        // The path taken when there is no first name to import with: it needs only an address.
        Assert.Equal(0, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(201, code);
        Assert.Equal("fresh@example.test", data!["email"]!.GetValue<string>());
        Assert.Single(account.Contacts);
    }

    // -------------------------------------------------------------------------------------------------------
    // Being told to misbehave
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_answer_can_be_lost_after_the_write_has_landed()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.WithList(5, "Warm");
        account.LosesTheAnswerAfterWriting("POST", "/v3/contact-lists/5/add-contacts");
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001) };

        var (exit, stdout, _) = await account.RunAsync(
            [.. Prefix, "api", "/v3/contact-lists/5/add-contacts", "--method", "POST", "--body", "-"],
            body.ToJsonString(),
            Ct);

        // The provider acted and the caller cannot know it. Nothing but a recovery read can answer what happened,
        // which is the whole reason this ending is reproducible on demand.
        Assert.Equal(1, exit);
        Assert.Equal(502, Answer(stdout).Code);
        Assert.Equal([1001], account.MembersOf(5));
    }

    [Fact]
    public async Task An_instruction_is_spent_once_so_the_attempt_after_it_sees_the_account()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");
        account.Answers("GET", "/v3/sequences/7", 429, Problem(429, "rate.limited").ToJsonString());

        var (first, refused, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);
        var (second, answered, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        Assert.Equal(1, first);
        Assert.Equal(429, Answer(refused).Code);
        Assert.Equal(0, second);
        Assert.Equal(200, Answer(answered).Code);
    }

    [Fact]
    public async Task The_credential_it_holds_never_reaches_anything_the_caller_can_see()
    {
        using var account = new ReplyAccount();
        account.WithMarker("marker-e3b0c44298fc");
        account.WithSequence(7, "Nurture", "active");

        var (_, stdout, stderr) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

        Assert.Equal("marker-e3b0c44298fc", account.Marker);
        Assert.DoesNotContain(account.Marker, stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Marker, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Marker, string.Join(' ', Assert.Single(account.Calls).Args), StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------------
    // Holding a call open, for the tests that have to interrupt one
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_call_the_account_was_told_to_hold_says_it_started_and_waits_to_be_let_go()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");
        var directory = Path.Combine(Path.GetTempPath(), "jason-hold", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var started = Path.Combine(directory, "started");
        var release = Path.Combine(directory, "release");

        try
        {
            account.HoldsOn("GET", "/v3/sequences/7", started, release);
            var call = account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct);

            // It says it has started, which is the whole point: a caller can now interrupt a call it has
            // observed rather than one it hopes is running.
            var appeared = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(started) && DateTime.UtcNow < appeared)
            {
                await Task.Delay(25, Ct);
            }

            Assert.True(File.Exists(started), "The held call never said it had started.");
            Assert.False(call.IsCompleted, "The call answered without waiting to be let go.");

            await File.WriteAllTextAsync(release, string.Empty, Ct);
            var (exit, stdout, _) = await call.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            // And once let go it answers exactly as it would have: holding is about when, never about what.
            Assert.Equal(0, exit);
            Assert.Equal(200, Answer(stdout).Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_held_call_that_is_never_let_go_ends_at_its_cap_rather_than_holding_the_run_open()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");
        var directory = Path.Combine(Path.GetTempPath(), "jason-hold", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            // Nothing will ever create the sentinel. The cap is the last resort, so it is proven in
            // milliseconds rather than left as a promise about minutes.
            account.HoldsOn(
                "GET",
                "/v3/sequences/7",
                Path.Combine(directory, "started"),
                Path.Combine(directory, "never"),
                capMs: 200);

            var (exit, stdout, _) = await account
                .RunAsync([.. Prefix, "api", "/v3/sequences/7"], Ct)
                .WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.Equal(0, exit);
            Assert.Equal(200, Answer(stdout).Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // A sequence paused and started
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reply documents the pause as idempotent and answers the sequence itself, paused, whether it was just paused or
    /// already was.
    /// </summary>
    [Theory]
    [InlineData("active")]
    [InlineData("paused")]
    public async Task A_pause_leaves_the_sequence_paused_and_answers_it(string status)
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", status, false, 11);

        var (exit, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/pause", "--method", "POST"], null, Ct);

        Assert.Equal(0, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal("paused", (string?)data!["status"]);
        Assert.Equal("paused", (string?)account.Sequences.Single()!["status"]);
    }

    /// <summary>The start likewise: a draft goes live, a paused sequence resumes, a live one stays live.</summary>
    [Theory]
    [InlineData("new")]
    [InlineData("paused")]
    [InlineData("active")]
    public async Task A_start_leaves_the_sequence_live_and_answers_it(string status)
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", status, false, 11).WithContact(1001, "held@example.test", "Held").WithEnrollment(7, 1001);

        var (exit, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/start", "--method", "POST"], null, Ct);

        Assert.Equal(0, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal("active", (string?)data!["status"]);
        Assert.Equal("active", (string?)account.Sequences.Single()!["status"]);
    }

    /// <summary>
    /// The refusals Reply publishes for both: a sequence it does not hold, and one archived — an incompatible state,
    /// so a 409 rather than the 400 the bulk enrol answers for the same fact.
    /// </summary>
    [Theory]
    [InlineData("pause", 8, false, 404, "sequenceAction.notFound")]
    [InlineData("pause", 7, true, 409, "sequenceAction.archived")]
    [InlineData("start", 8, false, 404, "sequenceAction.notFound")]
    [InlineData("start", 7, true, 409, "sequenceAction.archived")]
    public async Task A_sequence_that_is_missing_or_archived_is_refused_by_both(string act, int sequence, bool archived, int status, string providerCode)
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", archived, 11).WithContact(1001, "held@example.test", "Held").WithEnrollment(7, 1001);

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", $"/v3/sequences/{sequence}/{act}", "--method", "POST"], null, Ct);

        Assert.Equal((status, providerCode), Coded(stdout));
        Assert.Equal("active", (string?)account.Sequences.Single()!["status"]);
    }

    /// <summary>
    /// The business refusals of a start: nobody to send to, or nothing to send from, each under the word Reply documents
    /// for it; the plugin carries whatever code arrives rather than relying on one.
    /// </summary>
    [Fact]
    public async Task A_start_with_nobody_enrolled_or_nothing_to_send_from_is_a_business_refusal()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "new", false, 11).WithSequence(8, "Outbound", "new", false, 21)
            .WithContact(1001, "held@example.test", "Held").WithEnrollment(8, 1001).WithoutSendingAccount(8);

        var (_, empty, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/start", "--method", "POST"], null, Ct);
        var (_, unsent, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/8/start", "--method", "POST"], null, Ct);

        Assert.Equal((400, "sequenceAction.noContacts"), Coded(empty));
        Assert.Equal((400, "sequenceAction.noEmailAccounts"), Coded(unsent));
        Assert.All(account.Sequences, sequence => Assert.Equal("new", (string?)sequence!["status"]));
    }

    /// <summary>
    /// Pausing a draft is a domain rule rejection, a 400 with no code: the one 400 word Reply documents for a pause names a
    /// team member's rejection, and this is not one. The plugin never asks, because the state it reads first already
    /// says the sequence never started.
    /// </summary>
    [Fact]
    public async Task Pausing_a_sequence_that_never_started_is_a_domain_rule_rejection()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "new", false, 11);

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/pause", "--method", "POST"], null, Ct);

        Assert.Equal((400, (string?)null), Coded(stdout));
        Assert.Equal("new", (string?)account.Sequences.Single()!["status"]);
    }

    /// <summary>What the stand-in keeps for its own refusals is never part of an answer Reply would give.</summary>
    [Fact]
    public async Task The_stand_in_s_own_state_never_reaches_an_answer()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", false, 11).WithoutSendingAccount(7);

        var (_, read, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7"], null, Ct);
        var (_, paused, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/7/pause", "--method", "POST"], null, Ct);

        Assert.DoesNotContain("_sending", read, StringComparison.Ordinal);
        Assert.DoesNotContain("_sending", paused, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------------
    // A person opted out, and taken out of a sequence
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reply's opt-out is a flag on the contact, set or cleared for up to a hundred at once; a contact already in the
    /// state asked for counts as updated, so the answer is the same empty dictionary either time.
    /// </summary>
    [Fact]
    public async Task An_opt_out_sets_the_flag_and_answers_an_empty_dictionary_each_time()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001), ["isOptedOut"] = true }.ToJsonString();
        string[] args = [.. Prefix, "api", "/v3/contacts/set-opted-out", "--method", "POST", "--body", "-"];

        var (exit, first, _) = await account.RunAsync(args, body, Ct);
        var (_, second, _) = await account.RunAsync(args, body, Ct);

        Assert.Equal(0, exit);
        Assert.Equal((200, "{}"), (Answer(first).Code, Answer(first).Data!.ToJsonString()));
        Assert.Equal((200, "{}"), (Answer(second).Code, Answer(second).Data!.ToJsonString()));
        Assert.Equal([1001], account.OptOuts.Select(entry => entry!.GetValue<int>()));
    }

    [Fact]
    public async Task Clearing_the_flag_takes_the_person_off_the_register()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held", optedOut: true);
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001), ["isOptedOut"] = false }.ToJsonString();

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/set-opted-out", "--method", "POST", "--body", "-"], body, Ct);

        Assert.Equal(200, Answer(stdout).Code);
        Assert.Empty(account.OptOuts);
    }

    /// <summary>A contact the account does not hold is a per-item failure inside a 200, in the bulk calls' own shape.</summary>
    [Fact]
    public async Task A_contact_the_account_does_not_hold_is_a_per_item_failure_of_its_own()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        var body = new JsonObject { ["contactIds"] = new JsonArray(1001, 4242), ["isOptedOut"] = true }.ToJsonString();

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/set-opted-out", "--method", "POST", "--body", "-"], body, Ct);

        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal(["4242"], data!.AsObject().Select(entry => entry.Key));
        Assert.Equal("notFound", (string?)data["4242"]!["error"]);
        Assert.NotNull(data["4242"]!["errorDetails"]);
        Assert.Equal([1001], account.OptOuts.Select(entry => entry!.GetValue<int>()));
    }

    /// <summary>A request without its flag, or naming nobody, is a validation failure: `errors[]` and no code.</summary>
    [Theory]
    [InlineData("""{"contactIds":[1001]}""", "/isOptedOut")]
    [InlineData("""{"contactIds":[],"isOptedOut":true}""", "/contactIds")]
    public async Task An_opt_out_that_says_too_little_is_a_validation_failure(string body, string pointer)
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/contacts/set-opted-out", "--method", "POST", "--body", "-"], body, Ct);

        var (code, data) = Answer(stdout);
        Assert.Equal(400, code);
        Assert.Null(data!["code"]);
        Assert.Equal(pointer, (string?)data["errors"]![0]!["pointer"]);
        Assert.Empty(account.OptOuts);
    }

    /// <summary>
    /// The removal answers 204 with an empty body — which the real CLI prints as `data: null` — and the person no
    /// longer takes part. A second removal finds nobody to remove, in the participation read's own words.
    /// </summary>
    [Fact]
    public async Task A_removal_takes_the_person_out_and_a_second_one_finds_nobody()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", false, 11).WithContact(1001, "held@example.test", "Held").WithEnrollment(7, 1001);
        string[] args = [.. Prefix, "api", "/v3/sequences/7/contact-links/1001", "--method", "DELETE"];

        var (exit, first, _) = await account.RunAsync(args, null, Ct);
        var (_, second, _) = await account.RunAsync(args, null, Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"code":204,"data":null}""", first.Trim());
        Assert.Empty(account.EnrolledIn(7));
        Assert.Equal((404, "sequenceContact.notInSequence"), Coded(second));
    }

    /// <summary>
    /// Reply documents one 404 for a removal or a participation read, whatever was missing: a sequence it does not hold
    /// answers exactly as a person who takes no part does.
    /// </summary>
    [Fact]
    public async Task A_removal_from_a_sequence_the_account_does_not_hold_answers_the_one_lumped_404()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", false, 11).WithContact(1001, "held@example.test", "Held").WithEnrollment(7, 1001);

        var (_, stdout, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/8/contact-links/1001", "--method", "DELETE"], null, Ct);
        var (_, read, _) = await account.RunAsync([.. Prefix, "api", "/v3/sequences/8/contacts/1001"], null, Ct);

        Assert.Equal((404, "sequenceContact.notInSequence"), Coded(stdout));
        Assert.Equal((404, "sequenceContact.notInSequence"), Coded(read));
        Assert.Equal([1001], account.EnrolledIn(7));
    }

    // -------------------------------------------------------------------------------------------------------
    // Reading what it printed
    // -------------------------------------------------------------------------------------------------------

    private static JsonObject Problem(int status, string code) => new()
    {
        ["title"] = status == 404 ? "Not Found" : "Error",
        ["status"] = status,
        ["code"] = code,
    };

    private static (int Code, string? ProviderCode) Coded(string stdout)
    {
        var (code, data) = Answer(stdout);
        return (code, (string?)data?["code"]);
    }

    private static (int Code, JsonNode? Data) Answer(string stdout)
    {
        var answer = Parsed(stdout);
        Assert.NotNull(answer);
        return (answer["code"]!.GetValue<int>(), answer["data"]);
    }

    private static JsonNode? Parsed(string stdout)
    {
        try
        {
            return JsonNode.Parse(stdout);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
