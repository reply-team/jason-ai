using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Uninstall;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// <c>jason uninstall</c> run as a jason the desktop application carries. It would delete the executable out of the
/// application, so it refuses before anything is read or removed, and names the system's way of removing the application.
/// </summary>
public sealed class UninstallCarriedTests : IDisposable
{
    private readonly TempPaths _dir = new();
    private readonly TempPaths _application = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _dir.Dispose();
        _application.Dispose();
    }

    [Theory]
    [Critical(Family.I3, "the desktop's jason is never deleted out of the application")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task It_refuses_a_carried_executable_before_anything_is_read_or_removed(bool dryRun, bool purgeData)
    {
        var (env, executable, output, registrar, remover) = Carried();

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: dryRun, PurgeData: purgeData, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal("uninstall_carried_by_desktop", refusal.Code);
        Assert.False(refusal.Retryable, "the application carries the same file next time");
        Assert.Contains("`jason uninstall` would delete it out of the application", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(executable, refusal.Message, StringComparison.Ordinal);

        Assert.Empty(remover.Calls);
        Assert.Equal(0, registrar.Removals);
        Assert.True(File.Exists(executable), "the application's executable was removed");
        Assert.True(Directory.Exists(_dir.Paths.Root), "the data directory was removed");
    }

    /// <summary>A person reading it is told the same on stderr, with how this system removes an application.</summary>
    [Fact]
    public async Task A_person_is_told_how_this_system_removes_the_application()
    {
        var (env, _, output, _, remover) = Carried();
        var error = new StringWriter();

        var exit = await UninstallCommand.RunAsync(env with { Error = error }, new UninstallOptions(Human: true, DryRun: false, PurgeData: false, Yes: false, Force: false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(output.ToString());
        var said = error.ToString();
        Assert.Contains("`jason uninstall` would delete it out of the application", said, StringComparison.Ordinal);
        Assert.Contains(
            OperatingSystem.IsWindows() ? "Installed apps" : OperatingSystem.IsMacOS() ? "Jason.app to the Trash" : "AppImage",
            said,
            StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    private (CliEnvironment Env, string Executable, StringWriter Output, RecordingRegistrar Registrar, RecordingRemover Remover) Carried()
    {
        var directory = Path.Combine(_application.Paths.Root, "bundle");
        Directory.CreateDirectory(directory);
        var executable = Path.GetFullPath(Path.Combine(directory, OperatingSystem.IsWindows() ? "jason.exe" : "jason"));
        File.WriteAllText(executable, "jason 0.34.0");
        CarriedRecord.Beside(executable);

        var output = new StringWriter();
        var registrar = new RecordingRegistrar { State = new Jason.Cli.Autostart.AutostartState(true, ["jason", "runtime", "run"], null) };
        var remover = new RecordingRemover { ReallyRemoves = true };
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            _dir.Paths,
            Autostart: registrar,
            Harnesses: HarnessLocators.At(Path.Combine(_dir.Paths.Root, "harness")),
            InstallPath: executable,
            Removes: remover);
        return (env, executable, output, registrar, remover);
    }
}
