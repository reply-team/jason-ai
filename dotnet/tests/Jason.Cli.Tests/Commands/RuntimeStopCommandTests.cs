using System.Net;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Process;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

public class RuntimeStopCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.R2, "exit 3 without a runtime")]
    public async Task Nothing_to_stop_exits_3_with_no_descriptor()
    {
        using var dir = new TempPaths();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.NoDescriptor, RuntimeVerbs.Envelope(output.ToString()).Code);
    }

    /// <summary>
    /// A person's own <c>jason runtime stop</c> says no start is wanted, so a standing note that one is owed goes with a stop
    /// that succeeded; a stop with nothing to stop leaves it, and the routines that stop on their own account leave it too.
    /// </summary>
    [Fact]
    public async Task A_persons_own_stop_takes_back_the_note_that_a_start_is_owed()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(dir.Paths.RunDirectory);
        File.WriteAllText(dir.Paths.StartOwedFile, "{\"stopped_at\":\"2026-11-17T15:05:35Z\"}");

        var (idle, _, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());
        Assert.Equal(ExitCodes.RuntimeUnavailable, await CliApp.RunAsync(["runtime", "stop"], idle, Ct));
        Assert.True(File.Exists(dir.Paths.StartOwedFile), "a stop with nothing to stop took the note back");

        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (routine, _, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());
        Assert.Equal(ExitCodes.Success, await RuntimeStopCommand.RunAsync(routine, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll));
        Assert.True(File.Exists(dir.Paths.StartOwedFile), "a routine's own stop took the note back");

        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (person, _, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "stop"], person, Ct));
        Assert.False(File.Exists(dir.Paths.StartOwedFile), "a person's own stop left the note that a start is owed");
    }

    /// <summary>
    /// A stop made on behalf of an install or an update (<c>--start-owed</c>) writes the note that a start is owed, the
    /// moment the stop began, where a person's own stop takes it back: the answer printed is the stop's own, unchanged.
    /// </summary>
    [Fact]
    [Critical(Family.R2, "a stop on an installer's behalf leaves the start it owes")]
    public async Task With_start_owed_the_stop_writes_the_note_and_without_it_a_stop_takes_it_back()
    {
        using var dir = new TempPaths();
        var moment = new DateTimeOffset(2026, 11, 17, 17, 40, 12, TimeSpan.Zero);
        var body = RuntimeVerbs.ShutdownJson("rt_LIVE");
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, body);
        });

        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());
        var exit = await CliApp.RunAsync(["runtime", "stop", "--start-owed"], env with { Clock = new FixedClock(moment) }, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("{\"stopped_at\":\"2026-11-17T17:40:12Z\"}", File.ReadAllText(dir.Paths.StartOwedFile));

        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (person, _, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "stop"], person, Ct));
        Assert.False(File.Exists(dir.Paths.StartOwedFile), "a stop without --start-owed left the note that a start is owed");
    }

    /// <summary>
    /// The note stands only for a runtime that was stopped: one that refused the shutdown, or acknowledged it and did not
    /// go, takes it back, as the install routine does. A note that stood before such a stop is not one this stop wrote, and
    /// a stop that cannot find a runtime writes nothing and leaves what stands.
    /// </summary>
    [Fact]
    public async Task With_start_owed_a_runtime_that_did_not_stop_leaves_no_note()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var stays = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (outlives, _, _) = RuntimeVerbs.Environment(dir, new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"))), stays);

        Assert.Equal(ExitCodes.ApiError, await RuntimeStopCommand.RunOnBehalfAsync(outlives, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll));
        Assert.False(File.Exists(dir.Paths.StartOwedFile), "a runtime that did not go was said to owe a start");

        const string Refused = "{\"error\":{\"code\":\"internal_error\",\"message\":\"no\",\"retryable\":true}}";
        var (refuses, output, _) = RuntimeVerbs.Environment(dir, new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.InternalServerError, Refused)), new FakeProcessControl());

        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["runtime", "stop", "--start-owed"], refuses, Ct));
        Assert.Equal(Refused + System.Environment.NewLine, output.ToString());
        Assert.False(File.Exists(dir.Paths.StartOwedFile), "a runtime that refused the stop was said to owe a start");

        File.Delete(dir.Paths.DescriptorFile);
        File.WriteAllText(dir.Paths.StartOwedFile, "{\"stopped_at\":\"2026-11-17T15:05:35Z\"}");
        var (idle, _, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        Assert.Equal(ExitCodes.RuntimeUnavailable, await CliApp.RunAsync(["runtime", "stop", "--start-owed"], idle, Ct));
        Assert.Equal("{\"stopped_at\":\"2026-11-17T15:05:35Z\"}", File.ReadAllText(dir.Paths.StartOwedFile));
    }

    /// <summary>With no data directory there is no runtime to stop: the flag writes nothing and makes no folder.</summary>
    [Fact]
    public async Task With_start_owed_and_no_data_directory_nothing_is_written()
    {
        using var dir = new TempPaths();
        Directory.Delete(dir.Paths.Root, recursive: true);
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["runtime", "stop", "--start-owed"], env, Ct);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.NoDescriptor, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.False(Directory.Exists(dir.Paths.Root), "a stop with --start-owed made the data directory");
    }

    /// <summary>The flag's help says whose stop it is for and what it writes.</summary>
    [Fact]
    public async Task The_help_of_stop_names_the_flag_and_whose_stop_it_is_for()
    {
        using var dir = new TempPaths();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "stop", "--help"], env, Ct));
        var help = string.Join(' ', output.ToString().Split((char[])['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("--start-owed", help, StringComparison.Ordinal);
        Assert.Contains("on behalf of an install or an update, never one a person or an agent asks for", help, StringComparison.Ordinal);
        Assert.Contains("run/start-owed.json", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runtime_that_goes_away_prints_the_acknowledgement_and_exits_0()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var body = RuntimeVerbs.ShutdownJson("rt_LIVE");
        var handler = new FakeHandler(_ =>
        {
            // What the runtime does moments after acknowledging: the descriptor goes, then the process.
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, body);
        });
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    [Critical(Family.R2, "a stop that does not stop is not success")]
    public async Task A_process_that_outlives_its_acknowledgement_exits_1_with_shutdown_timeout()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE")));
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.ShutdownTimeout, envelope.Code);
        Assert.True(envelope.Retryable);
    }

    /// <summary>The line a stop writes when the runtime it stopped could not remove its descriptor.</summary>
    private static string LeftBehind(TempPaths dir) =>
        $"Its endpoint descriptor at '{dir.Paths.DescriptorFile}' was left behind: the runtime could not remove it. Another program may be holding it, or it may be read-only; the runtime's log says which. A CLI reading it finds no runtime at its address and says so, and the next start replaces it once the file is free.";

    /// <summary>
    /// Gone is the acknowledged process having ended. A descriptor still naming it is one the runtime could not remove,
    /// which a program holding it can keep there for as long as it likes: the stop succeeds, says so on stderr without
    /// claiming which of the two it was, and leaves standard output what it always was.
    /// </summary>
    [Fact]
    public async Task A_descriptor_the_stopped_runtime_left_behind_is_said_on_stderr_and_the_stop_succeeds()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var body = RuntimeVerbs.ShutdownJson("rt_LIVE");
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, body));
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(LeftBehind(dir) + System.Environment.NewLine, error.ToString());
        Assert.DoesNotContain("held it", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_human_mode_a_descriptor_left_behind_follows_the_stopped_line_on_stderr()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE")));
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: true, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal($"Runtime stopped (instance rt_LIVE, pid {RuntimeVerbs.Pid})." + System.Environment.NewLine, output.ToString());
        Assert.Equal(LeftBehind(dir) + System.Environment.NewLine, error.ToString());
    }

    /// <summary>
    /// A descriptor naming another instance is a runtime started since. That is not this stop's failure, and it is not
    /// the stopped runtime's leftover either: the line names it for what it is.
    /// </summary>
    [Fact]
    public async Task A_descriptor_another_runtime_published_since_is_named_and_the_stop_succeeds()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var body = RuntimeVerbs.ShutdownJson("rt_LIVE");
        var handler = new FakeHandler(_ =>
        {
            // The acknowledged runtime goes, and another starts and publishes before the stop looks again.
            dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_NEXT", pid: 88));
            return RuntimeVerbs.Response(HttpStatusCode.OK, body);
        });
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl { RunningPids = { 88 } });

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(
            "A runtime with another instance id, 'rt_NEXT', has started since; this stop ended only the one it acknowledged." + System.Environment.NewLine,
            error.ToString());
    }

    /// <summary>
    /// A descriptor there but unreadable — malformed here; one this account may not read is the same answer — names no
    /// other instance, so the stopped runtime is gone and the line is the hedged one.
    /// </summary>
    [Fact]
    public async Task A_descriptor_left_unreadable_is_the_hedged_line_and_the_stop_succeeds()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ =>
        {
            File.WriteAllText(dir.Paths.DescriptorFile, "{ not a descriptor");
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (env, _, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(LeftBehind(dir) + System.Environment.NewLine, error.ToString());
    }

    /// <summary>
    /// The timeout is said only of what it is true of: the acknowledged process still there, whatever the descriptor.
    /// </summary>
    [Fact]
    public async Task A_process_still_there_with_its_descriptor_gone_is_still_a_timeout()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.ShutdownTimeout, envelope.Code);
        Assert.Equal("The runtime acknowledged the shutdown but had not exited after 0.3 s.", envelope.Message);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task A_rejected_token_exits_3_with_unauthorized()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}"));
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.Unauthorized, RuntimeVerbs.Envelope(output.ToString()).Code);
    }

    [Fact]
    public async Task An_api_error_is_printed_verbatim_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        const string Envelope = "{\"error\":{\"code\":\"internal_error\",\"message\":\"nope\",\"retryable\":true}}";
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.InternalServerError, Envelope));
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(Envelope + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task An_api_error_whose_body_is_not_json_is_printed_as_an_envelope_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(string.Empty) });
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeError, envelope.Code);
        Assert.True(envelope.Retryable);
    }

    [Fact]
    public async Task Human_mode_names_the_instance_that_stopped()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: true, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains("rt_LIVE", text, StringComparison.Ordinal);
        Assert.Contains("77", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);

        // The descriptor went with the runtime, so there is nothing to say about it.
        Assert.Equal(string.Empty, error.ToString());
    }
}
