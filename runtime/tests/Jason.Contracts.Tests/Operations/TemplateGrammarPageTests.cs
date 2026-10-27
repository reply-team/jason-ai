using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The template standard the contracts README publishes is the one the validator reads, row by row: a construct or a
/// name the page lists and the code does not, or the other way round, or the same one said differently, is a failure
/// that names the row.
/// </summary>
public class TemplateGrammarPageTests
{
    private const string Constructs = "### What a template may write";

    private const string Names = "### The names a template may use";

    [Fact]
    public void The_published_constructs_are_the_ones_the_validator_reads_row_by_row()
    {
        Compare(TemplateGrammar.Table(), PublishedTable(Constructs), Constructs);
    }

    [Fact]
    public void The_published_names_are_the_ones_the_validator_reads_row_by_row()
    {
        Compare(TemplateGrammar.NamesTable(), PublishedTable(Names), Names);
    }

    /// <summary>
    /// What counts as no value, and how two values compare, is the provider's: the standard says so and leaves Reply's to
    /// the Reply package's README, the rule the runtime's own example follows is stated as the example's, not the
    /// standard's, and the standard says what that means for the message sent.
    /// </summary>
    [Fact]
    public void The_standard_leaves_no_value_and_comparing_to_the_provider()
    {
        var page = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")).ReplaceLineEndings("\n");
        var start = page.IndexOf("\n## The template standard\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/contracts/README.md has no '## The template standard' heading.");
        var section = string.Join(' ', page[start..page.IndexOf(Constructs, start, StringComparison.Ordinal)].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "What counts as no value, and how two values compare, is the provider's; [the Reply package's README](../../plugins/reply/README.md) says Reply's.",
            section,
            StringComparison.Ordinal);
        Assert.Contains("The approval's example counts a value that is null, empty or white space as no value", section, StringComparison.Ordinal);
        Assert.DoesNotContain("A value that is null, empty or white space is **no value**", section, StringComparison.Ordinal);
        Assert.DoesNotContain("A comparison compares the value with it exactly", section, StringComparison.Ordinal);
        Assert.Contains(
            "The message sent may take another branch than the example shows, and the provider may fill a name the example lists as unfilled.",
            section,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A comparison is a condition on the value being, or not being, the quoted text: how the two compare is the
    /// provider's, so the standard's row does not say exactly.
    /// </summary>
    [Fact]
    public void The_comparison_s_row_leaves_how_the_two_compare_to_the_provider()
    {
        var row = Assert.Single(TemplateGrammar.Table(), line => line.Contains("`{{#if name == 'text'}}`", StringComparison.Ordinal));

        Assert.EndsWith("| accepted | a condition on the value being, or not being, the quoted text |", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages say what ships: the Reply package refuses any <c>{{</c> in a final message, and from 0.10.0 writes a
    /// template in Reply's syntax rather than refusing every one.
    /// </summary>
    [Fact]
    public void The_pages_say_the_reply_package_writes_a_template_from_0_10_0()
    {
        var operation = Flat(File.ReadAllText(Path.Combine(ContractFiles.Operations, "message.send.md")));
        var contracts = Flat(File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")));

        Assert.Contains(
            "The Reply package refuses any `{{` in a final message, as it did before 0.10.0; from 0.10.0 it writes a template in Reply's syntax, and refuses before anything is called what it cannot write there.",
            operation,
            StringComparison.Ordinal);
        Assert.Contains("as the Reply package before 0.10.0 refused any `{{`; otherwise it is breaking.", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("every template", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("refuses every template", contracts, StringComparison.Ordinal);
    }

    private static string Flat(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Compare(IReadOnlyList<string> expected, List<string> published, string heading)
    {
        for (var row = 0; row < Math.Max(expected.Count, published.Count); row++)
        {
            var wanted = row < expected.Count ? expected[row] : "(no row)";
            var found = row < published.Count ? published[row] : "(no row)";
            Assert.True(
                string.Equals(wanted, found, StringComparison.Ordinal),
                $"Line {row + 1} of the table under '{heading}' in docs/contracts/README.md:\n  the standard: {wanted}\n  the page:     {found}");
        }
    }

    /// <summary>The table's lines, from the first line that starts a table after the heading to the last.</summary>
    private static List<string> PublishedTable(string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(ContractFiles.Package, "README.md"));
        var at = Array.IndexOf(lines, heading);
        Assert.True(at >= 0, $"docs/contracts/README.md has no '{heading}' heading.");

        return [.. lines.Skip(at + 1).SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|'))];
    }
}
