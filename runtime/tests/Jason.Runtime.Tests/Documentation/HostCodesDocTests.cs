using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The plugin page names the codes the host writes into a failed outcome itself, and the runtime passes exactly those
/// past an operation's declared codes. The page is held to the one list both read, so a code the host starts writing
/// is a failure here until a plugin author can find it.
/// </summary>
public sealed partial class HostCodesDocTests
{
    [Fact]
    public void The_plugin_page_names_exactly_the_codes_the_host_writes()
    {
        var page = Flat(Source("docs/plugins.md"));
        var sentence = HostSentence().Match(page);
        Assert.True(sentence.Success, "docs/plugins.md no longer has its 'The other codes the host itself writes into a failed outcome … are …' sentence.");

        var listed = Code().Matches(sentence.Groups["list"].Value).Select(match => match.Groups["code"].Value)
            .Append(HostOutcomeCodes.PluginTimeout)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(HostOutcomeCodes.All.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("docs/plugins.md", "the class is what the runtime acts on; the code is held to the operation's list")]
    [InlineData("docs/plugins.md", "Unreachable by construction, not a pattern")]
    [InlineData("docs/plugins.md", "A read made on a person's request outside any work item is held to the list by the same check")]
    [InlineData("docs/plugins.md", "The events family is the one exception: its reads refuse every code the events contract does not declare, the host's included (§15).")]
    [InlineData("docs/plugins.md", "All of them are `permanent`, and every operation's check passes them but the events family's (§15).")]
    [InlineData("docs/plugins.md", "Any other failure, the host's own codes of §8 included, is answered `result_invalid`.")]
    [InlineData("docs/contracts/README.md", "pass under the class the host writes them with ([the plugin guide, section 8](../plugins.md#8-failure-classes)); the events family alone refuses those as well.")]
    public void The_pages_say_where_the_host_s_codes_pass_and_where_they_do_not(string page, string sentence)
    {
        var text = Flat(Source(page));
        Assert.True(text.Contains(sentence, StringComparison.Ordinal), $"{page} no longer says: {sentence}");
    }

    [GeneratedRegex(@"The other codes the host itself writes into a failed outcome — as opposed to your own vocabulary — are (?<list>.+?)\. All of them are `permanent`")]
    private static partial Regex HostSentence();

    [GeneratedRegex("`(?<code>[a-z_]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
