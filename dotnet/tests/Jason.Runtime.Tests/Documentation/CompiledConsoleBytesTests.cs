using System.Text;
using System.Xml.Linq;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// No text the programs carry is a glyph that the code page of a Windows console turns into a control byte: what a
/// program of this installation compiles is ASCII, or made only of characters that every default OEM code page of a
/// Windows console writes as a byte of 0x20 or above.
/// </summary>
/// <remarks>
/// <para>
/// A console decodes what a program writes through its output code page, which is the OEM page of the language Windows
/// is set to unless something changes it. A glyph the page lacks is written as its nearest relative, or as a question
/// mark, and reads wrongly but reads. A glyph the page sends to a byte below 0x20 does not read at all: the byte is a
/// control code, so a terminal draws a symbol for the code instead of a character, or draws nothing and rings the bell.
/// The arrow in <c>0.15.0 -&gt; 0.18.0</c> is the case that was seen: page 437 sends it to 0x1A, the substitute
/// control, and a person updating under PowerShell on Windows read a symbol where the arrow was.
/// </para>
/// <para>
/// The pages are every default OEM page and not the two a console most often runs on, because a glyph that reads on one
/// page is a control byte on another: the middle dot is 0xFA on 437 and 866 and the bell, 0x07, on 852, the Central
/// European page; the ellipsis is a full stop on 437 and the bell on 852, on 857, the Turkish page, and on 775, the
/// Baltic one. The CLI does not set the console's output code page to repair this. The setting outlives the process and
/// changes how the person's shell decodes every program that runs after it, so the text is what is held.
/// </para>
/// <para>
/// The strings are read from each production assembly's user-string heap, as the page-names guard reads them, so a
/// comment is never read and a string put together by the compiler is. An ASCII character is the console's own business
/// whatever its byte, since a newline, a tab and an escape are written as the controls they are meant to be; a character
/// outside ASCII is the one tried against each page. A process that registers no encoding provider, as none of these
/// programs does, writes through Windows' own conversion of the page. The pages here come from the code pages provider,
/// whose tables run on every platform. The provider and Windows' conversion were compared on Windows on 2026-10-26, over
/// every character of the Basic Multilingual Plane outside ASCII on each of the fifteen pages: they write a control byte
/// for the same characters, and the same byte, and differ only in which printable byte they choose for a few others, at
/// most 34 on a page.
/// </para>
/// <para>
/// Three kinds of compiled text lie outside the user-string heap, so the heap's rule does not reach them. A <c>char</c>
/// literal compiles to an integer constant, so an <c>Append</c> of an arrow written as a <c>char</c> would pass; none is
/// written. A <c>u8</c> literal compiles to static data; none is written either. And the text resources embedded into
/// Jason.Contracts are the contract documents, whose summary templates <c>jason approval list --human</c> prints; a row
/// below holds those to the same rule, read from disk.
/// </para>
/// </remarks>
public sealed class CompiledConsoleBytesTests
{
    /// <summary>
    /// The default OEM code page of a Windows console in every language Windows is set to: US English, Arabic, Greek,
    /// Baltic, Western European, Central European, Turkish, Hebrew, Cyrillic, Thai, Japanese, Simplified Chinese, Korean,
    /// Traditional Chinese and Vietnamese.
    /// </summary>
    private static readonly int[] Pages = [437, 720, 737, 775, 850, 852, 857, 862, 866, 874, 932, 936, 949, 950, 1258];

    [Fact]
    [Critical(Family.R2, "no compiled glyph a console writes as a control byte")]
    public void No_string_the_programs_carry_is_written_to_a_console_as_a_control_byte()
    {
        var root = RepositoryTree.Root();
        var found = new List<string>();
        foreach (var assembly in CompiledCalls.ProductionAssemblies(root))
        {
            foreach (var text in UserStrings.Of(assembly))
            {
                found.AddRange(ControlBytes(text).Select(failure =>
                    $"{Path.GetFileNameWithoutExtension(assembly)}: U+{failure.Glyph.Value:X4} is written as 0x{failure.Control:X2} on code page {failure.Page}, in \"{Shown(text)}\""));
            }
        }

        Assert.True(found.Count == 0, $"The programs carry {found.Count} glyph(s) a console writes as a control byte:\n" + string.Join('\n', found));
    }

