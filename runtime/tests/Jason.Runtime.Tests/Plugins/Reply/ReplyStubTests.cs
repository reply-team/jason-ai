using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's own rules, beside what its commands answer (<see cref="ReplyStubCommandTests"/>): where it finds its
/// account, what it refuses before anything happens, the call log it writes before it answers, the endings a test
/// scripts for it, and the one raw call — the contact search — which prints <c>{code, data}</c> for any status and
/// exits 1 from 400 up.
/// </summary>
public class ReplyStubTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The global prefix the package always passes, so each test says only what it is about.</summary>
    private static string[] Prefix => ["--json", "-q"];

    /// <summary>The contact search, the one raw call the plugin still makes, as it asks for its first page.</summary>
    private const string Search = "/v3/contacts/filter?top=100&skip=0";

    /// <summary>The search's whole argument vector after the prefix.</summary>
    private static string[] SearchCall => [.. Prefix, "api", Search, "--method", "POST", "--body", "-"];

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
    public async Task The_raw_call_prints_code_and_data_and_exits_one_when_the_status_is_four_hundred()
    {
        using var account = new ReplyAccount();
        account.Answers("POST", Search, 503, Problem(503, "service.unavailable").ToJsonString());

        var (exit, stdout, _) = await account.RunAsync(SearchCall, """{"searchTerm":"held@example.test"}""", Ct);

        // `reply api` prints {code, data} for any status and exits 1 from 400 up; a stand-in that exited 0 would let a
        // plugin pass a test it would fail against the real thing.
        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(503, code);
        Assert.Equal("service.unavailable", data!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_status_below_four_hundred_is_a_success()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");

        var (exit, stdout, stderr) = await account.RunAsync(SearchCall, """{"searchTerm":"held@example.test"}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal(1001, data!["items"]![0]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task An_empty_bodied_four_hundred_and_one_answers_with_no_data_at_all()
    {
        using var account = new ReplyAccount();
        account.Answers("POST", Search, 401, string.Empty);

        var (exit, stdout, _) = await account.RunAsync(SearchCall, """{"searchTerm":"held@example.test"}""", Ct);

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

        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, flag, "value", "sequence", "get", "7"], Ct);

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
        var body = new JsonObject { ["searchTerm"] = "written@example.test" };

        var (exit, _, stderr) = await account.RunAsync(
            [.. Prefix, "api", Search, "--method", "POST", "--body", body.ToJsonString()],
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

        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);

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
        var (exit, stdout, stderr) = await account.RunAsync([.. Prefix, "--profile", "nobody", "sequence", "get", "7"], Ct);

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

        string[] arguments = [.. Prefix, "sequence", "get", "7"];
        var (_, fromFirst, _) = await first.RunAsync(arguments, Ct);
        var (_, fromSecond, _) = await second.RunAsync(arguments, Ct);

        // The same argument vector, two accounts, two answers: nothing on the command line says which account
        // this is. The child's environment is built from nothing and the package declares no `env`, so this is
        // the only way a test could choose one — and it is the way the real CLI finds its store.
        Assert.Equal("Nurture", JsonNode.Parse(fromFirst)!["name"]!.GetValue<string>());
        Assert.Equal("Winback", JsonNode.Parse(fromSecond)!["name"]!.GetValue<string>());
        Assert.DoesNotContain(first.Root, string.Join(' ', Assert.Single(first.Calls).Args), StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------------------------------------
    // The call log
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_call_is_recorded_before_it_runs()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");
        account.Hangs("sequence get", 3000);

        var run = account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (account.Calls.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        // Written before the answer, not after it: the entry is readable while the program is still working. The
        // state left behind could never show that, because a provider answers the same either way.
        var recorded = Assert.Single(account.Calls);
        Assert.False(run.IsCompleted);
        Assert.Equal("sequence get", recorded.Command);
        Assert.Equal(["7"], recorded.Operands);
        Assert.Null(recorded.Body);
        Assert.Equal([.. Prefix, "sequence", "get", "7"], recorded.Args);

        var (exit, _, _) = await run;
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task The_body_is_read_from_stdin_when_the_arguments_say_so()
    {
        using var account = new ReplyAccount();
        var body = new JsonObject { ["searchTerm"] = "sent@example.test" };

        var (exit, stdout, _) = await account.RunAsync(SearchCall, body.ToJsonString(), Ct);

        Assert.Equal(0, exit);
        Assert.Equal(200, Answer(stdout).Code);

        var recorded = Assert.Single(account.Calls);
        Assert.Equal("api", recorded.Command);
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
        account.WithSequence(7, "Nurture", "active");

        await account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);
        var mark = account.Mark();
        await account.RunAsync([.. Prefix, "sequence", "contacts", "7"], Ct);

        // An assertion about one attempt has to be able to start where that attempt started; over a whole
        // lifetime, "it never wrote before it read" is answered by the attempt before it.
        Assert.Equal(2, account.Calls.Count);
        var since = Assert.Single(account.CallsSince(mark));
        Assert.Equal("sequence contacts 7", since.Line);
    }

    // -------------------------------------------------------------------------------------------------------
    // The contact search, the one raw call the plugin still makes
    // -------------------------------------------------------------------------------------------------------

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
    // Being told to misbehave
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_instruction_is_spent_once_so_the_attempt_after_it_sees_the_account()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.Answers("POST", Search, 429, Problem(429, "rate.limited").ToJsonString());

        var (first, refused, _) = await account.RunAsync(SearchCall, """{"searchTerm":"held@example.test"}""", Ct);
        var (second, answered, _) = await account.RunAsync(SearchCall, """{"searchTerm":"held@example.test"}""", Ct);

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

        var (_, stdout, stderr) = await account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);

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
            account.HoldsOn("sequence get", started, release);
            var call = account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);

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
            Assert.Equal("Nurture", JsonNode.Parse(stdout)!["name"]!.GetValue<string>());
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
            account.HoldsOn("sequence get", Path.Combine(directory, "started"), Path.Combine(directory, "never"), capMs: 200);

            var (exit, stdout, _) = await account
                .RunAsync([.. Prefix, "sequence", "get", "7"], Ct)
                .WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.Equal(0, exit);
            Assert.Equal("Nurture", JsonNode.Parse(stdout)!["name"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // A sequence paused and started
    // -------------------------------------------------------------------------------------------------------

    /// <summary>What the stand-in keeps for its own refusals is never part of an answer.</summary>
    [Fact]
    public async Task The_stand_in_s_own_state_never_reaches_an_answer()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active", false, 11).WithoutSendingAccount(7);

        var (_, read, _) = await account.RunAsync([.. Prefix, "sequence", "get", "7"], null, Ct);
        var (_, paused, _) = await account.RunAsync([.. Prefix, "sequence", "pause", "7"], null, Ct);

        Assert.DoesNotContain("_sending", read, StringComparison.Ordinal);
        Assert.DoesNotContain("_sending", paused, StringComparison.Ordinal);
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
