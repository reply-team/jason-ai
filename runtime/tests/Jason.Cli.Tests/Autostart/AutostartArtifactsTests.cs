using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// What autostart would register, on all three platforms, from whichever one these tests are running on.
/// Composing is a pure function of its arguments, so the artifact a Mac would get is asserted here without a
/// Mac — and, more to the point, nothing in this file can register anything anywhere.
/// </summary>
/// <remarks>
/// Every one of them carries the same two things, and the tests say so separately as well as in the documents:
/// the executable, absolute, and the data directory the registration was made under. A registration that let
/// either be worked out at logon instead would be a background runtime on a database nobody chose.
/// </remarks>
public class AutostartArtifactsTests
{
    private const string Executable = @"C:\Users\ada\AppData\Local\Programs\jason\jason.exe";
    private const string Windows = @"C:\Users\ada\.jason";
    private const string Sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
    private const string Unix = "/home/ada/.jason";
    private const string Home = "/home/ada";

    /// <summary>
    /// The Windows registration is a task document, not a command line. <c>/TR</c> would carry the whole
    /// invocation as one string with a documented length limit, and it cannot say what kind of logon this is —
    /// which is the one thing this task has to say.
    /// </summary>
    [Fact]
    public void The_windows_task_runs_at_logon_in_a_session_with_no_desktop()
    {
        var task = AutostartArtifacts.Compose(AutostartPlatform.Windows, [Executable], Windows, @"C:\Users\ada", Sid);

        Assert.Equal(AutostartPlatform.Windows, task.Platform);
        Assert.Equal("Jason", task.Name);
        Assert.Equal(Path.Combine(Windows, "autostart", "jason-task.xml"), task.ArtifactPath);

        // The whole of the no-window guarantee: an S4U logon has no desktop to put a console window on.
        Assert.Contains("<LogonType>S4U</LogonType>", task.Artifact, StringComparison.Ordinal);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", task.Artifact, StringComparison.Ordinal);

        // Whose logon, said as the account's SID in both places the document names an account.
        Assert.Equal(2, task.Artifact.Split($"<UserId>{Sid}</UserId>").Length - 1);

        // No time limit. The Task Scheduler's own default is three days, and a runtime stopped after three days
        // because nobody said otherwise would be the strangest thing this product could ship.
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", task.Artifact, StringComparison.Ordinal);

        // What it runs: the runtime itself, told which data directory it owns.
        Assert.Contains($"<Command>{Executable}</Command>", task.Artifact, StringComparison.Ordinal);
        Assert.Contains($"<Arguments>runtime run --detached --data-dir {Windows}</Arguments>", task.Artifact, StringComparison.Ordinal);

        // Every one of the three asks for /HRESULT, because the answer has to be a code rather than a
        // sentence in the language Windows happens to be installed in. The query had it from the start; the two
        // that act got it after a real machine refused one of them and the code could not say why.
        // AutostartInterpretationTests is where what the codes mean is asserted.
        Assert.Equal([["schtasks", "/Create", "/XML", task.ArtifactPath, "/TN", "Jason", "/F", "/HRESULT"]], task.Apply);
        Assert.Equal([["schtasks", "/Delete", "/TN", "Jason", "/F", "/HRESULT"]], task.Remove);
        Assert.Equal(["schtasks", "/Query", "/TN", "Jason", "/XML", "ONE", "/HRESULT"], task.Query);
    }

