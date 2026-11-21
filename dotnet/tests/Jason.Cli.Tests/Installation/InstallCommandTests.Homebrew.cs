using System.Net;
using System.Text.Json;
using Jason.Cli.Installation;
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
