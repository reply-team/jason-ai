using Jason.Cli.Human;

namespace Jason.Cli.Tests;

public class HumanTableTests
{
    [Fact]
    public void Columns_are_left_aligned_two_spaces_apart_under_an_underlined_header()
    {
        var text = new HumanTable("ID", "NAME")
            .Row("cmp_1", "LatAm")
            .Row("cmp_22", null)
            .Render();

        Assert.Equal(
            string.Join(
                System.Environment.NewLine,
                "ID      NAME",
                "------  -----",
                "cmp_1   LatAm",
                "cmp_22  -"),
            text);
    }

    [Fact]
    public void A_table_without_rows_is_the_header_alone()
    {
        var text = new HumanTable("CHANNEL", "VALUE").Render();

        Assert.Equal(string.Join(System.Environment.NewLine, "CHANNEL  VALUE", "-------  -----"), text);
    }

    [Fact]
    public void The_widest_cell_sets_the_column_width()
    {
        var text = new HumanTable("A").Row("a much longer cell").Render();

        Assert.Equal(string.Join(System.Environment.NewLine, "A", "------------------", "a much longer cell"), text);
    }

    /// <summary>
    /// A cell is one line, and what a table lists is often somebody's text: a character that changes how the line reads
    /// is shown as U+FFFD by the rule every human view holds for such text, and the column is as wide as what is shown.
    /// </summary>
    [Fact]
    public void A_cell_shows_a_character_that_changes_the_line_as_a_replacement_mark()
    {
        var text = new HumanTable("NAME", "ID").Row("Ada\u001b[2J\nGrace‮\tx", "cnt_1").Render();

        Assert.Equal(
            string.Join(System.Environment.NewLine, "NAME              ID", "----------------  -----", "Ada�[2J�Grace�\tx  cnt_1"),
            text);
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_cells_is_rejected()
    {
        var table = new HumanTable("ID", "NAME");

        Assert.Throws<ArgumentException>(() => table.Row("only one"));
    }
}
