namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The splitter every guard types a printed line through. A page that hands a request to standard input prints a
/// redirection after the words, and a shell never hands that to the program: so the splitter stops there and says
/// what the redirection was, rather than passing <c>&lt;&lt;'EOF'</c> to the router as an argument.
/// </summary>
public class ShellWordsTests
{
    [Theory]
    [InlineData("jason campaign create --name \"Q3\" --file - <<'EOF'", "EOF")]
    [InlineData("jason campaign create --name \"Q3\" --file - <<\"EOF\"", "EOF")]
    [InlineData("jason campaign create --name \"Q3\" --file - <<EOF", "EOF")]
    [InlineData("jason campaign create --name \"Q3\" --file - <<'BODY'  ", "BODY")]
    public void A_here_document_ends_the_words_and_is_returned_apart(string line, string delimiter)
    {
        var command = ShellWords.Parse(line);

        Assert.Equal(["campaign", "create", "--name", "Q3", "--file", "-"], command.Words);
        Assert.Equal(new ShellRedirection(ShellRedirectionKind.HereDocument, delimiter), command.Input);
        Assert.Equal(command.Words, ShellWords.Split(line));
    }

    [Theory]
    [InlineData("jason campaign create --name \"Q3\" --file - < brief.json", "brief.json")]
    [InlineData("jason campaign create --name \"Q3\" --file - < 'my brief.json'", "my brief.json")]
    public void A_file_on_standard_input_ends_the_words_and_is_returned_apart(string line, string file)
    {
        var command = ShellWords.Parse(line);

        Assert.Equal(["campaign", "create", "--name", "Q3", "--file", "-"], command.Words);
        Assert.Equal(new ShellRedirection(ShellRedirectionKind.File, file), command.Input);
    }

    /// <summary>
    /// The angle brackets a page prints around a placeholder, and the ones inside a quoted value, are words: only a
    /// <c>&lt;&lt;</c> that opens a delimiter and a <c>&lt;</c> standing alone redirect.
    /// </summary>
    [Theory]
    [InlineData("jason decision get <decision-id> --human", new[] { "decision", "get", "<decision-id>", "--human" })]
    [InlineData("jason rolenote set --note '{\"a\":\"x < y\"}'", new[] { "rolenote", "set", "--note", "{\"a\":\"x < y\"}" })]
    [InlineData("jason contact list --query \"<<EOF\"", new[] { "contact", "list", "--query", "<<EOF" })]
    public void An_angle_bracket_inside_a_word_or_a_quote_is_no_redirection(string line, string[] words)
    {
        var command = ShellWords.Parse(line);

        Assert.Equal(words, command.Words);
        Assert.Null(command.Input);
    }

    /// <summary>A word printed after the redirection would reach the program in a shell and never in this reading, so it is refused.</summary>
    [Theory]
    [InlineData("jason campaign create --file - <<'EOF' --human")]
    [InlineData("jason campaign create --file - < brief.json --human")]
    [InlineData("jason campaign create --file - <<'EOF")]
    [InlineData("jason campaign create --file - <")]
    public void A_redirection_that_is_not_the_line_s_end_is_refused(string line)
    {
        var refused = Assert.Throws<FormatException>(() => ShellWords.Parse(line));
        Assert.Contains(line, refused.Message, StringComparison.Ordinal);
    }
}
