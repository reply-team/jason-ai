using Jason.Cli.Autostart;
using Jason.Cli.Tests.Installation;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// <c>jason runtime autostart enable</c> on an installation a package manager owns registers the manager's stable entry:
/// the one path that names this installation across the manager's upgrades. Never the resolved keg, which the next
/// <c>brew upgrade</c> deletes and leaves a registration that fails at every logon without a word.
/// </summary>
/// <remarks>
/// The winget facts rest on the record of a real winget install that <see cref="ManagedLayouts"/> states; the Homebrew
/// facts on a planted keg.
/// </remarks>
public sealed class AutostartManagedTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();
    private readonly TempPaths _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _dir.Dispose();
        _layouts.Dispose();
    }

    [Fact]
    public async Task Enable_registers_homebrews_link_and_never_the_keg()
    {
        var brew = _layouts.Homebrew();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs);

        var exit = await CliApp.RunAsync(["runtime", "autostart", "enable"], Environment(registrar, brew.Executable, new PlantedRecords()), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var registered = Assert.Single(registrar.Applied);
        Assert.Equal(brew.Link, registered.Run[0]);
        Assert.Contains(brew.Link, registered.Artifact, StringComparison.Ordinal);
        Assert.DoesNotContain(brew.Keg, registered.Artifact, StringComparison.Ordinal);
    }

    /// <summary>Without a link, winget's stable entry is the executable in the package's own directory, which an upgrade replaces in place.</summary>
    [Fact]
    public async Task Enable_registers_the_executable_in_wingets_package_directory_where_there_is_no_link()
    {
        var winget = _layouts.Winget();
        var registrar = new RecordingRegistrar(AutostartPlatform.Windows);

        var exit = await CliApp.RunAsync(["runtime", "autostart", "enable"], Environment(registrar, winget.Executable, new PlantedRecords(winget.Registration())), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(winget.Executable, Assert.Single(registrar.Applied).Run[0]);
    }

    /// <summary>
    /// With one, winget's link — whatever path the process was started by. A symbolic link needs a privilege an ordinary
    /// Windows account does not hold, so there this is skipped; elsewhere, and on a runner, it runs.
    /// </summary>
    [Fact]
    public async Task Enable_registers_wingets_link_where_there_is_one()
    {
        var winget = _layouts.Winget();
        if (!ManagedLayouts.TryLink(winget.Link, winget.Executable))
        {
            Assert.Skip(ManagedLayouts.NoLinkPrivilege);
        }

        var registrar = new RecordingRegistrar(AutostartPlatform.Windows);

        var exit = await CliApp.RunAsync(["runtime", "autostart", "enable"], Environment(registrar, winget.Executable, new PlantedRecords(winget.Registration())), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(winget.Link, Assert.Single(registrar.Applied).Run[0]);
    }

    /// <summary>And Jason's own installation registers the file it is installed as, exactly as before.</summary>
    [Fact]
    public async Task Jasons_own_installation_registers_the_file_it_is_installed_as()
    {
        var brew = _layouts.Homebrew(receipt: null);
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs);

        await CliApp.RunAsync(["runtime", "autostart", "enable"], Environment(registrar, brew.Executable, new PlantedRecords()), Ct);

        Assert.Equal(brew.Executable, Assert.Single(registrar.Applied).Run[0]);
    }

    private CliEnvironment Environment(RecordingRegistrar registrar, string installPath, PlantedRecords records) =>
        new(new StringWriter(), new StringWriter(), _dir.Paths, InstallPath: installPath, Autostart: registrar, Managers: records);
}
