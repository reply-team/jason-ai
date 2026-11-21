using System.Net;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Installation;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// Set-up beside Homebrew's older formula, linked in a Homebrew prefix: on macOS and on Linux it is an older Jason, which
/// set-up names with what takes it off and leaves as it is, and a runtime started from it is stopped as a retired
/// install's runtime is, its keg's files never touched.
/// </summary>
public sealed partial class InstallCommandTests
{
    private readonly ManagedLayouts _brew = new();

    private (CliEnvironment Env, StringWriter Output, HomebrewLayout Keg) FormulaBeside(SetupPlaces places, string executable)
    {
        var keg = _brew.LinkedFormula();
        Assert.SkipWhen(keg is null, ManagedLayouts.NoLinkPrivilege);
        var (env, output) = Environment(places, executable, new Dictionary<string, string> { [HomebrewCask.PrefixVariable] = keg.Prefix });
        return (env with { Managers = new PlantedRecords() }, output, keg);
    }

    [Theory]
    [InlineData("macos")]
    [InlineData("linux")]
    public async Task A_linked_formula_behind_the_wrapper_is_named_with_brew_uninstall_formula_and_left(string system)
    {
        var places = system == "macos" ? MacPlaces() : LinuxPlaces();
        var executable = system == "macos" ? Carried() : Carried(under: Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "appimage-copy")).FullName, linux: "appimage");
        var (env, _, keg) = FormulaBeside(places, executable);
        var formula = HomebrewFormula.Linked(env)!;

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, places, new InstallOptions(false, false, null), Ct);

        Assert.Contains(formula.Problem(), report.Problems);
        Assert.Single(report.Problems, problem => problem.Contains("brew uninstall --formula jason", StringComparison.Ordinal));
        Assert.Equal(MachineSetup.RuntimeStates.NotRunning, report.Runtime);
        Assert.True(File.Exists(keg.Executable), "the keg's jason was touched");
        Assert.True(File.Exists(Path.Combine(keg.Keg, "INSTALL_RECEIPT.json")), "the keg's receipt was touched");
        Assert.NotNull(new FileInfo(keg.Link).LinkTarget);
    }

    /// <summary>
    /// A logon start registered in the formula's time names the formula's link or its keg's jason: set-up registers it again
    /// naming the carried jason and says so, on macOS without loading it, since a load would start a runtime. One naming
    /// anything else, or for another data directory, is left.
    /// </summary>
    [Theory]
    [InlineData("macos", "link")]
    [InlineData("linux", "link")]
    [InlineData("macos", "keg")]
    [InlineData("linux", "keg")]
    [InlineData("macos", "opt")]
    [InlineData("macos", "older keg")]
    [Critical(Family.I4, "a logon start the formula registered moves to the carried jason")]
    public async Task A_logon_start_naming_the_formula_moves_to_the_carried_jason(string system, string names)
    {
        var places = system == "macos" ? MacPlaces() : LinuxPlaces();
        var executable = system == "macos" ? Carried() : Carried(under: Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "appimage-copy")).FullName, linux: "appimage");
        var (env, _, keg) = FormulaBeside(places, executable);
        var (named, version) = names switch
        {
            "link" => (keg.Link, HomebrewFormula.Linked(env)!.Version),
            "keg" => (keg.Executable, HomebrewFormula.Linked(env)!.Version),
            "opt" => (Path.Combine(keg.Prefix, "opt", "jason", "bin", "jason"), HomebrewFormula.Linked(env)!.Version),
            _ => (Path.Combine(keg.Prefix, "Cellar", "jason", "0.40.0", "bin", "jason"), "0.40.0"),
        };
        var registrar = new RecordingRegistrar(system == "macos" ? AutostartPlatform.MacOs : AutostartPlatform.Linux) { State = new AutostartState(true, [named, "runtime", "run", "--detached"], null) };
        env = env with { Autostart = registrar };

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, places, new InstallOptions(false, false, null, TerminalPath: "/usr/bin:/bin"), Ct);

        var applied = Assert.Single(registrar.Applied);
        Assert.Equal(executable, applied.Run[0]);
        if (system == "macos")
        {
            Assert.Empty(applied.Apply);
        }
        else
        {
            // daemon-reload, then enable: the unit is registered again, and nothing is started.
            Assert.Equal(["--user daemon-reload", $"--user enable {AutostartArtifacts.UnitName}"], applied.Apply.Select(command => string.Join(' ', command.Skip(1))));
        }

        var moved = Assert.Single(report.Done, line => line.StartsWith("Registered the runtime's logon start again", StringComparison.Ordinal));
        Assert.StartsWith($"Registered the runtime's logon start again, naming {executable}: it named Homebrew's formula jason {version} at {Path.GetFullPath(named)}, which would have started that older Jason at every logon", moved, StringComparison.Ordinal);
        Assert.True(File.Exists(keg.Executable), "the keg's jason was touched");
        Assert.NotNull(new FileInfo(keg.Link).LinkTarget);
    }

    [Theory]
    [InlineData("another jason")]
    [InlineData("another data directory")]
    public async Task A_logon_start_naming_neither_the_formula_nor_this_data_directory_is_left_beside_it(string what)
    {
        var executable = Carried();
        var (env, _, keg) = FormulaBeside(MacPlaces(), executable);
        string[] run = what == "another jason"
            ? [Path.Combine(_tree.Paths.Root, "elsewhere", "jason"), "runtime", "run"]
            : [keg.Link, "runtime", "run", "--data-dir", Path.Combine(_tree.Paths.Root, "other")];
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, run, null) };
        env = env with { Autostart = registrar };

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, MacPlaces(), new InstallOptions(false, false, null), Ct);

        Assert.Empty(registrar.Applied);
        Assert.DoesNotContain(report.Done, line => line.Contains("it named Homebrew's formula", StringComparison.Ordinal));
        if (what == "another data directory")
        {
            Assert.Contains($"The logon registration names {Path.GetFullPath(keg.Link)} with the data directory {Path.Combine(_tree.Paths.Root, "other")}, not this one, so it was left as it is.", report.Left);
        }
    }

    /// <summary>Windows has no Homebrew: a registration that names a formula's path there is not this step's, and nothing is applied.</summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task On_windows_a_logon_start_naming_a_formula_path_is_not_moved()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Set-up on Windows writes the Windows Path, which is Windows's.");
        var places = WindowsPlaces(NoKey) with { AccountPath = new AccountPathInMemory(null) };
        var executable = Carried("jason.exe");
        var (env, _, keg) = FormulaBeside(places, executable);
        var registrar = new RecordingRegistrar(AutostartPlatform.Windows) { State = new AutostartState(true, [keg.Link, "runtime", "run"], null) };
        env = env with { Autostart = registrar };

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, places, new InstallOptions(false, false, null), Ct);

        Assert.Empty(registrar.Applied);
        Assert.DoesNotContain(report.Done, line => line.Contains("it named Homebrew's formula", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_formula_a_logon_start_naming_a_prefix_s_jason_is_not_moved()
    {
        var executable = Carried();
        var prefix = Directory.CreateDirectory(Path.Combine(_brew.Root, "no-formula")).FullName;
        var (env, _) = Environment(MacPlaces(), executable, new Dictionary<string, string> { [HomebrewCask.PrefixVariable] = prefix });
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [Path.Combine(prefix, "bin", "jason"), "runtime", "run"], null) };
        env = env with { Autostart = registrar, Managers = new PlantedRecords() };

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, MacPlaces(), new InstallOptions(false, false, null), Ct);

        Assert.Empty(registrar.Applied);
        Assert.DoesNotContain(report.Done, line => line.Contains("it named Homebrew's formula", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_formula_set_up_says_nothing_of_Homebrew()
    {
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable, new Dictionary<string, string> { [HomebrewCask.PrefixVariable] = _brew.Root });

        var report = await MachineSetup.RunAsync(env with { Managers = new PlantedRecords() }, CarriedExecutable.Of(executable)!, MacPlaces(), new InstallOptions(false, false, null), Ct);

        Assert.DoesNotContain(report.Problems, problem => problem.Contains("Homebrew", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Critical(Family.I4, "set-up stops a runtime started from the formula's keg and leaves the keg")]
    public async Task A_runtime_started_from_the_formulas_keg_is_stopped_as_a_retired_installs_is_and_the_keg_is_left(bool commandLineInstallToo)
    {
        var old = commandLineInstallToo ? PlantMacCommandLineInstall() : null;
        var executable = Carried();
        var (env, _, keg) = FormulaBeside(MacPlaces(), executable);
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_KEG") with { InstalledImage = keg.Executable });
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
                    return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_KEG"));
                default:
                    return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_KEG"));
            }
        });
        env = env with { HttpHandler = handler, Processes = processes };

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, MacPlaces(), new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(MachineSetup.RuntimeStates.Stopped, report.Runtime);
        Assert.Contains(Operations.SystemShutdown, asked);
        Assert.Contains($"Stopped the runtime that ran from {Path.GetFullPath(keg.Executable)}.", report.Done);
        Assert.Empty(processes.Launches);
        Assert.True(File.Exists(keg.Executable), "the keg's jason was touched");
        Assert.Contains(HomebrewFormula.Linked(env)!.Problem(), report.Problems);
        if (old is not null)
        {
            Assert.DoesNotContain(report.Left, line => line.Contains("so it was left running", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_runtime_started_from_elsewhere_is_left_running_beside_the_formula()
    {
        var executable = Carried();
        var (env, _, _) = FormulaBeside(MacPlaces(), executable);
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_ELSE") with { InstalledImage = executable });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var asked = new List<string>();
        env = env with
        {
            Processes = processes,
            HttpHandler = new FakeHandler(request =>
            {
                asked.Add(request.RequestUri!.AbsolutePath.Split('/')[^1]);
                return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_ELSE"));
            }),
        };

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, MacPlaces(), new InstallOptions(false, false, null), Ct);

        Assert.Equal(MachineSetup.RuntimeStates.Running, report.Runtime);
        Assert.DoesNotContain(Operations.SystemShutdown, asked);
    }
}
