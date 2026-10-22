namespace Jason.Runtime.Tests;

/// <summary>
/// How the critical tier's guard reads a mark's reason from the sources: a marked <c>InlineData</c> row gives it in a
/// <c>// Critical:</c> comment on the line above it, and a member that marks its rows through
/// <see cref="CriticalRows.Mark"/> gives it in one above the member. A row takes no argument of its own, so the comment is
/// where its reason is kept.
/// </summary>
public class CriticalReasonsTests
{
    private const string File = "runtime/tests/Example/ExampleTests.cs";

    [Fact]
    public void A_marked_row_gives_its_reason_on_the_line_above_it_and_belongs_to_the_theory_below()
    {
        var source = """
            public class ExampleTests
            {
                [Theory]
                [InlineData("kept")]
                // Critical: the chosen row goes red first.
                [InlineData("chosen", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)])]
                public void Holds_the_rule(string value)
                {
                }
            }
            """;

        var (rows, members) = CriticalTier.ReadMarks(File, source);

        var row = Assert.Single(rows);
        Assert.Equal(new CriticalTier.Mark(File, 6, "Holds_the_rule", "the chosen row goes red first.", "ExampleTests"), row);
        Assert.Empty(members);
    }

    [Fact]
    public void A_marked_row_with_no_comment_directly_above_it_gives_no_reason()
    {
        var source = """
            public class ExampleTests
            {
                [Theory]
                // Critical: this reason belongs to no row.

                [InlineData("chosen", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)])]
                // Critical:
                [InlineData("also chosen", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)])]
                public void Holds_the_rule(string value)
                {
                }
            }
            """;

        var (rows, _) = CriticalTier.ReadMarks(File, source);

        Assert.Equal([(6, string.Empty), (8, string.Empty)], rows.Select(row => (row.Line, row.Reason)));
    }

    [Fact]
    public void A_member_that_marks_rows_gives_its_reason_above_the_member()
    {
        var source = """
            public class ExampleTests
            {
                public static TheoryData<string> Rows() => ["kept", "chosen"];

                // Critical: the chosen row goes red first.
                public static TheoryData<string, string?> ChosenRows() =>
                    CriticalRows.Mark(Rows(), Family.X1, data => data is
                        ["chosen"]);

                public static TheoryData<string> UnexplainedRows() =>
                    CriticalRows.Mark(Rows(), Family.X1, data => data is ["kept"]);
            }
            """;

        var (rows, members) = CriticalTier.ReadMarks(File, source);

        Assert.Empty(rows);
        Assert.Equal(
            [new CriticalTier.Mark(File, 7, "ChosenRows", "the chosen row goes red first.", "ExampleTests"), new CriticalTier.Mark(File, 11, "UnexplainedRows", string.Empty, "ExampleTests")],
            members);
    }

    [Fact]
    public void A_mark_belongs_to_the_class_it_sits_in_so_two_theories_of_one_name_do_not_pool()
    {
        var source = """
            public class First
            {
                [Theory]
                // Critical: the first class explains its row.
                [InlineData("chosen", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)])]
                public void Holds_the_rule(string value)
                {
                }
            }

            public class Second
            {
                [Theory]
                [InlineData("chosen", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)])]
                public void Holds_the_rule(string value)
                {
                }
            }
            """;

        var (rows, _) = CriticalTier.ReadMarks(File, source);

        Assert.Equal([("First", "the first class explains its row."), ("Second", string.Empty)], rows.Select(row => (row.Class, row.Reason)));
        var second = CriticalTier.RowsOf(rows, "Second", "Holds_the_rule");
        Assert.True(
            second.Count == 1 && second[0].Reason.Length == 0,
            $"the theory of class Second holds {second.Count} marked row(s), reasons '{string.Join("', '", second.Select(row => row.Reason))}', and the one without a reason is Second's own");
    }

    [Fact]
    public void A_suite_smaller_than_the_table_measured_is_told_to_re_measure_and_not_that_it_runs_another_tier()
    {
        var problems = CriticalTier.FloorProblems(184, 269, (22, 185, 300));

        Assert.Equal(
            [
                "the suite has 184 method(s), fewer than the 185 the table measured the ceiling on",
                "the suite has 269 case(s), fewer than nine tenths of the 300 the table measured the ceiling on",
            ],
            problems);
        Assert.Contains("removes tests rewrites it", CriticalTier.Differs, StringComparison.Ordinal);
        Assert.DoesNotContain("tier other than", CriticalTier.Differs, StringComparison.Ordinal);
        Assert.Empty(CriticalTier.FloorProblems(185, 271, (22, 185, 300)));
    }

    [Fact]
    public void A_call_inside_a_test_a_comment_and_a_row_of_other_traits_are_not_marks()
    {
        var source = """
            public class ExampleTests
            {
                /// <c>Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)]</c> marks a row.
                [Theory]
                [InlineData("live", Traits = ["Category", "manual-live"])]
                public void Holds_the_rule(string value)
                {
                    TheoryData<string> rows = ["a"];

                    var marked = CriticalRows.Mark(rows, Family.X1, data => data is ["a"]);
                }
            }
            """;

        var (rows, members) = CriticalTier.ReadMarks(File, source);

        Assert.Empty(rows);
        Assert.Empty(members);
    }
}