    /// <summary>
    /// The LaunchAgent starts the runtime once, at login, and never again: <c>RunAtLoad</c> and no
    /// <c>KeepAlive</c> at all. A runtime somebody stopped has to stay stopped.
    /// </summary>
    [Fact]
    public void The_launch_agent_runs_at_load_and_supervises_nothing()
    {
        var agent = AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/usr/local/bin/jason"], Unix, Home, "ada");

        Assert.Equal("ai.jason.runtime", agent.Name);
        Assert.Equal("/home/ada/Library/LaunchAgents/ai.jason.runtime.plist", agent.ArtifactPath.Replace('\\', '/'));
        Assert.Contains("<key>RunAtLoad</key>", agent.Artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepAlive", agent.Artifact, StringComparison.Ordinal);

        // One string per word, which is what launchd takes: nothing here has to be quoted or re-split.
        Assert.Contains("<string>/usr/local/bin/jason</string>", agent.Artifact, StringComparison.Ordinal);
        Assert.Contains("<string>--data-dir</string>", agent.Artifact, StringComparison.Ordinal);
        Assert.Contains($"<string>{Unix}</string>", agent.Artifact, StringComparison.Ordinal);

        Assert.Equal([["launchctl", "load", "-w", agent.ArtifactPath]], agent.Apply);
        Assert.Equal([["launchctl", "unload", "-w", agent.ArtifactPath]], agent.Remove);
    }

    /// <summary>
    /// The user unit is written, the manager is told to re-read its directory, and the unit is enabled into the
    /// account's own default target. Removing it is the same three steps backwards.
    /// </summary>
    [Fact]
    public void The_user_unit_starts_at_login_and_restarts_nothing()
    {
        var unit = AutostartArtifacts.Compose(AutostartPlatform.Linux, ["/home/ada/.local/bin/jason"], Unix, Home, "ada");

        Assert.Equal("jason.service", unit.Name);
        Assert.Equal("/home/ada/.config/systemd/user/jason.service", unit.ArtifactPath.Replace('\\', '/'));
        Assert.Contains("Type=simple", unit.Artifact, StringComparison.Ordinal);
        Assert.Contains($"ExecStart=/home/ada/.local/bin/jason runtime run --detached --data-dir {Unix}", unit.Artifact, StringComparison.Ordinal);
        Assert.Contains("Restart=no", unit.Artifact, StringComparison.Ordinal);
        Assert.Contains("WantedBy=default.target", unit.Artifact, StringComparison.Ordinal);

        Assert.Equal(
            [["systemctl", "--user", "daemon-reload"], ["systemctl", "--user", "enable", "jason.service"]],
            unit.Apply);
        Assert.Equal(
            [["systemctl", "--user", "disable", "jason.service"], ["systemctl", "--user", "daemon-reload"]],
            unit.Remove);
        Assert.Equal(["systemctl", "--user", "is-enabled", "jason.service"], unit.Query);
    }

    /// <summary>
    /// Every registration names the executable and the data directory, absolutely, and survives a directory
    /// with a space in it — which is where an ordinary Windows installation puts things.
    /// </summary>
    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.MacOs)]
    [InlineData(AutostartPlatform.Linux)]
    public void Every_registration_names_the_data_directory_and_the_executable_absolutely(AutostartPlatform platform)
    {
        const string Spaced = @"C:\Program Files\Jason\jason.exe";
        const string Data = @"C:\Users\ada\Jason Data";

        var registration = AutostartArtifacts.Compose(platform, [Spaced], Data, @"C:\Users\ada", Sid);

        Assert.Contains(Spaced, registration.Artifact, StringComparison.Ordinal);
        Assert.Contains(Data, registration.Artifact, StringComparison.Ordinal);
        Assert.Equal([Spaced, "runtime", "run", "--detached", "--data-dir", Data], registration.Run);

        // And the space survives the round trip, which is the half a quoting mistake breaks.
        Assert.Equal(registration.Run, AutostartArtifacts.Read(platform, registration.Artifact));
    }

    /// <summary>
    /// What is composed is what is read back. This is how <c>status</c> answers at all: it reads the document a
    /// machine holds rather than composing a second one and hoping they agree.
    /// </summary>
    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.MacOs)]
    [InlineData(AutostartPlatform.Linux)]
    public void What_is_composed_is_what_is_read_back(AutostartPlatform platform)
    {
        var registration = AutostartArtifacts.Compose(platform, [Executable], Windows, Home, Sid);

        Assert.Equal(registration.Run, AutostartArtifacts.Read(platform, registration.Artifact));
        Assert.Equal(
            [Executable, "runtime", "run", "--detached", "--data-dir", Windows],
            AutostartArtifacts.Read(platform, registration.Artifact));
    }

    /// <summary>A build that runs through the muxer registers the muxer and this build's assembly, both.</summary>
    [Fact]
    public void A_build_run_through_the_muxer_registers_the_muxer_and_its_assembly()
    {
        var registration = AutostartArtifacts.Compose(
            AutostartPlatform.Linux,
            ["/usr/bin/dotnet", "/src/jason/Jason.App.dll"],
            Unix,
            Home,
            "ada");

        Assert.Equal(
            ["/usr/bin/dotnet", "/src/jason/Jason.App.dll", "runtime", "run", "--detached", "--data-dir", Unix],
            AutostartArtifacts.Read(AutostartPlatform.Linux, registration.Artifact));
    }

    /// <summary>
    /// A per-cent in a path is a specifier to systemd, which would substitute it for something else entirely.
    /// It is doubled in the unit and undoubled on the way back, so what a unit says it runs is what it was
    /// given — and the other two platforms, which have no such rule, are unaffected.
    /// </summary>
    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.MacOs)]
    [InlineData(AutostartPlatform.Linux)]
    public void A_per_cent_in_a_data_directory_survives_the_registration(AutostartPlatform platform)
    {
        const string Data = "/home/ada/100% of it";

        var registration = AutostartArtifacts.Compose(platform, ["/usr/local/bin/jason"], Data, Home, Sid);

        Assert.Equal(Data, AutostartArtifacts.Read(platform, registration.Artifact)[^1]);
        if (platform is AutostartPlatform.Linux)
        {
            Assert.Contains("100%% of it", registration.Artifact, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And a double quote, which a POSIX path may carry, has no spelling a unit file and its reader would both
    /// agree on: it is refused by name rather than written into a unit that would split into other words than
    /// it was given. Windows cannot reach this — a quote is not a legal character in a path there.
    /// </summary>
    [Fact]
    public void A_double_quote_in_a_data_directory_is_refused_by_name()
    {
        var refused = Assert.Throws<AutostartException>(
            () => AutostartArtifacts.Compose(
                AutostartPlatform.Linux,
                ["/usr/local/bin/jason"],
                "/home/ada/Jason \"Q\" Co",
                Home,
                "ada"));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("double quote", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Jason \"Q\" Co", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The question a registrar asks is the one the registration carries. A registrar has to ask before it
    /// knows what this installation would register — what is there may have been put there by another one — so
    /// the question is composed from the platform alone, and this is what keeps the two from drifting apart.
    /// </summary>
    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.MacOs)]
    [InlineData(AutostartPlatform.Linux)]
    public void The_question_a_registrar_asks_is_the_one_the_registration_carries(AutostartPlatform platform) =>
        Assert.Equal(
            AutostartArtifacts.Compose(platform, [Executable], Windows, Home, Sid).Query,
            AutostartArtifacts.QueryFor(platform));

    /// <summary>And a machine that registers nothing is asked nothing.</summary>
    [Fact]
    public void A_platform_that_registers_nothing_is_asked_nothing() =>
        Assert.Empty(AutostartArtifacts.QueryFor(AutostartPlatform.Unsupported));

    /// <summary>There is nothing to compose for a platform that cannot register anything; the verb says so first.</summary>
    [Fact]
    public void An_unsupported_platform_is_not_composed() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AutostartArtifacts.Compose(AutostartPlatform.Unsupported, [Executable], Windows, Home, Sid));

    [Fact]
    public void A_registration_with_no_program_to_run_is_a_programming_error() =>
        Assert.Throws<ArgumentException>(
            () => AutostartArtifacts.Compose(AutostartPlatform.Linux, [], Unix, Home, "ada"));

    /// <summary>A document that is not one of ours names no command line, rather than half of one.</summary>
    [Theory]
    [InlineData(AutostartPlatform.Windows, "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Settings /></Task>")]
    [InlineData(AutostartPlatform.MacOs, "<plist version=\"1.0\"><dict><key>Label</key><string>x</string></dict></plist>")]
    [InlineData(AutostartPlatform.Linux, "[Unit]\nDescription=something else\n")]
    public void A_document_that_names_no_command_is_read_as_naming_none(AutostartPlatform platform, string artifact) =>
        Assert.Empty(AutostartArtifacts.Read(platform, artifact));
}
