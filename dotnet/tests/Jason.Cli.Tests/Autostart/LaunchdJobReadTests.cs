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

    /// <summary>A job launchd is about to start again, with no process of its own yet: in progress, as a running one is.</summary>
    private const string SpawnScheduled = "gui/501/ai.jasonharness.desktop.ShipIt = {\n\ttype = Submitted\n\tstate = spawn scheduled\n}\n";

    /// <summary>The login domain as launchctl prints one that is there: the domain's own name, then its properties.</summary>
    private const string DomainThere = "gui/501 = {\n\ttype = login\n\thandle = 501\n}\n";

    /// <summary>
    /// Only "not running", with no process of its own, is a job loaded and idle; any other state of its own is one in
    /// progress. Only launchctl's 113, "Could not find service", says by itself that the job is not there. Any other exit
    /// says launchctl could not tell, and the domain itself is then asked about: here it is there, so the job is not read,
    /// which leaves it as it is and says so.
    /// </summary>
    [Theory]
    [InlineData("not running, a nested state aside", 0, NotRunning, LaunchdJobState.Loaded, null)]
    [InlineData("running, its process named", 0, Running, LaunchdJobState.Running, 6262)]
    [InlineData("running, no process named", 0, RunningNoProcess, LaunchdJobState.Running, null)]
    [InlineData("a process named, indented by spaces", 0, ProcessNamed, LaunchdJobState.Running, 77)]
    [InlineData("spawn scheduled, no process named", 0, SpawnScheduled, LaunchdJobState.Running, null)]
    [InlineData("no state of its own", 0, NoState, LaunchdJobState.Unread, null)]
    [InlineData("not there", 113, "", LaunchdJobState.Absent, null)]
    [InlineData("an exit other than 113", 3, "", LaunchdJobState.Unread, null)]
    public void A_job_is_read_by_its_own_state_line_and_its_own_process(string row, int exit, string printed, LaunchdJobState state, int? pid)
    {
        var ran = new List<(string Command, TimeSpan? Limit)>();

        var job = LaunchdJobs.Read("gui/501", Label, (command, limit) =>
        {
            ran.Add((string.Join(" ", command), limit));
            return command[^1] == "gui/501"
                ? (0, DomainThere, string.Empty)
                : (exit, printed, exit == 0 ? string.Empty : $"Could not find service \"{Label}\" in domain for user gui: 501");
        });

        Assert.True(job.State == state && job.Pid == pid, $"{row}: {job}");
        var asked = exit is 0 or LaunchdJobs.NotFound
            ? new[] { $"/bin/launchctl print gui/501/{Label}" }
            : [$"/bin/launchctl print gui/501/{Label}", "/bin/launchctl print gui/501"];
        Assert.Equal(asked.Select(command => (command, (TimeSpan?)LaunchdJobs.Limit)), ran);
    }

    /// <summary>
    /// A launchctl that could not be started, or did not answer in time, about the job leaves it unread; nothing more is
    /// asked of a launchctl that gave no answer.
    /// </summary>
    [Fact]
    public void A_launchctl_that_cannot_be_run_or_does_not_answer_leaves_the_job_unread()
    {
        var ran = new List<string>();
        var job = LaunchdJobs.Read("gui/501", Label, (command, _) =>
        {
            ran.Add(string.Join(" ", command));
            throw new AutostartException(AutostartCodes.Refused, "did not answer within 10 seconds");
        });

        Assert.Equal(LaunchdJobState.Unread, job.State);
        Assert.Equal("did not answer within 10 seconds", job.Why);
        Assert.Equal([$"/bin/launchctl print gui/501/{Label}"], ran);
    }

    /// <summary>An exit other than 113, in a domain launchd says is there, is said by its code and the first line launchctl wrote about it.</summary>
    [Theory]
    [InlineData("Input/output error\nsecond line\n", "`launchctl print` ended with 5: Input/output error")]
    [InlineData("", "`launchctl print` ended with 5")]
    public void An_exit_other_than_113_says_what_launchctl_said(string error, string why)
    {
        var job = LaunchdJobs.Read("gui/501", Label, (command, _) => command[^1] == "gui/501" ? (0, DomainThere, string.Empty) : (5, string.Empty, error));

        Assert.Equal(LaunchdJobState.Unread, job.State);
        Assert.Equal(why, job.Why);
    }

    /// <summary>
    /// An account with no login session - reached over ssh, or run by a management tool, while it is not signed in at the
    /// screen - has no login domain, and launchctl, asked about a job there, answers something other than 113. So where
    /// launchctl cannot answer for the job, launchd is asked about the domain itself, by launchctl's full path and within
    /// the same limit: a domain launchd says is not there, any exit but 0, holds no job, which is absent. One it says is
    /// there leaves the job unread, and so does a launchctl that could not be started or did not answer about the domain,
    /// each said by what launchctl answered about the job. No exit code stands here for one of launchd's own: the
    /// stand-in's codes are its own, and the release's dry run records what launchd answers.
    /// </summary>
    [Theory]
    [InlineData("the domain not there", 7, 1, false, LaunchdJobState.Absent)]
    [InlineData("the domain not there, another code", 64, 9, false, LaunchdJobState.Absent)]
    [InlineData("the domain there", 7, 0, false, LaunchdJobState.Unread)]
    [InlineData("no answer about the domain", 7, 0, true, LaunchdJobState.Unread)]
    public void Where_launchctl_cannot_answer_for_the_job_launchd_is_asked_about_the_domain_itself(string row, int jobExit, int domainExit, bool domainUnanswered, LaunchdJobState state)
    {
        var ran = new List<(string Command, TimeSpan? Limit)>();

        var job = LaunchdJobs.Read("gui/501", Label, (command, limit) =>
        {
            ran.Add((string.Join(" ", command), limit));
            if (command[^1] != "gui/501")
            {
                return (jobExit, string.Empty, "the stand-in's own words about the job\n");
            }

            return domainUnanswered
                ? throw new AutostartException(AutostartCodes.Refused, "did not answer within 10 seconds")
                : (domainExit, domainExit == 0 ? DomainThere : string.Empty, domainExit == 0 ? string.Empty : "the stand-in's own words about the domain\n");
        });

        Assert.True(job.State == state, $"{row}: {job}");
        Assert.Equal(state == LaunchdJobState.Unread ? $"`launchctl print` ended with {jobExit}: the stand-in's own words about the job" : null, job.Why);
        Assert.Equal(
            [($"/bin/launchctl print gui/501/{Label}", (TimeSpan?)LaunchdJobs.Limit), ("/bin/launchctl print gui/501", (TimeSpan?)LaunchdJobs.Limit)],
            ran);
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
