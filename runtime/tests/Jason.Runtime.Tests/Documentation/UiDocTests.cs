using Jason.Contracts.Ui;
using Jason.Runtime.Configuration;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>The page that tells a person how the UI is opened, paired and kept, held to the code it describes.</summary>
public class UiDocTests
{
    private static string Page() => File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "docs", "ui.md"));

    [Theory]
    [InlineData("jason ui open")]
    [InlineData("jason ui tokens")]
    [InlineData("jason ui revoke")]
    [InlineData("ui/custom")]
    [InlineData("ui/default")]
    [InlineData("Runtime:Port")]
    public void The_page_names_it(string text) => Assert.Contains(text, Page(), StringComparison.Ordinal);

    [Fact]
    public void The_page_prints_the_numbers_and_names_from_the_code()
    {
        var page = Page();
        Assert.Contains(RuntimeOptions.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture), page, StringComparison.Ordinal);
        Assert.Contains($"{(int)UiTickets.Lifetime.TotalSeconds} seconds", page, StringComparison.Ordinal);
        Assert.Contains(UiAssetLayout.AssetName, page, StringComparison.Ordinal);
        Assert.Contains(UiFiles.ContentSecurityPolicy, page, StringComparison.Ordinal);
        Assert.Contains("ui_not_installed", page, StringComparison.Ordinal);
    }
}
