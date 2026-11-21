using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Installation;
using Jason.Cli.Tests.Autostart;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// Set-up's step that registers the runtime's start at logon: where it registers, what it leaves, and that it starts
/// nothing and never fails the run. Against the recording registrar the environment names: nothing here registers anything
/// on the machine.
/// </summary>
public sealed partial class InstallCommandTests
{
    private void PlantInstallation()
    {
        Directory.CreateDirectory(_data.Paths.StateDirectory);
        File.WriteAllBytes(_data.Paths.DatabaseFile, [0]);
    }

    private void PlantDesktopRecord(string logon) =>
        Written(Path.Combine(_data.Paths.Root, "desktop", "machine-setup.json"), "{\"ran_for\":\"0.0.0\",\"ran_from\":\"x\",\"report\":{\"logon\":\"" + logon + "\"}}");

    private static void Written(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private static RecordingRegistrar Registrar(CliEnvironment env) => Assert.IsType<RecordingRegistrar>(env.Autostart);

    /// <summary>A new installation's first start of the application keeps its Demo: no flags and no installation, nothing registered, nothing written.</summary>
    [Fact]
    public async Task With_no_installation_and_no_roles_nothing_is_registered()
    {
        var (env, output) = Environment(MacPlaces(), Carried());

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));

        Assert.Empty(Registrar(env).Applied);
        Assert.Equal("no_installation", Report(output).GetProperty("logon").GetString());
    }

    /// <summary>An install script's run (<c>--roles</c>) on a new data directory registers the start, on every system, and the run still completes.</summary>
    [Theory]
    [InlineData("macos")]
    [InlineData("linux")]
    public async Task An_install_scripts_run_registers_the_start_naming_this_jason(string system)
    {
        var places = system == "linux" ? LinuxPlaces() : MacPlaces();
        var executable = system == "linux" ? Carried(linux: "appimage", under: Path.Combine(_tree.Paths.Root, "bundles", "0.0.0")) : Carried();
        var (env, output) = Environment(places, executable);

        await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct);

        var applied = Assert.Single(Registrar(env).Applied);
        Assert.Equal(executable, applied.Run[0]);
        Assert.Contains(_data.Paths.Root, applied.Run);
        var report = Report(output);
        Assert.Equal("registered", report.GetProperty("logon").GetString());
        Assert.Contains(report.GetProperty("done").EnumerateArray(), said => said.GetString()!.Contains("Nothing is started now: the runtime runs from the next logon, from Jason Desktop, or from `jason runtime start`.", StringComparison.Ordinal));
    }

    /// <summary>On macOS the LaunchAgent is written and not loaded: a load would start a runtime now (RunAtLoad), and set-up starts none.</summary>
    [Fact]
    public async Task On_macos_the_agent_is_written_and_not_loaded()
    {
        var (env, _) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct);

        var applied = Assert.Single(Registrar(env).Applied);
        Assert.Empty(applied.Apply);
        Assert.Empty(applied.Remove);
    }

    /// <summary>The search path the terminal's login shell gave is the one the registration carries.</summary>
    [Fact]
    public async Task The_registration_carries_the_terminal_path_set_up_was_told()
    {
        var (env, _) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, true, null, TerminalPath: "/opt/homebrew/bin:/usr/bin:/bin"), Ct);

        Assert.Equal("/opt/homebrew/bin:/usr/bin:/bin", Assert.Single(Registrar(env).Applied).SearchPath);
    }

    /// <summary>
    /// The desktop's second run, after it brought Live up on a new installation: its record of the first run says it found
    /// none, and this run registers.
    /// </summary>
    [Fact]
    public async Task The_desktops_run_after_live_is_brought_up_registers()
    {
        PlantInstallation();
        PlantDesktopRecord("no_installation");
        var (env, output) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Single(Registrar(env).Applied);
        Assert.Equal("registered", Report(output).GetProperty("logon").GetString());
    }

    /// <summary>The person's turn-off is left as it is, on every system, and said.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_turn_off_is_left_off(bool launchdRecord)
    {
        var (env, output) = Environment(MacPlaces(), Carried());
        if (launchdRecord)
        {
            Registrar(env).OffRecorded = true;
        }
        else
        {
            AutostartOff.Write(_data.Paths, DateTimeOffset.UtcNow, "0.49.3");
        }

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct));

        Assert.Empty(Registrar(env).Applied);
        var report = Report(output);
        Assert.Equal("off", report.GetProperty("logon").GetString());
        Assert.Contains(report.GetProperty("left").EnumerateArray(), said => said.GetString()!.Contains("jason runtime autostart disable", StringComparison.Ordinal));
    }

    /// <summary>A registration naming this jason is left as it is; one naming another program is left and said.</summary>
    [Theory]
    [InlineData(true, "already")]
    [InlineData(false, "left")]
    public async Task A_standing_registration_is_left(bool ours, string logon)
    {
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        var named = ours ? executable : "/usr/local/bin/other-jason";
        Registrar(env).State = new AutostartState(true, [named, "runtime", "run", "--detached", "--data-dir", _data.Paths.Root], null);

        await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct);

        Assert.Empty(Registrar(env).Applied);
        var report = Report(output);
        Assert.Equal(logon, report.GetProperty("logon").GetString());
        Assert.Equal(!ours, report.GetProperty("left").EnumerateArray().Any(said => said.GetString()!.Contains(named, StringComparison.Ordinal)));
    }

    /// <summary>A system that refuses is said and is never a problem: the install scripts end 0 on the run's own outcome.</summary>
    [Fact]
    public async Task A_refusal_is_said_and_never_a_problem()
    {
        var (env, output) = Environment(LinuxPlaces(), Carried(linux: "appimage", under: Path.Combine(_tree.Paths.Root, "bundles", "0.0.0")));
        Registrar(env).Refuses = new AutostartException("systemctl --user could not reach the user manager.");

        await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct);

        var report = Report(output);
        Assert.Equal("refused", report.GetProperty("logon").GetString());
        Assert.DoesNotContain(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("logon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.GetProperty("left").EnumerateArray(), said => said.GetString()!.Contains("could not reach the user manager", StringComparison.Ordinal));
    }
    /// <summary>
    /// An update of an installation set up before this default: its first set-up turns the start on once, from Jason
    /// Desktop's start (no flags) and from a terminal's (<c>--roles</c>) alike, and records that in
    /// <c>autostart/default.json</c>.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_update_turns_the_start_on_once_and_records_it(bool roles)
    {
        PlantInstallation();
        var (env, output) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, roles, null), Ct);

        Assert.Single(Registrar(env).Applied);
        Assert.Equal("registered", Report(output).GetProperty("logon").GetString());
        using var record = JsonDocument.Parse(File.ReadAllText(_data.Paths.AutostartDefaultFile));
        Assert.False(string.IsNullOrEmpty(record.RootElement.GetProperty("turned_on_by").GetString()));
    }

    /// <summary>Once is once: a start the default turned on and that is gone now was taken off by other means, and is left off.</summary>
    [Fact]
    public async Task A_start_turned_on_once_and_gone_is_not_turned_on_again()
    {
        PlantInstallation();
        AutostartOff.WriteDefault(_data.Paths, DateTimeOffset.UtcNow, "0.0.0");
        var (env, output) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Empty(Registrar(env).Applied);
        var report = Report(output);
        Assert.Equal("left", report.GetProperty("logon").GetString());
        Assert.Contains(report.GetProperty("left").EnumerateArray(), said => said.GetString()!.Contains("turned on once already", StringComparison.Ordinal));
    }

    /// <summary>
    /// Once is once even where the desktop's record still says it found no installation: a start the default turned on and
    /// that was taken off by hand is not registered again.
    /// </summary>
    [Fact]
    public async Task A_desktop_record_saying_no_installation_does_not_beat_the_once()
    {
        PlantInstallation();
        PlantDesktopRecord("no_installation");
        AutostartOff.WriteDefault(_data.Paths, DateTimeOffset.UtcNow, "0.0.0");
        var (env, output) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Empty(Registrar(env).Applied);
        Assert.Equal("left", Report(output).GetProperty("logon").GetString());
    }

    /// <summary>An update honours the person's turn-off: nothing registered, and no record that the default turned it on.</summary>
    [Fact]
    public async Task An_update_leaves_a_turn_off_and_records_nothing()
    {
        PlantInstallation();
        AutostartOff.Write(_data.Paths, DateTimeOffset.UtcNow, "0.49.3");
        var (env, output) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Empty(Registrar(env).Applied);
        Assert.Equal("off", Report(output).GetProperty("logon").GetString());
        Assert.False(File.Exists(_data.Paths.AutostartDefaultFile));
    }

    /// <summary>A registration the person made already counts as the default's once: the record is written, and nothing is registered again.</summary>
    [Fact]
    public async Task A_standing_registration_of_this_jason_is_recorded_as_the_once()
    {
        PlantInstallation();
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);
        Registrar(env).State = new AutostartState(true, [executable, "runtime", "run", "--detached", "--data-dir", _data.Paths.Root], null);

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Empty(Registrar(env).Applied);
        Assert.True(File.Exists(_data.Paths.AutostartDefaultFile));
    }
}
