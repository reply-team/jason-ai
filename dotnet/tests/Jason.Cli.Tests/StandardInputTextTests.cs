using System.Text;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Tests.Commands;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

/// <summary>
/// Standard input, when it is redirected, is read as UTF-8 on every system: a person's words in any alphabet arrive as
/// they were written, a byte-order mark is dropped, and bytes that are not UTF-8 are refused as a usage error rather than
/// stored as whatever a code page would have made of them. A console that is not redirected is read as it always was.
/// </summary>
public class StandardInputTextTests
{
    private const string Words = "Привет, Мария! Ёлки, «тест» — 你好 ✓";

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"context\":{\"brief\":\"" + Words + "\"}}");

    [Fact]
    public async Task Words_in_another_alphabet_arrive_as_they_were_written()
    {
        using var cli = new CliRun(input: StandardInputText.Utf8(new MemoryStream(Body)));

        var exit = await cli.RunAsync("campaign", "create", "--name", "Piped", "--file", "-");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Words, (string?)JsonNode.Parse(cli.Sent!)!["context"]!["brief"]);
    }

    [Fact]
    public async Task A_byte_order_mark_is_dropped()
    {
        using var cli = new CliRun(input: StandardInputText.Utf8(new MemoryStream([0xEF, 0xBB, 0xBF, .. Body])));

        var exit = await cli.RunAsync("campaign", "create", "--name", "Piped", "--file", "-");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Words, (string?)JsonNode.Parse(cli.Sent!)!["context"]!["brief"]);
    }

    /// <summary>The refusal, through a verb: the usage exit, the sentence, and no request sent.</summary>
    [Fact]
    [Critical(Family.F4, "the CLI is a child of every agent host, and its standard input is where a person's words reach it")]
    public async Task Bytes_that_are_not_UTF8_are_refused_as_a_usage_error_and_nothing_is_sent()
    {
        // The same words as Windows' Cyrillic code page writes them: every byte of them is above 0x7F and none starts a
        // UTF-8 sequence that the next completes.
        var windows1251 = new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x22, 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0x22, 0x7D };
        using var cli = new CliRun(input: StandardInputText.Utf8(new MemoryStream(windows1251)));

        var exit = await cli.RunAsync("campaign", "create", "--name", "Piped", "--file", "-");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(StandardInputText.Refusal, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>
    /// Every other shape of bytes that are not UTF-8 is refused the same way: a UTF-16 text with its mark, which a
    /// UTF-8 reader would otherwise read as text with a zero between each letter, and a sequence cut short at the end.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x7B, 0x00, 0x7D, 0x00 })]
    [InlineData(new byte[] { 0x7B, 0x7D, 0xD0 })]
    [InlineData(new byte[] { 0x7B, 0xC0, 0xAF, 0x7D })]
    public async Task Other_bytes_that_are_not_UTF8_are_refused_too(byte[] bytes)
    {
        using var cli = new CliRun(input: StandardInputText.Utf8(new MemoryStream(bytes)));

        var exit = await cli.RunAsync("campaign", "create", "--name", "Piped", "--file", "-");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(StandardInputText.Refusal, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, cli.Requests);
    }

    /// <summary>A line read as an answer is decoded the same way, and refused the same way.</summary>
    [Fact]
    public void A_line_is_decoded_and_refused_as_the_whole_is()
    {
        using var words = StandardInputText.Utf8(new MemoryStream(Encoding.UTF8.GetBytes("да\nнет\n")));
        Assert.Equal("да", words.ReadLine());
        Assert.Equal("нет", words.ReadLine());
        Assert.Null(words.ReadLine());

        using var bytes = StandardInputText.Utf8(new MemoryStream([0xE4, 0xE0, 0x0A]));
        var refused = Assert.Throws<UsageException>(() => bytes.ReadLine());
        Assert.Equal(StandardInputText.Refusal, refused.Message);
    }

    [Fact]
    public void Redirected_input_is_read_as_UTF8_and_a_console_as_it_is()
    {
        var console = new StringReader("typed");
        var opened = 0;
        Stream Open()
        {
            opened++;
            return new MemoryStream(Encoding.UTF8.GetBytes("Привет"));
        }

        Assert.Same(console, StandardInputText.For(redirected: false, Open, () => console));
        Assert.Equal(0, opened);

        using var piped = StandardInputText.For(redirected: true, Open, () => throw new InvalidOperationException("The console reader was read."));
        Assert.Equal("Привет", piped.ReadToEnd());
        Assert.Equal(1, opened);
    }
}
