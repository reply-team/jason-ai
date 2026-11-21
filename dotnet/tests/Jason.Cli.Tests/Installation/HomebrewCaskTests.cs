using System.Text.RegularExpressions;
using Jason.Cli.Installation;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// Whether Homebrew's cask placed Jason Desktop's application: by the rule every manager is held to, the path says where
/// and the record says whose. And the cask itself, the one template this repository keeps, held to what the uninstall
/// it calls keeps and takes.
/// </summary>
public sealed partial class HomebrewCaskTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();

    private string Application(string folder = "Applications")
    {
        var application = Path.Combine(_layouts.Root, folder, "Jason.app");
        Directory.CreateDirectory(application);
        return application;
    }

    [Fact]
    public void An_application_in_the_folder_the_casks_record_names_is_Homebrews()
    {
        var application = Application();
        var prefix = _layouts.HomebrewCask(Path.GetDirectoryName(application));

        Assert.Equal(prefix, HomebrewCask.Placed(application, prefix, new PlantedRecords()));
    }

    [Fact]
    public void Without_the_casks_record_the_application_is_nobodys_but_Jasons()
    {
        var application = Application();
        var prefix = _layouts.HomebrewCask(Path.GetDirectoryName(application), receipt: null);

        Assert.Null(HomebrewCask.Placed(application, prefix, new PlantedRecords()));
    }

    [Theory]
    [InlineData("""{"used_options":[],"uninstall_artifacts":[{"app":["Jason.app"]}]}""")]
    [InlineData("""["homebrew_version"]""")]
    [InlineData("not json")]
    [InlineData("""{"homebrew_version":"4.4.0","uninstall_artifacts":[{"app":["Other.app"]}]}""")]
    public void A_record_that_does_not_name_Homebrew_or_the_application_is_no_record(string receipt)
    {
        var application = Application();
        var prefix = _layouts.HomebrewCask(Path.GetDirectoryName(application), receipt);

        Assert.Null(HomebrewCask.Placed(application, prefix, new PlantedRecords()));
    }

    [Fact]
    public void A_receipt_that_lists_no_uninstall_artifacts_is_read_by_its_homebrew_version_alone()
    {
        var application = Application();
        var prefix = _layouts.HomebrewCask(Path.GetDirectoryName(application), """{"homebrew_version":"4.4.0"}""");

        Assert.Equal(prefix, HomebrewCask.Placed(application, prefix, new PlantedRecords()));
    }

    [Fact]
    public void An_application_elsewhere_than_the_folder_the_record_names_is_not_Homebrews()
    {
        var recorded = Application();
        var elsewhere = Application("Elsewhere");
        var prefix = _layouts.HomebrewCask(Path.GetDirectoryName(recorded));

        Assert.Null(HomebrewCask.Placed(elsewhere, prefix, new PlantedRecords()));
    }

    [Fact]
    public void With_no_appdir_recorded_the_folder_is_Homebrews_default_Applications()
    {
        var application = Application();
        var prefix = _layouts.HomebrewCask(appDir: null);

        Assert.Null(HomebrewCask.Placed(application, prefix, new PlantedRecords()));
        Assert.Equal("/Applications", HomebrewCask.DefaultAppDir);
    }

    [Fact]
    public void The_prefix_HOMEBREW_PREFIX_names_is_asked_first_then_Homebrews_defaults_once_each()
    {
        var prefix = _layouts.HomebrewCask(appDir: null);

        Assert.Equal([prefix, "/opt/homebrew", "/usr/local", "/home/linuxbrew/.linuxbrew"], HomebrewCask.Prefixes(prefix + Path.DirectorySeparatorChar));
        Assert.Equal(["/opt/homebrew", "/usr/local", "/home/linuxbrew/.linuxbrew"], HomebrewCask.Prefixes("relative/homebrew"));
        Assert.Equal(["/opt/homebrew", "/usr/local", "/home/linuxbrew/.linuxbrew"], HomebrewCask.Prefixes(null));
    }

    [Fact]
    public void An_environment_that_names_no_records_has_no_cask()
    {
        var application = Application();
        var prefix = _layouts.HomebrewCask(Path.GetDirectoryName(application));
        var env = new CliEnvironment(TextWriter.Null, TextWriter.Null, new Jason.Contracts.Discovery.JasonPaths(_layouts.Root), Variables: new Dictionary<string, string> { [HomebrewCask.PrefixVariable] = prefix });

        Assert.Null(HomebrewCask.Placed(application, env));
        Assert.Equal(prefix, HomebrewCask.Placed(application, env with { Managers = new PlantedRecords() }));
    }

    // --- the cask's template --------------------------------------------------------------------------------------

    /// <summary>
    /// The zap trashes exactly the application's own state under <c>~/Library</c> that the carried
    /// <c>jason uninstall --keep-application</c> it calls first keeps; everything else Jason set up that call takes itself.
    /// </summary>
    [Fact]
    public void The_casks_trash_is_the_state_under_Library_that_keep_application_keeps()
    {
        var trash = TrashList().Matches(Between(Template(), "trash:", "]")).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal);
        var kept = DesktopRemoval.ApplicationStatePlaces("~").Select(place => place.Replace('\\', '/')).Order(StringComparer.Ordinal);

        Assert.Equal(kept, trash);
    }

    [Fact]
    public void The_cask_quits_the_application_by_its_identifier_and_calls_the_jason_it_carries()
    {
        var template = Template();
        var carried = "#{appdir}/Jason.app/Contents/Resources/bundle/jason";

        Assert.Contains($"uninstall quit:   \"{DesktopRemoval.Identifier}\"", template, StringComparison.Ordinal);
        Assert.Contains($"binary \"{carried}\"", template, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(template, Regex.Escape($"executable:   \"{carried}\"")));
        Assert.Contains("args:         [\"runtime\", \"stop\", \"--start-owed\"]", template, StringComparison.Ordinal);
        Assert.Contains("args:         [\"uninstall\", \"--keep-application\"]", template, StringComparison.Ordinal);
        Assert.Contains("version \"{{VERSION}}\"", template, StringComparison.Ordinal);
        Assert.Contains("sha256 \"{{SHA256_MACOS_ARM64_ZIP}}\"", template, StringComparison.Ordinal);
        Assert.Contains("url \"https://get.jasonharness.ai/releases/download/v#{version}/jason-desktop-macos-arm64.zip\"", template, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("runtime", "stop", "--start-owed", "--help")]
    [InlineData("uninstall", "--keep-application", "--dry-run", "--help")]
    public async Task What_the_cask_calls_the_carried_jason_with_is_a_command_it_takes(params string[] arguments)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(arguments, new CliEnvironment(output, error, dir.Paths), TestContext.Current.CancellationToken);

        Assert.True(exit == ExitCodes.Success, error.ToString());
    }

    [Fact]
    public void The_caveats_name_the_formulas_uninstall_and_what_a_plain_uninstall_leaves()
    {
        var caveats = Between(Template(), "caveats <<~EOS", "EOS\nend");

        Assert.Contains($"brew uninstall --formula jason", caveats, StringComparison.Ordinal);
        Assert.Equal(HomebrewFormula.UninstallCommand, "brew uninstall --formula jason");
        Assert.Contains("brew uninstall --cask jason takes off the application and its link, and leaves the jason\n    command's wrapper and the start at logon pointing at nothing;", caveats, StringComparison.Ordinal);
        Assert.Contains(DesktopRemoval.HomebrewZap + " takes them off too.", caveats, StringComparison.Ordinal);
        Assert.DoesNotContain("reinstall", Template(), StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[(from + start.Length)..to];
    }

    private static string Template() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "cask", "jason.rb.template")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    [GeneratedRegex("\"(~/Library/[^\"]+)\"")]
    private static partial Regex TrashList();

    public void Dispose() => _layouts.Dispose();
}
