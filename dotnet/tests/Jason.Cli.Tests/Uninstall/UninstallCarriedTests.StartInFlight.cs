using System.Runtime.Versioning;
using System.Text.Json;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// A runtime of this installation still starting when the uninstall looks: started a moment before by the application, by
/// a logon or by a person, it runs this installation's jason and has not yet published its descriptor, so it does not
/// answer. The uninstall waits for it, a bounded time, and stops it once it answers; one that does not come up, and any
/// other jason of this installation still running after the stop, refuses with nothing further removed. Held through the
/// verb with the remover's look scripted look by look, and a clock on which a short wait passes at once.
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
        Assert.Contains("a runtime that never came up", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("so nothing was removed", refusal.Message, StringComparison.Ordinal);
        Assert.True(clock.Passed >= TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
        Assert.True(Directory.Exists(application));
    }

    [Fact]
    public async Task A_runtime_that_answers_at_the_first_look_makes_no_wait_and_its_descendants_are_not_counted()
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
        var clock = new Leaping();

        // This process, the runtime, and a plugin host the runtime started, all from the carried jason, until the stop. The
        // remover here tells the plugin host's parent, as Windows does; elsewhere no parent is told, and a runtime's children
        // count until its stop ends them, within the moment the look after the stop gives them.
        remover.Parents[9002] = RuntimeVerbs.Pid;
        remover.Looks = (image, _) => Same(image, executable) && processes.RunningPids.Contains(RuntimeVerbs.Pid)
            ? [System.Environment.ProcessId, RuntimeVerbs.Pid, 9002]
            : [];
        env = env with { Processes = processes, Clock = clock, HttpHandler = Stopping(processes) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString() + env.Error);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString() == "Stopped the runtime.");
        Assert.DoesNotContain(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains("starting", StringComparison.Ordinal));
        Assert.DoesNotContain("may be starting", env.Error.ToString()!, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, clock.Passed);
    }

    [Fact]
    public async Task A_jason_of_the_installation_still_running_after_the_stop_refuses_before_the_application_goes()
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var registrar = (Autostart.RecordingRegistrar)env.Autostart!;
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
        var clock = new Leaping();

        // A jason nothing of this data directory started: it runs on after the runtime has stopped.
        remover.Looks = (image, _) => !Same(image, executable)
            ? []
            : processes.RunningPids.Contains(RuntimeVerbs.Pid) ? [RuntimeVerbs.Pid, 9300] : [9300];
        env = env with { Processes = processes, Clock = clock, HttpHandler = Stopping(processes) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal(CliErrors.JasonRunning, report.GetProperty("refusal_code").GetString());
        var refusal = report.GetProperty("refusal").GetString()!;
        Assert.Contains("(process 9300)", refusal, StringComparison.Ordinal);
        Assert.Contains("with its runtime stopped", refusal, StringComparison.Ordinal);
        Assert.Contains("its logon registration included", refusal, StringComparison.Ordinal);
        Assert.True(clock.Passed >= TimeSpan.FromSeconds(10), $"it gave what remained {clock.Passed} to end");
        Assert.True(clock.Passed < TimeSpan.FromSeconds(60), $"it waited {clock.Passed}, as for a start");
        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
        Assert.True(Directory.Exists(application));
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
    /// A command-line install, as on Linux: the same wait, by the executable this uninstall removes.
    /// </summary>
    [Fact]
    public async Task A_command_line_install_waits_for_a_start_in_flight_the_same_way()
    {
        var bin = Path.Combine(_tree.Paths.Root, "bin");
        Directory.CreateDirectory(bin);
        var executable = Path.GetFullPath(Path.Combine(bin, "jason"));
        File.WriteAllText(executable, "jason 0.34.0");
        var (env, output, remover) = Environment(null, executable);
        var clock = new Leaping();
        remover.Looks = (image, _) => Same(image, executable) ? [Starter] : [];
        env = env with { Clock = clock };

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
    /// Windows, from a terminal: the wait comes first, and what it leaves once the runtime answers is judged by the look the
    /// application's uninstaller makes, which refuses a starter that has not left within its moment.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_the_wait_comes_before_the_look_for_another_jason_and_that_look_judges_what_remains()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The application's uninstaller and its look are Windows'.");
        var executable = WindowsApplication();
        var (env, output, remover) = Environment(AbsentKeysPlaces(), executable);
        var registrar = (Autostart.RecordingRegistrar)env.Autostart!;
        var processes = new FakeProcessControl();
        var clock = new Leaping();
        var looks = 0;
        remover.Looks = (image, _) =>
        {
            if (!Same(image, executable))
            {
                return [];
            }

            if (++looks == 3)
            {
                _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STARTING"));
                processes.RunningPids.Add(RuntimeVerbs.Pid);
            }

            return [Starter, RuntimeVerbs.Pid];
        };
        env = env with { Processes = processes, Clock = clock };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.JasonRunning, refusal.Code);
        Assert.Contains($"(process {Starter})", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("the application's uninstaller removes nothing while one does", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(1, Count(env.Error.ToString()!, "may be starting"));
        Assert.True(clock.Passed >= TimeSpan.FromSeconds(5) && clock.Passed < TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
    }

    /// <summary>
    /// Windows, from a terminal: a runtime still starting is waited for, the starter leaves within its moment, the look the
    /// application's uninstaller makes then finds none, and the uninstall goes on to the end, the runtime stopped. Places
    /// whose registry keys are never made, so nothing is written under this account's registry.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_the_starter_leaving_within_its_moment_leaves_the_look_nothing_and_the_uninstall_completes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The application's uninstaller and its look are Windows'.");
        var executable = WindowsApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
        var (env, output, remover) = Environment(AbsentKeysPlaces(), executable);
        var processes = new FakeProcessControl();
        var clock = new Leaping();
        var stops = 0;
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
        Assert.Equal(1, Count(env.Error.ToString()!, "may be starting"));
        Assert.True(clock.Passed < TimeSpan.FromSeconds(60), $"it waited {clock.Passed}");
        Assert.Contains($"any-session {executable}", remover.Reads);
        Assert.Equal($"application.uninstaller {application}", remover.Calls[^1]);
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
