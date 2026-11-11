using Jason.Cli.Autostart;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

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
    private const string Task = "Jason-" + Sid;
    private const string Unix = "/home/ada/.jason";
    private const string Home = "/home/ada";

    /// <summary>
    /// The Windows registration is a task document, not a command line: <c>/TR</c> carries the invocation as one
    /// string with a documented length limit and cannot say what kind of logon this is. The kind is interactive —
    /// the account's own session, with the token it already has — which an ordinary prompt may register on any
    /// account, a standard one included.
    /// </summary>
    [Fact]
    public void The_windows_task_runs_at_logon_in_the_account_s_own_session()
    {
        var task = AutostartArtifacts.Compose(AutostartPlatform.Windows, [Executable], Windows, @"C:\Users\ada", Sid);

        Assert.Equal(AutostartPlatform.Windows, task.Platform);
        Assert.Equal(Task, task.Name);
        Assert.Equal(Path.Combine(Windows, "autostart", "jason-task.xml"), task.ArtifactPath);

        // Interactive, not S4U: S4U is what an ordinary prompt may not register, and a standard account cannot.
        // What keeps a window off that session's desktop is the executable's manifest, not the task.
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", task.Artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("S4U", task.Artifact, StringComparison.Ordinal);
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
        Assert.Equal([["schtasks", "/Create", "/XML", task.ArtifactPath, "/TN", Task, "/F", "/HRESULT"]], task.Apply);
        Assert.Equal([["schtasks", "/Delete", "/TN", Task, "/F", "/HRESULT"]], task.Remove);
        Assert.Equal([AutostartArtifacts.TaskDelete(Task)], task.Remove);
        Assert.Equal(["schtasks", "/Query", "/TN", Task, "/XML", "ONE", "/HRESULT"], task.Query);
    }

    /// <summary>
    /// The task is named for the account it belongs to. The Task Scheduler's root folder is one namespace for the
    /// whole machine, and another account's task in it answers an ordinary prompt with access denied — measured —
    /// so a fixed name let the first account that ran <c>enable</c> lock every other account out of its own
    /// autostart, and out of <c>jason uninstall</c>. The SID in the name is how other programs on the same machine
    /// avoid it: <c>OneDrive Reporting Task-&lt;SID&gt;</c>, <c>ZoomUpdateTaskUser-&lt;SID&gt;</c>.
    /// </summary>
    [Fact]
    public void Each_account_s_task_carries_its_own_name()
    {
        const string Other = "S-1-5-21-1004336348-1177238915-682003330-1002";

        var mine = AutostartArtifacts.Compose(AutostartPlatform.Windows, [Executable], Windows, @"C:\Users\ada", Sid);
        var theirs = AutostartArtifacts.Compose(AutostartPlatform.Windows, [Executable], @"C:\Users\bob\.jason", @"C:\Users\bob", Other);

        Assert.Equal($"Jason-{Sid}", mine.Name);
        Assert.Equal($"Jason-{Other}", theirs.Name);
        Assert.Contains($@"<URI>\Jason-{Sid}</URI>", mine.Artifact, StringComparison.Ordinal);

        // The name 0.1.0 registered under is its own constant, because it is still asked after: see WindowsTasks.
        Assert.Equal("Jason", AutostartArtifacts.LegacyTaskName);
        Assert.NotEqual(AutostartArtifacts.LegacyTaskName, mine.Name);
    }

    /// <summary>
    /// A task is deleted by name, without being asked. Without <c>/F</c> schtasks asks "(Y/N)?" on the standard input
    /// it inherited and waits there, so the verb would hang without a word; and without <c>/HRESULT</c> its answer is a
    /// sentence in the language Windows was installed in. Both the removal a registration carries and the removal of
    /// the task 0.1.0 left use this one command.
    /// </summary>
    [Fact]
    public void A_task_is_deleted_by_name_without_being_asked() =>
        Assert.Equal(["schtasks", "/Delete", "/TN", "Jason", "/F", "/HRESULT"], AutostartArtifacts.TaskDelete("Jason"));

    /// <summary>
    /// The whole document, for fixed inputs. Two things changed from what 0.1.0 registered, and only two: the
    /// logon type (<c>S4U</c> became <c>InteractiveToken</c>) and the task's name in <c>URI</c>. Every other field
    /// is what 0.1.0 had — a trigger that is enabled, a second instance ignored, no battery rule, no time limit —
    /// and until this test only a reader said so.
    /// </summary>
    [Fact]
    public void The_windows_document_is_0_1_0_s_but_for_the_logon_type_and_the_name()
    {
        var task = AutostartArtifacts.Compose(
            AutostartPlatform.Windows, [@"C:\Program Files\Jason & Co\jason.exe"], Windows, @"C:\Users\ada", Sid);

        const string Expected = $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts the Jason runtime when this account logs on. Nothing supervises it: a runtime that stops stays stopped until somebody starts it.</Description>
                <URI>\Jason-{{Sid}}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{{Sid}}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{{Sid}}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>C:\Program Files\Jason &amp; Co\jason.exe</Command>
                  <Arguments>runtime run --detached --data-dir C:\Users\ada\.jason</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        // Line by line, so the test says nothing about how this file's own lines end on a given checkout.
        Assert.Equal(Expected.ReplaceLineEndings("\n"), task.Artifact.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// What the Task Scheduler hands back is not what it was handed. Measured on Windows 11 24H2 (build 26100)
    /// with a task registered from this document's shape: the principal keeps the SID while the trigger's account
    /// comes back as a <c>MACHINE\name</c>; the defaults (<c>RunLevel</c>, <c>Priority</c>, the <c>Enabled</c>
    /// flags) are left out; <c>IdleSettings</c> and <c>UseUnifiedSchedulingEngine</c> appear; the order changes;
    /// and every line ends in CR CR LF, which an editor shows as a blank line after every element. Whatever reads a registration back — <c>autostart status</c>,
    /// <c>jason status</c>, <c>jason uninstall</c> — reads the command and nothing else, so none of that may
    /// matter.
    /// </summary>
    [Fact]
    public void A_task_read_back_in_the_task_scheduler_s_own_words_names_the_same_command()
    {
        var stored = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts the Jason runtime when this account logs on.</Description>
                <URI>\{Task}</URI>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{Sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                </Principal>
              </Principals>
              <Settings>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <IdleSettings>
                  <StopOnIdleEnd>true</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
              </Settings>
              <Triggers>
                <LogonTrigger>
                  <UserId>ADA-PC\ada</UserId>
                </LogonTrigger>
              </Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>{Executable}</Command>
                  <Arguments>runtime run --detached --data-dir {Windows}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """.ReplaceLineEndings("\r\r\n");

        Assert.Equal(
            [Executable, "runtime", "run", "--detached", "--data-dir", Windows],
            AutostartArtifacts.Read(AutostartPlatform.Windows, stored));
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
    /// launchd starts a LaunchAgent with a search path of its own, <c>/usr/bin:/bin:/usr/sbin:/sbin</c>, which holds no
    /// program a package manager installed, so a plugin whose program is on the person's PATH would be unavailable after
    /// every logon start. The agent carries the search path it was registered with, escaped like every other word in the
    /// document, and reads it back unchanged.
    /// </summary>
    [Fact]
    public void The_launch_agent_carries_the_search_path_it_was_registered_with_escaped()
    {
        const string Searched = "/opt/homebrew/bin:/Users/ada/a&b<c>:/usr/bin";

        var agent = AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/usr/local/bin/jason"], Unix, Home, "ada", Searched);

        Assert.Contains(
            "<key>EnvironmentVariables</key>\n    <dict>\n      <key>PATH</key>\n      <string>/opt/homebrew/bin:/Users/ada/a&amp;b&lt;c&gt;:/usr/bin</string>\n    </dict>",
            agent.Artifact.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Equal(Searched, agent.SearchPath);
        Assert.Equal(Searched, AutostartArtifacts.SearchPathIn(AutostartPlatform.MacOs, agent.Artifact));
        Assert.Equal(agent.Run, AutostartArtifacts.Read(AutostartPlatform.MacOs, agent.Artifact));
    }

    /// <summary>With no search path given there is none to carry: the agent has no environment key, and none is read back.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_launch_agent_given_no_search_path_carries_no_environment(string? searched)
    {
        var agent = AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/usr/local/bin/jason"], Unix, Home, "ada", searched);

        Assert.DoesNotContain("EnvironmentVariables", agent.Artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("<key>PATH</key>", agent.Artifact, StringComparison.Ordinal);
        Assert.Null(agent.SearchPath);
        Assert.Null(AutostartArtifacts.SearchPathIn(AutostartPlatform.MacOs, agent.Artifact));
        Assert.Equal(
            AutostartArtifacts.Compose(AutostartPlatform.MacOs, ["/usr/local/bin/jason"], Unix, Home, "ada").Artifact,
            agent.Artifact);
    }

    /// <summary>
    /// The logon task is composed as it always was, a search path given or not: on Windows the task starts with the
    /// account's own environment.
    /// </summary>
    [Fact]
    public void The_logon_task_carries_no_search_path()
    {
        var without = AutostartArtifacts.Compose(AutostartPlatform.Windows, [Executable], Windows, Home, Sid);
        var with = AutostartArtifacts.Compose(AutostartPlatform.Windows, [Executable], Windows, Home, Sid, "/opt/homebrew/bin:/usr/bin");

        Assert.Equal(without.Artifact, with.Artifact);
        Assert.Null(with.SearchPath);
        Assert.Null(AutostartArtifacts.SearchPathIn(AutostartPlatform.Windows, with.Artifact));
    }

    /// <summary>
    /// The user manager starts a unit with an environment of its own, which reads no shell profile, so a program the
    /// person's PATH finds is missing to a runtime started at logon. The unit carries the search path it was registered
    /// with as its <c>PATH</c>, the whole assignment quoted so an entry with a space stays one word, a per-cent doubled as
    /// in the command line, and reads it back unchanged.
    /// </summary>
    [Fact]
    public void The_user_unit_carries_the_search_path_it_was_registered_with()
    {
        const string Searched = "/home/ada/.local/bin:/home/ada/My Tools/100% bin:/usr/bin:/bin";

        var unit = AutostartArtifacts.Compose(AutostartPlatform.Linux, ["/home/ada/.local/bin/jason"], Unix, Home, "ada", Searched);

        Assert.Contains(
            "[Service]\nType=simple\nEnvironment=\"PATH=/home/ada/.local/bin:/home/ada/My Tools/100%% bin:/usr/bin:/bin\"\nExecStart=",
            unit.Artifact.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Equal(Searched, unit.SearchPath);
        Assert.Equal(Searched, AutostartArtifacts.SearchPathIn(AutostartPlatform.Linux, unit.Artifact));
        Assert.Equal(unit.Run, AutostartArtifacts.Read(AutostartPlatform.Linux, unit.Artifact));
    }

    /// <summary>With no search path given the unit is the one it always was, with no environment line, and none is read back.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_user_unit_given_no_search_path_carries_no_environment(string? searched)
    {
        var unit = AutostartArtifacts.Compose(AutostartPlatform.Linux, ["/home/ada/.local/bin/jason"], Unix, Home, "ada", searched);

        Assert.DoesNotContain("Environment=", unit.Artifact, StringComparison.Ordinal);
        Assert.Null(unit.SearchPath);
        Assert.Null(AutostartArtifacts.SearchPathIn(AutostartPlatform.Linux, unit.Artifact));
        Assert.Equal(
            AutostartArtifacts.Compose(AutostartPlatform.Linux, ["/home/ada/.local/bin/jason"], Unix, Home, "ada").Artifact,
            unit.Artifact);
    }

    /// <summary>
    /// A double quote, a backslash or a control character in the search path has no spelling the unit and its reader
    /// would both agree on, as a double quote has none in the command line: it is refused by name rather than written into
    /// a unit whose PATH would be other than the one it was given.
    /// </summary>
    [Theory]
    [InlineData("/home/ada/\"q\"/bin:/usr/bin", "\"q\"")]
    [InlineData("/home/ada/a\\b/bin:/usr/bin", "a\\b")]
    [InlineData("/home/ada/bin\n[Service]:/usr/bin", "[Service]")]
    [Critical(Family.I4, "no quoting injection into a registration")]
    public void A_search_path_the_unit_cannot_carry_is_refused_by_name(string searched, string named)
    {
        var refused = Assert.Throws<AutostartException>(
            () => AutostartArtifacts.Compose(AutostartPlatform.Linux, ["/usr/local/bin/jason"], Unix, Home, "ada", searched));

        Assert.Equal(AutostartCodes.Refused, refused.Code);
        Assert.Contains("PATH", refused.Message, StringComparison.Ordinal);
        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
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
    /// The unit's documentation is the product's front door, its own address, which a person reading
    /// <c>systemctl --user status jason</c> can open. Only <c>ExecStart</c> is read back, so a unit written before
    /// this still reads as Jason's, and the next enable rewrites it.
    /// </summary>
    [Fact]
    public void The_user_unit_documents_itself_at_the_products_address()
    {
        var unit = AutostartArtifacts.Compose(AutostartPlatform.Linux, ["/home/ada/.local/bin/jason"], Unix, Home, "ada");

        Assert.Contains($"\nDocumentation={ProductAddress.Root}\n", unit.Artifact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every registration names the executable and the data directory, absolutely, and survives a directory
    /// with a space in it — which is where an ordinary Windows installation puts things.
    /// </summary>
    [Theory]
    [Critical(Family.I4, "the logon registration on every platform")]
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
    [Critical(Family.I4, "no quoting injection into a registration")]
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
    /// the question is composed from the platform and, on Windows, the account the task is named for; this is what
    /// keeps the two from drifting apart.
    /// </summary>
    [Theory]
    [InlineData(AutostartPlatform.Windows)]
    [InlineData(AutostartPlatform.MacOs)]
    [InlineData(AutostartPlatform.Linux)]
    public void The_question_a_registrar_asks_is_the_one_the_registration_carries(AutostartPlatform platform) =>
        Assert.Equal(
            AutostartArtifacts.Compose(platform, [Executable], Windows, Home, Sid).Query,
            AutostartArtifacts.QueryFor(platform, Sid));

    /// <summary>And a machine that registers nothing is asked nothing.</summary>
    [Fact]
    public void A_platform_that_registers_nothing_is_asked_nothing() =>
        Assert.Empty(AutostartArtifacts.QueryFor(AutostartPlatform.Unsupported, Sid));

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
