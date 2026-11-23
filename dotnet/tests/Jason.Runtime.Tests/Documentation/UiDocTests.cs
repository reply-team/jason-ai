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
    [InlineData("`" + Jason.Contracts.Notifications.AttentionLinks.List + "/approvals/<id>/prompt` and `" + Jason.Contracts.Notifications.AttentionLinks.List + "/questions/<id>/prompt`, the toast's Copy prompt button, put that item's prompt on the clipboard and open nothing")]
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

    /// <summary>
    /// The sealed set is printed as the server sends it and stated as a rule for any build, with what it does not stop in
    /// plain words; the section names no page, since the rule knows none.
    /// </summary>
    [Fact]
    public void The_page_states_the_sealed_rule_as_the_server_has_it()
    {
        var page = Page();
        Assert.Contains($"`Content-Security-Policy: {UiFiles.SealedContentSecurityPolicy}`", page, StringComparison.Ordinal);
        foreach (var (name, value) in UiFiles.HeadersFor(UiAssetLayout.SealedDirectory + "/page.html").Where(header => header.Key is not "Content-Security-Policy"))
        {
            Assert.Contains($"`{name}: {value}`", page, StringComparison.Ordinal);
        }

        Assert.Contains("a `Permissions-Policy` that denies each feature it names to every origin", page, StringComparison.Ordinal);
        Assert.Contains("named exactly `sealed`, in lower case", page, StringComparison.Ordinal);
        Assert.Contains("Only pages live under `sealed/`, files whose names end in `" + UiAssetLayout.SealedPageExtension + "`", page, StringComparison.Ordinal);
        Assert.Contains("Every answer to a GET under `/ui/sealed/`", page, StringComparison.Ordinal);
        Assert.Contains("a link a build places under the UI's folder is followed, and what it leads to is served under the set of the path that asks for it", page, StringComparison.Ordinal);

        Assert.Contains("A build of the UI, the release's or a custom one, may hold under `sealed/` content the UI does not trust", page, StringComparison.Ordinal);
        Assert.Contains("a browser may look up a host name the file mentions before any policy applies", page, StringComparison.Ordinal);
        Assert.Contains("the file can freeze its own tab", page, StringComparison.Ordinal);
        Assert.Contains("it can move its own tab to another address", page, StringComparison.Ordinal);
        Assert.Contains("None of these reaches the installation.", page, StringComparison.Ordinal);
        Assert.Contains("The set sends no `Cross-Origin-Opener-Policy`", page, StringComparison.Ordinal);
        Assert.Contains("Every file is served by the exact name its directory holds", page, StringComparison.Ordinal);

        var start = page.IndexOf("### Files the UI does not trust", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/ui.md has no section on files the UI does not trust");
        var end = page.IndexOf("## ", start + 4, StringComparison.Ordinal);
        var section = page[start..(end < 0 ? page.Length : end)];
        Assert.DoesNotContain("demo", section, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The door for a web interface of one's own is said in one section, and said as the code keeps it: the verbs that pair
    /// and open such a page, the exchange's route, codes and answer, where the build goes, and the sentence the release's
    /// build draws in a tab with none. Breaks if the door changes and the section still describes the old one, or if the
    /// section goes.
    /// </summary>
    [Fact]
    public void The_section_on_a_web_interface_of_ones_own_states_the_door_as_the_code_keeps_it()
    {
        var page = Page();
        var start = page.IndexOf("## A web interface of your own", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/ui.md has no section on a web interface of your own");
        var end = page.IndexOf(" ## ", start + 4, StringComparison.Ordinal);
        var section = page[start..(end < 0 ? page.Length : end)];

        Assert.Contains("`jason ui pair`", section, StringComparison.Ordinal);
        Assert.Contains("`jason ui open --web`", section, StringComparison.Ordinal);
        Assert.Contains("`--web --no-open`", section, StringComparison.Ordinal);
        Assert.Contains($"`{Jason.Cli.CliErrors.CustomUiNotInstalled}`, exit {Jason.Cli.ExitCodes.ApiError}", section, StringComparison.Ordinal);
        Assert.Contains($"`http://127.0.0.1:<port>{UiPaths.Prefix}/#/attention?t=<ticket>`", section, StringComparison.Ordinal);
        Assert.Contains("`<data>/ui/custom/`", section, StringComparison.Ordinal);
        Assert.Contains($"`POST {UiPaths.Door}`", section, StringComparison.Ordinal);
        Assert.Contains($"{(int)UiTickets.Lifetime.TotalSeconds} seconds", section, StringComparison.Ordinal);
        Assert.Contains("`ticket_invalid`", section, StringComparison.Ordinal);
        Assert.Contains("`origin_refused`", section, StringComparison.Ordinal);
        Assert.Contains($"`{Jason.Contracts.Api.Streams.BearerSubProtocolPrefix}<token>`", section, StringComparison.Ordinal);

        var answer = System.Text.Json.JsonSerializer.SerializeToNode(new Jason.Contracts.Api.UiSessionResponse("t", "i", "a", "v", "n"), Jason.Contracts.Json.JasonJson.Options)!.AsObject();
        Assert.All(answer.Select(member => member.Key), member => Assert.Contains($"`{member}`", section, StringComparison.Ordinal));

        var locale = File.ReadAllText(Path.Combine(RepositoryTree.Root(), "ui", "src", "locale", "en.ts"));
        var sentence = System.Text.RegularExpressions.Regex.Match(locale, @"'ownPlace\.message':\s*'(?<words>[^']+)'");
        Assert.True(sentence.Success, "ui/src/locale/en.ts has no ownPlace.message");
        Assert.Contains($"\"{sentence.Groups["words"].Value}\"", section, StringComparison.Ordinal);
    }
}