    /// <summary>
    /// The contract documents Jason.Contracts embeds, read as the text they are on disk: every file its project's
    /// embedded resources name. The runtime composes their summary templates into what
    /// <c>jason approval list --human</c> prints, and an embedded resource is not in the user-string heap the row above
    /// reads.
    /// </summary>
    [Fact]
    public void No_contract_document_Jason_Contracts_embeds_is_written_to_a_console_as_a_control_byte()
    {
        var root = RepositoryTree.Root();
        var project = Path.Combine(root, "dotnet", "src", "Jason.Contracts", "Jason.Contracts.csproj");
        var documents = new List<string>();
        foreach (var resource in XDocument.Load(project).Descendants("EmbeddedResource"))
        {
            var include = ((string?)resource.Attribute("Include") ?? "").Replace('\\', '/');
            var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, Path.GetDirectoryName(include)!));
            var named = Directory.GetFiles(directory, Path.GetFileName(include));
            Assert.True(named.Length > 0, $"The embedded resource {include} names no file.");
            documents.AddRange(named);
        }

        Assert.NotEmpty(documents);
        var found = new List<string>();
        foreach (var document in documents)
        {
            found.AddRange(ControlBytes(File.ReadAllText(document)).Select(failure =>
                $"{Path.GetRelativePath(root, document).Replace('\\', '/')}: U+{failure.Glyph.Value:X4} is written as 0x{failure.Control:X2} on code page {failure.Page}"));
        }

        Assert.True(found.Count == 0, $"The contract documents carry {found.Count} glyph(s) a console writes as a control byte:\n" + string.Join('\n', found));
    }

    /// <summary>
    /// The rule itself: a glyph is found on each page that writes it as a control byte, with the byte, and on no other.
    /// The arrow and the bullet are control bytes on most pages; the middle dot and the ellipsis only on the few that are
    /// why the pages are every default one. The arrow's byte also shows the pages fit a glyph as the console does, since
    /// a page that replaced it would write a question mark; and a glyph a text carries twice is found once on each page.
    /// </summary>
    [Theory]
    [InlineData("0.15.0 \U00002192 0.18.0", 0x2192, 0x1A, 437, 737, 775, 850, 852, 857, 862, 866, 874)]
    [InlineData("a \U00002192 b \U00002192 c", 0x2192, 0x1A, 437, 737, 775, 850, 852, 857, 862, 866, 874)]
    [InlineData("one \U00002022 two", 0x2022, 0x07, 437, 737, 775, 850, 852, 857, 862, 866)]
    [InlineData("ready \U000000B7 running", 0x00B7, 0x07, 852)]
    [InlineData("and so on\U00002026", 0x2026, 0x07, 775, 852, 857)]
    public void A_glyph_is_found_on_each_page_that_writes_it_as_a_control_byte(string text, int glyph, byte control, params int[] pages)
    {
        Assert.Equal(
            pages.Select(page => (glyph, page, control)),
            ControlBytes(text).Select(failure => (failure.Glyph.Value, failure.Page, failure.Control)));
    }

    /// <summary>
    /// And what reads passes: ASCII, controls included; the em dash, which no page writes as a control byte; and the en
    /// space, which 437 writes as the space itself, 0x20, the first byte that is not a control.
    /// </summary>
    [Theory]
    [InlineData("a plain line\twith a tab,\nand an escape \u001b[0m")]
    [InlineData("one \U00002014 two")]
    [InlineData("one\U00002002two")]
    public void A_glyph_every_page_writes_as_a_printable_byte_passes(string text)
    {
        Assert.Empty(ControlBytes(text));
    }

    /// <summary>
    /// Each non-ASCII character of a text that one of the pages writes as a byte below 0x20, with the page and the byte,
    /// once per character and page.
    /// </summary>
    private static List<(Rune Glyph, int Page, byte Control)> ControlBytes(string text)
    {
        var found = new List<(Rune Glyph, int Page, byte Control)>();
        foreach (var glyph in text.EnumerateRunes().Where(rune => !rune.IsAscii).Distinct())
        {
            foreach (var page in Pages)
            {
                var encoding = CodePagesEncodingProvider.Instance.GetEncoding(page)
                    ?? throw new InvalidOperationException($"Code page {page} is not provided.");
                if (encoding.GetBytes(glyph.ToString()).Where(written => written < 0x20).ToArray() is [var control, ..])
                {
                    found.Add((glyph, page, control));
                }
            }
        }

        return found;
    }

    /// <summary>A text as a failure names it: short, and with nothing in it that a console could draw wrongly.</summary>
    private static string Shown(string text)
    {
        var shown = text.Length > 80 ? text[..80] + "..." : text;
        return string.Concat(shown.Select(character => character is >= ' ' and < '\u007F' ? character.ToString() : $"\\u{(int)character:X4}"));
    }
}
