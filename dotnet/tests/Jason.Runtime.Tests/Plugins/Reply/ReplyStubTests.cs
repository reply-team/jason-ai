using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's own rules, beside what its commands answer (<see cref="ReplyStubCommandTests"/>): where it finds its
/// account, what it refuses before anything happens — the raw call among it, which the official package never runs —
/// the call log it writes before it answers, and the endings a test scripts for it.
/// </summary>
public class ReplyStubTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The global prefix the package always passes, so each test says only what it is about.</summary>
    private static string[] Prefix => ["--json", "-q"];

    /// <summary>The exact lookup of one address, with the address on stdin.</summary>
    private static string[] FindCall => [.. Prefix, "contact", "find", "--contact", "-"];

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
    // Usage: the command surface, and nothing beyond it
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The raw call is the real CLI's, and the official package never runs it: every request it makes has a command made
    /// for it. So here it is a command this program does not know, refused before anything is recorded, and a package
    /// that reached for it fails here.
    /// </summary>
    [Theory]
    [InlineData("/v3/contacts/filter?top=100&skip=0", "--method", "POST", "--body", "-")]
    [InlineData("/v3/sequences", null, null, null, null)]
    public async Task The_raw_call_is_a_command_this_stand_in_does_not_know(string path, string? method, string? verb, string? body, string? from)
    {
        using var account = new ReplyAccount();
        string[] arguments = [.. Prefix, "api", path, .. new[] { method, verb, body, from }.OfType<string>()];

        var (exit, stdout, stderr) = await account.RunAsync(arguments, "{}", Ct);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("unknown command 'api'", stderr, StringComparison.Ordinal);
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// A call refused before stdin is read can find the stand-in gone by the time the text is written, and a pipe nobody
    /// reads any more refuses the write: a broken pipe on macOS and Linux, a pipe being closed on Windows. What the
    /// stand-in answered is what a row holds it to, so the text it never needed does not fail the row that sent it. The
    /// row waits for the exit before the write, which is the order the race only sometimes takes.
    /// </summary>
    [Fact]
    public async Task A_stand_in_gone_before_its_stdin_is_written_is_still_heard()
    {
        using var account = new ReplyAccount();

        var (exit, stdout, stderr) = await account.RunAsync(
            [.. Prefix, "api", "/v3/sequences"],
            "{}",
            static (process, cancellationToken) => process.WaitForExitAsync(cancellationToken),
            Ct);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("unknown command 'api'", stderr, StringComparison.Ordinal);
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
    public async Task An_address_that_is_not_read_from_stdin_is_a_usage_error()
    {
        using var account = new ReplyAccount();
        var person = new JsonObject { ["email"] = "written@example.test" };

        var (exit, _, stderr) = await account.RunAsync([.. Prefix, "contact", "find", "--contact", person.ToJsonString()], Ct);

        // An address on the command line would be a person's address in the runtime's log. The CLI takes '-' and
        // nothing else, so the rule holds even against a plugin that tried — and the refusal does not repeat it.
        Assert.Equal(2, exit);
        Assert.Equal("usage.contact", (string?)JsonNode.Parse(stderr)!["error"]!["code"]);
        Assert.DoesNotContain("written@example.test", stderr, StringComparison.Ordinal);
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

    /// <summary>
    /// Several stand-ins in flight against one account, each writing lines of its own length, leave every line in the log
    /// and every line whole. An append that took its position when the file was opened would let a short line land on
    /// the tail of a long one and lose the entry it overwrote; the log is the evidence a row stands on, so a lost or a
    /// torn entry is a row that passes or fails for no reason of its own.
    /// </summary>
    [Fact]
    public async Task Stand_ins_in_flight_together_leave_every_call_in_the_log_whole()
    {
        const int Writers = 8;
        const int PerWriter = 25;
        using var account = new ReplyAccount();

        // Every writer is a real process; all of them have ended before the log is read, because each task awaits its
        // own process to the end and the tasks are awaited together.
        var runs = Enumerable.Range(0, Writers).Select(writer => Task.Run(async () =>
        {
            for (var call = 0; call < PerWriter; call++)
            {
                // The body is recorded with the entry, so its length sets the length of the line.
                var body = new string('x', 20 + ((writer * 37) + (call * 13)) % 400);
                await account.RunAsync(FindCall, body, Ct);
            }
        }, Ct)).ToArray();

        try
        {
            await Task.WhenAll(runs).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"{Writers} stand-ins of {PerWriter} calls each did not all finish within two minutes: {runs.Count(run => !run.IsCompleted)} of the writers were still running.");
        }

        var calls = account.Calls;
        Assert.Equal(Writers * PerWriter, calls.Count);
        Assert.All(calls, call => Assert.Equal("contact find", call.Command));
    }

    /// <summary>
    /// What a reader does with a log that has a damaged line: a last line with no newline is a write still in flight and
    /// is left out, while a line that is torn anywhere else is a failure and never skipped.
    /// </summary>
    [Fact]
    public async Task A_reader_leaves_out_a_write_in_flight_and_fails_on_a_line_torn_in_the_middle()
    {
        using var account = new ReplyAccount();
        await account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);
        var file = Path.Combine(account.Root, ReplyAccount.CallsFile);
        var whole = File.ReadAllText(file);
        Assert.EndsWith("\n", whole, StringComparison.Ordinal);

        await File.AppendAllTextAsync(file, whole[..(whole.Length / 2)], Ct);
        Assert.Single(account.Calls);

        await File.AppendAllTextAsync(file, "\n" + whole, Ct);
        Assert.ThrowsAny<JsonException>(() => account.Calls);
    }

    [Fact]
    public async Task Every_call_is_recorded_before_it_runs()
    {
        using var account = new ReplyAccount();
        account.WithSequence(7, "Nurture", "active");
        var directory = Path.Combine(Path.GetTempPath(), "jason-hold", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var started = Path.Combine(directory, "started");
        var release = Path.Combine(directory, "release");

        try
        {
            // Held until the test lets it go, so "still working" is a state the test controls rather than a
            // margin of time it hopes the machine leaves it.
            account.HoldsOn("sequence get", started, release);
            var run = account.RunAsync([.. Prefix, "sequence", "get", "7"], Ct);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!File.Exists(started) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, Ct);
            }

            Assert.True(File.Exists(started), "The held call never said it had started.");

            // Written before the answer, not after it: the entry is readable while the program is still working. The
            // state left behind could never show that, because a provider answers the same either way.
            var recorded = Assert.Single(account.Calls);
            Assert.False(run.IsCompleted, "The call answered without waiting to be let go.");
            Assert.Equal("sequence get", recorded.Command);
            Assert.Equal(["7"], recorded.Operands);
            Assert.Null(recorded.Body);
            Assert.Equal([.. Prefix, "sequence", "get", "7"], recorded.Args);

            await File.WriteAllTextAsync(release, string.Empty, Ct);
            var (exit, _, _) = await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.Equal(0, exit);
        }
        finally
        {
            // Let the call go first, so a stub still held when an assertion failed ends now rather than waiting out
            // its cap; then take the directory away. Neither may hide what failed before it.
            try
            {
                File.WriteAllText(release, string.Empty);
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // The temporary folder outlives the test, which is better than a failure that is not the real one.
            }
            catch (UnauthorizedAccessException)
            {
                // The same: a file still held by the stub that was let go a moment ago.
            }
        }
    }

    [Fact]
    public async Task The_address_is_read_from_stdin_when_the_arguments_say_so()
    {
        using var account = new ReplyAccount();
        var body = new JsonObject { ["email"] = "sent@example.test" };

        var (exit, stdout, _) = await account.RunAsync(FindCall, body.ToJsonString(), Ct);

        Assert.Equal(0, exit);
        Assert.Equal("""{"contact_id":null}""", JsonNode.Parse(stdout)!.ToJsonString());

        var recorded = Assert.Single(account.Calls);
        Assert.Equal("contact find", recorded.Command);
        Assert.Empty(recorded.Operands);
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
    // Being told to misbehave
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_instruction_is_spent_once_so_the_attempt_after_it_sees_the_account()
    {
        using var account = new ReplyAccount();
        account.WithContact(1001, "held@example.test", "Held");
        account.Refuses("contact find", "rate_limited", status: 429);

        var (first, _, refused) = await account.RunAsync(FindCall, """{"email":"held@example.test"}""", Ct);
        var (second, answered, _) = await account.RunAsync(FindCall, """{"email":"held@example.test"}""", Ct);

        Assert.Equal(1, first);
        Assert.Equal("rate_limited", (string?)JsonNode.Parse(refused)!["error"]!["code"]);
        Assert.Equal(0, second);
        Assert.Equal("""{"contact_id":1001}""", JsonNode.Parse(answered)!.ToJsonString());
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
