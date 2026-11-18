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
    /// disabled record cleared in the account's login domain, where it stands, each with the bound, before the file goes;
    /// and where the file is gone already, as an earlier version's <c>disable</c> left it, with the label disabled, the label
    /// is still cleared.
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
            return string.Join(" ", command) == "/bin/launchctl print-disabled gui/501"
                ? (0, "disabled services = {\n\t\"ai.jason.runtime\" => disabled\n}\n", string.Empty)
                : (0, string.Empty, string.Empty);
        }, home);
        var registration = AutostartArtifacts.LeavingNoMark(
            AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone") with { ArtifactPath = plist },
            501);

        Assert.True(registrar.Remove(registration).Registration);
        Assert.Equal(
            [$"launchctl unload {plist}", "/bin/launchctl remove ai.jason.runtime", "/bin/launchctl print-disabled gui/501", "/bin/launchctl enable gui/501/ai.jason.runtime", "/bin/launchctl print-disabled user/501"],
            ran.Select(run => run.Command));
        Assert.All(ran, run => Assert.True(run.FileThere, $"{run.Command} ran after the file went"));
        Assert.All(ran, run => Assert.Equal(LaunchdJobs.Limit, run.Limit));
        Assert.False(File.Exists(plist), "the agent's file is still there");

        ran.Clear();
        Assert.False(registrar.Remove(registration).Registration);
        Assert.Equal(["/bin/launchctl remove ai.jason.runtime", "/bin/launchctl print-disabled gui/501", "/bin/launchctl enable gui/501/ai.jason.runtime", "/bin/launchctl print-disabled user/501"], ran.Select(run => run.Command));
    }

    /// <summary>
    /// Jason never writes a launchd record where none stands: with nothing registered and no record of the label in either
    /// domain, the uninstall's removal reads both domains and runs no <c>enable</c>; nor where a domain records the label
    /// as enabled, names another label disabled, cannot be read, or does not answer.
    /// </summary>
    [Theory]
    [InlineData("no record", "disabled services = {\n\t\"com.apple.Siri.agent\" => disabled\n}\n", 0)]
    [InlineData("recorded enabled", "disabled services = {\n\t\"ai.jason.runtime\" => enabled\n}\n", 0)]
    [InlineData("recorded false", "disabled services = {\n\t\"ai.jason.runtime\" => false\n}\n", 0)]
    [InlineData("another label of ours disabled", "disabled services = {\n\t\"ai.jason.runtime.other\" => disabled\n}\n", 0)]
    [InlineData("not readable", "", 1)]
    public void Where_no_record_of_the_label_stands_the_uninstall_writes_none(string what, string answer, int exit)
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var ran = new List<string>();
        var registrar = new LaunchAgentRegistrar((command, _) =>
        {
            ran.Add(string.Join(" ", command));
            return command.Count > 1 && command[1] == "print-disabled" ? (exit, answer, string.Empty) : (0, string.Empty, string.Empty);
        }, home);
        var registration = AutostartArtifacts.LeavingNoMark(
            AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone"),
            501);

        Assert.False(registrar.Remove(registration).Registration);

        Assert.True(
            ran.SequenceEqual(["/bin/launchctl remove ai.jason.runtime", "/bin/launchctl print-disabled gui/501", "/bin/launchctl print-disabled user/501"]),
            $"{what}: {string.Join(" | ", ran)}");
    }

    /// <summary>
    /// A disabled record in one domain is cleared there alone, whichever of the two holds it, in the words macOS prints today
    /// and in the ones earlier versions print; one that does not answer in time writes nothing.
    /// </summary>
    [Theory]
    [InlineData("gui/501", "disabled")]
    [InlineData("user/501", "disabled")]
    [InlineData("gui/501", "true")]
    public void A_disabled_record_in_one_domain_is_cleared_there_alone(string domain, string word)
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var ran = new List<string>();
        var registrar = new LaunchAgentRegistrar((command, _) =>
        {
            ran.Add(string.Join(" ", command));
            return string.Join(" ", command) == $"/bin/launchctl print-disabled {domain}"
                ? (0, $"disabled services = {{\n\t\"ai.jason.runtime\" => {word}\n}}\n", string.Empty)
                : (0, "disabled services = {\n}\n", string.Empty);
        }, home);
        var registration = AutostartArtifacts.LeavingNoMark(
            AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone"),
            501);

        registrar.Remove(registration);

        Assert.Equal([$"/bin/launchctl enable {domain}/ai.jason.runtime"], ran.Where(command => command.Contains(" enable ", StringComparison.Ordinal)));

        ran.Clear();
        var silent = new LaunchAgentRegistrar((command, _) =>
        {
            ran.Add(string.Join(" ", command));
            return command[1] == "print-disabled" ? throw new AutostartException(AutostartCodes.Refused, "did not answer within 10 seconds") : (0, string.Empty, string.Empty);
        }, home);
        silent.Remove(registration);
        Assert.DoesNotContain(ran, command => command.Contains(" enable ", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>enable</c> where launchd holds no record of the label: both domains are read, the job is loaded without <c>-w</c>,
    /// and no record is written; and the uninstall after it writes none either, so the records after equal the records before.
    /// </summary>
    [Fact]
    public void Enable_where_no_record_stands_loads_plainly_and_writes_none()
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var launchd = new FakeLaunchd();
        var registrar = new LaunchAgentRegistrar(launchd.Run, home, 501);
        var registration = Agent(home);

        registrar.Apply(registration);

        Assert.Equal(["/bin/launchctl print-disabled gui/501", "/bin/launchctl print-disabled user/501", $"launchctl load {registration.ArtifactPath}"], launchd.Ran);
        Assert.Empty(launchd.Records);

        launchd.Ran.Clear();
        registrar.Apply(registration);
        Assert.Equal(["/bin/launchctl print-disabled gui/501", "/bin/launchctl print-disabled user/501", $"launchctl unload {registration.ArtifactPath}", $"launchctl load {registration.ArtifactPath}"], launchd.Ran);
        Assert.Empty(launchd.Records);

        registrar.Remove(AutostartArtifacts.LeavingNoMark(registration, 501));
        Assert.Empty(launchd.Records);
        Assert.DoesNotContain(launchd.Ran, command => command.Contains(" enable ", StringComparison.Ordinal) || command.Contains(" -w ", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>disable</c> keeps its record: it unloads with <c>-w</c>, which records the label as disabled, and removes by label,
    /// reading and clearing nothing. The <c>enable</c> after it finds that record and loads with <c>-w</c>, which clears it.
    /// </summary>
    [Fact]
    public void Disable_keeps_its_record_and_the_enable_after_it_loads_with_w()
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var launchd = new FakeLaunchd();
        var registrar = new LaunchAgentRegistrar(launchd.Run, home, 501);
        var registration = Agent(home);
        registrar.Apply(registration);
        launchd.Ran.Clear();

        registrar.Remove(registration);

        Assert.Equal([$"launchctl unload -w {registration.ArtifactPath}", "/bin/launchctl remove ai.jason.runtime"], launchd.Ran);
        Assert.Equal("disabled", launchd.Records["gui/501"]);

        launchd.Ran.Clear();
        registrar.Apply(registration);

        Assert.Equal(["/bin/launchctl print-disabled gui/501", $"launchctl load -w {registration.ArtifactPath}"], launchd.Ran);
        Assert.Equal("enabled", launchd.Records["gui/501"]);
    }

    /// <summary>
    /// A disabled record in the user domain alone is found as well; and where a domain cannot be read, or does not answer,
    /// the record is taken as one that may stand and the job is loaded with <c>-w</c>, so that <c>enable</c> still loads it.
    /// </summary>
    [Theory]
    [InlineData("user domain disabled", "user/501", false)]
    [InlineData("not readable", null, true)]
    public void Enable_loads_with_w_where_a_disabled_record_stands_or_may(string what, string? disabledIn, bool unreadable)
    {
        var home = Path.Combine(_tree.Paths.Root, "home");
        var launchd = new FakeLaunchd { Unreadable = unreadable };
        if (disabledIn is not null)
        {
            launchd.Records[disabledIn] = "disabled";
        }

        var registration = Agent(home);
        new LaunchAgentRegistrar(launchd.Run, home, 501).Apply(registration);

        Assert.True(launchd.Ran[^1] == $"launchctl load -w {registration.ArtifactPath}", $"{what}: {string.Join(" | ", launchd.Ran)}");
    }

    private AutostartRegistration Agent(string home) =>
        AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/opt/jason"], Path.Combine(_tree.Paths.Root, "data"), home, "someone");

    /// <summary>
    /// launchctl as far as the records go: <c>print-disabled</c> prints them, <c>unload -w</c> records the label as disabled and
    /// <c>load -w</c> as enabled in the login domain, <c>enable</c> records it as enabled in its own; nothing else writes one.
    /// </summary>
    private sealed class FakeLaunchd
    {
        public Dictionary<string, string> Records { get; } = new(StringComparer.Ordinal);

        public List<string> Ran { get; } = [];

        public bool Unreadable { get; init; }

        public (int Exit, string Output, string Error) Run(IReadOnlyList<string> command, TimeSpan? limit)
        {
            Ran.Add(string.Join(" ", command));
            switch (command)
            {
                case [_, "print-disabled", var domain]:
                    if (Unreadable)
                    {
                        return (1, string.Empty, "Could not print disabled services");
                    }

                    var line = Records.TryGetValue(domain, out var state) ? $"\t\"ai.jason.runtime\" => {state}\n" : string.Empty;
                    return (0, $"disabled services = {{\n{line}}}\n", string.Empty);
                case ["launchctl", "unload", "-w", _]:
                    Records["gui/501"] = "disabled";
                    break;
                case ["launchctl", "load", "-w", _]:
                    Records["gui/501"] = "enabled";
                    break;
                case [_, "enable", var target]:
                    Records[target[..target.LastIndexOf('/')]] = "enabled";
                    break;
            }

            return (0, string.Empty, string.Empty);
        }
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
