using Jason.Cli.Process;

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

    [Fact]
    public void Launching_without_a_data_directory_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => RuntimeProcessControl.Instance.Launch(null!));
}
