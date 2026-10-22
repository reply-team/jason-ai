using Jason.Cli.Installation;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// Who owns this installation: Jason, or the package manager that installed it. The path says where; the record says
/// whose. A path in a manager's layout without that manager's own record is Jason's, and each near miss below is one
/// such path.
/// </summary>
/// <remarks>
/// The winget facts rest on what a real <c>winget install</c> of the release recorded for a portable package, which
/// <see cref="ManagedLayouts"/> states. The Homebrew facts rest on Homebrew's documented receipt and are planted, never
/// read from a real keg.
/// </remarks>
public sealed class PackageOwnerTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();

    public void Dispose() => _layouts.Dispose();

    /// <summary>
    /// A keg whose receipt Homebrew wrote is Homebrew's, and everything a verb needs is named here once: the commands
    /// that upgrade and remove it, and the link in the prefix that a registration names — never the keg, which
    /// <c>brew upgrade</c> deletes.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "Homebrew owns a keg")]
    public void A_keg_carrying_homebrews_receipt_is_homebrews()
    {
        var brew = _layouts.Homebrew();

        var owner = PackageOwner.Of(brew.Executable, launched: null, new PlantedRecords());

        Assert.NotNull(owner);
        Assert.Equal("Homebrew", owner.Manager);
        Assert.Equal("jason", owner.Package);
        Assert.Equal("brew upgrade jason", owner.UpgradeCommand);
        Assert.Equal("brew uninstall jason", owner.UninstallCommand);
        Assert.Equal(brew.Executable, owner.Executable);
        Assert.Equal(Path.GetDirectoryName(brew.Executable), owner.Directory);
        Assert.Equal(brew.Link, owner.Link);
        Assert.Equal(brew.Link, owner.StableEntry);
    }

    /// <summary>
    /// The sequence a person types under a manager is three lines, never one: the manager knows nothing of the runtime
    /// Jason keeps running, and replaces the image its plugin hosts are started from.
    /// </summary>
    [Fact]
    public void The_typed_upgrade_stops_the_runtime_first_and_starts_it_after()
    {
        var owner = PackageOwner.Of(_layouts.Homebrew().Executable, launched: null, new PlantedRecords());

        Assert.NotNull(owner);
        Assert.Equal(["jason runtime stop", "brew upgrade jason", "jason runtime start"], owner.TypedUpgrade());
    }

    /// <summary>A directory merely named <c>Cellar</c>, in the keg's shape and with no receipt, is a path and nothing more.</summary>
    [Fact]
    public void A_directory_merely_named_Cellar_is_jasons_own()
    {
        Assert.Null(PackageOwner.Of(_layouts.MerelyNamedCellar(), launched: null, new PlantedRecords()));
    }

    /// <summary>A whole Homebrew prefix, link and all, whose keg carries no receipt: the record is what says whose.</summary>
    [Fact]
    public void A_keg_without_its_receipt_is_jasons_own()
    {
        var brew = _layouts.Homebrew(receipt: null);

        Assert.Null(PackageOwner.Of(brew.Executable, launched: null, new PlantedRecords()));
    }

    /// <summary>
    /// A keg Homebrew installed, receipt and all, whose executable is not in the keg's <c>bin</c>: Homebrew links what is
    /// in <c>bin</c>, so this file is not the one it installed as <c>jason</c>, and it is Jason's own.
    /// </summary>
    [Fact]
    public void An_executable_outside_the_kegs_bin_is_jasons_own_even_beside_a_receipt()
    {
        var brew = _layouts.Homebrew(within: "libexec");

        Assert.Null(PackageOwner.Of(brew.Executable, launched: null, new PlantedRecords()));
    }

    /// <summary>A receipt that is not JSON is not a record Homebrew wrote.</summary>
    [Fact]
    public void A_receipt_that_is_not_json_is_jasons_own()
    {
        var brew = _layouts.Homebrew(receipt: "homebrew_version: 4.4.0");

        Assert.Null(PackageOwner.Of(brew.Executable, launched: null, new PlantedRecords()));
    }

    /// <summary>JSON that does not name the Homebrew that wrote it is not Homebrew's receipt either.</summary>
    [Theory]
    [InlineData("""{"used_options":[]}""")]
    [InlineData("""["homebrew_version"]""")]
    public void A_receipt_that_does_not_name_homebrew_version_is_jasons_own(string receipt)
    {
        var brew = _layouts.Homebrew(receipt: receipt);

        Assert.Null(PackageOwner.Of(brew.Executable, launched: null, new PlantedRecords()));
    }

    /// <summary>
    /// A package directory winget registered is winget's. Without a link the package directory's own executable is the
    /// stable entry, which is what <c>winget upgrade</c> replaces in place.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "winget owns its package")]
    public void A_package_directory_winget_registered_is_wingets()
    {
        var winget = _layouts.Winget();

        var owner = PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords(winget.Registration()));

        Assert.NotNull(owner);
        Assert.Equal("winget", owner.Manager);
        Assert.Equal("Reply.Jason", owner.Package);
        Assert.Equal("winget upgrade Reply.Jason", owner.UpgradeCommand);
        Assert.Equal("winget uninstall Reply.Jason", owner.UninstallCommand);
        Assert.Equal(winget.Package, owner.Directory);
        Assert.Null(owner.Link);
        Assert.Equal(winget.Executable, owner.StableEntry);
        Assert.Equal(["jason runtime stop", "winget upgrade Reply.Jason", "jason runtime start"], owner.TypedUpgrade());
    }

    /// <summary>
    /// A package from winget's community source, whose names carry the source by winget's rule: the same rule answers it,
    /// since both forms begin with the identifier and an underscore. Only the local manifest's form was recorded.
    /// </summary>
    [Fact]
    public void A_package_from_the_community_source_is_wingets_too()
    {
        var winget = _layouts.Winget(ManagedLayouts.CommunitySource);

        var owner = PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords(winget.Registration()));

        Assert.Equal("winget", owner?.Manager);
    }

    /// <summary>
    /// Across a <c>winget upgrade</c>, as recorded, the entry, its install location and the package directory stay as they
    /// were and the new executable takes the old one's place in that directory: the installation is winget's after the
    /// upgrade, and its stable entry names the same file.
    /// </summary>
    [Fact]
    public void The_stable_entry_names_the_installation_across_a_winget_upgrade()
    {
        var winget = _layouts.Winget();
        var records = new PlantedRecords(winget.Registration());
        var before = PackageOwner.Of(winget.Executable, launched: null, records);

        File.WriteAllText(winget.Executable, "jason 0.9.1");
        var after = PackageOwner.Of(winget.Executable, launched: null, records);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.StableEntry, after.StableEntry);
        Assert.Equal(winget.Executable, after.StableEntry);
    }

    /// <summary>
    /// Started through winget's link, which the operating system reported rather than its target: the link is winget's,
    /// so it is the stable entry, and the rule was asked about the image all the same.
    /// </summary>
    [Fact]
    public void Wingets_link_when_the_process_was_started_through_it_is_the_stable_entry()
    {
        var winget = _layouts.Winget();

        var owner = PackageOwner.Of(winget.Executable, launched: winget.Link, new PlantedRecords(winget.Registration()));

        Assert.NotNull(owner);
        Assert.Equal(winget.Link, owner.Link);
        Assert.Equal(winget.Link, owner.StableEntry);
        Assert.Equal(winget.Executable, owner.Executable);
    }

    /// <summary>
    /// And the same link found on disk when the process was started some other way. A symbolic link needs a privilege an
    /// ordinary Windows account does not hold, so there this is skipped; elsewhere, and on a runner, it runs.
    /// </summary>
    [Fact]
    public void Wingets_link_on_disk_is_the_stable_entry()
    {
        var winget = _layouts.Winget();
        if (!ManagedLayouts.TryLink(winget.Link, winget.Executable))
        {
            Assert.Skip(ManagedLayouts.NoLinkPrivilege);
        }

        var owner = PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords(winget.Registration()));

        Assert.Equal(winget.Link, owner?.StableEntry);
    }

    /// <summary>A package directory nobody registered is winget's layout and nobody's record.</summary>
    [Fact]
    public void A_package_directory_with_no_registration_is_jasons_own()
    {
        var winget = _layouts.Winget();

        Assert.Null(PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords()));
    }

    /// <summary>A registration whose install location is somewhere else does not hold this executable.</summary>
    [Fact]
    public void A_registration_whose_install_location_does_not_hold_this_executable_is_jasons_own()
    {
        var winget = _layouts.Winget();
        var elsewhere = Path.Combine(winget.Root, "Packages", "Reply.Jason_Elsewhere");

        Assert.Null(PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords(winget.Registration(installLocation: elsewhere))));
    }

    /// <summary>An entry with the package's name that winget did not write for this package.</summary>
    [Fact]
    public void A_registration_for_another_package_identifier_is_jasons_own()
    {
        var winget = _layouts.Winget();

        Assert.Null(PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords(winget.Registration(identifier: "Reply.Jason.Preview"))));
    }

    /// <summary>
    /// An entry that carries the package's identifier and an install location holding this executable, under a key winget
    /// does not name this package's entries by: winget names them by the identifier and an underscore, and a key that
    /// merely begins with the identifier's letters is another package's.
    /// </summary>
    [Fact]
    public void A_registration_under_a_key_winget_does_not_name_this_package_by_is_jasons_own()
    {
        var winget = _layouts.Winget();

        Assert.Null(PackageOwner.Of(winget.Executable, launched: null, new PlantedRecords(winget.Registration(key: "Reply.JasonTools__DefaultSource"))));
    }

    /// <summary>
    /// <c>WinGet\Links\jason.exe</c> as a copy rather than a link: the file is in winget's layout, and winget's record
    /// says the package lives in its own directory, which does not hold this file.
    /// </summary>
    [Fact]
    public void A_copy_in_wingets_links_directory_is_jasons_own()
    {
        var winget = _layouts.Winget();
        File.Copy(winget.Executable, winget.Link);

        Assert.Null(PackageOwner.Of(winget.Link, launched: null, new PlantedRecords(winget.Registration())));
    }

    /// <summary>A file that is in no manager's layout is Jason's own, whatever records the machine keeps.</summary>
    [Fact]
    public void An_executable_in_no_managers_layout_is_jasons_own()
    {
        var winget = _layouts.Winget();
        var own = Path.Combine(_layouts.Root, "Programs", "jason", "jason.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "jason 0.9.0");

        Assert.Null(PackageOwner.Of(own, launched: null, new PlantedRecords(winget.Registration(installLocation: Path.GetDirectoryName(own)))));
    }

    /// <summary>
    /// An environment that names no records is one in which no manager owns anything: the answer every test that did not
    /// ask about managers gets, without reading this machine's registry.
    /// </summary>
    [Fact]
    public void An_environment_naming_no_records_owns_every_installation_itself()
    {
        var brew = _layouts.Homebrew();
        using var output = new StringWriter();

        Assert.Null(PackageOwner.Of(new CliEnvironment(output, output, new JasonPaths(_layouts.Root), InstallPath: brew.Executable)));
    }

    /// <summary>The environment names the installation and its records, and the rule is asked about what it names.</summary>
    [Fact]
    public void An_environment_is_asked_about_the_file_it_is_installed_as()
    {
        var brew = _layouts.Homebrew();
        using var output = new StringWriter();

        var owner = PackageOwner.Of(new CliEnvironment(output, output, new JasonPaths(_layouts.Root), InstallPath: brew.Executable, Managers: new PlantedRecords()));

        Assert.Equal("Homebrew", owner?.Manager);
    }

    /// <summary>
    /// Started through a link, the rule is asked about the image the link resolves to, and the link is what the process
    /// was launched by. Skipped where this account may not make a symbolic link.
    /// </summary>
    [Fact]
    public void An_installation_reached_through_a_link_is_asked_about_the_image()
    {
        var winget = _layouts.Winget();
        if (!ManagedLayouts.TryLink(winget.Link, winget.Executable))
        {
            Assert.Skip(ManagedLayouts.NoLinkPrivilege);
        }

        using var output = new StringWriter();
        var owner = PackageOwner.Of(new CliEnvironment(output, output, new JasonPaths(_layouts.Root), InstallPath: winget.Link, Managers: new PlantedRecords(winget.Registration())));

        Assert.NotNull(owner);
        Assert.Equal(winget.Executable, owner.Executable);
        Assert.Equal(winget.Link, owner.StableEntry);
    }

    /// <summary>This machine's environment reads this machine's records; only a test's names none.</summary>
    [Fact]
    public void The_real_environment_reads_this_machines_records()
    {
        Assert.NotNull(CliEnvironment.Default().Managers);
    }
}
