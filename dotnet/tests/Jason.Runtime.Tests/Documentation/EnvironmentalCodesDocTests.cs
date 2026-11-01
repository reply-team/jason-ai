using System.Text.RegularExpressions;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The environmental problem codes — the ones that hold back only their own plugin — are named in two places a reader
/// trusts: the plugin page lists them, and the repository's working notes count them. Both are held to the runtime's own
/// set, so a code added there is a failure here until both pages name it.
/// </summary>
public sealed partial class EnvironmentalCodesDocTests
{
    [Fact]
    public void The_plugin_page_lists_exactly_the_environmental_codes()
    {
        var page = Flat(Source("docs/plugins.md"));
        var sentence = EnvironmentalSentence().Match(page);
        Assert.True(sentence.Success, "docs/plugins.md no longer has its '**Environmental problems** — … — are about the machine' sentence.");

        var listed = Code().Matches(sentence.Groups["list"].Value).Select(match => match.Groups["code"].Value).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ProblemCodes.Environmental.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_working_notes_count_the_executable_codes_and_name_the_rest()
    {
        var notes = Flat(Source("CLAUDE.md"));
        var executables = ProblemCodes.Environmental.Count(code => code.StartsWith("executable_", StringComparison.Ordinal));
        string[] words = ["none", "one", "two", "three", "four", "five", "six", "seven", "eight"];

        Assert.Contains($"the {words[executables]} `executable_*` ones", notes, StringComparison.Ordinal);
        foreach (var code in ProblemCodes.Environmental.Where(code => !code.StartsWith("executable_", StringComparison.Ordinal)))
        {
            Assert.Contains($"`{code}` are environmental", notes, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"\*\*Environmental problems\*\* — (?<list>.+?) — are about the machine")]
    private static partial Regex EnvironmentalSentence();

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
