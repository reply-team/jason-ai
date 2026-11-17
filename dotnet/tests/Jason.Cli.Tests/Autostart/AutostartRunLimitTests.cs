using System.Diagnostics;
using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// A system tool waited for with a bound, as the uninstall waits for launchctl: one that does not answer in time is ended
/// and refused, so the caller goes on without its answer instead of waiting for ever. And on macOS a LaunchAgent's job taken
/// out of launchd before its file goes, wherever a file goes, through a stub that stands for launchctl.
/// </summary>
public sealed class AutostartRunLimitTests : IDisposable
{
    private readonly TempPaths _tree = new();

    public void Dispose() => _tree.Dispose();

    /// <summary>
    /// The runtime's logon registration on macOS: the job is unloaded through its file while the file is still there, then
    /// removed by its label, each with the bound, and only then does the file go.
    /// </summary>
    [Fact]
    public void A_registered_agent_is_unloaded_and_removed_by_label_before_its_file_goes()
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var plist = AutostartArtifacts.ArtifactPath(AutostartPlatform.MacOs, home, string.Empty);
        Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
        File.WriteAllText(plist, "<plist/>");
        var ran = new List<(string Command, bool FileThere, TimeSpan? Limit)>();
        var registrar = new LaunchAgentRegistrar((command, limit) =>
        {
            ran.Add((string.Join(" ", command), File.Exists(plist), limit));
            return (0, string.Empty, string.Empty);
        }, home);
        var registration = AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone");

        var removed = registrar.Remove(registration with { ArtifactPath = plist });

        Assert.True(removed.Registration);
        Assert.Equal([$"launchctl unload -w {plist}", "/bin/launchctl remove ai.jason.runtime"], ran.Select(run => run.Command));
        Assert.All(ran, run => Assert.True(run.FileThere, $"{run.Command} ran after the file went"));
        Assert.All(ran, run => Assert.Equal(LaunchdJobs.Limit, run.Limit));
        Assert.False(File.Exists(plist), "the agent's file is still there");
    }

    /// <summary>
    /// The uninstall's removal on macOS: unloaded through its file without <c>-w</c>, removed by its label, and the label's
    /// disabled state cleared in the account's login domain, each with the bound, before the file goes; and where the file is
    /// gone already, as an earlier version's <c>disable</c> left it, with the label disabled, the label is still cleared.
    /// </summary>
    [Fact]
    public void The_uninstalls_removal_unloads_without_w_and_clears_the_labels_disabled_state_file_or_none()
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var plist = AutostartArtifacts.ArtifactPath(AutostartPlatform.MacOs, home, string.Empty);
        Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
        File.WriteAllText(plist, "<plist/>");
        var ran = new List<(string Command, bool FileThere, TimeSpan? Limit)>();
        var registrar = new LaunchAgentRegistrar((command, limit) =>
        {
            ran.Add((string.Join(" ", command), File.Exists(plist), limit));
            return (0, string.Empty, string.Empty);
        }, home);
        var registration = AutostartArtifacts.LeavingNoMark(
            AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone") with { ArtifactPath = plist },
            501);

        Assert.True(registrar.Remove(registration).Registration);
        Assert.Equal(
            [$"launchctl unload {plist}", "/bin/launchctl remove ai.jason.runtime", "/bin/launchctl enable gui/501/ai.jason.runtime"],
            ran.Select(run => run.Command));
        Assert.All(ran, run => Assert.True(run.FileThere, $"{run.Command} ran after the file went"));
        Assert.All(ran, run => Assert.Equal(LaunchdJobs.Limit, run.Limit));
        Assert.False(File.Exists(plist), "the agent's file is still there");

        ran.Clear();
        Assert.False(registrar.Remove(registration).Registration);
        Assert.Equal(["/bin/launchctl remove ai.jason.runtime", "/bin/launchctl enable gui/501/ai.jason.runtime"], ran.Select(run => run.Command));
    }

    /// <summary>
    /// A job left loaded with its file gone is taken out by its label all the same, and a job that is not loaded, which
    /// launchctl answers with a failure, or a launchctl that cannot be run, is no error.
    /// </summary>
    [Fact]
    public void A_job_whose_file_is_gone_is_removed_by_its_label_and_none_loaded_is_no_error()
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var ran = new List<string>();
        var registrar = new LaunchAgentRegistrar((command, _) =>
        {
            ran.Add(string.Join(" ", command));
            return ran.Count == 1 ? (113, string.Empty, "Could not find specified service") : throw new AutostartException(AutostartCodes.Refused, "launchctl could not be run");
        }, home);
        var registration = AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone");

        var removed = registrar.Remove(registration);

        Assert.False(removed.Registration);
        Assert.Equal(["/bin/launchctl remove ai.jason.runtime"], ran);

        LaunchdJobs.TakeOut([["/bin/launchctl", "unload", "x.plist"]], "ai.jasonharness.desktop", (command, _) =>
        {
            ran.Add(string.Join(" ", command));
            throw new AutostartException(AutostartCodes.Refused, "did not answer within 10 seconds");
        });
        Assert.Equal(["/bin/launchctl remove ai.jason.runtime", "/bin/launchctl unload x.plist", "/bin/launchctl remove ai.jasonharness.desktop"], ran);
    }

    [Fact]
    public void A_command_that_outlives_its_bound_is_ended_and_refused()
    {
        IReadOnlyList<string> slow = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n", "60", "127.0.0.1"]
            : ["/bin/sleep", "60"];
        var watch = Stopwatch.StartNew();

        var refused = Assert.Throws<AutostartException>(() => AutostartRegistrars.Run(slow, TimeSpan.FromSeconds(1)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"the bound was not kept: {watch.Elapsed}");
        Assert.Contains("did not answer within 1 seconds", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_that_ends_within_its_bound_is_answered()
    {
        IReadOnlyList<string> quick = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "exit 3"]
            : ["/bin/sh", "-c", "exit 3"];

        var (exit, _, _) = AutostartRegistrars.Run(quick, TimeSpan.FromSeconds(30));

        Assert.Equal(3, exit);
    }
}
