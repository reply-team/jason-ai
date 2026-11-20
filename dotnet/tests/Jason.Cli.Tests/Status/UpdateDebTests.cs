using Jason.Cli.Installation;
using Jason.Cli.Status;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The <c>update</c> check where Jason Desktop is the deb on Linux, whose updates come with the system's: it says when they
/// cannot arrive, because Jason's apt source is turned off (a release upgrade of Ubuntu turns third-party sources off),
/// and when the system installed a version this installation has not been set up for yet. Run against a package folder, a
/// source file and a data directory of the test's own.
/// </summary>
public sealed class UpdateDebTests : IDisposable
{
    private readonly TempPaths _dir = new();
    private readonly TempPaths _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _dir.Dispose();
        _tree.Dispose();
    }

    [Theory]
    [InlineData("on", "0.34.0", null)]
    [InlineData("turned off", "0.34.0", "Jason's apt source, ")]
    [InlineData("gone", "0.34.0", "Jason's apt source, ")]
    [InlineData("on", "0.33.0", "Jason Desktop's package is 0.34.0, and this installation was last set up for 0.33.0")]
    public async Task On_the_deb_the_update_check_says_when_the_system_s_updates_cannot_arrive_or_are_not_finished(string source, string setUpFor, string? said)
    {
        var resources = Path.Combine(_tree.Paths.Root, "opt", "Jason", "resources");
        Directory.CreateDirectory(Path.Combine(resources, "bundle"));
        File.WriteAllText(Path.Combine(resources, "package-type"), "deb\n");
        var executable = Path.Combine(resources, "bundle", "jason");
        File.WriteAllText(executable, "jason");
        CarriedRecord.Beside(executable);
        var sources = Path.Combine(_tree.Paths.Root, "jason-desktop.sources");
        var line = "Types: deb\nURIs: https://get.jasonharness.ai/apt/\nSuites: ./\nSigned-By: /usr/share/keyrings/jason-desktop.gpg\n";
        if (source != "gone")
        {
            File.WriteAllText(sources, source == "on" ? line : line + "Enabled: no\n");
        }

        Directory.CreateDirectory(Path.Combine(_dir.Paths.Root, "desktop"));
        var record = Path.Combine(_dir.Paths.Root, "desktop", "machine-setup.json");
        File.WriteAllText(record, AsTheDesktopsStartWritesIt(setUpFor, executable));

        var update = await UpdateCheck(executable, sources);
        if (said is null)
        {
            Assert.Equal(CheckState.Ok, update.State);
            return;
        }

        Assert.Equal(CheckState.Failed, update.State);
        Assert.False(update.Required);
        Assert.Contains(said, update.Fact, StringComparison.Ordinal);
        Assert.Null(update.Fix);
        if (source != "on")
        {
            Assert.Contains("Software & Updates, Other Software", update.Fact, StringComparison.Ordinal);
            return;
        }

        // The act it names is Jason Desktop's start, or in an open window the item that restarts it or the one that runs its
        // set-up again, each by the label the desktop gives it in the menu it is in; the check passes once a start has written
        // its record for the package's version. `jason update apply --finish` alone writes none, so it is not the repair.
        Assert.Contains("Open Jason Desktop, whose start finishes it", update.Fact, StringComparison.Ordinal);
        Assert.Contains("label: `Restart Jason and Its Runtime to Finish the Update to ${moved}`", DesktopSource("updates.ts"), StringComparison.Ordinal);
        Assert.Contains("Restart Jason and Its Runtime to Finish the Update to 0.34.0", update.Fact, StringComparison.Ordinal);
        Assert.Contains("export const putBackLabel = 'Put Back jason and the Skills…';", DesktopSource("machine-setup.ts"), StringComparison.Ordinal);
        Assert.Contains("Command Line, Put Back jason and the Skills…", update.Fact, StringComparison.Ordinal);
        Assert.Contains("{ label: 'Jason', submenu: jason },", DesktopSource("desk.ts"), StringComparison.Ordinal);
        Assert.Contains("return [{ label: 'Command Line', submenu: items }];", DesktopSource("desk.ts"), StringComparison.Ordinal);
        File.WriteAllText(record, AsTheDesktopsStartWritesIt("0.34.0", executable));
        Assert.Equal(CheckState.Ok, (await UpdateCheck(executable, sources)).State);
    }

    /// <summary>
    /// <c>&lt;data&gt;/desktop/machine-setup.json</c> as Jason Desktop's start writes it once <c>jason install</c> and the
    /// finishing step have both completed, held to the desktop's own source: the place, the writer, and the version it writes
    /// for, read from the <c>version.json</c> beside the jason it runs, the record this check reads the package's version from.
    /// </summary>
    private static string AsTheDesktopsStartWritesIt(string version, string executable)
    {
        var paths = DesktopSource("paths.ts");
        var setup = DesktopSource("machine-setup.ts");
        var main = DesktopSource("main.ts");

        Assert.Contains("const desktop = join(data, 'desktop');", paths, StringComparison.Ordinal);
        Assert.Contains("machineSetup: join(desktop, 'machine-setup.json'),", paths, StringComparison.Ordinal);
        Assert.Contains("writeFileSync(temporary, `${JSON.stringify({ ran_for: version, ran_from: place, report: printed }, null, 2)}\\n`, { mode: 0o600 });", setup, StringComparison.Ordinal);
        Assert.Contains("const completed = routineDone && (finishing === null || finishing.completed);", setup, StringComparison.Ordinal);
        Assert.Contains("writeSetupRecord(record, version, this.#deps.executable, JSON.parse(result.stdout));", setup, StringComparison.Ordinal);
        Assert.Contains("version: bundleVersion(bundle),", main, StringComparison.Ordinal);
        Assert.Contains("JSON.parse(readFileSync(join(bundle, 'version.json'), 'utf8'))", main, StringComparison.Ordinal);
        Assert.Equal("version.json", CarriedExecutable.RecordName);

        return $"{{\n  \"ran_for\": {System.Text.Json.JsonSerializer.Serialize(version)},\n  \"ran_from\": {System.Text.Json.JsonSerializer.Serialize(executable)},\n  \"report\": {{}}\n}}\n";
    }

    private async Task<StatusCheck> UpdateCheck(string executable, string sources)
    {
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: executable) with
        {
            Setup = new SetupPlaces(SetupSystem.Linux, Path.Combine(_tree.Paths.Root, "home"), null, UserPathValue.EnvironmentKey, "/bin/bash") { AptSource = sources },
        };

        await CliApp.RunAsync(["status"], env, Ct);

        return Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "update");
    }

    private static string DesktopSource(string file) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src", file)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
