using System.Net;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The order an uninstall takes its steps in, and the refusal that ends it.
/// </summary>
/// <remarks>
/// The order is the safety property, not a tidiness one. The registration goes first so that a logon in the
/// middle of an uninstall cannot start the thing being removed; a runtime that will not stop ends the whole
/// verb, because deleting a binary out from under a live process is how a machine ends up with neither a
/// working installation nor a clean one.
/// </remarks>
public class UninstallOrderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_registration_is_removed_before_the_runtime_is_asked_to_stop()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var log = new StepLog();

        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);

        var (exit, _, _) = await RunAsync(dir, log, StopsWhenAsked(dir, log, processes), processes: processes);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["autostart.remove", "system.shutdown"], log.Steps.Take(2));
    }

    /// <summary>
    /// A runtime that acknowledges and then does not go takes the verb with it: nothing further is removed,
    /// the exit code says refused, and the message names the runtime rather than blaming the operator.
    /// </summary>
    /// <remarks>
    /// With something planned at every step after it — a recorded skill, a PATH entry that is ours, the data
    /// directory on the explicit word — because a refusal can only be seen to stop the steps after it when
    /// there are steps after it to stop. Without them this passed whatever step 2 decided.
    /// </remarks>
    [Fact]
    public async Task A_runtime_that_will_not_stop_refuses_the_whole_verb_and_removes_nothing_further()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STUBBORN"));
        var log = new StepLog();

        var (exit, output, remover) = await RunAsync(dir, log, AcknowledgesAndStays(log), everything: true);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(["autostart.remove", "system.shutdown"], log.Steps);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// A runtime that is alive and does not answer is not a runtime that has stopped. <c>runtime stop</c> exits 3
    /// for a refused connection, a request that timed out and a token the runtime rejected — all three with its
    /// descriptor still on disk and its process still in the table — and this verb used to read every 3 as
    /// "the descriptor went between the plan and here", carry on, and remove the executable and the data
    /// directory from under a runtime that was still running.
    /// </summary>
    [Theory]
    [InlineData("refused")]
    [InlineData("unauthorized")]
    [InlineData("slow")]
    public async Task A_runtime_that_is_running_and_does_not_answer_ends_the_verb(string how)
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_SILENT"));
        var log = new StepLog();

        var (exit, output, remover) = await RunAsync(dir, log, DoesNotAnswer(how, log), everything: true);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(RuntimeVerbs.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), output.ToString(), StringComparison.Ordinal);
        Assert.Equal(["autostart.remove", "system.shutdown"], log.Steps);
        Assert.Empty(remover.Calls);
        Assert.True(Directory.Exists(dir.Paths.Root), "the data directory of a running runtime was removed.");
    }

    /// <summary>
    /// And a descriptor whose process has gone is not a runtime either: a runtime that crashed, or a machine that
    /// restarted under it, leaves one behind. Refusing there would hold up every uninstall on a machine whose
    /// runtime once died, with a message saying it "did not stop" when nothing was running at all.
    /// </summary>
    [Fact]
    public async Task A_descriptor_whose_process_has_gone_does_not_hold_the_verb_up()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_GONE"));
        var log = new StepLog();
        var processes = new FakeProcessControl();

        var (exit, output, _) = await RunAsync(dir, log, DoesNotAnswer("refused", log), processes: processes, running: false);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("was not running", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("path.remove", log.Steps);
    }

    /// <summary>
    /// A runtime that stopped but could not remove its descriptor — a program held it — has stopped. The verb used to
    /// read the descriptor left behind as a runtime that "did not stop", refuse, and leave the installation as it was.
    /// It goes on now, and the stop's line saying the descriptor was left behind reaches the verb's standard error as
    /// the diagnostic it is.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_stopped_but_left_its_descriptor_behind_does_not_hold_the_verb_up()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var log = new StepLog();
        var processes = new FakeProcessControl();
        var handler = new FakeHandler(_ =>
        {
            // The process goes; the descriptor stays, as one held by another program does.
            log.Add("system.shutdown");
            processes.RunningPids.Remove(RuntimeVerbs.Pid);
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var errors = new StringWriter();

        var (exit, output, remover) = await RunAsync(dir, log, handler, processes: processes, everything: true, errors: errors);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Stopped the runtime.", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("path.remove", log.Steps);
        Assert.NotEmpty(remover.Calls);
        Assert.Contains($"Its endpoint descriptor at '{dir.Paths.DescriptorFile}' was left behind: the runtime could not remove it.", errors.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nor is one that went between the plan and the question, with its process gone too — which is the one
    /// case the old reading of exit 3 was written for, and still carries on.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_went_between_the_plan_and_the_question_does_not_hold_the_verb_up()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_WENT"));
        var log = new StepLog();
        var processes = new FakeProcessControl();

        var handler = new FakeHandler(_ =>
        {
            log.Add("system.shutdown");
            File.Delete(dir.Paths.DescriptorFile);
            processes.RunningPids.Remove(RuntimeVerbs.Pid);
            throw new HttpRequestException("Connection refused");
        });

        var (exit, _, _) = await RunAsync(dir, log, handler, processes: processes);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("path.remove", log.Steps);
    }

    /// <summary>
    /// But a descriptor that went while the process it named is still in the table is a runtime on its way out
    /// rather than gone, and the verb does not remove anything from under it.
    /// </summary>
    [Fact]
    public async Task A_descriptor_that_went_while_its_process_stayed_ends_the_verb()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LEAVING"));
        var log = new StepLog();

        var handler = new FakeHandler(_ =>
        {
            log.Add("system.shutdown");
            File.Delete(dir.Paths.DescriptorFile);
            throw new HttpRequestException("Connection refused");
        });

        var (exit, output, remover) = await RunAsync(dir, log, handler, everything: true);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// A pid names a process only while that process lives. After a restart the pid a stale descriptor recorded
    /// belongs to whatever the machine started next under it, and "is that pid running" says yes about a stranger:
    /// the verb refused with <c>runtime_still_running</c> and told the operator to end it. A process that started
    /// after the runtime recorded its own start is not that runtime, and the verb carries on as it does for a
    /// process that has gone.
    /// </summary>
    [Fact]
    public async Task A_live_process_that_started_after_the_runtime_it_is_taken_for_does_not_hold_the_verb_up()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_REUSED"));
        var log = new StepLog();
        var processes = new FakeProcessControl();
        processes.StartTimes[RuntimeVerbs.Pid] = DateTimeOffset.UnixEpoch.AddHours(1);

        var (exit, output, _) = await RunAsync(dir, log, DoesNotAnswer("refused", log), processes: processes, everything: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("started after", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("path.remove", log.Steps);
    }

    /// <summary>But a process that started before the runtime recorded its start is that runtime, and still ends the verb.</summary>
    [Fact]
    public async Task A_live_process_that_started_before_the_runtime_recorded_its_start_still_ends_the_verb()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_SILENT"));
        var log = new StepLog();
        var processes = new FakeProcessControl();
        processes.StartTimes[RuntimeVerbs.Pid] = DateTimeOffset.UnixEpoch.AddSeconds(-1);

        var (exit, output, remover) = await RunAsync(dir, log, DoesNotAnswer("refused", log), processes: processes, everything: true);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("end that process", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// And one this prompt may not ask when it started is neither shown to be the runtime nor shown not to be: the
    /// verb refuses, because it may be, and does not tell the operator to end a process nobody has shown is it.
    /// </summary>
    [Fact]
    public async Task A_live_process_whose_start_cannot_be_read_ends_the_verb_and_is_not_named_for_ending()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_HIDDEN"));
        var log = new StepLog();
        var processes = new FakeProcessControl();
        processes.StartTimes[RuntimeVerbs.Pid] = null;

        var (exit, output, remover) = await RunAsync(dir, log, DoesNotAnswer("refused", log), processes: processes, everything: true);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("cannot be told", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("end that process", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// A descriptor that cannot be read names nothing that can be asked about, so the verb refuses rather than guess
    /// past it — and names the file, which is what the operator deletes where no runtime is running.
    /// </summary>
    [Fact]
    public async Task A_descriptor_that_cannot_be_read_ends_the_verb()
    {
        using var dir = new TempPaths();
        Directory.CreateDirectory(Path.GetDirectoryName(dir.Paths.DescriptorFile)!);
        File.WriteAllText(dir.Paths.DescriptorFile, "{ not a descriptor");
        var log = new StepLog();

        var (exit, output, remover) = await RunAsync(dir, log, RuntimeVerbs.NeverCalled(), everything: true);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("cannot be read", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("end that process", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// The whole order, with something to do at every step, so that a step moved — the receipts after the PATH,
    /// the data directory before the executable — arrives in this diff rather than silently.
    /// </summary>
    [Fact]
    public async Task Every_step_happens_in_its_place()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var log = new StepLog();
        var processes = new FakeProcessControl();

        var (exit, _, _) = await RunAsync(dir, log, StopsWhenAsked(dir, log, processes), processes: processes, everything: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            [
                "autostart.remove",
                "system.shutdown",
                "notifications.remove", // the toast's registration, after the runtime that could write it back stopped
                "notifications.remove", // and the two entries Windows made for it at the first toast
                "notifications.remove",
                "file.remove", // the recorded skill
                "file.remove", // then its record
                "directory.remove_if_empty", // the skill's directory
                "directory.remove_if_empty", // and the root, each only if empty
                "path.remove",
                "executable.remove",
                "directory.remove_if_empty", // the install directory
                "tree.remove",
            ],
            log.Steps);
    }

    /// <summary>
    /// And it says what it had already done. The registration is gone by then — that is the order — so a verb
    /// that stopped without saying so would leave an installation whose autostart had silently stopped
    /// working, with nothing to tell the operator why.
    /// </summary>
    [Fact]
    public async Task The_refusal_names_the_registration_it_had_already_removed()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STUBBORN"));

        var (_, output, _) = await RunAsync(dir, new StepLog(), AcknowledgesAndStays(new StepLog()));

        Assert.Contains("jason runtime autostart enable", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>No runtime at all is not an obstacle: there is nothing to stop, and the verb carries on.</summary>
    [Fact]
    public async Task With_no_runtime_running_the_verb_carries_on()
    {
        using var dir = new TempPaths();
        var log = new StepLog();

        var (exit, _, _) = await RunAsync(dir, log, RuntimeVerbs.NeverCalled());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("system.shutdown", log.Steps);

        // The whole order, so a step added later arrives in this diff rather than silently: the registration,
        // then the receipts (none here), then the PATH.
        Assert.Equal(["autostart.remove", "path.remove", "executable.remove", "directory.remove_if_empty"], log.Steps);
    }

    /// <summary>
    /// Removing a registration that was never made is not an error, and the report says which of the two
    /// happened rather than claiming a removal either way.
    /// </summary>
    [Fact]
    public async Task Removing_a_registration_that_is_not_there_is_not_an_error()
    {
        using var dir = new TempPaths();

        var (exit, output, _) = await RunAsync(dir, new StepLog(), RuntimeVerbs.NeverCalled());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("No logon registration", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Something nobody planned for at the registration — before the runtime is even asked — is the last problem
    /// of a report, not an exception out of the verb. The catch that turns one into the other covered steps 3 to
    /// 6 only, and the registration may already be gone by the time step 1 or step 2 fails.
    /// </summary>
    [Fact]
    public async Task Something_nobody_planned_for_at_the_registration_is_reported_rather_than_thrown()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new BreaksOnRemove(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root + "-install", "jason"),
            Removes: new RecordingRemover());

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = System.Text.Json.JsonSerializer.Deserialize<UninstallReport>(output.ToString(), Contracts.Json.JasonJson.Options)!;
        Assert.Contains(report.Problems, problem => problem.Contains("stopped part-way", StringComparison.Ordinal));
    }

    /// <summary>
    /// A denial <c>disable</c> would have explained is explained for this verb instead: the elevated
    /// <c>disable</c>, then <c>jason uninstall</c> again. Wrapping <c>disable</c>'s own advice put two instructions
    /// side by side — enable again, and remove it with an ordinary <c>disable</c> — and neither was what a person
    /// uninstalling should do.
    /// </summary>
    [Theory]
    [InlineData(Cli.Autostart.AutostartDenialKind.Legacy, "Jason")]
    [InlineData(Cli.Autostart.AutostartDenialKind.Own, "Jason-S-1-5-21-1-2-3-1001")]
    public async Task A_denied_registration_tells_the_uninstall_what_to_run_next(Cli.Autostart.AutostartDenialKind kind, string task)
    {
        using var dir = new TempPaths();
        var denial = new Cli.Autostart.AutostartDenial(kind, task, false, "ERROR: Access is denied.");
        var registrar = new RecordingRegistrar
        {
            Refuses = new Cli.Autostart.AutostartException(
                Cli.Autostart.AutostartCodes.Refused,
                Cli.Autostart.WindowsTasks.Denied(Cli.Autostart.AutostartAct.Disable, denial)) { Denial = denial },
        };

        var report = await RefusedAtTheRegistrationAsync(dir, registrar);

        Assert.Equal(
            "The logon registration could not be removed, so nothing further was: "
            + Cli.Autostart.WindowsTasks.Denied(Cli.Autostart.AutostartAct.Uninstall, denial),
            report.Refusal);
        Assert.EndsWith("never run `jason` under another account. Then run `jason uninstall` again. schtasks said: ERROR: Access is denied.", report.Refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove the logon registration with", report.Refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("autostart enable", report.Refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The report says what step 1 really took away. An account that upgraded from 0.1.0 has only the old task, which
    /// the plan cannot see; the report said "No logon registration was registered for this account" right after
    /// removing it.
    /// </summary>
    [Theory]
    [InlineData(false, false, "No logon registration was registered for this account.")]
    [InlineData(true, false, "Removed the logon registration.")]
    [InlineData(false, true, "Removed the logon task named Jason that an earlier version registered for this account.")]
    [InlineData(true, true, "Removed the logon task named Jason that an earlier version registered for this account.")]
    public async Task The_report_says_what_the_first_step_took_away(bool registration, bool legacy, string said)
    {
        using var dir = new TempPaths();
        var registrar = new RecordingRegistrar { Removes = new Cli.Autostart.AutostartRemoval(registration, legacy) };

        var (exit, output, _) = await RunAsync(dir, new StepLog(), RuntimeVerbs.NeverCalled(), registrar, running: false);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(said, output.ToString(), StringComparison.Ordinal);
        if (legacy && registration)
        {
            Assert.Contains("Removed the logon registration.", output.ToString(), StringComparison.Ordinal);
        }

        if (legacy || registration)
        {
            Assert.DoesNotContain("No logon registration was registered", output.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And the plan says, on Windows, that the old task goes too where there is one: a dry run that left out the one
    /// thing a real run removes would not be a dry run of it. The plan does not ask after it — only the removal does —
    /// so it says so conditionally.
    /// </summary>
    [Theory]
    [InlineData(Cli.Autostart.AutostartPlatform.Windows, true)]
    [InlineData(Cli.Autostart.AutostartPlatform.Linux, false)]
    public async Task A_windows_plan_names_the_task_an_earlier_version_left(Cli.Autostart.AutostartPlatform platform, bool named)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(platform),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root + "-install", "jason"),
            Removes: new RecordingRemover());

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: true, DryRun: true, PurgeData: false, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            named,
            output.ToString().Contains("  a logon task named Jason, if an earlier version registered one for this account", StringComparison.Ordinal));
    }

    /// <summary>
    /// A refusal that came after this account's 0.1.0 task was removed still leaves that removal in the report's done
    /// list, whether the refusal was a denial or not: the report never says less than what happened.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_refusal_after_the_legacy_task_went_still_reports_that_it_went(bool denial)
    {
        using var dir = new TempPaths();
        var denied = new Cli.Autostart.AutostartDenial(Cli.Autostart.AutostartDenialKind.Own, "Jason-S-1-5-21-1-2-3-1001", true, "ERROR: Access is denied.");
        var refusal = denial
            ? new Cli.Autostart.AutostartException(Cli.Autostart.AutostartCodes.Refused, Cli.Autostart.WindowsTasks.Denied(Cli.Autostart.AutostartAct.Disable, denied)) { Denial = denied, LegacyRemoved = true }
            : new Cli.Autostart.AutostartException(Cli.Autostart.AutostartCodes.Refused, "schtasks refused: ERROR: something else.") { LegacyRemoved = true };

        var report = await RefusedAtTheRegistrationAsync(dir, new RecordingRegistrar { Refuses = refusal });

        Assert.Contains("Removed the logon task named Jason that an earlier version registered for this account.", report.Done);
        Assert.NotNull(report.Refusal);
    }

    /// <summary>
    /// Any other refusal keeps the one instruction it always had, and says what to remove: after the tool's own line,
    /// "it" was whatever that line had named last.
    /// </summary>
    [Fact]
    public async Task Any_other_refusal_at_the_registration_says_to_disable_and_run_again()
    {
        using var dir = new TempPaths();
        var registrar = new RecordingRegistrar
        {
            Refuses = new Cli.Autostart.AutostartException(Cli.Autostart.AutostartCodes.Refused, "schtasks refused: ERROR: The Task Scheduler service is not running."),
        };

        var report = await RefusedAtTheRegistrationAsync(dir, registrar);

        Assert.Equal(
            "The logon registration could not be removed, so nothing further was: schtasks refused: ERROR: The Task "
            + "Scheduler service is not running. Remove the logon registration with 'jason runtime autostart disable' and "
            + "run this again.",
            report.Refusal);
    }

    private static async Task<UninstallReport> RefusedAtTheRegistrationAsync(TempPaths dir, RecordingRegistrar registrar)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: registrar,
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root + "-install", "jason"),
            Removes: new RecordingRemover());

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = System.Text.Json.JsonSerializer.Deserialize<UninstallReport>(output.ToString(), Contracts.Json.JasonJson.Options)!;
        Assert.Equal(CliErrors.UninstallRefused, report.RefusalCode);
        return report;
    }

    /// <summary>A registrar that fails in a way nothing planned for.</summary>
    private sealed class BreaksOnRemove : Cli.Autostart.IAutostartRegistrar
    {
        public Cli.Autostart.AutostartPlatform Platform => Cli.Autostart.AutostartPlatform.Windows;

        public Cli.Autostart.AutostartState Read() => new(false, [], null);

        public void Apply(Cli.Autostart.AutostartRegistration registration) => throw new NotSupportedException();

        public Cli.Autostart.AutostartRemoval Remove(Cli.Autostart.AutostartRegistration registration) => throw new InvalidOperationException("the task scheduler said something new.");
    }

    /// <summary>A machine with no way to register anything at logon has nothing to take away, and says so.</summary>
    [Fact]
    public async Task A_machine_that_registers_nothing_at_logon_has_nothing_to_remove()
    {
        using var dir = new TempPaths();
        var registrar = new RecordingRegistrar(Cli.Autostart.AutostartPlatform.Unsupported);

        var (exit, output, _) = await RunAsync(dir, new StepLog(), RuntimeVerbs.NeverCalled(), registrar);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(0, registrar.Removals);
        Assert.Contains("no way of starting anything at logon", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A runtime that answers the shutdown and really goes: the descriptor is deleted with it.</summary>
    private static HttpMessageHandler StopsWhenAsked(TempPaths dir, StepLog log, FakeProcessControl processes) => new FakeHandler(request =>
    {
        log.Add("system.shutdown");

        // As a runtime does: it removes its descriptor and then leaves the process table. `runtime stop` waits for the
        // second; a process still in the table is a runtime that is still going.
        File.Delete(dir.Paths.DescriptorFile);
        processes.RunningPids.Remove(RuntimeVerbs.Pid);
        return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
    });

    /// <summary>And one that says it is going and does not. The descriptor stays, and so does the pid.</summary>
    private static HttpMessageHandler AcknowledgesAndStays(StepLog log) => new FakeHandler(request =>
    {
        log.Add("system.shutdown");
        return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_STUBBORN"));
    });

    /// <summary>
    /// One that does not answer at all, in each of the three ways <c>runtime stop</c> reports as exit 3: nothing
    /// listening, a token it rejects, and a request that never comes back.
    /// </summary>
    private static HttpMessageHandler DoesNotAnswer(string how, StepLog log) => new FakeHandler(request =>
    {
        log.Add("system.shutdown");
        return how switch
        {
            "refused" => throw new HttpRequestException("Connection refused"),
            "unauthorized" => RuntimeVerbs.Response(HttpStatusCode.Unauthorized, "{}"),
            "slow" => throw new TaskCanceledException("The request timed out."),
            _ => throw new ArgumentOutOfRangeException(nameof(how), how, null),
        };
    });

    /// <param name="everything">
    /// Something planned at every step after the runtime: a recorded skill in the harness root, a PATH entry
    /// that is ours, and the data directory on the explicit word.
    /// </param>
    /// <param name="running">Whether the pid the descriptor names is in the process table.</param>
    /// <param name="errors">The verb's standard error, for a test that reads it on its own.</param>
    private static async Task<(int Exit, StringWriter Output, RecordingRemover Remover)> RunAsync(
        TempPaths dir,
        StepLog log,
        HttpMessageHandler handler,
        RecordingRegistrar? registrar = null,
        FakeProcessControl? processes = null,
        bool everything = false,
        bool running = true,
        StringWriter? errors = null)
    {
        var output = new StringWriter();
        var error = errors ?? new StringWriter();
        var install = Path.Combine(dir.Paths.Root + "-install", "jason");
        var remover = new RecordingRemover(log)
        {
            PathEntry = everything ? new PathEntryPlan(Path.GetDirectoryName(install)!, [], "the value", Ours: true) : null,
            NotificationKeys = everything ? [.. Jason.Contracts.Notifications.ToastRegistration.Keys] : [],
        };

        processes ??= new FakeProcessControl();
        if (running)
        {
            processes.RunningPids.Add(RuntimeVerbs.Pid);
        }

        var harness = Path.Combine(dir.Paths.Root, "harness");
        if (everything)
        {
            UninstallReceiptTests.Recorded(harness, "operating-the-installation");
        }

        var env = new CliEnvironment(
            output,
            error,
            dir.Paths,
            handler,
            Processes: processes,
            // Beside the data directory rather than in it: a data directory holding the installation is one the
            // purge refuses, which is not what these tests are about.
            InstallPath: install,
            Autostart: (Cli.Autostart.IAutostartRegistrar?)registrar ?? new LoggingRegistrar(log),
            Harnesses: HarnessLocators.At(harness),
            Removes: remover);

        var exit = await UninstallCommand.RunAsync(
            env,
            // The machine shape: one document carrying the steps, what was kept and the code a caller
            // branches on. The person's shape says the same things in prose, and a test asserting on prose
            // is a test about wording.
            new UninstallOptions(Human: false, DryRun: false, PurgeData: everything, Yes: true, Force: false, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll),
            Ct);

        // Both writers, because a refusal is a diagnostic and the steps are not.
        return (exit, new StringWriter(output.GetStringBuilder().Append(error.GetStringBuilder())), remover);
    }

    /// <summary>A registrar that records into the shared log, so the order asserted is the machine's order.</summary>
    private sealed class LoggingRegistrar(StepLog log) : Cli.Autostart.IAutostartRegistrar
    {
        public Cli.Autostart.AutostartPlatform Platform => Cli.Autostart.AutostartPlatform.Windows;

        public int Removals { get; private set; }

        public Cli.Autostart.AutostartState Read() => new(false, [], null);

        public void Apply(Cli.Autostart.AutostartRegistration registration) => throw new NotSupportedException();

        public Cli.Autostart.AutostartRemoval Remove(Cli.Autostart.AutostartRegistration registration)
        {
            Removals++;
            log.Add("autostart.remove");
            return new Cli.Autostart.AutostartRemoval(Registration: false, Legacy: false);
        }
    }
}
