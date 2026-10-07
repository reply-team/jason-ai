using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The pattern grammar the contracts README publishes is the one the validator reads, row by row: a construct the
/// page names and the code does not, or the other way round, or the same construct said differently, is a failure that
/// names the row.
/// </summary>
public class PatternGrammarPageTests
{
    private const string Heading = "### The pattern grammar";

    [Fact]
    public void The_published_table_is_the_grammar_the_validator_reads_row_by_row()
    {
        var expected = PatternGrammar.Table();
        var published = PublishedTable();

        for (var row = 0; row < Math.Max(expected.Count, published.Count); row++)
        {
            var wanted = row < expected.Count ? expected[row] : "(no row)";
            var found = row < published.Count ? published[row] : "(no row)";
            Assert.True(
                string.Equals(wanted, found, StringComparison.Ordinal),
                $"Line {row + 1} of the table under '{Heading}' in docs/contracts/README.md:\n  the grammar: {wanted}\n  the page:    {found}");
        }
    }

    /// <summary>The sentence that stood for the grammar until it was published is gone from the page.</summary>
    [Fact]
    public void The_page_no_longer_says_the_classes_follow_the_engine()
    {
        var page = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md"));

        Assert.DoesNotContain("Until a pattern grammar is published", page, StringComparison.Ordinal);
        Assert.Contains(Heading, page, StringComparison.Ordinal);
    }

    /// <summary>The table's lines, from the first line that starts a table after the heading to the last.</summary>
    private static List<string> PublishedTable()
    {
        var lines = File.ReadAllLines(Path.Combine(ContractFiles.Package, "README.md"));
        var heading = Array.IndexOf(lines, Heading);
        Assert.True(heading >= 0, $"docs/contracts/README.md has no '{Heading}' heading.");

        return [.. lines.Skip(heading + 1).SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|'))];
    }
}
