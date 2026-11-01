using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// Which of the children a fake-host runtime ran it may still end by the number the runtime recorded. A number is
/// only a number: once a child has exited it can be given to any other program, so an attempt whose launch has an
/// exit code names nobody, and one without it names a child only when the child's own start line agrees.
/// </summary>
public class FakeHostChildTests
{
    private const string Began = "2026-09-14T12:00:00.1234567Z";

    private static string StartLine(int pid) =>
        $"fake-agent-host: behaviour=hang pid={pid} started={Began} attempt=att_one number=1 data-dir=set cwd=/work token-in-env=false";

    private static AttemptLaunchDto Launch(int? pid, int? exitCode) => new(["dotnet", "host.dll"], "/work", pid, exitCode);

    [Fact]
    public void A_child_the_run_recorded_an_exit_code_for_is_nobody_to_end()
    {
        Assert.Null(FakeHostRuntime.StillRunning(Launch(4242, exitCode: 0), StartLine(4242)));
        Assert.Null(FakeHostRuntime.StillRunning(Launch(4242, exitCode: 137), StartLine(4242)));
    }

    [Fact]
    public void A_child_without_an_exit_code_is_known_by_its_pid_and_the_start_it_said()
    {
        var child = FakeHostRuntime.StillRunning(Launch(4242, exitCode: null), StartLine(4242));

        Assert.Equal((4242, DateTimeOffset.Parse(Began, System.Globalization.CultureInfo.InvariantCulture)), child);
    }

    [Fact]
    public void A_start_line_of_another_process_is_not_the_child_recorded()
    {
        Assert.Null(FakeHostRuntime.StillRunning(Launch(4242, exitCode: null), StartLine(4243)));
        Assert.Null(FakeHostRuntime.StillRunning(Launch(null, exitCode: null), StartLine(4242)));
        Assert.Null(FakeHostRuntime.StillRunning(null, StartLine(4242)));
    }

    [Fact]
    public void A_start_line_that_did_not_say_when_it_began_names_no_start()
    {
        Assert.Null(FakeHostRuntime.StillRunning(Launch(4242, exitCode: null), "fake-agent-host: behaviour=hang pid=4242 attempt=att_one"));
        Assert.Null(FakeHostRuntime.StartIn("fake-agent-host: behaviour=hang pid=4242 started=yesterday attempt=att_one"));
    }
}
