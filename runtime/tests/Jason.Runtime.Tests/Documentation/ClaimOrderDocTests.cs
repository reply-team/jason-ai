using System.Text.RegularExpressions;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The claim's checks are published twice, as routing's numbered table and as work execution's chain, and both are held
/// to the order the code asks them in, so a check added or moved in one place cannot leave a page stating the old order.
/// </summary>
public sealed partial class ClaimOrderDocTests
{
    [Fact]
    public void Routing_s_table_numbers_the_claim_s_checks_in_the_order_the_code_asks_them()
    {
        var page = Source("docs/routing.md").ReplaceLineEndings("\n");
        var start = page.IndexOf("### At the claim", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/routing.md has no section '### At the claim'.");
        var end = page.IndexOf("\n#", start + 1, StringComparison.Ordinal);
        var rows = TableRow().Matches(page[start..(end < 0 ? page.Length : end)])
            .Select(row => $"{row.Groups["n"].Value} {row.Groups["code"].Value}")
            .ToList();

        Assert.Equal(ProviderOpPreflight.Codes.Select((code, index) => $"{index + 1} {code}"), rows);
    }

    [Fact]
    public void Work_execution_s_chain_is_the_order_the_code_asks_the_checks_in()
    {
        var page = Source("docs/work-execution.md").ReplaceLineEndings("\n");
        var chain = Chain().Matches(page).Select(block => Whitespace().Replace(block.Groups["chain"].Value, " ").Trim()).ToList();

        Assert.Equal([string.Join(" → ", ProviderOpPreflight.Codes)], chain);
    }

    [Fact]
    public void Routing_names_the_bound_a_read_on_request_does_not_have()
    {
        Assert.Contains(
            "**A read on request has no bound of its own.** It runs outside the handler pool, one child each; `Dispatcher:MaxParallel` bounds work items only.",
            Whitespace().Replace(Source("docs/routing.md"), " "),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every sentence that counts the claim's checks says the number the code asks: all of them, the refusals before the
    /// approval, the permanent ones among those, the questions after the route's, or the row a check sits at. The number is
    /// read as a number, so a check added or removed fails here until every count in prose follows the table.
    /// </summary>
    [Theory]
    [InlineData("docs/routing.md", "Of the (?<n>\\w+) answers a claim can give", "all")]
    [InlineData("docs/routing.md", "The last (?<n>\\w+) are questions about a work item", "after binding_invalid")]
    [InlineData("docs/routing.md", "### At the claim — the (?<n>\\w+), in the order they are checked", "all")]
    [InlineData("docs/routing.md", "rows 7 and (?<n>\\w+) below", "row of input_invalid")]
    [InlineData("docs/routing.md", "The (?<n>\\w+) is not a refusal at all", "row of approval_required")]
    [InlineData("docs/routing.md", "The (?<n>\\w+), `provider_blocked`, is not a refusal either", "row of provider_blocked")]
    [InlineData("docs/work-execution.md", "whichever of the (?<n>\\w+) pre-flight reasons applies", "all")]
    [InlineData("docs/work-execution.md", "the same rule the (?<n>\\w+) provider checks follow", "all")]
    [InlineData("docs/work-execution.md", "\\| the (?<n>\\w+) pre-flight codes \\|", "all")]
    [InlineData("docs/work-execution.md", "(?<n>\\w+) checks, in this order", "all")]
    [InlineData("docs/work-execution.md", "The first (?<n>\\w+) are refusals", "refusals")]
    [InlineData("docs/work-execution.md", "all of them are final: (?<n>\\w+) are `permanent`", "permanent")]
    [InlineData("docs/work-execution.md", "The (?<n>\\w+) is not a refusal: `approval_required`", "row of approval_required")]
    [InlineData("docs/work-execution.md", "The (?<n>\\w+), `provider_blocked`, is not a refusal either", "row of provider_blocked")]
    [InlineData("CLAUDE.md", "The claim is fail-closed: (?<n>\\w+) checks in one published order", "all")]
    public void Each_count_of_the_claim_s_checks_is_the_number_the_code_asks(string relative, string sentence, string counted)
    {
        var codes = ProviderOpPreflight.Codes;
        var expected = counted switch
        {
            "all" => codes.Count,
            "refusals" => IndexOf(codes, "approval_required"),
            "permanent" => IndexOf(codes, "approval_required") - 1,
            "after binding_invalid" => codes.Count - (IndexOf(codes, "binding_invalid") + 1),
            "row of input_invalid" => IndexOf(codes, "input_invalid") + 1,
            "row of approval_required" => IndexOf(codes, "approval_required") + 1,
            "row of provider_blocked" => IndexOf(codes, "provider_blocked") + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(counted), counted, "No such count."),
        };
        var matches = Regex.Matches(Whitespace().Replace(Source(relative), " "), sentence);

        Assert.True(matches.Count == 1, $"{relative} states '{sentence}' {matches.Count} times.");
        var word = matches[0].Groups["n"].Value;
        Assert.True(NumberWord(word) == expected, $"{relative} says '{matches[0].Value}', and the code asks {expected} ({counted}).");
    }

    [GeneratedRegex(@"^\| (?<n>\d+) \| `(?<code>[a-z_]+)` \|", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"```text\n(?<chain>operation_unknown →[^`]*)```")]
    private static partial Regex Chain();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static int IndexOf(IReadOnlyList<string> codes, string code)
    {
        var index = codes.ToList().IndexOf(code);
        Assert.True(index >= 0, $"The claim asks no check '{code}'.");
        return index;
    }

    /// <summary>A count as prose writes it — a numeral, a cardinal or an ordinal up to twenty; -1 for anything else.</summary>
    private static int NumberWord(string word)
    {
        string[] cardinals = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
            "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];
        string[] ordinals = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh",
            "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth"];
        var lower = word.ToLowerInvariant();
        var cardinal = Array.IndexOf(cardinals, lower);
        return int.TryParse(lower, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : cardinal >= 0 ? cardinal : Array.IndexOf(ordinals, lower);
    }

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
