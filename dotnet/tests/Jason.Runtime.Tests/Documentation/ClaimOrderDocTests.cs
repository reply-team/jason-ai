using System.Text.RegularExpressions;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The claim's checks are published twice, as routing's numbered table and as work execution's chain, and both are held
/// to the order the code asks them in, so a check added or moved in one place cannot leave a page stating the old order.
/// The table's numbers are held to the numbers the code publishes, which never move: a check's number is its name.
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

        Assert.Equal(ProviderOpPreflight.Codes.Select(code => $"{ProviderOpPreflight.Numbers[code]} {code}"), rows);
    }

    /// <summary>
    /// The numbers every release has published stay those checks' names, and a check added later takes the next one: the
    /// twenty-six numbered together keep 1 to 26, and the thread's check is 27, wherever each is asked. A check added since
    /// is held by its place among the published ones, never by a number written here: the numbers after the published ones
    /// are the next ones free, each taken once.
    /// </summary>
    [Fact]
    public void No_check_s_number_moves()
    {
        string[] numbered =
        [
            "operation_unknown", "no_route", "plugin_not_loaded", "plugin_unavailable", "plugin_operation_unsupported",
            "contract_incompatible", "binding_invalid", "contact_required", "no_channel_value", "suppressed",
            "contact_not_linked", "send_unknown", "send_reference_absent", "send_route_mismatch", "search_unknown",
            "search_reference_absent", "search_route_mismatch", "audience_unknown", "input_invalid", "definition_invalid",
            "argument_unsupported", "template_not_offered", "provider_answer_lost", "search_unfinished",
            "approval_required", "provider_blocked", "thread_route_mismatch",
        ];

        var byNumber = ProviderOpPreflight.Numbers.OrderBy(pair => pair.Value).ToList();
        Assert.Equal(
            numbered.Select((code, index) => $"{index + 1} {code}"),
            byNumber.Take(numbered.Length).Select(pair => $"{pair.Value} {pair.Key}"));
        Assert.Equal(Enumerable.Range(1, ProviderOpPreflight.Codes.Count), byNumber.Select(pair => pair.Value));
        Assert.Equal(ProviderOpPreflight.Codes.Order(StringComparer.Ordinal), ProviderOpPreflight.Numbers.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The lock of an opt-out is asked right after the register, under its gate: its place is its neighbour, and its number
    /// is whatever the published order gives it, after every number published before it.
    /// </summary>
    [Fact]
    public void The_opt_out_lock_is_asked_right_after_the_register_and_numbered_after_the_published_checks()
    {
        var codes = ProviderOpPreflight.Codes.ToList();

        Assert.Equal(codes.IndexOf("suppressed") + 1, codes.IndexOf("opted_out"));
        Assert.True(ProviderOpPreflight.Numbers["opted_out"] > ProviderOpPreflight.Numbers["thread_route_mismatch"]);
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
    [InlineData("docs/routing.md", "Of the (?<n>[\\w-]+) answers a claim can give", "all")]
    [InlineData("docs/routing.md", "The last (?<n>[\\w-]+) are questions about a work item", "after binding_invalid")]
    [InlineData("docs/routing.md", "### At the claim — the (?<n>[\\w-]+), in the order they are checked", "all")]
    [InlineData("docs/routing.md", "rows 7, (?<n>[\\w-]+) and [\\w-]+ below", "row of input_invalid")]
    [InlineData("docs/routing.md", "rows 7, [\\w-]+ and (?<n>[\\w-]+) below", "row of definition_invalid")]
    [InlineData("docs/routing.md", "The (?<n>[\\w-]+), `search_unfinished`, is not a refusal", "row of search_unfinished")]
    [InlineData("docs/routing.md", "The (?<n>[\\w-]+) is not a refusal at all", "row of approval_required")]
    [InlineData("docs/routing.md", "The (?<n>[\\w-]+), `provider_blocked`, is not a refusal either", "row of provider_blocked")]
    [InlineData("docs/routing.md", "every refusal from check 2 to check (?<n>[\\w-]+) keeps its code", "row of template_not_offered")]
    [InlineData("docs/routing.md", "keeps its code, and so does check (?<n>[\\w-]+), and is recorded", "row of thread_route_mismatch")]
    [InlineData("docs/routing.md", "so row (?<n>[\\w-]+) stands between rows", "row of thread_route_mismatch")]
    [InlineData("docs/work-execution.md", "whichever of the (?<n>[\\w-]+) pre-flight reasons applies", "all")]
    [InlineData("docs/work-execution.md", "the same rule the (?<n>[\\w-]+) provider checks follow", "all")]
    [InlineData("docs/work-execution.md", "\\| the (?<n>[\\w-]+) pre-flight codes \\|", "all")]
    [InlineData("docs/work-execution.md", "(?<n>[\\w-]+) checks, in this order", "all")]
    [InlineData("docs/work-execution.md", "The first (?<n>[\\w-]+) are refusals", "refusals")]
    [InlineData("docs/work-execution.md", "all of them are final: (?<n>[\\w-]+) are `permanent`", "permanent")]
    [InlineData("docs/work-execution.md", "The (?<n>[\\w-]+) is not a refusal: `search_unfinished`", "row of search_unfinished")]
    [InlineData("docs/work-execution.md", "(?<n>[\\w-]+) is not a refusal either: `approval_required`", "row of approval_required")]
    [InlineData("docs/work-execution.md", "The (?<n>[\\w-]+), `provider_blocked`, is not a refusal either", "row of provider_blocked")]
    [InlineData("docs/work-execution.md", "every refusal of the claim from check 2 to check (?<n>[\\w-]+) keeps its code", "row of template_not_offered")]
    [InlineData("docs/work-execution.md", "keeps its code, and so does check (?<n>[\\w-]+), and is recorded", "row of thread_route_mismatch")]
    [InlineData("docs/work-execution.md", "The (?<n>[\\w-]+), `thread_route_mismatch`, is the last of those refusals", "row of thread_route_mismatch")]
    [InlineData("docs/work-execution.md", "the claim's (?<n>[\\w-]+) check parks held work an open block already holds", "row of provider_blocked")]
    [InlineData("CLAUDE.md", "The claim is fail-closed: (?<n>[\\w-]+) checks in one published order", "all")]
    public void Each_count_of_the_claim_s_checks_is_the_number_the_code_asks(string relative, string sentence, string counted)
    {
        var codes = ProviderOpPreflight.Codes;
        var expected = counted switch
        {
            "all" => codes.Count,
            // The checks asked before the first that parks rather than refuses: a search behind another, an approval, a block.
            "refusals" => IndexOf(codes, "search_unfinished"),
            // Every refusal but three: input_invalid and definition_invalid are validation, provider_answer_lost is ambiguous.
            "permanent" => IndexOf(codes, "search_unfinished") - 3,
            "after binding_invalid" => codes.Count - (IndexOf(codes, "binding_invalid") + 1),
            "row of input_invalid" => Row("input_invalid"),
            "row of definition_invalid" => Row("definition_invalid"),
            // The last refusal by place an unsettled send turns ambiguous: the row just before provider_answer_lost's own.
            "row of template_not_offered" => Row("template_not_offered"),
            "row of thread_route_mismatch" => Row("thread_route_mismatch"),
            "row of search_unfinished" => Row("search_unfinished"),
            "row of approval_required" => Row("approval_required"),
            "row of provider_blocked" => Row("provider_blocked"),
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

    private static int Row(string code)
    {
        IndexOf(ProviderOpPreflight.Codes, code);
        return ProviderOpPreflight.Numbers[code];
    }

    private static int IndexOf(IReadOnlyList<string> codes, string code)
    {
        var index = codes.ToList().IndexOf(code);
        Assert.True(index >= 0, $"The claim asks no check '{code}'.");
        return index;
    }

    /// <summary>
    /// A count as prose writes it — a numeral, a cardinal or an ordinal up to thirty, the ones past twenty hyphenated as
    /// "twenty-five" and "twenty-fifth"; -1 for anything else.
    /// </summary>
    private static int NumberWord(string word)
    {
        string[] cardinals = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
            "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];
        string[] ordinals = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh",
            "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth"];
        var lower = word.ToLowerInvariant();
        if (int.TryParse(lower, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (lower is "thirty" or "thirtieth")
        {
            return 30;
        }

        if (lower.StartsWith("twenty-", StringComparison.Ordinal))
        {
            var unit = lower["twenty-".Length..];
            var past = Array.IndexOf(cardinals, unit);
            if (past < 0)
            {
                past = Array.IndexOf(ordinals, unit);
            }

            return past is > 0 and < 10 ? 20 + past : -1;
        }

        var cardinal = Array.IndexOf(cardinals, lower);
        return cardinal >= 0 ? cardinal : Array.IndexOf(ordinals, lower);
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
