using System.IO.Compression;
using Jason.Cli.Files;

namespace Jason.Cli.Tests.Files;

/// <summary>
/// The rules a name in an archive is held to, shared by the skills package, the archive <c>jason export</c> writes and the
/// one <c>jason import</c> reads: one row for each way a name can escape where it is placed or be written as another, and
/// the names that look odd and are plain.
/// </summary>
public sealed class ArchiveNamesTests
{
    [Theory]
    [InlineData("", nameof(NameRefusal.Empty))]
    [InlineData("/data/rooted", nameof(NameRefusal.Rooted))]
    [InlineData(@"data\config\settings.json", nameof(NameRefusal.Backslash))]
    [InlineData("C:/data/rooted", nameof(NameRefusal.Drive))]
    [InlineData("c:data", nameof(NameRefusal.Drive))]
    [InlineData("data//config", nameof(NameRefusal.EmptySegment))]
    [InlineData("data/", nameof(NameRefusal.EmptySegment))]
    [InlineData("data/./config", nameof(NameRefusal.DotSegment))]
    [InlineData("data/../../escaped", nameof(NameRefusal.ParentSegment))]
    [InlineData("..", nameof(NameRefusal.ParentSegment))]
    [InlineData("data/con\u0001fig", nameof(NameRefusal.ControlCharacter))]
    [InlineData("data/con:fig", nameof(NameRefusal.Colon))]
    [InlineData("data/con*fig", nameof(NameRefusal.Asterisk))]
    [InlineData("data/con?fig", nameof(NameRefusal.QuestionMark))]
    [InlineData("data/con\"fig", nameof(NameRefusal.QuotationMark))]
    [InlineData("data/con<fig", nameof(NameRefusal.LessThan))]
    [InlineData("data/con>fig", nameof(NameRefusal.GreaterThan))]
    [InlineData("data/con|fig", nameof(NameRefusal.VerticalBar))]
    [InlineData("data/config.", nameof(NameRefusal.TrailingDot))]
    [InlineData("data/config ", nameof(NameRefusal.TrailingSpace))]
    [InlineData("harness/CON", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/prn.md", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/Aux", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/nul.tar.gz", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/COM1", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/com9.log", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/LPT1", nameof(NameRefusal.DeviceName))]
    [InlineData("harness/lpt9.txt/notes.md", nameof(NameRefusal.DeviceName))]
    public void A_name_that_escapes_or_is_written_as_another_is_refused_by_its_own_rule(string name, string rule)
    {
        Assert.Equal(rule, ArchiveNames.Refusal(name)?.ToString());
        Assert.False(ArchiveNames.Plain(name), $"'{name}' was taken as plain.");
    }

    [Theory]
    [InlineData("export.json")]
    [InlineData("data/state/jason.db")]
    [InlineData("data/skills/roles/.jason-skills.json")]
    [InlineData("harness/acme/.jason/marker.json")]
    [InlineData("harness/acme/notes about q4.md")]
    [InlineData("harness/console.md")]
    [InlineData("harness/COM0")]
    [InlineData("harness/LPT10")]
    [InlineData("harness/nul-and-void.md")]
    [InlineData("harness/acme.con")]
    public void A_plain_name_is_taken(string name)
    {
        Assert.Null(ArchiveNames.Refusal(name));
        Assert.True(ArchiveNames.Plain(name));
    }

    [Fact]
    public void An_entry_whose_mode_says_link_is_a_link_and_a_file_is_not()
    {
        using var stream = new MemoryStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var link = archive.CreateEntry("harness/linked.md");
        link.ExternalAttributes = unchecked((int)0xA1FF0000);
        var file = archive.CreateEntry("harness/plain.md");
        file.ExternalAttributes = unchecked((int)0x81A40000);

        Assert.True(ArchiveNames.IsLink(link));
        Assert.False(ArchiveNames.IsLink(file));
    }
}
