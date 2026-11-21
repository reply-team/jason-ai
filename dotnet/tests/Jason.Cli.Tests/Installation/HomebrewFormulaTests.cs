using Jason.Cli.Installation;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// Homebrew's older formula linked beside the jason Jason Desktop carries: a link in a Homebrew prefix's <c>bin</c> to a
/// keg whose receipt says it is Homebrew's. A file there that is not such a link is not the formula.
/// </summary>
public sealed class HomebrewFormulaTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();

    private static CliEnvironment Env(string prefix, IManagerRecords? records) =>
        new(TextWriter.Null, TextWriter.Null, new Jason.Contracts.Discovery.JasonPaths(Path.Combine(prefix, "data")), Managers: records, Variables: new Dictionary<string, string> { [HomebrewCask.PrefixVariable] = prefix });

    [Fact]
    [Critical(Family.I4, "a link into Homebrew's keg is read as the older formula's")]
    public void A_link_to_the_formulas_keg_is_the_formula_linked_and_said_with_what_takes_it_off()
    {
        var keg = _layouts.LinkedFormula();
        Assert.SkipWhen(keg is null, ManagedLayouts.NoLinkPrivilege);

        var formula = HomebrewFormula.Linked(Env(keg.Prefix, new PlantedRecords()));

        Assert.NotNull(formula);
        Assert.Equal(keg.Link, formula.Link);
        Assert.Equal("0.48.7", formula.Version);
        Assert.Equal(Path.GetFullPath(keg.Executable), formula.Owner.Executable);
        Assert.Equal("brew uninstall --formula jason", formula.Owner.UninstallCommand);
        Assert.Equal(
            $"Homebrew's formula jason 0.48.7 is linked at {keg.Link} beside this jason: it is an older Jason, and while it is linked Homebrew does not link the cask's jason. `jason runtime stop`, then `brew uninstall --formula jason`, takes it off. It was left as it is.",
            formula.Problem());
    }

    [Fact]
    public void A_keg_without_Homebrews_receipt_is_not_the_formula()
    {
        var keg = _layouts.Homebrew(receipt: null, withLink: false);
        var link = Path.Combine(keg.Prefix, "bin", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Assert.SkipUnless(ManagedLayouts.TryLink(link, keg.Executable), ManagedLayouts.NoLinkPrivilege);

        Assert.Null(HomebrewFormula.Linked(Env(keg.Prefix, new PlantedRecords())));
    }

    [Fact]
    public void A_file_where_the_link_would_be_is_not_the_formula()
    {
        var keg = _layouts.Homebrew(withLink: false);
        var file = Path.Combine(keg.Prefix, "bin", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "a jason of somebody's own");

        Assert.Null(HomebrewFormula.Linked(Env(keg.Prefix, new PlantedRecords())));
    }

    [Fact]
    public void An_environment_that_names_no_records_has_no_formula()
    {
        var keg = _layouts.LinkedFormula();
        Assert.SkipWhen(keg is null, ManagedLayouts.NoLinkPrivilege);

        Assert.Null(HomebrewFormula.Linked(Env(keg.Prefix, records: null)));
    }

    public void Dispose() => _layouts.Dispose();
}
