using Jason.Cli.Status;
using Jason.Cli.Tests.Installation;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The <c>path</c> check on an installation a package manager owns. The manager puts <c>jason</c> on the PATH — Homebrew's
/// link in its prefix, winget's link or its package directory — so reaching it that way is ok, and the repair that writes
/// a PATH entry is never offered: the entry is the manager's.
/// </summary>
/// <remarks>
/// The winget facts rest on the record of a real winget install that <see cref="ManagedLayouts"/> states; the Homebrew
/// facts on a planted keg.
/// </remarks>
public sealed class PathManagedTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();
    private readonly TempPaths _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _dir.Dispose();
        _layouts.Dispose();
    }

    /// <summary>
    /// <c>jason</c> resolves to Homebrew's link while the file answering is the keg: one installation, reached through the
    /// manager's link — never "two installations on this machine".
    /// </summary>
    [Fact]
    public async Task Jason_reached_through_homebrews_link_is_ok()
    {
        var brew = _layouts.Homebrew();
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: brew.Executable) with
        {
            SearchPath = Path.GetDirectoryName(brew.Link),
            Managers = new PlantedRecords(),
        };

        await CliApp.RunAsync(["status"], env, Ct);

        var path = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Ok, path.State);
        Assert.Contains("through Homebrew's link", path.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("two installations", path.Fact, StringComparison.Ordinal);
        Assert.Null(path.Fix);
    }

    /// <summary>Where winget puts its package directory on the PATH rather than a link, reaching it there is ok as well.</summary>
    [Fact]
    public async Task Jason_reached_through_wingets_package_directory_is_ok()
    {
        var winget = _layouts.Winget();
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: winget.Executable) with
        {
            SearchPath = winget.Package,
            Managers = new PlantedRecords(winget.Registration()),
        };

        await CliApp.RunAsync(["status"], env, Ct);

        var path = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Ok, path.State);
        Assert.Contains("through winget's directory", path.Fact, StringComparison.Ordinal);
        Assert.Null(path.Fix);
    }

    /// <summary>
    /// And through winget's link, where there is one. A symbolic link needs a privilege an ordinary Windows account does not
    /// hold, so there this is skipped; elsewhere, and on a runner, it runs.
    /// </summary>
    [Fact]
    public async Task Jason_reached_through_wingets_link_is_ok()
    {
        var winget = _layouts.Winget();
        if (!ManagedLayouts.TryLink(winget.Link, winget.Executable))
        {
            Assert.Skip(ManagedLayouts.NoLinkPrivilege);
        }

        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: winget.Executable) with
        {
            SearchPath = Path.GetDirectoryName(winget.Link),
            Managers = new PlantedRecords(winget.Registration()),
        };

        await CliApp.RunAsync(["status"], env, Ct);

        var path = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Ok, path.State);
        Assert.Contains("through winget's link", path.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Not on the PATH at all: still a required failure, because nothing can type <c>jason</c> — but with no repair. The
    /// entry that puts it there is the manager's to make, and a line that wrote one into a profile or this account's Path
    /// would be a second entry the manager knows nothing about.
    /// </summary>
    [Theory]
    [InlineData("Homebrew")]
    [InlineData("winget")]
    public async Task A_managed_installation_off_the_path_is_never_offered_a_path_entry(string manager)
    {
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, onPath: false);
        if (manager == "Homebrew")
        {
            env = env with { InstallPath = _layouts.Homebrew().Executable, Managers = new PlantedRecords() };
        }
        else
        {
            var winget = _layouts.Winget();
            env = env with { InstallPath = winget.Executable, Managers = new PlantedRecords(winget.Registration()) };
        }

        await CliApp.RunAsync(["status"], env, Ct);

        var path = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Failed, path.State);
        Assert.True(path.Required);
        Assert.Null(path.Fix);
        Assert.Contains(manager, path.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Homebrew's older formula linked beside the jason Jason Desktop carries: the check passes, since a terminal finds this
    /// jason, and carries the sentence that names the formula and what takes it off as its detail, which <c>--human</c>
    /// prints below the table. Without the formula there is no detail.
    /// </summary>
    [Fact]
    [Critical(Family.I4, "status names the linked formula beside the carried jason")]
    public async Task A_linked_formula_beside_the_carried_jason_is_a_detail_of_the_passing_path_check()
    {
        var keg = _layouts.LinkedFormula();
        Assert.SkipWhen(keg is null, ManagedLayouts.NoLinkPrivilege);
        var bundle = Directory.CreateDirectory(Path.Combine(_dir.Paths.Root, "Jason.app", "Contents", "Resources", "bundle")).FullName;
        var carried = Path.Combine(bundle, Jason.Contracts.Update.ReleaseAssets.ExecutableName);
        File.WriteAllText(carried, string.Empty);
        CarriedRecord.Beside(carried);
        var variables = new Dictionary<string, string> { [Jason.Cli.Installation.HomebrewCask.PrefixVariable] = keg.Prefix };
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: carried) with { SearchPath = bundle, Managers = new PlantedRecords(), Variables = variables };
        var sentence = Jason.Cli.Installation.HomebrewFormula.Linked(env)!.Sentence();

        await CliApp.RunAsync(["status"], env, Ct);

        var path = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Ok, path.State);
        Assert.Equal(sentence, path.Details![Jason.Cli.Installation.HomebrewFormula.DetailName]);
        Assert.Contains("brew uninstall --formula jason", sentence, StringComparison.Ordinal);

        output = new StringWriter();
        await CliApp.RunAsync(["status", "--human"], env with { Out = output }, Ct);
        Assert.Contains(sentence, output.ToString(), StringComparison.Ordinal);

        output = new StringWriter();
        await CliApp.RunAsync(["status"], env with { Out = output, Managers = null }, Ct);
        Assert.Null(Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path").Details);
    }

    /// <summary>Where the formula's link is what a terminal finds first, the failed check names the formula's own uninstall.</summary>
    [Fact]
    public async Task A_formula_found_first_by_name_fails_the_path_check_naming_brew_uninstall_formula()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Homebrew's link is a file named jason, which a Windows shell never runs by name.");
        var keg = _layouts.LinkedFormula();
        Assert.SkipWhen(keg is null, ManagedLayouts.NoLinkPrivilege);
        var bundle = Directory.CreateDirectory(Path.Combine(_dir.Paths.Root, "Jason.app", "Contents", "Resources", "bundle")).FullName;
        var carried = Path.Combine(bundle, "jason");
        File.WriteAllText(carried, string.Empty);
        CarriedRecord.Beside(carried);
        var output = new StringWriter();
        var env = StatusCommandTests.Machine(_dir, output, installPath: carried) with
        {
            SearchPath = Path.GetDirectoryName(keg.Link),
            Managers = new PlantedRecords(),
            Variables = new Dictionary<string, string> { [Jason.Cli.Installation.HomebrewCask.PrefixVariable] = keg.Prefix },
        };

        await CliApp.RunAsync(["status"], env, Ct);

        var path = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Failed, path.State);
        Assert.Contains("It is Homebrew's older formula: `jason runtime stop`, then `brew uninstall --formula jason`, takes it off", path.Fact, StringComparison.Ordinal);
    }
}
