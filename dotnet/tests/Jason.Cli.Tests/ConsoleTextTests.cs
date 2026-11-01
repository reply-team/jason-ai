using System.Text;

namespace Jason.Cli.Tests;

/// <summary>
/// The console's writer marks a glyph its code page writes as a control byte, and writes everything else as it came. The
/// page is 437, the US English console's, from the code pages provider, whose tables write a control byte for the same
/// characters as Windows' own conversion does.
/// </summary>
public class ConsoleTextTests
{
    private static readonly Encoding Page437 = CodePagesEncodingProvider.Instance.GetEncoding(437)!;

    /// <summary>
    /// The right arrow is 0x1A on page 437, the left arrow ESC, the eighth note CR and the house DEL: each is marked.
    /// </summary>
    [Theory]
    [InlineData(0x2192, 0x1A)]
    [InlineData(0x2190, 0x1B)]
    [InlineData(0x266A, 0x0D)]
    [InlineData(0x2302, 0x7F)]
    public void A_glyph_the_page_writes_as_a_control_byte_is_marked(int glyph, byte control)
    {
        var written = char.ConvertFromUtf32(glyph);
        Assert.Equal([control], Page437.GetBytes(written));

        var text = Written(Page437, writer => writer.WriteLine("0.15.0 " + written + " 0.18.0"));

        Assert.Equal("0.15.0 \uFFFD 0.18.0" + Environment.NewLine, text);
        Assert.DoesNotContain(Page437.GetBytes(text.TrimEnd()), value => value is < 0x20 or 0x7F);
    }

    /// <summary>ASCII is the console's own business, its controls included; a glyph the page writes as a printable byte passes.</summary>
    [Fact]
    public void Ascii_and_a_glyph_the_page_writes_as_printable_pass_as_they_came()
    {
        const string text = "a\tb \u001b[0m one \u2014 caf\u00E9 \U0001F600";

        Assert.Equal(text, Written(Page437, writer => writer.Write(text)));
    }

    /// <summary>
    /// UTF-8, UTF-16 and UTF-32, either byte order, write no character outside ASCII as a control code, though the last two
    /// write a zero byte in most of them: under each the console's writer is left as it is, and nothing is marked.
    /// </summary>
    [Theory]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void A_unicode_encoding_leaves_the_writer_as_it_is(int page)
    {
        const string text = "0.15.0 \u2192 0.18.0 \u2190 \u266A caf\u00E9 \u0416";
        using var console = new StringWriter();

        var writer = ConsoleText.Guarded(console, Encoding.GetEncoding(page));
        writer.Write(text);

        Assert.Same(console, writer);
        Assert.Equal(text, console.ToString());
    }

    /// <summary>A character written one half at a time is still one character, and is read as one.</summary>
    [Fact]
    public void A_surrogate_pair_written_one_half_at_a_time_passes_whole()
    {
        var text = Written(Page437, writer =>
        {
            writer.Write('\uD83D');
            writer.Write('\uDE00');
            writer.Write('\u2192');
        });

        Assert.Equal("\U0001F600\uFFFD", text);
    }

    /// <summary>The environment this program really runs with writes its output and its diagnostics through the guard.</summary>
    [Fact]
    public void The_environment_this_program_really_runs_with_writes_through_the_guard()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environment = CliEnvironment.Default(output, error, Page437);

        environment.Out.Write("0.15.0 \u2192 0.18.0");
        environment.Out.Flush();
        environment.Error.Write("\u2190 back");
        environment.Error.Flush();

        Assert.Equal("0.15.0 \uFFFD 0.18.0", output.ToString());
        Assert.Equal("\uFFFD back", error.ToString());
    }

    private static string Written(Encoding encoding, Action<TextWriter> write)
    {
        using var console = new StringWriter();
        var writer = ConsoleText.Guarded(console, encoding);
        write(writer);
        writer.Flush();
        return console.ToString();
    }
}
