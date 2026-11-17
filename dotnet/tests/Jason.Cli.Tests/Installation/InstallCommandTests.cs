using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// <c>jason install</c>, the machine setup, run against a scratch home, a scratch data directory, a carried folder of the
/// test's own and, on Windows, a registry key of the test's own: never the person's profiles, Path or skills.
/// </summary>
public sealed class InstallCommandTests : IDisposable
{
    private readonly TempPaths _data = new();
    private readonly TempPaths _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Home => Path.Combine(_tree.Paths.Root, "home");

    private string LocalAppData => Path.Combine(_tree.Paths.Root, "local");

    public InstallCommandTests()
    {
        // The data directory as a person's first start finds it: not there at all.
        Directory.Delete(_data.Paths.Root);
        Directory.CreateDirectory(Home);
    }

    public void Dispose()
    {
        _data.Dispose();
        _tree.Dispose();
    }

    // --- refusals ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_jason_the_application_does_not_carry_is_refused_and_nothing_is_written()
    {
        var executable = Path.Combine(_tree.Paths.Root, "loose", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "jason");
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CliErrors.InstallNotCarried, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
        Assert.False(Directory.Exists(_data.Paths.Root));
    }

    [Fact]
    public async Task A_machine_with_no_places_to_set_up_is_refused()
    {
        var (env, output) = Environment(null, Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CliErrors.InstallUnsupported, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
    }

    // --- macOS: the wrapper and the profile lines ---------------------------------------------------------

    [Fact]
    [Critical(Family.I3, "jason install writes nothing under the data directory without --roles")]
    public async Task On_macos_it_places_the_wrapper_and_the_install_scripts_lines_and_nothing_under_the_data_directory()
    {
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var wrapper = Path.Combine(Home, ".local", "bin", "jason");
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(wrapper));
        Assert.Equal($"#!/bin/sh\n# Written by the Jason desktop application.\nexec '{executable}' \"$@\"\n", File.ReadAllText(wrapper));
        var line = $"export PATH=\"{Path.Combine(Home, ".local", "bin")}:$PATH\"";
        foreach (var profile in new[] { ".zprofile", ".zshrc" })
        {
            Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, profile)));
        }

        Assert.False(File.Exists(Path.Combine(Home, ".profile")), "zsh's account got a ~/.profile");
        Assert.False(Directory.Exists(_data.Paths.Root), "the data directory was made, which turns a first start's Demo into Live");

        var report = Report(output);
        Assert.Equal("0.34.0", report.GetProperty("version").GetString());
        Assert.Equal(wrapper, report.GetProperty("command").GetProperty("runs").GetString());
        Assert.Equal(wrapper, report.GetProperty("command").GetProperty("open_shell").GetString());
        Assert.True(report.GetProperty("command").GetProperty("changed").GetBoolean());
        Assert.Equal(2, report.GetProperty("command").GetProperty("profiles").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Equal("not_running", report.GetProperty("runtime").GetString());
        Assert.True(report.GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var executable = Carried();
        PlantHosts(claude: true, codex: true);
        var (env, _) = Environment(MacPlaces(), executable);
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));
        var before = Snapshot(Home);

        var (again, output) = Environment(MacPlaces(), executable);
        var exit = await InstallCommand.RunAsync(again, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, Snapshot(Home));
        var report = Report(output);
        Assert.False(report.GetProperty("command").GetProperty("changed").GetBoolean());
        Assert.Equal(0, report.GetProperty("command").GetProperty("profiles").GetArrayLength());
        Assert.All(report.GetProperty("skills").EnumerateArray(), root => Assert.Equal(0, root.GetProperty("written").GetInt32()));
    }

    /// <summary>
    /// The gate <c>install.sh</c> keeps before it writes a profile, judged by the PATH a terminal has: the agent's script passes
    /// nothing, and the routine reads its own process's PATH, as the script does; the application passes its login shell's
    /// PATH with <c>--terminal-path</c>, or the empty value where the shell gave no answer, which names no folder. The
    /// process's own PATH is the seam's here, so a row that leaves the option out is judged by it and the rest are not.
    /// </summary>
    [Theory]
    [InlineData("on the terminal's PATH", "/usr/bin:{bin}", "/usr/bin:/bin", false)]
    [InlineData("only on the caller's own PATH", "/usr/bin:/bin", "/usr/bin:{bin}:/bin", true)]
    [InlineData("the terminal's PATH unknown", "", "{bin}:/usr/bin:/bin", true)]
    [InlineData("no option, on the routine's own PATH", null, "/usr/bin:{bin}", false)]
    [InlineData("no option, not on the routine's own PATH", null, "/usr/bin:/bin", true)]
    public async Task The_profiles_are_written_only_where_a_terminal_does_not_find_the_folder(string row, string? terminalPath, string ownPath, bool written)
    {
        var bin = Path.Combine(Home, ".local", "bin");
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        env = env with { SearchPath = ownPath.Replace("{bin}", bin, StringComparison.Ordinal) };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: terminalPath?.Replace("{bin}", bin, StringComparison.Ordinal)), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(bin, "jason")));
        var report = Report(output);
        var profiles = report.GetProperty("command").GetProperty("profiles");
        if (written)
        {
            var line = $"export PATH=\"{bin}:$PATH\"";
            foreach (var profile in new[] { ".zprofile", ".zshrc" })
            {
                Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, profile)));
            }

            Assert.Equal(2, profiles.GetArrayLength());
        }
        else
        {
            Assert.False(File.Exists(Path.Combine(Home, ".zprofile")), $"{row}: a profile was written for a folder a terminal finds");
            Assert.False(File.Exists(Path.Combine(Home, ".zshrc")), $"{row}: a profile was written for a folder a terminal finds");
            Assert.Equal(0, profiles.GetArrayLength());
            Assert.Contains(report.GetProperty("done").EnumerateArray(), said => said.GetString() == $"{bin} is on the PATH a terminal has already, so no profile was written.");
        }
    }

    /// <summary>
    /// The option as a caller types it: the empty value reaches the routine as "the shell gave no answer", and a PATH as that
    /// PATH, with the routine's own PATH naming the folder in both, so only the option decides.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("/usr/bin:{bin}", false)]
    public async Task The_terminal_path_option_is_read_as_the_caller_types_it(string value, bool written)
    {
        var bin = Path.Combine(Home, ".local", "bin");
        var (env, _) = Environment(MacPlaces(), Carried());
        env = env with { SearchPath = bin };

        var exit = await CliApp.RunAsync(["install", "--terminal-path", value.Replace("{bin}", bin, StringComparison.Ordinal)], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(written, File.Exists(Path.Combine(Home, ".zprofile")));
    }

    /// <summary>The gate is macOS's: on Windows a terminal PATH that names the folder already still leaves the Path entry written.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_the_terminal_path_changes_nothing()
    {
        using var key = ScratchKey.Create();
        key.SetPath(@"C:\Tools", RegistryValueKind.String);
        var executable = Carried("jason.exe");
        var bundle = Path.GetDirectoryName(executable)!;
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: bundle), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal((@"C:\Tools;" + bundle, RegistryValueKind.String), key.ReadPath());
        Assert.True(Report(output).GetProperty("command").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task A_profile_that_holds_the_line_already_takes_nothing()
    {
        var line = $"export PATH=\"{Path.Combine(Home, ".local", "bin")}:$PATH\"";
        File.WriteAllText(Path.Combine(Home, ".zshrc"), $"alias ll='ls -l'\n{line}\n");
        var (env, _) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal($"alias ll='ls -l'\n{line}\n", File.ReadAllText(Path.Combine(Home, ".zshrc")));
        Assert.Contains("# added by jason install", File.ReadAllText(Path.Combine(Home, ".zprofile")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_wrapper_names_the_data_directory_where_one_is_set()
    {
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);
        env = env with { Variables = new Dictionary<string, string> { ["JASON_DATA_DIR"] = _data.Paths.Root } };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        var target = CommandFiles.WrapperTarget(File.ReadAllText(Path.Combine(Home, ".local", "bin", "jason")));
        Assert.Equal((executable, (string?)_data.Paths.Root), target);
    }

    [Fact]
    public async Task Another_programs_jason_in_local_bin_is_left_and_nothing_is_placed()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), "#!/bin/sh\necho somebody else's\n");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("#!/bin/sh\necho somebody else's\n", File.ReadAllText(Path.Combine(bin, "jason")));
        Assert.False(File.Exists(Path.Combine(Home, ".zprofile")), "the lines were written for a jason that is not Jason's");
        var report = Report(output);
        Assert.Contains("another program's", report.GetProperty("problems")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("command").ValueKind);
    }

    [Fact]
    public async Task An_older_wrapper_of_the_applications_is_pointed_at_this_jason()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), CommandFiles.Wrapper("/Applications/Old.app/Contents/Resources/bundle/jason", null));
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));

        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(bin, "jason")));
    }

    // --- macOS: retiring a command-line install --------------------------------------------------------------

    [Fact]
    public async Task On_macos_the_command_line_install_is_replaced_by_the_wrapper_its_notices_go_and_its_lines_stay()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "jason"), new byte[200_000]);
        File.WriteAllText(Path.Combine(bin, "jason-LICENSE"), "MIT");
        File.WriteAllText(Path.Combine(bin, "jason-THIRD-PARTY-NOTICES"), "notices");
        var line = $"export PATH=\"{bin}:$PATH\"";
        File.WriteAllText(Path.Combine(Home, ".zprofile"), $"\n# added by jason install\n{line}\n");
        File.WriteAllText(Path.Combine(Home, ".zshrc"), $"\n# added by jason install\n{line}\n");
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(bin, "jason")));
        Assert.False(File.Exists(Path.Combine(bin, "jason-LICENSE")));
        Assert.False(File.Exists(Path.Combine(bin, "jason-THIRD-PARTY-NOTICES")));
        Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, ".zprofile")));
        var retired = Report(output).GetProperty("retired");
        Assert.Equal(Path.Combine(bin, "jason"), retired.GetProperty("executable").GetString());
        Assert.Equal(2, retired.GetProperty("removed").GetArrayLength());
    }

    [Fact]
    public async Task The_logon_registration_that_named_the_retired_executable_is_made_again_naming_this_jason()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run"], null) };
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        env = env with { Autostart = registrar };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var applied = Assert.Single(registrar.Applied);
        Assert.Equal(executable, applied.Run[0]);
        Assert.True(Report(output).GetProperty("retired").GetProperty("reregistered").GetBoolean());
    }

    [Fact]
    public async Task A_registration_for_another_data_directory_is_left_as_it_is()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--data-dir", Path.Combine(_tree.Paths.Root, "other")], null) };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { Autostart = registrar };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Empty(registrar.Applied);
        Assert.False(Report(output).GetProperty("retired").GetProperty("reregistered").GetBoolean());
    }

    [Fact]
    public async Task A_runtime_running_from_the_retired_executable_is_drained_and_stopped_and_none_is_started()
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var asked = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            asked.Add(operation);
            switch (operation)
            {
                case Operations.SystemDrain:
                    return RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 0), JasonJson.Options));
                case Operations.SystemShutdown:
                    File.Delete(_data.Paths.DescriptorFile);
                    processes.RunningPids.Clear();
                    return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD"));
                default:
                    return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD"));
            }
        });
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = handler, Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Operations.SystemDrain, asked[0]);
        Assert.Contains(Operations.SystemShutdown, asked);
        Assert.Empty(processes.Launches);
        var report = Report(output);
        Assert.Equal("stopped", report.GetProperty("runtime").GetString());
        Assert.Contains(report.GetProperty("left").EnumerateArray(), line => line.GetString()!.Contains("`jason runtime start`", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_runtime_of_another_executable_is_left_running()
    {
        PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_ELSE") with { InstalledImage = Path.Combine(_tree.Paths.Root, "elsewhere", "jason") });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("running", Report(output).GetProperty("runtime").GetString());
    }

    /// <summary>Called where nothing is to be retired, it touches no runtime: the runtime is not even asked anything.</summary>
    [Fact]
    public async Task Where_nothing_is_retired_no_runtime_is_asked_stopped_or_started()
    {
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE") with { InstalledImage = Path.Combine(_tree.Paths.Root, "elsewhere", "jason") });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(processes.Launches);
        Assert.Equal("running", Report(output).GetProperty("runtime").GetString());
        Assert.Equal(JsonValueKind.Null, Report(output).GetProperty("retired").ValueKind);
    }

    // --- the skills ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_harness_skills_go_to_claude_code_and_codex_where_found_and_are_recorded()
    {
        PlantHosts(claude: true, codex: true);
        Directory.CreateDirectory(Path.Combine(Home, ".gemini", "antigravity-cli"));
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var root in new[] { Path.Combine(Home, ".claude", "skills"), Path.Combine(Home, ".agents", "skills") })
        {
            Assert.True(File.Exists(Path.Combine(root, "operating", "SKILL.md")), $"no runtime pack in {root}");
            Assert.True(File.Exists(Path.Combine(root, "campaign-planning", "SKILL.md")), $"no business pack in {root}");
            Assert.NotNull(SkillsRecord.Read(root));
        }

        Assert.False(Directory.Exists(Path.Combine(Home, ".gemini", "antigravity-cli", "skills")), "a host other than Claude Code and Codex was deployed into");
        Assert.False(Directory.Exists(_data.Paths.Root), "the harness skills wrote under the data directory");
        Assert.Equal(2, Report(output).GetProperty("skills").GetArrayLength());
    }

    [Fact]
    public async Task With_no_host_found_no_skills_are_deployed_and_it_says_so()
    {
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = Report(output);
        Assert.Equal(0, report.GetProperty("skills").GetArrayLength());
        Assert.Contains(report.GetProperty("left").EnumerateArray(), line => line.GetString()!.Contains("No Claude Code", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(Home, ".claude")));
    }

    [Fact]
    [Critical(Family.I3, "a person's own skill under a name Jason ships is left alone")]
    public async Task A_persons_own_skill_under_a_name_jason_ships_is_left_alone_and_named()
    {
        PlantHosts(claude: true, codex: false);
        var own = Path.Combine(Home, ".claude", "skills", "operating");
        Directory.CreateDirectory(own);
        File.WriteAllText(Path.Combine(own, "SKILL.md"), "mine");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(own, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(Home, ".claude", "skills", "campaign-planning", "SKILL.md")));
        var root = Assert.Single(Report(output).GetProperty("skills").EnumerateArray());
        Assert.Equal(["operating"], root.GetProperty("left_alone").EnumerateArray().Select(name => name.GetString()));
        var recorded = SkillsRecord.Read(Path.Combine(Home, ".claude", "skills"))!.Packs.SelectMany(pack => pack.Files).Select(file => file.Path);
        Assert.DoesNotContain(recorded, path => path.StartsWith("operating/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_roles_the_role_skills_go_into_the_data_directory()
    {
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(Path.Combine(_data.Paths.RoleSkillsDirectory, "researcher", "SKILL.md")));
        Assert.Equal(_data.Paths.RoleSkillsDirectory, Report(output).GetProperty("roles").GetProperty("root").GetString());
    }

    // --- Windows ---------------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_the_bundle_folder_goes_on_the_path_once_and_the_applications_command_files_are_taken_back()
    {
        using var key = ScratchKey.Create();
        key.SetPath(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps", RegistryValueKind.ExpandString);
        var executable = Carried("jason.exe");
        var apps = Path.Combine(LocalAppData, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "jason.cmd"), $"@echo off\r\n\"{executable}\" %*\r\n");
        File.WriteAllText(Path.Combine(apps, "jason"), CommandFiles.Wrapper(executable.Replace('\\', '/'), null));
        File.WriteAllText(Path.Combine(apps, "jason-desktop.cmd"), "@echo off\r\nsomebody else's\r\n");
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var bundle = Path.GetDirectoryName(executable)!;
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + bundle, RegistryValueKind.ExpandString), key.ReadPath());
        Assert.False(File.Exists(Path.Combine(apps, "jason.cmd")));
        Assert.False(File.Exists(Path.Combine(apps, "jason")));
        Assert.True(File.Exists(Path.Combine(apps, "jason-desktop.cmd")), "a file the application did not write was removed");
        var command = Report(output).GetProperty("command");
        Assert.Equal(bundle, command.GetProperty("runs").GetString());
        Assert.Equal(executable, command.GetProperty("open_shell").GetString());
        Assert.Equal(2, command.GetProperty("taken_back").GetArrayLength());

        var (again, second) = Environment(WindowsPlaces(key.SubKey), executable);
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(again, new InstallOptions(false, false, null), Ct));
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + bundle, RegistryValueKind.ExpandString), key.ReadPath());
        Assert.False(Report(second).GetProperty("command").GetProperty("changed").GetBoolean());
        Assert.False(Directory.Exists(_data.Paths.Root));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_the_command_line_install_goes_with_its_path_entry_and_its_folder()
    {
        using var key = ScratchKey.Create();
        var folder = Path.Combine(LocalAppData, "Programs", "jason");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "jason.exe", "jason-LICENSE", "jason-THIRD-PARTY-NOTICES", "jason.previous.exe" })
        {
            File.WriteAllText(Path.Combine(folder, name), name);
        }

        key.SetPath(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + folder, RegistryValueKind.ExpandString);
        var executable = Carried("jason.exe");
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(folder), "the command-line install's folder is still there");
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + Path.GetDirectoryName(executable), RegistryValueKind.ExpandString), key.ReadPath());
        var retired = Report(output).GetProperty("retired");
        Assert.Equal(Path.Combine(folder, "jason.exe"), retired.GetProperty("executable").GetString());
        Assert.Equal(JsonValueKind.Null, retired.GetProperty("aside").ValueKind);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_an_executable_a_running_process_holds_is_renamed_aside_and_nothing_is_started_to_wait()
    {
        using var key = ScratchKey.Create();
        var folder = Path.Combine(LocalAppData, "Programs", "jason");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "jason.exe");
        File.WriteAllText(old, "running");
        key.SetPath(folder, RegistryValueKind.ExpandString);
        var processes = new FakeProcessControl();
        // What Windows answers for the image of a running process: a rename is allowed, a delete is not.
        var places = WindowsPlaces(key.SubKey) with { Delete = file => DeleteUnlessRunning(file, old) };
        var (env, output) = Environment(places, Carried("jason.exe"));
        env = env with { Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var aside = Report(output).GetProperty("retired").GetProperty("aside").GetString()!;
        Assert.True(File.Exists(aside));
        Assert.StartsWith(Path.Combine(_tree.Paths.Root, "jason-retired-"), aside, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(old));
        Assert.Empty(processes.Launches);
        Assert.Empty(processes.Runs);
    }

    // --- the critic's rows: what must never happen ------------------------------------------------------------

    /// <summary>
    /// B1, macOS: where the wrapper cannot be written (a folder stands at ~/.local/bin/jason) or another program's file
    /// stands there, the terminal command was not placed, so a command-line install elsewhere is not retired: its executable
    /// and the notice that marks it Jason's stay, and it says why.
    /// </summary>
    [Theory]
    [InlineData("folder")]
    [InlineData("foreign")]
    public async Task On_macos_nothing_is_retired_where_the_terminal_command_was_not_placed(string blocker)
    {
        var old = PlantElsewhere("jason");
        var target = Path.Combine(Home, ".local", "bin", "jason");
        if (blocker == "folder")
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "#!/bin/sh\necho somebody else's\n");
        }

        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, old), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(old), "the old executable went though no terminal command replaced it");
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(old)!, "jason-LICENSE")), "the notice that marks the old file Jason's went");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains($"{old} was not retired", StringComparison.Ordinal));
    }

    /// <summary>B1, Windows: a Path that cannot be written leaves the command-line install, its Path entry and the application's command files as they were.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_nothing_is_retired_or_taken_back_where_the_path_could_not_be_written()
    {
        using var key = ScratchKey.Create();
        var folder = Path.Combine(LocalAppData, "Programs", "jason");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "jason.exe");
        File.WriteAllText(old, "old");
        key.SetPath(folder, RegistryValueKind.ExpandString);
        var executable = Carried("jason.exe");
        var apps = Path.Combine(LocalAppData, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(apps);
        var shim = Path.Combine(apps, "jason.cmd");
        File.WriteAllText(shim, $"@echo off\r\n\"{executable}\" %*\r\n");
        key.DenySetValue();
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(old), "jason.exe went though the bundle folder never reached the Path");
        Assert.True(File.Exists(shim), "the application's command file went though nothing else answers jason");
        Assert.Equal((folder, RegistryValueKind.ExpandString), key.ReadPath());
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Equal(0, report.GetProperty("command").GetProperty("taken_back").GetArrayLength());
    }

    /// <summary>B2: on macOS the registration is moved on disk and takes effect at the next logon: no load, which would start a runtime now (RunAtLoad), and no unload.</summary>
    [Fact]
    public async Task On_macos_the_registration_is_moved_on_disk_and_nothing_is_loaded()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--detached", "--data-dir", _data.Paths.Root], null) };
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        env = env with { Autostart = registrar };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        var applied = Assert.Single(registrar.Applied);
        Assert.Equal(executable, applied.Run[0]);
        Assert.Empty(applied.Apply);
        Assert.Empty(applied.Remove);
        Assert.Contains("RunAtLoad", applied.Artifact, StringComparison.Ordinal);
        Assert.Contains(Report(output).GetProperty("done").EnumerateArray(), said => said.GetString()!.Contains("next logon", StringComparison.Ordinal));
    }

    /// <summary>B3: <c>--retire</c> takes only a regular file named jason with a sign of its own that it is Jason's; anything else is named and left.</summary>
    [Theory]
    [InlineData("node", true)]
    [InlineData("jason", false)]
    public async Task Retire_leaves_a_file_that_is_not_a_jason_of_jasons(string name, bool notices)
    {
        var folder = Path.Combine(_tree.Paths.Root, "tools");
        Directory.CreateDirectory(folder);
        var named = Path.Combine(folder, name);
        File.WriteAllBytes(named, new byte[1000]);
        if (notices)
        {
            File.WriteAllText(Path.Combine(folder, "jason-LICENSE"), "MIT");
        }

        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, named), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(named), $"--retire deleted {named}");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.StartsWith($"--retire names {named}", StringComparison.Ordinal));
    }

    /// <summary>B4: a folder install.sh refuses to spell inside double quotes takes no profile line; the hand line, in single quotes, is given.</summary>
    [Theory]
    [InlineData("ho$me")]
    [InlineData("ho`me")]
    public async Task A_folder_the_script_cannot_spell_takes_no_profile_line_and_the_hand_line_is_given(string homeName)
    {
        var home = Path.Combine(_tree.Paths.Root, homeName);
        Directory.CreateDirectory(home);
        var bin = Path.Combine(home, ".local", "bin");
        var (env, output) = Environment(MacPlaces() with { Home = home }, Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(File.Exists(Path.Combine(home, ".zprofile")), "a line the shell reads as code was written");
        Assert.False(File.Exists(Path.Combine(home, ".zshrc")), "a line the shell reads as code was written");
        Assert.True(File.Exists(Path.Combine(bin, "jason")));
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains($"export PATH='{bin}':\"$PATH\"", StringComparison.Ordinal));
    }

    /// <summary>B5: a Path of a kind that is not a string is not overwritten with the bundle folder alone; it is refused and said.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task A_path_value_of_another_kind_is_refused_and_left()
    {
        using var key = ScratchKey.Create();
        key.SetMulti("Path", [@"C:\Tools", @"C:\More"]);
        var (env, output) = Environment(WindowsPlaces(key.SubKey), Carried("jason.exe"));

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([@"C:\Tools", @"C:\More"], key.ReadMulti("Path")!);
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("MultiString", StringComparison.Ordinal));
    }

    /// <summary>B6: skills a deployment wrote before it was cut off, with no record yet, equal the bundle's: they are Jason's, adopted and recorded.</summary>
    [Fact]
    public async Task Skills_an_interrupted_deployment_left_unrecorded_are_adopted_and_recorded()
    {
        PlantHosts(claude: true, codex: false);
        var executable = Carried();
        var (first, _) = Environment(MacPlaces(), executable);
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(first, new InstallOptions(false, false, null), Ct));
        var root = Path.Combine(Home, ".claude", "skills");
        File.Delete(Path.Combine(root, ".jason-skills.json"));
        Assert.Null(SkillsRecord.Read(root));

        var (env, output) = Environment(MacPlaces(), executable);
        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var deployed = Assert.Single(Report(output).GetProperty("skills").EnumerateArray());
        Assert.Equal(0, deployed.GetProperty("left_alone").GetArrayLength());
        var recorded = SkillsRecord.Read(root)!.Packs.SelectMany(pack => pack.Files).Select(file => file.Path).ToList();
        Assert.Contains(recorded, path => path.StartsWith("operating/", StringComparison.Ordinal));
    }

    /// <summary>Note 7: a stop that fails leaves the runtime resumed, not draining.</summary>
    [Fact]
    public async Task A_runtime_that_would_not_stop_is_resumed()
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var asked = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            asked.Add(operation);
            return operation switch
            {
                Operations.SystemDrain => RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 0), JasonJson.Options)),
                Operations.SystemShutdown => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD")),
                _ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD")),
            };
        });
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = handler, Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("would_not_stop", Report(output).GetProperty("runtime").GetString());
        Assert.Equal(Operations.SystemResume, asked[^1]);
        Assert.True(File.Exists(old));
    }

    /// <summary>Note 7: a runtime whose descriptor names no image may run from the file, so the file is not retired, and the report says why.</summary>
    [Fact]
    public async Task A_runtime_that_names_no_image_keeps_the_file_it_may_run_from()
    {
        var old = PlantElsewhere("jason");
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_ANON") with { InstalledImage = null });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, old), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(old), "the file a runtime may run from was retired under it");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("does not name the executable it runs from", StringComparison.Ordinal));
    }

    /// <summary>Note 7: a registration that named no data directory is registered again naming none.</summary>
    [Fact]
    public async Task A_registration_without_a_data_directory_keeps_having_none()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--detached"], null) };
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);
        env = env with { Autostart = registrar };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        var applied = Assert.Single(registrar.Applied);
        Assert.Equal([executable, "runtime", "run", "--detached"], applied.Run);
    }

    /// <summary>Note 8: where retiring would stop a runtime or move a registration, a data directory of a newer layout refuses that part, with or without --roles.</summary>
    [Fact]
    public async Task A_newer_layout_keeps_its_runtime_and_registration_and_the_install_is_not_retired()
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        File.WriteAllText(Path.Combine(_data.Paths.Root, "layout.json"), """{"layout":2,"written_by":"0.43.0","at":"2026-11-20T10:00:00Z"}""");
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--detached", "--data-dir", _data.Paths.Root], null) };
        var (env, output) = Environment(MacPlaces(), Carried());
        // The stamp is read when the paths are made, as at a real start.
        env = env with { Paths = new Jason.Contracts.Discovery.JasonPaths(_data.Paths.Root), HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes, Autostart = registrar };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(registrar.Applied);
        Assert.True(File.Exists(old), "the command-line install was retired over a newer layout");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("layout_newer", StringComparison.Ordinal));
    }

    /// <summary>Note 9: a failure no step expected (here the home cannot be read for agent hosts) still ends in the one document, the failure named.</summary>
    [Fact]
    public async Task An_unexpected_input_output_failure_still_prints_the_one_report()
    {
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { Harnesses = new UnreadableHome() };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        Assert.False(report.GetProperty("completed").GetBoolean());
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("the disk went away", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Home, ".local", "bin", "jason")), "the steps before the failure did not run");
    }

    private sealed class UnreadableHome : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => throw new IOException("the disk went away");
    }

    /// <summary>Note 10: on macOS 0.48's jason-desktop wrapper of the application's is taken back, as the Windows command files are; another program's stays.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task On_macos_the_applications_jason_desktop_wrapper_is_taken_back(bool ours)
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var desktop = Path.Combine(bin, "jason-desktop");
        File.WriteAllText(desktop, ours ? CommandFiles.Wrapper("/Applications/Jason.app/Contents/Resources/bundle/jason", null) : "#!/bin/sh\necho mine\n");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(!ours, File.Exists(desktop));
        Assert.Equal(ours ? 1 : 0, Report(output).GetProperty("command").GetProperty("taken_back").GetArrayLength());
    }

    /// <summary>Note 11: a profile that is a link to a regular file is appended through; a link to anything else is refused with the hand line.</summary>
    [Fact]
    public async Task A_profile_link_is_followed_only_to_a_regular_file()
    {
        var dotfiles = Path.Combine(_tree.Paths.Root, "dotfiles");
        Directory.CreateDirectory(dotfiles);
        var real = Path.Combine(dotfiles, "zshrc");
        File.WriteAllText(real, "# mine\n");
        var zshrc = Path.Combine(Home, ".zshrc");
        var zprofile = Path.Combine(Home, ".zprofile");
        try
        {
            File.CreateSymbolicLink(zshrc, real);
            Directory.CreateSymbolicLink(zprofile, dotfiles);
        }
        catch (Exception unsupported) when (unsupported is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This account cannot make symbolic links.");
        }

        var bin = Path.Combine(Home, ".local", "bin");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.NotNull(new FileInfo(zshrc).LinkTarget);
        Assert.Equal($"# mine\n\n# added by jason install\nexport PATH=\"{bin}:$PATH\"\n", File.ReadAllText(real));
        Assert.NotNull(new DirectoryInfo(zprofile).LinkTarget);
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains(zprofile, StringComparison.Ordinal) && said.GetString()!.Contains($"export PATH=\"{bin}:$PATH\"", StringComparison.Ordinal));
    }

    // --- the verb itself -----------------------------------------------------------------------------------

    [Fact]
    public async Task Its_help_states_the_contract_its_callers_are_written_against()
    {
        var output = new StringWriter();
        using var paths = new TempPaths();

        await CliApp.RunAsync(["install", "--help"], new CliEnvironment(output, new StringWriter(), paths.Paths), Ct);

        var help = output.ToString();
        foreach (var phrase in new[] { "--roles", "--retire", "install_not_carried", "open_shell", "left_alone", "would_not_stop", "Exit codes: 0", "no flags", "# added by jason install", "WindowsApps", "--terminal-path", "the PATH a terminal has", "the empty value" })
        {
            Assert.Contains(phrase, help, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The wrapper is the application's own shape, so its Command Line item reads what this writes as its own: the marker,
    /// the lines and the shapes are held to <c>desktop/src/command-line.ts</c>.
    /// </summary>
    [Fact]
    public void The_command_files_are_the_applications_own_shapes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "desktop", "src", "command-line.ts")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"export const wrapperMarker = '{CommandFiles.WrapperMarker}';", source, StringComparison.Ordinal);
        Assert.Contains("return ['#!/bin/sh', wrapperMarker, `${environment}exec ${shellQuote(executable)} \"$@\"`, ''].join('\\n');", source, StringComparison.Ordinal);
        Assert.Contains("const environment = dataDirectory === null ? '' : `JASON_DATA_DIR=${shellQuote(dataDirectory)} `;", source, StringComparison.Ordinal);
        Assert.Contains("const shellQuote = (value: string) => `'${value.replaceAll(\"'\", `'\\\\''`)}'`;", source, StringComparison.Ordinal);
        Assert.Contains("return ['@echo off', ...environment, `\"${executable}\" %*`, ''].join('\\r\\n');", source, StringComparison.Ordinal);
        Assert.Equal(("/A/it's/jason", (string?)"/d"), CommandFiles.WrapperTarget(CommandFiles.Wrapper("/A/it's/jason", "/d")));
        Assert.Equal(@"C:\J\jason.exe", CommandFiles.ShimTarget("@echo off\r\nsetlocal\r\nset \"JASON_DATA_DIR=C:\\d\"\r\n\"C:\\J\\jason.exe\" %*\r\n"));
    }

    // --- the fixture ---------------------------------------------------------------------------------------

    /// <summary>A carried folder of the test's own: the executable, its version.json, and the skills beside it.</summary>
    private string Carried(string name = "jason")
    {
        var bundle = Path.Combine(_tree.Paths.Root, "Jason.app", "Contents", "Resources", "bundle");
        Directory.CreateDirectory(bundle);
        var executable = Path.GetFullPath(Path.Combine(bundle, name));
        File.WriteAllText(executable, "jason 0.34.0");
        CarriedRecord.Beside(executable);
        foreach (var (file, text) in SkillsPackage.Files("v0.34.0").Where(entry => entry.Name.StartsWith("skills/", StringComparison.Ordinal)))
        {
            var path = Path.Combine(bundle, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return executable;
    }

    private static void DeleteUnlessRunning(string file, string running)
    {
        if (string.Equals(file, running, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("held by a running process");
        }

        File.Delete(file);
    }

    /// <summary>A command-line install outside ~/.local/bin, with the notice that marks it Jason's.</summary>
    private string PlantElsewhere(string name)
    {
        var folder = Path.Combine(_tree.Paths.Root, "opt", "jason");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, name);
        File.WriteAllBytes(old, new byte[200_000]);
        File.WriteAllText(Path.Combine(folder, "jason-LICENSE"), "MIT");
        return old;
    }

    private string PlantMacCommandLineInstall()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var old = Path.Combine(bin, "jason");
        File.WriteAllBytes(old, new byte[200_000]);
        File.WriteAllText(Path.Combine(bin, "jason-LICENSE"), "MIT");
        return old;
    }

    private void PlantHosts(bool claude, bool codex)
    {
        if (claude)
        {
            Directory.CreateDirectory(Path.Combine(Home, ".claude"));
        }

        if (codex)
        {
            Directory.CreateDirectory(Path.Combine(Home, ".codex"));
        }
    }

    private SetupPlaces MacPlaces() => new(SetupSystem.MacOs, Home, null, UserPathValue.EnvironmentKey, "/bin/zsh") { Temporary = _tree.Paths.Root };

    private SetupPlaces WindowsPlaces(string pathKey) => new(SetupSystem.Windows, Home, LocalAppData, pathKey, null) { Temporary = _tree.Paths.Root };

    private (CliEnvironment Env, StringWriter Output) Environment(SetupPlaces? places, string executable)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            _data.Paths,
            HttpHandler: RuntimeVerbs.NeverCalled(),
            Processes: new FakeProcessControl(),
            InstallPath: executable,
            Autostart: new RecordingRegistrar(places?.Platform is SetupSystem.Windows ? AutostartPlatform.Windows : AutostartPlatform.MacOs),
            Harnesses: HarnessLocators.ForHome(Home),
            Variables: new Dictionary<string, string>(),
            SearchPath: "/usr/bin:/bin",
            Setup: places);
        return (env, output);
    }

    private static JsonElement Report(StringWriter output) => JsonDocument.Parse(output.ToString()).RootElement.Clone();

    private static string Snapshot(string root) =>
        string.Join('\n', Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)} {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))} {File.GetLastWriteTimeUtc(file).Ticks}"));

    /// <summary>A key of one test's own under <c>HKCU\Software\jason-tests</c>, never <c>HKCU\Environment</c>.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class ScratchKey : IDisposable
    {
        private const string Parent = @"Software\jason-tests";

        private ScratchKey(string subKey) => SubKey = subKey;

        public string SubKey { get; }

        public static ScratchKey Create()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "An account's Path is a registry value only on Windows.");
            var subKey = $@"{Parent}\{Guid.NewGuid():N}";
            Registry.CurrentUser.CreateSubKey(subKey).Dispose();
            return new ScratchKey(subKey);
        }

        public void SetPath(string raw, RegistryValueKind kind)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue("Path", raw, kind);
        }

        public void SetMulti(string name, string[] values)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue(name, values, RegistryValueKind.MultiString);
        }

        public string[]? ReadMulti(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue(name) as string[];
        }

        /// <summary>This account may no longer set a value on the key, as on a Path a policy locks; it may still delete the key.</summary>
        public void DenySetValue()
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true)!;
            var security = key.GetAccessControl();
            security.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.RegistryRights.SetValue,
                System.Security.AccessControl.AccessControlType.Deny));
            key.SetAccessControl(security);
        }

        public (string Raw, RegistryValueKind Kind)? ReadPath()
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string raw
                ? (raw, key.GetValueKind("Path"))
                : null;
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(SubKey, throwOnMissingSubKey: false);
            try
            {
                Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
            }
            catch (InvalidOperationException)
            {
                // Another test's key is under it still.
            }
        }
    }
}
