using System.Runtime.Versioning;
using System.Text.Json;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// A runtime of this installation still starting when the uninstall looks: started a moment before by the application, by
/// a logon or by a person, it runs this installation's jason and has not yet published its descriptor, so it does not
/// answer. Off Windows the uninstall waits for it, a bounded time, and stops it once it answers; one that does not come up,
/// and any other jason of this installation still running after the stop, refuses with nothing further removed, and one
/// started in the removal's own seconds keeps what it runs from. Windows waits for nothing and refuses at once, as it always
/// has. Held through the verb with the remover's look scripted look by look, and a clock on which a short wait passes at
/// once.
/// </summary>
public sealed partial class UninstallCarriedTests
{
    private const int Starter = 9100;

    [Fact]
    public async Task A_runtime_still_starting_is_waited_for_then_stopped_and_the_starter_leaving_after_it_refuses_nothing()
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var processes = new FakeProcessControl();
        var clock = new Leaping();
        var stops = 0;

        // The starter (`jason runtime start`) and the runtime it launched both run the carried jason; the runtime publishes
        // its descriptor at the third look, and the starter is still there for two looks after that before it leaves.
        var looks = 0;
        remover.Looks = (image, _) =>
        {
            if (!Same(image, executable))
            {
                return [];
            }

            looks++;
            if (looks == 3)
            {
                _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
                processes.RunningPids.Add(RuntimeVerbs.Pid);
            }

            var running = new List<int>();
            if (looks <= 5)
            {
                running.Add(Starter);
            }

            if (looks < 3 || processes.RunningPids.Contains(RuntimeVerbs.Pid))
            {
                running.Add(RuntimeVerbs.Pid);
            }

            return running;
        };
        env = env with { Processes = processes, Clock = clock, HttpHandler = Stopping(processes, () => stops++) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + env.Error);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal(RuntimeVerbs.Pid, report.GetProperty("plan").GetProperty("runtime_pid").GetInt32());
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString() == "A runtime was starting when this began; it answered, and it was stopped.");
        Assert.True(stops > 0, "the runtime that came up was not stopped through jason runtime stop");
        var said = env.Error.ToString()!;
        Assert.Equal(1, Count(said, "may be starting"));
        Assert.Contains($"process {RuntimeVerbs.Pid}, {Starter}", said, StringComparison.Ordinal);
        Assert.Contains("up to 60 s", said, StringComparison.Ordinal);
        Assert.False(Directory.Exists(application), "the application was left");
        Assert.True(looks >= 6, $"the starter was not given its moment to leave: {looks} looks");
    }

    [Fact]
    public async Task A_jason_of_the_installation_that_never_comes_up_is_refused_at_the_bound_with_nothing_removed()
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var registrar = (Autostart.RecordingRegistrar)env.Autostart!;
        var clock = new Leaping();
        remover.Looks = (image, _) => Same(image, executable) ? [Starter] : [];
        env = env with { Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.JasonRunning, refusal.Code);
        Assert.Contains($"(process {Starter})", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("within 60 s", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("It may be a runtime still starting or migrating its database, or a set-up or an update still finishing, or a call to a provider", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("so nothing was removed", refusal.Message, StringComparison.Ordinal);
        Assert.True(clock.Passed >= TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
        Assert.True(Directory.Exists(application));
    }

    /// <summary>
    /// A runtime that answers at the first look is the plan's runtime, stopped as any is, with no wait and nothing said on
    /// stderr; the plugin host it started ends with its stop. That a live runtime's descendants are never named is held where
    /// that rule acts, the look the Windows uninstaller makes:
    /// <see cref="On_Windows_what_the_stopped_runtime_started_is_not_counted_and_what_it_did_not_start_is_named"/>.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_answers_at_the_first_look_makes_no_wait_and_says_nothing_on_stderr()
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
        var clock = new Leaping();

        // This process, the runtime, and a plugin host the runtime started, all from the carried jason, until the stop.
        remover.Looks = (image, _) => Same(image, executable) && processes.RunningPids.Contains(RuntimeVerbs.Pid)
            ? [System.Environment.ProcessId, RuntimeVerbs.Pid, 9002]
            : [];
        env = env with { Processes = processes, Clock = clock, HttpHandler = Stopping(processes) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + env.Error);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString() == "Stopped the runtime.");
        Assert.DoesNotContain(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains("starting", StringComparison.Ordinal));
        Assert.Equal(string.Empty, env.Error.ToString());
        Assert.Equal(TimeSpan.Zero, clock.Passed);
    }

    /// <summary>
    /// A jason of the installation that outlives the stop past the settle, as a plugin host does whose call to a provider
    /// runs past the stop's drain: refused with jason_running, its sentence naming that call first, and what was removed by
    /// then said as the system has it: nothing on macOS, where the logon registration comes after the stop; the logon
    /// registration on Linux, where it came first.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_jason_of_the_installation_outliving_the_stop_past_the_settle_is_refused_naming_a_providers_call_first(bool linux)
    {
        const int Outlives = 9300;
        var executable = linux ? CommandLineExecutable() : MacApplication();
        var (env, output, remover) = Environment(linux ? null : MacPlaces(), executable);
        var registrar = new Autostart.RecordingRegistrar(linux ? Jason.Cli.Autostart.AutostartPlatform.Linux : Jason.Cli.Autostart.AutostartPlatform.MacOs);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
        var clock = new Leaping();
        remover.Looks = (image, _) => !Same(image, executable)
            ? []
            : processes.RunningPids.Contains(RuntimeVerbs.Pid) ? [RuntimeVerbs.Pid, Outlives] : [Outlives];
        env = env with { Autostart = registrar, Processes = processes, Clock = clock, HttpHandler = Stopping(processes) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal(CliErrors.JasonRunning, report.GetProperty("refusal_code").GetString());
        var refusal = report.GetProperty("refusal").GetString()!;
        Assert.Contains("(process 9300)", refusal, StringComparison.Ordinal);
        Assert.Contains("with its runtime stopped", refusal, StringComparison.Ordinal);
        var call = refusal.IndexOf("It may be a call to a provider that the stopped runtime left running, which ends by itself within that operation's own time limit", StringComparison.Ordinal);
        Assert.True(call >= 0 && call < refusal.IndexOf("a runtime still starting", StringComparison.Ordinal), refusal);
        Assert.Contains("wait until it ends, then uninstall again", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("end it", refusal, StringComparison.Ordinal);
        Assert.True(clock.Passed >= TimeSpan.FromSeconds(10), $"it gave what remained {clock.Passed} to end");
        Assert.True(clock.Passed < TimeSpan.FromSeconds(60), $"it waited {clock.Passed}, as for a start");
        Assert.Empty(remover.Calls);
        if (linux)
        {
            Assert.Contains("except the logon registration, which had already been taken away", refusal, StringComparison.Ordinal);
            Assert.Equal(1, registrar.Removals);
            Assert.True(File.Exists(executable));
        }
        else
        {
            Assert.Contains("its logon registration included", refusal, StringComparison.Ordinal);
            Assert.Equal(0, registrar.Removals);
            Assert.True(Directory.Exists(ApplicationOf(executable)));
        }
    }

    /// <summary>
    /// A jason started in the removal's own seconds, after the stop and the settle found none (a person's or an agent's
    /// runtime start): the last look, just before the application on macOS or the executable on Linux is deleted, finds it,
    /// and keeps what it runs from as a problem, so the verb does not report completed.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_jason_started_after_the_stop_and_the_settle_keeps_what_it_runs_from_and_the_verb_does_not_complete(bool linux)
    {
        const int Late = 9500;
        var executable = linux ? CommandLineExecutable() : MacApplication();
        var (env, output, remover) = Environment(linux ? null : MacPlaces(), executable);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
        var clock = new Leaping();
        var afterTheStop = 0;
        remover.Looks = (image, _) =>
        {
            if (!Same(image, executable))
            {
                return [];
            }

            if (processes.RunningPids.Contains(RuntimeVerbs.Pid))
            {
                return [RuntimeVerbs.Pid];
            }

            // The settle's look finds nothing; the look after it finds the jason started since.
            return ++afterTheStop == 1 ? [] : [Late];
        };
        env = env with
        {
            Autostart = new Autostart.RecordingRegistrar(linux ? Jason.Cli.Autostart.AutostartPlatform.Linux : Jason.Cli.Autostart.AutostartPlatform.MacOs),
            Processes = processes,
            Clock = clock,
            HttpHandler = Stopping(processes),
        };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal(JsonValueKind.Null, report.GetProperty("refusal_code").ValueKind);
        Assert.False(report.GetProperty("completed").GetBoolean());
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString() == "Stopped the runtime.");
        var problem = Assert.Single(report.GetProperty("problems").EnumerateArray()).GetString()!;
        Assert.Equal(2, afterTheStop);
        Assert.True(clock.Passed < TimeSpan.FromSeconds(1), $"the settle waited {clock.Passed} on nothing");
        if (linux)
        {
            Assert.StartsWith($"The executable at '{executable}' was kept", problem, StringComparison.Ordinal);
            Assert.Contains($"(process {Late})", problem, StringComparison.Ordinal);
            Assert.True(File.Exists(executable));
            Assert.DoesNotContain(remover.Calls, call => call.StartsWith("executable.remove", StringComparison.Ordinal));
        }
        else
        {
            var application = ApplicationOf(executable);
            Assert.StartsWith($"The application at {application} was not deleted", problem, StringComparison.Ordinal);
            Assert.Contains($"another jason of this installation runs from it (process {Late})", problem, StringComparison.Ordinal);
            Assert.True(Directory.Exists(application));
            Assert.DoesNotContain(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.StartsWith("Removed the application", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_dry_run_never_waits()
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var clock = new Leaping();
        remover.Looks = (image, _) => Same(image, executable) ? [Starter] : [];
        env = env with { Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + env.Error);
        Assert.DoesNotContain("may be starting", env.Error.ToString()!, StringComparison.Ordinal);
        Assert.DoesNotContain($"any-session {executable}", remover.Reads);
        Assert.Equal(TimeSpan.Zero, clock.Passed);
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task A_jason_of_the_installation_that_ends_by_itself_with_no_runtime_answering_lets_the_uninstall_go_on()
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var clock = new Leaping();
        var looks = 0;
        remover.Looks = (image, _) => Same(image, executable) && ++looks <= 2 ? [Starter] : [];
        env = env with { Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + env.Error);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains("ended by itself before anything was removed", StringComparison.Ordinal));
        Assert.Contains(report.GetProperty("kept").EnumerateArray(), line => line.GetString() == "No runtime was running.");
        Assert.Equal(1, Count(env.Error.ToString()!, "may be starting"));
        Assert.False(Directory.Exists(application));
    }

    [Fact]
    public async Task Jason_Desktop_open_is_refused_before_any_wait()
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var clock = new Leaping();
        var window = Path.Combine(application, "Contents", "MacOS", "Jason");
        remover.Looks = (image, _) => Same(image, window) ? [5150] : Same(image, executable) ? [Starter] : [];
        env = env with { Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CliErrors.DesktopRunning, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.DoesNotContain("may be starting", env.Error.ToString()!, StringComparison.Ordinal);
        Assert.DoesNotContain($"any-session {executable}", remover.Reads);
        Assert.Equal(TimeSpan.Zero, clock.Passed);
    }

    /// <summary>
    /// A command-line install on Linux: the same wait, by the executable this uninstall removes.
    /// </summary>
    [Fact]
    public async Task A_command_line_install_waits_for_a_start_in_flight_the_same_way()
    {
        var executable = CommandLineExecutable();
        var (env, output, remover) = Environment(null, executable);
        var clock = new Leaping();
        remover.Looks = (image, _) => Same(image, executable) ? [Starter] : [];
        env = env with { Autostart = new Autostart.RecordingRegistrar(Jason.Cli.Autostart.AutostartPlatform.Linux), Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.JasonRunning, refusal.Code);
        Assert.Contains($"(process {Starter})", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(1, Count(env.Error.ToString()!, "may be starting"));
        Assert.True(clock.Passed >= TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(executable));
    }

    /// <summary>
    /// Windows, from a terminal, stays as it always was: no wait is made, and a start in flight - the starter and the runtime
    /// it launched, which has not yet answered - is refused at once by the look the application's uninstaller makes, with the
    /// sentence that names it. Places whose registry keys are never made, so nothing is written under this account's
    /// registry.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_no_wait_is_made_and_a_start_in_flight_is_refused_at_once_with_the_sentence_that_names_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The application's uninstaller and its look are Windows'.");
        var executable = WindowsApplication();
        var (env, output, remover) = Environment(AbsentKeysPlaces(), executable);
        var registrar = (Autostart.RecordingRegistrar)env.Autostart!;
        var clock = new Leaping();
        remover.Looks = (image, _) => Same(image, executable) ? [Starter, RuntimeVerbs.Pid] : [];
        env = env with { Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.JasonRunning, refusal.Code);
        Assert.Contains($"(process {RuntimeVerbs.Pid}, {Starter})", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("the application's uninstaller removes nothing while one does", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("It may be a runtime still starting or migrating its database", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("in another session of this account", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, env.Error.ToString());
        Assert.Equal(TimeSpan.Zero, clock.Passed);
        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
    }

    /// <summary>
    /// A process of the same application under another account, as on a Mac more than one account uses, where the one
    /// application in /Applications is every account's: the look cannot tell its account, so it is refused, never taken
    /// from under that account's Jason. With this account's runtime down, at the bound, nothing removed; with it up, after
    /// this account's runtime is stopped and the look after the stop has given the rest their moment. Either way the
    /// refusal names the case without claiming it, and says nothing of another session of this account.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_jason_of_the_same_application_under_another_account_is_refused_and_named_as_such(bool runtimeUp)
    {
        const int Theirs = 9400;
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var registrar = (Autostart.RecordingRegistrar)env.Autostart!;
        var processes = new FakeProcessControl();
        var clock = new Leaping();
        var stops = 0;
        if (runtimeUp)
        {
            _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
            processes.RunningPids.Add(RuntimeVerbs.Pid);
        }

        remover.Looks = (image, _) => !Same(image, executable)
            ? []
            : processes.RunningPids.Contains(RuntimeVerbs.Pid) ? [RuntimeVerbs.Pid, Theirs] : [Theirs];
        env = env with { Processes = processes, Clock = clock, HttpHandler = Stopping(processes, () => stops++) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        string code;
        string message;
        if (runtimeUp)
        {
            var report = JsonDocument.Parse(output.ToString()).RootElement;
            code = report.GetProperty("refusal_code").GetString()!;
            message = report.GetProperty("refusal").GetString()!;
            Assert.True(stops > 0, "this account's runtime was not stopped first");
            Assert.Contains("with its runtime stopped", message, StringComparison.Ordinal);
            Assert.Contains("its logon registration included", message, StringComparison.Ordinal);
            Assert.True(clock.Passed >= TimeSpan.FromSeconds(10) && clock.Passed < TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        }
        else
        {
            var refusal = RuntimeVerbs.Envelope(output.ToString());
            code = refusal.Code;
            message = refusal.Message;
            Assert.Equal(0, stops);
            Assert.Contains("within 60 s", message, StringComparison.Ordinal);
            Assert.True(clock.Passed >= TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        }

        Assert.Equal(CliErrors.JasonRunning, code);
        Assert.Contains($"(process {Theirs})", message, StringComparison.Ordinal);
        Assert.Contains("on a computer more than one account uses, it may be Jason running under another account from this same application, which that account quits", message, StringComparison.Ordinal);
        Assert.DoesNotContain("another session of this account", message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
        Assert.True(Directory.Exists(application));
    }

    /// <summary>A command-line install's executable, in a folder of the test's own.</summary>
    private string CommandLineExecutable()
    {
        var bin = Path.Combine(_tree.Paths.Root, "bin");
        Directory.CreateDirectory(bin);
        var executable = Path.GetFullPath(Path.Combine(bin, "jason"));
        File.WriteAllText(executable, "jason 0.34.0");
        return executable;
    }

    private static string ApplicationOf(string executable) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));

    private static bool Same(string image, string executable) =>
        string.Equals(Path.GetFullPath(image), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);

    private static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    /// <summary>A runtime that stops when it is asked: its descriptor goes, and its process with it.</summary>
    private FakeHandler Stopping(FakeProcessControl processes, Action? asked = null) => new(_ =>
    {
        asked?.Invoke();
        File.Delete(_dir.Paths.DescriptorFile);
        processes.RunningPids.Remove(RuntimeVerbs.Pid);
        return RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_STARTING"));
    });

    /// <summary>
    /// A clock on which a short wait - a poll, at most a second - passes at once and moves the clock on by its length, so a
    /// bound of a minute is reached in a few hundred looks and no time. A longer timer, a request's budget, never fires, as on
    /// a machine where the request is answered first.
    /// </summary>
    private sealed class Leaping : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = new(2026, 11, 18, 18, 0, 0, TimeSpan.Zero);
        private TimeSpan _passed;

        public TimeSpan Passed
        {
            get
            {
                lock (_gate)
                {
                    return _passed;
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == Timeout.InfiniteTimeSpan || dueTime > TimeSpan.FromSeconds(1))
            {
                return new Dormant();
            }

            lock (_gate)
            {
                _now += dueTime;
                _passed += dueTime;
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new Dormant();
        }

        private sealed class Dormant : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
