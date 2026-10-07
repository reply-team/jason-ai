using Jason.Cli.Commands;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Commands;

public class RuntimeStartCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_runtime_that_is_already_running_is_reported_and_nothing_is_launched()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("a live runtime must never be launched again") };
        var (env, output, error) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(processes.Launches);
        Assert.Equal(RuntimeVerbs.InfoJson("rt_LIVE") + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Without_a_runtime_one_is_launched_in_the_data_directory_and_awaited()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(dir.Paths.Root, Assert.Single(processes.Launches));
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>And what it launches is another copy of this very program.</summary>
    /// <remarks>
    /// The seam takes the executable from its caller because not every caller wants the same one: an applier
    /// starts the build it has just unpacked, at a path of its own. So <c>runtime start</c> has to name its own,
    /// and naming the wrong one is the mistake that hides — a runtime started from the wrong binary answers
    /// every check the right one would, in the same data directory, under the same descriptor.
    /// </remarks>
    [Fact]
    public async Task What_runtime_start_launches_is_this_executable()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, _, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        var launched = Assert.Single(processes.Executables);
        Assert.Equal(SelfExecutable.Command, launched);
        Assert.NotEmpty(launched);
    }

    [Fact]
    public async Task A_stale_descriptor_is_replaced_by_the_instance_that_actually_answers()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));

        // The port in the stale descriptor is answered by somebody else entirely, so the claim is not believed.
        var handler = RuntimeVerbs.EchoesTheDescriptor(dir, named => named == "rt_OLD" ? "rt_SOMEONE_ELSE" : named);
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        var text = output.ToString();
        Assert.Contains("rt_NEW", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rt_OLD", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_dies_before_publishing_exits_1_and_points_at_the_logs()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 1) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.Contains(dir.Paths.LogsDirectory, envelope.Message, StringComparison.Ordinal);
        Assert.Contains("1", envelope.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And where there are no log files, it says so instead of sending somebody to read them.
    /// </summary>
    /// <remarks>
    /// A runtime can fail before its logging is configured: preparing the data directory is the first thing
    /// it does, and the log directory is created by that very step. So on the first run of a new installation
    /// -- the one case where this message matters most -- "see the log files" sent the operator to six empty
    /// directories.
    /// </remarks>
    [Fact]
    public async Task A_child_that_died_before_logging_started_is_not_sent_to_an_empty_log_directory()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 134) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.DoesNotContain("See the log files", message, StringComparison.Ordinal);
        Assert.Contains("stopped before logging started", message, StringComparison.Ordinal);
        Assert.Contains(dir.Paths.Root, message, StringComparison.Ordinal);

        // And not "run it in the foreground to see": `jason runtime run` does not return while the runtime runs,
        // so an agent told to type it would wait for good.
        Assert.DoesNotContain("jason runtime run", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A runtime that stops before it has a log has two files of its own that it reads first, and either one can be
    /// the reason: the data directory it has to prepare, and the settings it has to read. Naming only the first
    /// sent a person whose settings file was the cause to a directory that was fine. The sentence names the two
    /// paths; this verb reads neither file.
    /// </summary>
    [Fact]
    public async Task A_child_that_died_before_logging_started_names_the_settings_file_beside_the_data_directory()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 1) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.Contains(
            $"There are no log files under '{dir.Paths.LogsDirectory}', so it stopped before logging started — "
            + $"which is what happens when its settings file at '{dir.Paths.UserSettingsFile}' cannot be read, "
            + $"or the data directory at '{dir.Paths.Root}' cannot be prepared.",
            message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A child that says why it will not start is heard, in its own words: a runtime that refuses writes the
    /// reason to the standard error it was started with, and this read that and threw it away.
    /// </summary>
    [Fact]
    public async Task A_child_that_says_why_it_will_not_start_is_heard_in_its_own_words()
    {
        using var dir = new TempPaths();
        const string reason = "The data directory '/nowhere' could not be prepared, so the runtime did not start.";
        var processes = new FakeProcessControl { OnLaunch = _ => new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1, Said = reason } };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains($"It said: {reason}", RuntimeVerbs.Envelope(output.ToString()).Message, StringComparison.Ordinal);
    }

    /// <summary>And where this start wrote one, it still sends them there.</summary>
    [Fact]
    public async Task A_child_that_died_after_writing_a_log_is_sent_to_it()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl
        {
            OnLaunch = paths =>
            {
                Directory.CreateDirectory(paths.LogsDirectory);
                File.WriteAllText(Path.Combine(paths.LogsDirectory, "runtime-20261004.jsonl"), "{}\n");
                return new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1 };
            },
        };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Contains("See the log files", RuntimeVerbs.Envelope(output.ToString()).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And where it added to a file an earlier start began: a runtime that starts again on the same day writes on in
    /// that day's file rather than starting one of its own.
    /// </summary>
    [Fact]
    public async Task A_child_that_died_after_adding_to_the_days_log_is_sent_to_it()
    {
        using var dir = new TempPaths();
        var log = EarlierLog(dir, "runtime-20261004.jsonl");
        var processes = new FakeProcessControl
        {
            OnLaunch = _ =>
            {
                File.AppendAllText(log, "{\"@mt\":\"this start\"}\n");
                return new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1 };
            },
        };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Contains("See the log files", RuntimeVerbs.Envelope(output.ToString()).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Before logging started" is a fact about this start, not about the directory. Most hand edits are made on an
    /// installation that has run before, so its log directory already holds an earlier day's file — and a settings file
    /// that stopped this start before it could log anything sent the person to read files that said nothing about it.
    /// What the runtime said is the reason, and it is heard, in either shape a settings refusal takes.
    /// </summary>
    [Theory]
    [InlineData("Invalid Jason configuration: the settings file at '/data/config/settings.json' could not be read (JsonException at line 2, byte 3).")]
    [InlineData("Invalid Jason configuration: Dispatcher:TickSeconds could not be read as System.Int32.")]
    public async Task A_child_that_stopped_before_logging_where_an_earlier_start_logged_is_heard_and_not_sent_to_those_logs(string reason)
    {
        using var dir = new TempPaths();
        EarlierLog(dir, "runtime-20261003.jsonl");
        var processes = new FakeProcessControl { OnLaunch = _ => new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1, Said = reason } };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.Contains($"It said: {reason}", message, StringComparison.Ordinal);
        Assert.DoesNotContain("See the log files", message, StringComparison.Ordinal);
        Assert.Contains($"No log file under '{dir.Paths.LogsDirectory}' was written by this start.", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a child said is this launch's own answer — only this child writes the standard error this verb reads — and
    /// where it said why, the causes this verb would otherwise guess at are not set beside it. A guess next to the real
    /// reason only misleads: another runtime holding the lock is not a settings file that cannot be read.
    /// </summary>
    [Fact]
    public async Task A_child_that_said_why_is_not_given_a_guessed_cause_beside_it()
    {
        using var dir = new TempPaths();
        const string reason = "Another Jason runtime already holds the instance lock '/data/run/runtime.lock'. Exactly one runtime per user may run.";
        var processes = new FakeProcessControl { OnLaunch = _ => new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1, Said = reason } };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.Contains($"It said: {reason}", message, StringComparison.Ordinal);
        Assert.DoesNotContain("which is what happens when", message, StringComparison.Ordinal);
        Assert.DoesNotContain(dir.Paths.UserSettingsFile, message, StringComparison.Ordinal);
        Assert.Contains($"No log file under '{dir.Paths.LogsDirectory}' was written by this start.", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a child that said why after this start had begun its log — a section refused as the host started — is heard
    /// and still sent to the log it wrote.
    /// </summary>
    [Fact]
    public async Task A_child_that_said_why_after_logging_is_heard_and_sent_to_its_log()
    {
        using var dir = new TempPaths();
        EarlierLog(dir, "runtime-20261003.jsonl");
        const string reason = "Invalid Jason configuration: Plugins:Limits:TimeoutMs could not be read as System.Int32.";
        var processes = new FakeProcessControl
        {
            OnLaunch = paths =>
            {
                File.WriteAllText(Path.Combine(paths.LogsDirectory, "runtime-20261004.jsonl"), "{\"@mt\":\"this start\"}\n");
                return new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1, Said = reason };
            },
        };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.Contains($"It said: {reason} See the log files under '{dir.Paths.LogsDirectory}'.", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a child that stopped before it logged and said nothing, where an earlier start did log: nothing it said names
    /// the cause, so the two that come before a log are named, and the earlier start's files are not pointed at.
    /// </summary>
    [Fact]
    public async Task A_silent_child_that_stopped_before_logging_where_an_earlier_start_logged_is_told_the_causes_that_come_first()
    {
        using var dir = new TempPaths();
        EarlierLog(dir, "runtime-20261003.jsonl");
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 1) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.DoesNotContain("It said:", message, StringComparison.Ordinal);
        Assert.DoesNotContain("See the log files", message, StringComparison.Ordinal);
        Assert.Contains(
            $"None of the log files under '{dir.Paths.LogsDirectory}' was written by this start, so it stopped before logging started — "
            + $"which is what happens when its settings file at '{dir.Paths.UserSettingsFile}' cannot be read, "
            + $"or the data directory at '{dir.Paths.Root}' cannot be prepared.",
            message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And a child still running when the wait ran out, on such an installation, is not said to have logged: it has
    /// written nothing yet, and the files there are an earlier start's.
    /// </summary>
    [Fact]
    public async Task A_child_still_silent_when_the_wait_ran_out_is_not_sent_to_an_earlier_starts_logs()
    {
        using var dir = new TempPaths();
        EarlierLog(dir, "runtime-20261003.jsonl");
        var processes = new FakeProcessControl();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.DoesNotContain("See the log files", message, StringComparison.Ordinal);
        Assert.Contains($"None of the log files under '{dir.Paths.LogsDirectory}' has been written by this start yet.", message, StringComparison.Ordinal);
    }

    /// <summary>A log file an earlier start wrote, last written a day before this one.</summary>
    private static string EarlierLog(TempPaths dir, string name)
    {
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        var log = Path.Combine(dir.Paths.LogsDirectory, name);
        File.WriteAllText(log, "{\"@mt\":\"an earlier start\"}\n");
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-1));
        return log;
    }

    /// <remarks>
    /// And it says what is known: the child is still running, by its pid, and may yet publish. This said the
    /// runtime "stopped before logging started" of a process nothing had ended, and sent the operator to run it
    /// in the foreground.
    /// </remarks>
    [Fact]
    public async Task A_child_that_never_publishes_a_descriptor_exits_1_after_the_wait()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.Contains(dir.Paths.LogsDirectory, envelope.Message, StringComparison.Ordinal);
        Assert.Contains("process 4242", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("still running", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("stopped", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jason runtime run", envelope.Message, StringComparison.Ordinal);
        Assert.Single(processes.Launches);
    }

    /// <summary>
    /// The wait is this verb's own, and what it says when it runs out is what happened. Each question put to the
    /// runtime was bounded only by the HTTP client's 30 s timeout and the deadline was looked at only between
    /// questions, so a runtime that published its descriptor and then never answered kept this verb for up to
    /// one client timeout past its wait -- and the verb then said the runtime "did not publish" a descriptor,
    /// which it had.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_publishes_and_never_answers_does_not_carry_start_past_its_wait()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishedAtLaunch(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, new Silent(), processes);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, TimeSpan.FromSeconds(1), RuntimeVerbs.Poll);
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"start with a 1 s wait returned after {clock.Elapsed.TotalSeconds:F1} s, exit {exit}: {output}");
        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.Contains("published an endpoint descriptor", envelope.Message, StringComparison.Ordinal);
        Assert.Contains($"at {RuntimeVerbs.BaseUrl}", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("did not answer within 1 s", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("process 4242", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("did not publish", envelope.Message, StringComparison.Ordinal);
        Assert.True(envelope.Retryable);
        Assert.Single(processes.Launches);
    }

    /// <summary>
    /// A runtime that published its descriptor and was answered for by something else did answer, and saying it did not
    /// would send a person looking for a hung process. When the wait runs out, the sentence says what came back — never
    /// any of it — and keeps what to do about it.
    /// </summary>
    [Theory]
    [InlineData("refused", "nothing took the call at that address")]
    [InlineData("token", "what answered there did not accept the token the descriptor holds")]
    [InlineData("error", "what answered there answered with an error")]
    [InlineData("instance", "what answered there is another runtime instance than the one the descriptor names")]
    [InlineData("unreadable", "what answered there gave an answer that is not a runtime's")]
    [InlineData("not_http", "what answered there gave an answer that is not a runtime's")]
    [InlineData("cut_short", "what answered there gave an answer that is not a runtime's")]
    public async Task A_runtime_that_published_and_was_answered_for_otherwise_is_said_as_what_came_back(string answer, string said)
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishedAtLaunch(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var handler = new FakeHandler(_ => answer switch
        {
            // The client's own word for each: a connection nothing took, an answer that is not HTTP, and one cut short.
            // The last two were taken, so they are never "nothing took the call".
            "refused" => throw new HttpRequestException(HttpRequestError.ConnectionError, "SENTINEL_REFUSED"),
            "not_http" => throw new HttpRequestException(HttpRequestError.InvalidResponse, "SENTINEL_NOT_HTTP"),
            "cut_short" => throw new HttpRequestException(HttpRequestError.ResponseEnded, "SENTINEL_CUT_SHORT"),
            "token" => RuntimeVerbs.Response(System.Net.HttpStatusCode.Unauthorized, """{"error":{"code":"unauthorized","message":"SENTINEL_TOKEN","retryable":false}}"""),
            "error" => RuntimeVerbs.Response(System.Net.HttpStatusCode.InternalServerError, """{"error":{"code":"internal_error","message":"SENTINEL_ERROR","retryable":true}}"""),
            "instance" => RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_SENTINEL_OTHER")),
            _ => RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, "SENTINEL_BODY"),
        });
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Contains($"published an endpoint descriptor at {RuntimeVerbs.BaseUrl}, and {said}", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("still running", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", envelope.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A descriptor read when the wait has already run out is not asked about: a question with no time left to put it
    /// in says nothing of the runtime, and taking it for silence said a runtime that was never asked did not answer.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_published_as_the_wait_ran_out_is_said_not_to_have_been_asked()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl
        {
            OnLaunch = _ =>
            {
                dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_NEW"));
                return new FakeProcessHandle(4242);
            },
        };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, TimeSpan.Zero, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.Contains($"published an endpoint descriptor at {RuntimeVerbs.BaseUrl}, and the wait had run out before it could be asked whether it answers", message, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A descriptor left by a runtime that is still running but not answering is not stale: launching beside it
    /// would put a second process on the data directory the first one holds, to lose a fight over the lock.
    /// </summary>
    /// <remarks>
    /// This waits out the whole bound the verb puts on that first question, five seconds, because the bound is
    /// not a parameter: it is what the verb does, and the sentence it says carries the number.
    /// </remarks>
    [Fact]
    public async Task A_live_runtime_that_does_not_answer_is_not_launched_over()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_SILENT"));
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("a runtime that is still running must not be launched over") };
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        var (env, output, _) = RuntimeVerbs.Environment(dir, new Silent(), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.True(envelope.Retryable);
        Assert.Equal(
            $"A runtime (process {RuntimeVerbs.Pid}) published an endpoint descriptor at {RuntimeVerbs.BaseUrl} and did not answer within 5 s; it is still running. "
            + $"'jason runtime status' answers once it does; if it never does, end process {RuntimeVerbs.Pid} and start again.",
            envelope.Message);
    }

    /// <summary>
    /// And one whose process has gone is stale, however silent its port: the verb launches, and the question it
    /// put to the old descriptor delays that by no more than its bound.
    /// </summary>
    [Fact]
    public async Task A_descriptor_whose_runtime_is_gone_and_silent_delays_the_launch_by_no_more_than_the_bound()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, new SilentFor(dir, "rt_OLD"), processes);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);
        clock.Stop();

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);

        // The bound, and a margin for everything else the start does here — the child publishes after a moment and
        // is polled every few milliseconds — wide enough for a loaded machine and narrow enough that a question
        // allowed twice the bound would not fit in it.
        var margin = TimeSpan.FromSeconds(3);
        Assert.True(
            clock.Elapsed < RuntimeStartCommand.PreLaunchBound + margin,
            $"the stale descriptor held the launch for {clock.Elapsed.TotalSeconds:F1} s, more than the {RuntimeStartCommand.PreLaunchBound.TotalSeconds:F0} s bound and a {margin.TotalSeconds:F0} s margin");
    }

    /// <summary>
    /// A process id is only a name for a process while that process lives. After a restart the id a descriptor
    /// recorded belongs to whatever the machine started next, so "that pid is running" says yes about a
    /// stranger, and refusing to launch over it would send the person to end a program that is not the runtime.
    /// The descriptor carries the moment the runtime started, and a process that started after it is somebody
    /// else's.
    /// </summary>
    [Fact]
    public async Task A_silent_descriptor_whose_process_id_now_belongs_to_another_program_is_stale()
    {
        using var dir = new TempPaths();
        var recorded = RuntimeVerbs.Descriptor("rt_OLD");
        dir.WriteDescriptor(recorded);
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        processes.StartTimes[RuntimeVerbs.Pid] = recorded.StartedAt.AddHours(1);
        var (env, output, _) = RuntimeVerbs.Environment(dir, new SilentFor(dir, "rt_OLD"), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And a process that started a little after the moment the runtime recorded is still the runtime: the
    /// process starts first and the runtime takes the time once it is running, and a platform may report a start
    /// time only to the clock tick. The same allowance the uninstall verb gives it.
    /// </summary>
    [Fact]
    public async Task A_process_that_started_within_the_allowance_of_the_recorded_time_is_the_runtime()
    {
        using var dir = new TempPaths();
        var recorded = RuntimeVerbs.Descriptor("rt_SILENT");
        dir.WriteDescriptor(recorded);
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("a runtime that is still running must not be launched over") };
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        processes.StartTimes[RuntimeVerbs.Pid] = recorded.StartedAt.AddSeconds(4);
        var (env, output, _) = RuntimeVerbs.Environment(dir, new Silent(), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        Assert.Contains($"end process {RuntimeVerbs.Pid} and start again", RuntimeVerbs.Envelope(output.ToString()).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the start time cannot be read — a runtime a 0.1.0 logon registration started, in another session —
    /// the process may be the runtime and is not launched over, but it is not named as a process to end either:
    /// nothing shows that it is the runtime rather than a stranger that has its id.
    /// </summary>
    [Fact]
    public async Task A_silent_runtime_whose_process_start_cannot_be_read_is_not_launched_over_and_is_not_named_to_be_ended()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_SILENT"));
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("a process that may be the runtime must not be launched over") };
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        processes.StartTimes[RuntimeVerbs.Pid] = null;
        var (env, output, _) = RuntimeVerbs.Environment(dir, new Silent(), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.True(envelope.Retryable);
        Assert.Contains($"process {RuntimeVerbs.Pid}", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be told", envelope.Message, StringComparison.Ordinal);
        Assert.Contains(dir.Paths.DescriptorFile, envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("end process", envelope.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What one question may take is bounded by its own ceiling and not only by what is left of the wait: a
    /// runtime that published and never answers, whose process then exits, is found out within a couple of
    /// seconds and not at the end of a long wait. With only the time left as the bound, this verb sat in one
    /// question for the whole thirty seconds and said the runtime had not answered.
    /// </summary>
    [Fact]
    public async Task A_long_wait_is_cut_short_by_a_child_that_exits_while_its_runtime_is_not_answering()
    {
        using var dir = new TempPaths();
        var publishes = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW"));
        var processes = new FakeProcessControl
        {
            OnLaunch = paths =>
            {
                var child = (FakeProcessHandle)publishes(paths);
                var exits = new Thread(() =>
                {
                    Thread.Sleep(500);
                    child.ExitCode = 1;
                    child.HasExited = true;
                })
                {
                    IsBackground = true,
                    Name = "child-exit",
                };

                exits.Start();
                return child;
            },
        };
        var (env, output, _) = RuntimeVerbs.Environment(dir, new Silent(), processes);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"a child that exited after 0.5 s was found out after {clock.Elapsed.TotalSeconds:F1} s of a 30 s wait: {output}");
        Assert.Equal(ExitCodes.ApiError, exit);
        var message = RuntimeVerbs.Envelope(output.ToString()).Message;
        Assert.Contains("exited with code 1", message, StringComparison.Ordinal);

        // It had published one, and is not said to have exited before it did.
        Assert.Contains($"after publishing an endpoint descriptor at {RuntimeVerbs.BaseUrl}, and it did not answer", message, StringComparison.Ordinal);
        Assert.DoesNotContain("before publishing", message, StringComparison.Ordinal);
    }

    /// <summary>A runtime that accepts the call and never answers it: it ends only when the caller's token does.</summary>
    private sealed class Silent : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>Silent for one instance, and an ordinary runtime for any other the descriptor on disk names.</summary>
    private sealed class SilentFor(TempPaths dir, string instanceId) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = new Jason.Cli.Discovery.DescriptorReader(dir.Paths).Read()
                ?? throw new HttpRequestException("connection refused");
            if (string.Equals(current.InstanceId, instanceId, StringComparison.Ordinal))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, RuntimeVerbs.InfoJson(current.InstanceId));
        }
    }

    [Fact]
    public async Task Human_mode_renders_the_running_runtime_for_people()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), new FakeProcessControl());

        var exit = await RuntimeStartCommand.RunAsync(env, human: true, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains("Runtime:", text, StringComparison.Ordinal);
        Assert.Contains("rt_LIVE", text, StringComparison.Ordinal);
        Assert.Contains(RuntimeVerbs.BaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }
}
