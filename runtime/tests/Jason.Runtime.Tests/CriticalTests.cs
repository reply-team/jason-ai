namespace Jason.Runtime.Tests;

/// <summary>
/// The critical tier is selected by two traits, so what selects it is what these tests hold: the attribute on a test
/// says the tier and the family, and a theory fed by a member marks exactly the rows it names.
/// </summary>
public class CriticalTests
{
    [Fact]
    public void The_attribute_puts_its_test_in_the_tier_and_names_the_family()
    {
        var traits = new CriticalAttribute(Family.W2, "one guarded UPDATE").GetTraits();

        Assert.Equal(
            [KeyValuePair.Create("Category", "Critical"), KeyValuePair.Create("CriticalFamily", "W2")],
            traits.ToArray());
    }

    [Fact]
    public void The_attribute_keeps_the_reason_it_was_given()
    {
        var critical = new CriticalAttribute(Family.S1, "the register is read at the claim");

        Assert.Equal(Family.S1, critical.Family);
        Assert.Equal("the register is read at the claim", critical.Why);
    }

    [Fact]
    public void Marking_rows_marks_the_rows_it_names_and_no_other()
    {
        TheoryData<string, int> rows = new() { { "kept", 1 }, { "chosen", 2 }, { "kept", 3 } };

        var marked = CriticalRows.Mark(rows, Family.X1, data => data is ["chosen", _]);

        Assert.Same(rows, marked);
        var traits = marked.Select(row => (Data: ((ITheoryDataRow)row).GetData()[1], row.Traits)).ToArray();
        Assert.All(
            traits.Where(row => row.Data is 1 or 3),
            row => Assert.True(row.Traits.Count == 0, $"Row {row.Data} carries {string.Join(", ", row.Traits.Keys)}."));
        var chosen = Assert.Single(traits, row => row.Data is 2).Traits;
        Assert.Equal("Critical", Assert.Single(chosen["Category"]));
        Assert.Equal("X1", Assert.Single(chosen["CriticalFamily"]));
    }

    [Fact]
    public void Marking_rows_that_names_no_row_is_refused_so_the_whole_suite_fails_the_theory()
    {
        TheoryData<string> rows = ["a", "b"];

        var refused = Assert.Throws<InvalidOperationException>(
            () => CriticalRows.Mark(rows, Family.R3, data => data is ["gone"]));

        Assert.Contains("R3", refused.Message, StringComparison.Ordinal);
    }
}
