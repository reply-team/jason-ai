using System.Text.Json;
using Jason.Cli.Installation;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The application Homebrew's cask placed, met by the carried <c>jason uninstall</c>: Homebrew's record says whose it is,
/// so the application, its link and its own state under <c>~/Library</c> are left to Homebrew, and the report names the
/// command that takes them off. Everything Jason set up goes as it always does.
/// </summary>
public sealed partial class UninstallCarriedTests
{
    /// <summary>A Homebrew prefix whose cask record names the folder the test's application is in, with the cask's link in it.</summary>
    private string CaskPrefix(string application)
    {
        var prefix = Path.Combine(_tree.Paths.Root, "homebrew");
        var metadata = Path.Combine(prefix, "Caskroom", "jason", ".metadata");
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "INSTALL_RECEIPT.json"), ManagedLayouts.CaskReceipt);
        File.WriteAllText(Path.Combine(metadata, "config.json"), JsonSerializer.Serialize(new { @explicit = new { appdir = Path.GetDirectoryName(application) } }));
        Directory.CreateDirectory(Path.Combine(prefix, "bin"));
        File.WriteAllText(HomebrewCask.Link(prefix), "the cask's link, standing as a file");
        return prefix;
    }

    private (CliEnvironment Env, StringWriter Output, RecordingRemover Remover, string Application, string Prefix) CaskEnvironment()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var prefix = CaskPrefix(application);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        env = env with { Managers = new PlantedRecords(), Variables = new Dictionary<string, string> { [HomebrewCask.PrefixVariable] = prefix } };
        return (env, output, remover, application, prefix);
    }

    [Fact]
    public async Task On_macOS_an_application_Homebrew_placed_is_left_to_it_and_brew_uninstall_zap_is_named()
    {
        var (env, output, remover, application, prefix) = CaskEnvironment();
        var executable = Path.Combine(application, "Contents", "Resources", "bundle", "jason");
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), CommandFiles.Wrapper(executable, null));
        var zprofile = Path.Combine(Home, ".zprofile");
        File.WriteAllText(zprofile, "export EDITOR=vi\n");
        PathEntry.AddToProfile(zprofile, bin);
        var agent = DesktopRemoval.LaunchAgentFile(MacPlaces());
        Directory.CreateDirectory(Path.GetDirectoryName(agent)!);
        File.WriteAllText(agent, $"<plist><dict><key>ProgramArguments</key>\n<array>\n<string>{Path.Combine(application, "Contents", "MacOS", "Jason")}</string>\n<string>--opened-at-login</string></array></dict></plist>");
        var state = DesktopRemoval.ApplicationStatePlaces(Home).Select(Plant).ToList();
        StartOwed.Write(_dir.Paths, DateTimeOffset.UtcNow);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        var desktop = report.GetProperty("plan").GetProperty("desktop");
        Assert.Equal(ApplicationFates.LeftToHomebrew, desktop.GetProperty("application_fate").GetString());
        Assert.Empty(desktop.GetProperty("application_state").EnumerateArray());
        Assert.True(Directory.Exists(application), "the application Homebrew placed was deleted");
        Assert.True(File.Exists(HomebrewCask.Link(prefix)), "the cask's link was touched");
        Assert.All(state, place => Assert.True(Directory.Exists(place), $"{place} went, which the zap's trash takes"));
        Assert.DoesNotContain($"tree.remove {application}", remover.Calls);
        Assert.False(File.Exists(Path.Combine(bin, "jason")), "the wrapper is still there");
        Assert.Equal("export EDITOR=vi\n", File.ReadAllText(zprofile));
        Assert.False(File.Exists(agent), "Open Jason at Login is still on");
        Assert.DoesNotContain($"launchagent.unload {agent}", remover.Calls);
        Assert.False(File.Exists(_dir.Paths.StartOwedFile), "the note that a start is owed is still there");
        Assert.Contains(
            report.GetProperty("kept").EnumerateArray(),
            line => line.GetString() == $"The application at {application} was placed by Homebrew and is left to it, with its link {HomebrewCask.Link(prefix)} and its state under ~/Library: `brew uninstall --zap --cask jason` takes them off.");
    }

    [Fact]
    public async Task With_keep_application_an_application_Homebrew_placed_is_kept_as_the_zap_that_asked_removes_it_next()
    {
        var (env, output, _, application, _) = CaskEnvironment();

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false, KeepApplication: true), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal(ApplicationFates.Kept, JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.True(Directory.Exists(application), "the application was deleted");
    }

    [Fact]
    public async Task With_purge_data_the_data_goes_and_the_application_Homebrew_placed_stays()
    {
        var (env, output, _, application, _) = CaskEnvironment();

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, PurgeData: true, Yes: true, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal(ApplicationFates.LeftToHomebrew, JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.False(Directory.Exists(_dir.Paths.Root), "the data directory is still there");
        Assert.True(Directory.Exists(application), "the application Homebrew placed was deleted");
    }

    [Fact]
    public async Task A_dry_run_says_the_application_is_left_to_Homebrew()
    {
        var (env, output, remover, application, _) = CaskEnvironment();

        var json = await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct);
        var plan = output.ToString();
        output.GetStringBuilder().Clear();
        var human = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: true, DryRun: true, false, false, false), Ct);

        Assert.Equal(ExitCodes.Success, json);
        Assert.Equal(ExitCodes.Success, human);
        Assert.Equal(ApplicationFates.LeftToHomebrew, JsonDocument.Parse(plan).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.Contains($"  NOT touched: the application, {application}, which Homebrew placed, with its link and its state under ~/Library: brew uninstall --zap --cask jason takes them off", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task Where_the_environment_names_no_records_the_application_is_deleted_as_before()
    {
        var (env, output, _, application, _) = CaskEnvironment();

        var exit = await UninstallCommand.RunAsync(env with { Managers = null }, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal(ApplicationFates.Deleted, JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.False(Directory.Exists(application), "the application is still there");
    }
}
