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
    [InlineData("", "an empty name")]
    [InlineData("data/../../escaped", "a parent segment")]
    [InlineData("..", "a parent segment alone")]
    [InlineData("/data/rooted", "a name from the root")]
    [InlineData("C:/data/rooted", "a name with a drive")]
    [InlineData("data\\config\\settings.json", "a backslash")]
    [InlineData("data//config", "an empty segment")]
    [InlineData("data/./config", "a dot segment")]
    [InlineData("data/config.", "a segment ending in a dot")]
    [InlineData("data/config ", "a segment ending in a space")]
    [InlineData("data/con:fig", "a colon")]
    [InlineData("data/con*fig", "an asterisk")]
    [InlineData("data/con?fig", "a question mark")]
    [InlineData("data/con\"fig", "a quotation mark")]
    [InlineData("data/con<fig>", "angle brackets")]
    [InlineData("data/con|fig", "a vertical bar")]
    [InlineData("data/con\u0001fig", "a control character")]
    public void A_name_that_escapes_or_is_written_as_another_is_refused(string name, string why)
    {
        Assert.False(ArchiveNames.Plain(name), $"{why}: '{name}' was taken as plain.");
    }

    [Theory]
    [InlineData("export.json")]
    [InlineData("data/state/jason.db")]
    [InlineData("data/skills/roles/.jason-skills.json")]
    [InlineData("harness/acme/.jason/marker.json")]
    [InlineData("harness/acme/notes about q4.md")]
    public void A_plain_name_is_taken(string name)
    {
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
