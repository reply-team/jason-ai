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
    [Fact]
    public async Task A_runtime_that_will_not_stop_refuses_the_whole_verb_and_removes_nothing_further()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STUBBORN"));
        var log = new StepLog();

        var (exit, output, remover) = await RunAsync(dir, log, AcknowledgesAndStays(log));

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("file.remove", StringComparison.Ordinal));
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("tree.remove", StringComparison.Ordinal));
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
        Assert.Equal(["autostart.remove", "path.remove"], log.Steps);
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

        // Both signs of life, because that is what `runtime stop` waits for: a descriptor deleted by a
        // process that is still in the table is a runtime that is still going.
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

    private static async Task<(int Exit, StringWriter Output, RecordingRemover Remover)> RunAsync(
        TempPaths dir,
        StepLog log,
        HttpMessageHandler handler,
        RecordingRegistrar? registrar = null,
        FakeProcessControl? processes = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var remover = new RecordingRemover(log);
        processes ??= new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);

        var env = new CliEnvironment(
            output,
            error,
            dir.Paths,
            handler,
            Processes: processes,
            InstallPath: Path.Combine(dir.Paths.Root, "bin", "jason"),
            Autostart: (Cli.Autostart.IAutostartRegistrar?)registrar ?? new LoggingRegistrar(log),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            Removes: remover);

        var exit = await UninstallCommand.RunAsync(
            env,
            // The machine shape: one document carrying the steps, what was kept and the code a caller
            // branches on. The person's shape says the same things in prose, and a test asserting on prose
            // is a test about wording.
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll),
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

        public void Remove(Cli.Autostart.AutostartRegistration registration)
        {
            Removals++;
            log.Add("autostart.remove");
        }
    }
}
