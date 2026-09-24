using Jason.Cli.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Process;

public class RuntimeProcessControlTests
{
    [Fact]
    public void Resolving_self_always_asks_for_the_detached_runtime_mode()
    {
        var (fileName, arguments) = RuntimeProcessControl.ResolveSelf();

        Assert.False(string.IsNullOrWhiteSpace(fileName));
        Assert.Equal(["runtime", "run", "--detached"], arguments.TakeLast(3));
    }

    [Fact]
    public void Running_from_the_dotnet_host_launches_the_entry_assembly_through_dotnet()
    {
        var (fileName, arguments) = RuntimeProcessControl.ResolveSelf();

        // Under the test host the executable is the test runner, not dotnet; either way the entry point the
        // arguments name must be the thing that was started, so the child is this same build.
        if (string.Equals(Path.GetFileNameWithoutExtension(fileName), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(4, arguments.Count);
            Assert.Equal(System.Environment.GetCommandLineArgs()[0], arguments[0]);
        }
        else
        {
            Assert.Equal(3, arguments.Count);
            Assert.Equal(System.Environment.ProcessPath, fileName);
        }
    }

    [Fact]
    public void This_process_counts_as_running() =>
        Assert.True(RuntimeProcessControl.Instance.IsRunning(System.Environment.ProcessId));

    [Fact]
    public void A_pid_nobody_owns_does_not_count_as_running() =>
        Assert.False(RuntimeProcessControl.Instance.IsRunning(int.MaxValue));

    /// <summary>
    /// A process's start is read as the instant it was, in universal time: this process's own is before now, and
    /// is what the operating system says it is.
    /// </summary>
    [Fact]
    public void This_process_started_before_now_and_says_when()
    {
        var started = RuntimeProcessControl.Instance.StartTime(System.Environment.ProcessId);
        using var self = System.Diagnostics.Process.GetCurrentProcess();

        Assert.NotNull(started);
        Assert.Equal(TimeSpan.Zero, started.Value.Offset);
        Assert.InRange(started.Value, DateTimeOffset.UtcNow - TimeSpan.FromDays(1), DateTimeOffset.UtcNow);
        Assert.InRange(started.Value - new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero), TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_pid_nobody_owns_has_no_start() =>
        Assert.Null(RuntimeProcessControl.Instance.StartTime(int.MaxValue));

    [Fact]
    public void Launching_without_a_data_directory_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => RuntimeProcessControl.Instance.Launch(null!, SelfExecutable.Command));

    /// <summary>And a launch has to name a program: an empty command is a caller's mistake, not a start.</summary>
    [Fact]
    public void Launching_nothing_at_all_is_a_programming_error() =>
        Assert.Throws<ArgumentException>(() => RuntimeProcessControl.Resolve([]));

    /// <summary>
    /// The mode words are the seam's, and the program is the caller's: whatever it is handed comes back with
    /// `runtime run --detached` after it.
    /// </summary>
    [Fact]
    public void A_launch_runs_the_command_it_was_given_in_the_detached_runtime_mode()
    {
        var (fileName, arguments) = RuntimeProcessControl.Resolve(["/opt/jason/jason"]);

        Assert.Equal("/opt/jason/jason", fileName);
        Assert.Equal(["runtime", "run", "--detached"], arguments);

        // And a command that needs its own words first keeps them, which is how a muxer build is started.
        var (muxer, withDll) = RuntimeProcessControl.Resolve(["dotnet", "/opt/jason/jason.dll"]);
        Assert.Equal("dotnet", muxer);
        Assert.Equal(["/opt/jason/jason.dll", "runtime", "run", "--detached"], withDll);
    }
}
