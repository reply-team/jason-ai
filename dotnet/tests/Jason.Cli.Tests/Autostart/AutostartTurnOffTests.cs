using System.Net;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// The person's turn-off and the start now: <c>disable</c> records that the start at logon was turned off and warns about
/// a restart, <c>enable</c> takes that back and starts the runtime where none answers, <c>status</c> says which it is, and a
/// person's <c>runtime stop</c> says how long it holds where the start at logon stands. Against a recording registrar and
/// a fake process table: nothing here registers or starts anything.
/// </summary>
public class AutostartTurnOffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Installed = @"C:\Users\ada\AppData\Local\Programs\jason\jason.exe";

    private static (CliEnvironment Env, StringWriter Out, StringWriter Error) Machine(
        TempPaths dir,
        RecordingRegistrar registrar,
        FakeProcessControl? processes = null,
        HttpMessageHandler? handler = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliEnvironment(output, error, dir.Paths, handler, null, processes, InstallPath: Installed, Autostart: registrar, SearchPath: "/usr/local/bin:/usr/bin:/bin"), output, error);
    }

    private static AutostartStatusResponse Answer(StringWriter output) =>
        JsonSerializer.Deserialize<AutostartStatusResponse>(
            Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)),
            JasonJson.Options)!;

    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.MacOs)]
    [InlineData(AutostartPlatform.Linux)]
    [InlineData(AutostartPlatform.Unsupported)]
    public async Task Disable_records_the_turn_off_and_warns_about_a_restart(AutostartPlatform platform)
    {
        using var dir = new TempPaths();
        var registrar = new RecordingRegistrar(platform);
        var (env, output, error) = Machine(dir, registrar);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "disable"], env, Ct));

        Assert.True(File.Exists(dir.Paths.AutostartOffFile), "disable left no record of the turn-off");
        using (var record = JsonDocument.Parse(File.ReadAllText(dir.Paths.AutostartOffFile)))
        {
            Assert.Equal(1, record.RootElement.GetProperty("schema").GetInt32());
            Assert.True(DateTimeOffset.TryParse(record.RootElement.GetProperty("since").GetString(), out _));
            Assert.False(string.IsNullOrWhiteSpace(record.RootElement.GetProperty("version").GetString()));
        }

        var answer = Answer(output);
        Assert.False(answer.Registered);
        Assert.True(answer.TurnedOff);
        Assert.Contains("After a restart of this computer nothing of Jason runs until `jason runtime start`, or until you open Jason Desktop.", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Each verb's help says what it does to the start at logon, in its own words.</summary>
    [Theory]
    [InlineData("runtime autostart disable", "After a restart of this computer nothing of Jason runs until `jason runtime start`, or until you open Jason Desktop.")]
    [InlineData("runtime autostart disable", "record that you turned it off (autostart/off.json in the data directory)")]
    [InlineData("runtime stop", "Where the runtime is registered to start at logon, the stop holds until the next logon, which starts it again")]
    [InlineData("runtime autostart enable", "starts the runtime now where none answers")]
    public async Task The_help_says_what_the_verb_does_to_the_start_at_logon(string verb, string said)
    {
        using var dir = new TempPaths();
        var (env, output, _) = Machine(dir, new RecordingRegistrar());

        await CliApp.RunAsync([.. verb.Split(' '), "--help"], env, Ct);

        Assert.Contains(said, System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enable_on_a_machine_that_registers_nothing_refuses_and_starts_nothing()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishedAtLaunch(dir, RuntimeVerbs.Descriptor("rt_NOW")) };
        var registrar = new RecordingRegistrar(AutostartPlatform.Unsupported);
        var (env, output, _) = Machine(dir, registrar, processes, RuntimeVerbs.EchoesTheDescriptor(dir));

        Assert.Equal(ExitCodes.ApiError, await CliApp.RunAsync(["runtime", "autostart", "enable"], env, Ct));

        Assert.Empty(registrar.Applied);
        Assert.Empty(processes.Launches);
        Assert.Contains(AutostartCodes.Unsupported, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enable_takes_the_turn_off_back()
    {
        using var dir = new TempPaths();
        var registrar = new RecordingRegistrar();
        var (env, output, _) = Machine(dir, registrar);
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "disable"], env, Ct));
        output.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "enable"], env, Ct));

        Assert.False(File.Exists(dir.Paths.AutostartOffFile), "enable left the record of the turn-off standing");
        var answer = Answer(output);
        Assert.True(answer.Registered);
        Assert.False(answer.TurnedOff);
    }

    [Fact]
    public async Task On_macOS_launchds_own_disabled_record_counts_as_the_turn_off()
    {
        using var dir = new TempPaths();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { OffRecorded = true };
        var (env, output, _) = Machine(dir, registrar);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "status"], env, Ct));

        Assert.False(File.Exists(dir.Paths.AutostartOffFile));
        Assert.True(Answer(output).TurnedOff);
        Assert.True(AutostartOff.Stands(dir.Paths, registrar));
    }

    [Fact]
    public async Task Status_says_a_turn_off_in_words()
    {
        using var dir = new TempPaths();
        var (env, output, _) = Machine(dir, new RecordingRegistrar());
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "disable"], env, Ct));
        output.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "status", "--human"], env, Ct));

        Assert.Contains("you turned it off with `jason runtime autostart disable`", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.Linux)]
    public async Task Enable_starts_the_runtime_now_where_none_answers(AutostartPlatform platform)
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishedAtLaunch(dir, RuntimeVerbs.Descriptor("rt_NOW")) };
        var (env, output, error) = Machine(dir, new RecordingRegistrar(platform), processes, RuntimeVerbs.EchoesTheDescriptor(dir));

        Assert.Equal(ExitCodes.Success, await RuntimeAutostartCommands.EnableAsync(env, human: false, Ct, TimeSpan.FromMilliseconds(50)));

        Assert.True(Answer(output).Registered, "stdout carried something other than the one answer");
        Assert.Single(processes.Launches);
        Assert.Contains("The runtime runs now", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS the load starts the runtime and <c>enable</c> never launches one: where none answers within the wait, a
    /// slow first start still migrating say, it launches nothing, says the runtime may still be starting, and exits 0.
    /// </summary>
    [Fact]
    public async Task On_macOS_a_wait_that_runs_out_launches_nothing_and_says_it_may_still_be_starting()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishedAtLaunch(dir, RuntimeVerbs.Descriptor("rt_NOW")) };
        var (env, output, error) = Machine(dir, new RecordingRegistrar(AutostartPlatform.MacOs), processes, RuntimeVerbs.EchoesTheDescriptor(dir));

        Assert.Equal(ExitCodes.Success, await RuntimeAutostartCommands.EnableAsync(env, human: false, Ct, TimeSpan.FromMilliseconds(300)));

        Assert.True(Answer(output).Registered);
        Assert.Empty(processes.Launches);
        Assert.Contains("it may still be starting", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("The runtime runs now", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS a descriptor left before the load, stale or a re-enable's whose runtime the unload stopped, is not the
    /// load's runtime: <c>enable</c> waits for the one that answers as another instance, and launches nothing beside it.
    /// </summary>
    [Fact]
    public async Task On_macOS_a_descriptor_left_before_the_load_is_waited_past_and_none_is_launched()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STALE"));
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishedAtLaunch(dir, RuntimeVerbs.Descriptor("rt_NOW")) };
        var registrar = new LoadStarts(dir);
        var echoes = RuntimeVerbs.EchoesTheDescriptor(dir);
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment(output, error, dir.Paths, echoes, null, processes, InstallPath: Installed, Autostart: registrar);

        Assert.Equal(ExitCodes.Success, await RuntimeAutostartCommands.EnableAsync(env, human: false, Ct, TimeSpan.FromSeconds(30)));

        Assert.Empty(processes.Launches);
        Assert.Equal("rt_LOAD", new Jason.Cli.Discovery.DescriptorReader(dir.Paths).Read()!.InstanceId);
        Assert.Contains("The runtime runs now", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS the LaunchAgent carries <c>RunAtLoad</c>, so the load starts the runtime: <c>enable</c> waits for that one to
    /// publish its endpoint and launches no second one beside it.
    /// </summary>
    [Fact]
    public async Task On_macOS_enable_takes_the_runtime_the_load_started_and_launches_none()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl();
        var registrar = new LoadStarts(dir);
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment(output, error, dir.Paths, RuntimeVerbs.EchoesTheDescriptor(dir), null, processes, InstallPath: Installed, Autostart: registrar);

        Assert.Equal(ExitCodes.Success, await RuntimeAutostartCommands.EnableAsync(env, human: false, Ct, TimeSpan.FromSeconds(30)));

        Assert.Empty(processes.Launches);
        Assert.Contains("The runtime runs now", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enable_where_a_runtime_answers_launches_none()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl();
        var (env, _, error) = Machine(dir, new RecordingRegistrar(), processes, RuntimeVerbs.EchoesTheDescriptor(dir));

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "enable"], env, Ct));

        Assert.Empty(processes.Launches);
        Assert.Contains("The runtime runs now", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_that_fails_leaves_the_registration_and_says_why()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(1) };
        var (env, output, error) = Machine(dir, new RecordingRegistrar(), processes, RuntimeVerbs.EchoesTheDescriptor(dir));

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "enable"], env, Ct));

        Assert.True(Answer(output).Registered);
        Assert.Contains("but none runs now", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_environment_with_no_process_table_starts_nothing_and_says_so()
    {
        using var dir = new TempPaths();
        var (env, _, error) = Machine(dir, new RecordingRegistrar());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "autostart", "enable"], env, Ct));

        Assert.Contains("none was started now", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_persons_stop_says_it_holds_until_the_next_logon_where_the_start_at_logon_stands(bool registered)
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var registrar = new RecordingRegistrar();
        if (registered)
        {
            registrar.State = new AutostartState(true, [Installed, "runtime", "run"], null);
        }

        var (env, _, error) = Machine(dir, registrar, new FakeProcessControl(), handler);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "stop"], env, Ct));

        Assert.Equal(registered, error.ToString().Contains(RuntimeStopCommand.UntilTheNextLogon, StringComparison.Ordinal));
    }

    /// <summary>A macOS machine whose load starts the runtime, as <c>RunAtLoad</c> does: the descriptor appears a moment after <c>Apply</c>.</summary>
    private sealed class LoadStarts(TempPaths dir) : IAutostartRegistrar
    {
        private AutostartState _state = new(false, [], null);

        public AutostartPlatform Platform => AutostartPlatform.MacOs;

        public AutostartState Read() => _state;

        public void Apply(AutostartRegistration registration)
        {
            _state = new AutostartState(true, registration.Run, registration.ArtifactPath, registration.SearchPath);
            var thread = new Thread(() =>
            {
                Thread.Sleep(100);
                dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LOAD"));
            }) { IsBackground = true };
            thread.Start();
        }

        public AutostartRemoval Remove(AutostartRegistration registration) => new(_state.Registered, Legacy: false);
    }
}
