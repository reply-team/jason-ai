using Jason.Contracts.Ui;
using Jason.Runtime.Configuration;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>The page that tells a person how the UI is opened, paired and kept, held to the code it describes.</summary>
public class UiDocTests
{
    private static string Page() => System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "ui.md")), @"\s+", " ");

    [Theory]
    [InlineData("jason ui open")]
    [InlineData("jason ui tokens")]
    [InlineData("jason ui revoke")]
    [InlineData("jason campaign summary")]
    [InlineData("jason campaign activity")]
    [InlineData("jason campaign list-contacts --summary")]
    [InlineData("jason campaign activity-by-period")]
    [InlineData("jason campaign list-contacts --activity")]
    [InlineData("ui/custom")]
    [InlineData("ui/default")]
    [InlineData("Runtime:Port")]
    [InlineData("`" + Jason.Contracts.Notifications.AttentionLinks.List + "` for the list of what waits")]
    [InlineData("`" + Jason.Contracts.Notifications.AttentionLinks.List + "/approvals/<id>` and `" + Jason.Contracts.Notifications.AttentionLinks.List + "/questions/<id>` for one item")]
    [InlineData("the runtime takes only the route's half of the claim, the plugin is loaded and available, it performs the operation, and the route's binding passes its checks, and skips the rest of it, and with it the attempt, provenance, the journal, pins, approval and retry; it has no bound of its own, running outside the handler pool `Dispatcher:MaxParallel` sizes, one child each.")]
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
