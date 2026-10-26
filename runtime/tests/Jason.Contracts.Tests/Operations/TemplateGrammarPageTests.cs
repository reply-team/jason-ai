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
