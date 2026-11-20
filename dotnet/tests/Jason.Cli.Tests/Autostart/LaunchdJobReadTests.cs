using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// A job read from launchd as <c>launchctl print</c> prints it, and taken out of a domain by the routine that takes a
/// LaunchAgent's job out, through a stub that stands for launchctl: one command each, by launchctl's full path, each
/// waited for <see cref="LaunchdJobs.Limit"/> at most.
/// </summary>
public sealed class LaunchdJobReadTests
{
    private const string Label = "ai.jasonharness.desktop.ShipIt";

    /// <summary>As launchctl prints a job Squirrel submitted: its own properties one tab in, a nested dictionary's two.</summary>
    private const string NotRunning =
        "gui/501/ai.jasonharness.desktop.ShipIt = {\n\tactive count = 0\n\tpath = (submitted by Jason.4101)\n\ttype = Submitted\n\tstate = not running\n\n"
        + "\tprogram = /Applications/Jason.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt\n\tevent triggers = {\n\t\tstate = running\n\t\tpid = 9\n\t}\n}\n";

    private const string Running =
        "gui/501/ai.jasonharness.desktop.ShipIt = {\n\tactive count = 1\n\ttype = Submitted\n\tstate = running\n\n"
        + "\tprogram = /Applications/Jason.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt\n\tpid = 6262\n}\n";

    private const string RunningNoProcess = "gui/501/ai.jasonharness.desktop.ShipIt = {\n\ttype = Submitted\n\tstate = running\n}\n";

    private const string ProcessNamed = "gui/501/ai.jasonharness.desktop.ShipIt = {\n    type = Submitted\n    state = spawn scheduled\n    pid = 77\n}\n";

    private const string NoState = "gui/501/ai.jasonharness.desktop.ShipIt = {\n\ttype = Submitted\n\tevent triggers = {\n\t\tstate = not running\n\t}\n}\n";

    [Theory]
    [InlineData("not running, a nested state aside", 0, NotRunning, LaunchdJobState.Loaded, null)]
    [InlineData("running, its process named", 0, Running, LaunchdJobState.Running, 6262)]
    [InlineData("running, no process named", 0, RunningNoProcess, LaunchdJobState.Running, null)]
    [InlineData("a process named, indented by spaces", 0, ProcessNamed, LaunchdJobState.Running, 77)]
    [InlineData("no state of its own", 0, NoState, LaunchdJobState.Unread, null)]
    [InlineData("not there", 113, "", LaunchdJobState.Absent, null)]
    [InlineData("not there, said another way", 3, "", LaunchdJobState.Absent, null)]
    public void A_job_is_read_by_its_own_state_line_and_its_own_process(string row, int exit, string printed, LaunchdJobState state, int? pid)
    {
        var ran = new List<(string Command, TimeSpan? Limit)>();

        var job = LaunchdJobs.Read("gui/501", Label, (command, limit) =>
        {
            ran.Add((string.Join(" ", command), limit));
            return (exit, printed, exit == 0 ? string.Empty : $"Could not find service \"{Label}\" in domain for user gui: 501");
        });

        Assert.True(job.State == state && job.Pid == pid, $"{row}: {job}");
        Assert.Equal([($"/bin/launchctl print gui/501/{Label}", (TimeSpan?)LaunchdJobs.Limit)], ran);
    }

    [Fact]
    public void A_launchctl_that_cannot_be_run_or_does_not_answer_leaves_the_job_unread()
    {
        var job = LaunchdJobs.Read("gui/501", Label, (_, _) => throw new AutostartException(AutostartCodes.Refused, "did not answer within 10 seconds"));

        Assert.Equal(LaunchdJobState.Unread, job.State);
        Assert.Equal("did not answer within 10 seconds", job.Why);
    }

    /// <summary>
    /// Booted out of its domain, then removed by its label, as <see cref="LaunchdJobs.TakeOut"/> takes a LaunchAgent's job
    /// out: one routine, and nothing either is answered, nor a launchctl that does not answer, is an error.
    /// </summary>
    [Fact]
    public void A_job_is_booted_out_of_its_domain_then_removed_by_its_label_and_nothing_answered_is_an_error()
    {
        var ran = new List<(string Command, TimeSpan? Limit)>();

        LaunchdJobs.BootOut("gui/501", Label, (command, limit) =>
        {
            ran.Add((string.Join(" ", command), limit));
            return ran.Count == 1 ? (0, string.Empty, string.Empty) : throw new AutostartException(AutostartCodes.Refused, "did not answer within 10 seconds");
        });

        Assert.Equal([$"/bin/launchctl bootout gui/501/{Label}", $"/bin/launchctl remove {Label}"], ran.Select(run => run.Command));
        Assert.All(ran, run => Assert.Equal(LaunchdJobs.Limit, run.Limit));
        Assert.Equal("gui/501", LaunchdJobs.LoginDomain(501));
    }
}
