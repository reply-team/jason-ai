using System.Text;
using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// The layout stamp, <c>&lt;data&gt;/layout.json</c>: read once by the paths, never thrown from, and refused where it names a
/// layout newer than this build runs or cannot be read — never taken for layout 1 when it is there and unreadable.
/// </summary>
public sealed class LayoutStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-layout-stamp-" + Guid.NewGuid().ToString("N"));

    public LayoutStampTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string StampPath => Path.Combine(_root, "layout.json");

    private void Write(string text) => File.WriteAllText(StampPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    [Fact]
    public void No_file_is_layout_one_and_refuses_nothing()
    {
        var paths = new JasonPaths(_root);

        Assert.Equal(LayoutStampState.Missing, paths.Stamp!.State);
        Assert.Equal(1, paths.Layout);
        Assert.Null(LayoutStamp.Refusal(paths, "0.42.0"));
    }

    [Fact]
    public void A_stamp_of_layout_one_reads_and_runs()
    {
        Write("""{"layout":1,"written_by":"0.43.0","at":"2026-11-20T10:00:00Z"}""");

        var paths = new JasonPaths(_root);

        Assert.Equal(new LayoutStamp(LayoutStampState.Read, StampPath, 1, "0.43.0", "2026-11-20T10:00:00Z", null), paths.Stamp);
        Assert.Equal(1, paths.Layout);
        Assert.Null(LayoutStamp.Refusal(paths, "0.42.0"));
    }

    [Theory]
    [InlineData("""{"layout":2,"written_by":"0.43.0"}""", 2)]
    [InlineData("""{"layout":7,"written_by":"1.4.0","at":"2027-01-01T00:00:00Z","moved":["plugins"]}""", 2)]
    public void A_later_layout_is_answered_by_the_newest_table_and_refused(string text, int answered)
    {
        Write(text);

        var paths = new JasonPaths(_root);

        Assert.Equal(LayoutStampState.Read, paths.Stamp!.State);
        Assert.Equal(answered, paths.Layout);
        Assert.Equal(LayoutStamp.NewerCode, LayoutStamp.Refusal(paths, "0.42.0")!.Value.Code);
    }

    [Fact]
    public void The_refusal_of_a_newer_layout_says_the_sentence_exactly()
    {
        Write("""{"layout":2,"written_by":"0.43.0","at":"2026-11-20T10:00:00Z"}""");

        var refusal = LayoutStamp.Refusal(new JasonPaths(_root), "0.42.0");

        Assert.Equal(
            (LayoutStamp.NewerCode, "This data directory was set up by Jason 0.43.0, newer than this jason (0.42.0): update this jason, or use the one that set it up."),
            refusal);
    }

    /// <summary>
    /// The version a stamp names reaches a screen only as the version: what the parse reads past — space around it, build
    /// metadata after a <c>+</c>, a terminal's escape hidden there — is never carried, so a stamp cannot write text of its
    /// own into a refusal or a report.
    /// </summary>
    [Theory]
    [InlineData("0.43.0+x")]
    [InlineData(" 0.43.0")]
    [InlineData("\\n0.43.0\\n")]
    [InlineData("0.43.0+\\u001b[2J\\u001b[31mupdate now")]
    public void A_version_is_carried_as_the_version_alone(string written)
    {
        Write("{\"layout\":2,\"written_by\":\"" + written + "\"}");

        var paths = new JasonPaths(_root);

        Assert.Equal("0.43.0", paths.Stamp!.WrittenBy);
        Assert.Equal((LayoutStamp.NewerCode, LayoutStamp.NewerSentence("0.43.0", "0.42.0")), LayoutStamp.Refusal(paths, "0.42.0"));
    }

    [Theory]
    [InlineData("{", "it is not JSON at line 1, byte 2")]
    [InlineData("""{"layout":2,"written_by":"0.43.0",}""", "it is not JSON at line 1, byte 35")]
    [InlineData("""{"layout":2 /* moved */,"written_by":"0.43.0"}""", "it is not JSON at line 1, byte 13")]
    [InlineData("[2]", "it is not a JSON object")]
    [InlineData("2", "it is not a JSON object")]
    [InlineData("""{"written_by":"0.43.0"}""", "it has no whole-number layout")]
    [InlineData("""{"layout":null,"written_by":"0.43.0"}""", "it has no whole-number layout")]
    [InlineData("""{"layout":"2","written_by":"0.43.0"}""", "it has no whole-number layout")]
    [InlineData("""{"layout":2.5,"written_by":"0.43.0"}""", "it has no whole-number layout")]
    [InlineData("""{"layout":4294967296,"written_by":"0.43.0"}""", "it has no whole-number layout")]
    [InlineData("""{"layout":0,"written_by":"0.43.0"}""", "its layout is 0, and layouts count from 1")]
    [InlineData("""{"layout":-1,"written_by":"0.43.0"}""", "its layout is -1, and layouts count from 1")]
    [InlineData("""{"layout":2}""", "it names no version that wrote it")]
    [InlineData("""{"layout":2,"written_by":7}""", "it names no version that wrote it")]
    [InlineData("""{"layout":2,"written_by":"a newer one"}""", "it names no version that wrote it")]
    [InlineData("""{"layout":2,"written_by":"0.43.0","at":20261120}""", "its at is not text")]
    [InlineData("""{"layout":2,"written_by":"0.43.0","layout":1}""", "it names a member twice")]
    public void A_stamp_that_cannot_be_read_is_unreadable_with_its_reason_and_refused(string text, string reason)
    {
        Write(text);

        var paths = new JasonPaths(_root);

        Assert.Equal(new LayoutStamp(LayoutStampState.Unreadable, StampPath, null, null, null, reason), paths.Stamp);
        Assert.Equal(1, paths.Layout);
        Assert.Equal(
            (LayoutStamp.UnreadableCode, $"This data directory's layout stamp at {StampPath} cannot be read ({reason}), so this jason cannot tell how the directory is laid out: use the Jason that set it up, or update this jason."),
            LayoutStamp.Refusal(paths, "0.42.0"));
    }

    /// <summary>
    /// Bytes no text can be made of, and an escape no text can be made of — half of a surrogate pair — are a stamp that is
    /// not JSON, at the place they stand: the parser takes both, and only reading the string would throw.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x7B, 0x22, 0x6C, 0x61, 0x79, 0x6F, 0x75, 0x74, 0x22, 0x3A, 0x31, 0x2C, 0x22, 0x77, 0x72, 0x69, 0x74, 0x74, 0x65, 0x6E, 0x5F, 0x62, 0x79, 0x22, 0x3A, 0x22, 0x30, 0x2E, 0x34, 0x32, 0x2E, 0x30, 0xFF, 0x22, 0x7D }, "it is not JSON at line 1, byte 33")]
    [InlineData(new byte[] { 0x7B, 0x0A, 0x22, 0x6C, 0xC3, 0x22, 0x3A, 0x31, 0x7D }, "it is not JSON at line 2, byte 3")]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x7B, 0x22, 0xED, 0xA0, 0x80, 0x22, 0x3A, 0x31, 0x7D }, "it is not JSON at line 1, byte 3")]
    public void Bytes_that_are_not_UTF_8_are_not_JSON_where_they_stand(byte[] bytes, string reason)
    {
        File.WriteAllBytes(StampPath, bytes);

        var stamp = new JasonPaths(_root).Stamp!;

        Assert.Equal(new LayoutStamp(LayoutStampState.Unreadable, StampPath, null, null, null, reason), stamp);
    }

    [Theory]
    [InlineData("""{"layout":1,"written_by":"0.42.0\uD800"}""", "it is not JSON at line 1, byte 26")]
    [InlineData("""{"layout":1,"written_by":"0.42.0","at":"\uDC00"}""", "it is not JSON at line 1, byte 40")]
    [InlineData("""{"layout":1,"\uD800x":2,"written_by":"0.42.0"}""", "it is not JSON at line 1, byte 13")]
    [InlineData("{\"layout\":1,\n  \"written_by\":\"\\uD83D\"}", "it is not JSON at line 2, byte 16")]
    public void An_escape_that_is_half_a_surrogate_pair_is_not_JSON_where_its_string_starts(string text, string reason)
    {
        Write(text);

        Assert.Equal(reason, new JasonPaths(_root).Stamp!.Problem);
    }

    [Fact]
    public void A_whole_surrogate_pair_escaped_reads()
    {
        Write("""{"layout":1,"written_by":"0.42.0","at":"\uD83D\uDE00"}""");

        var stamp = new JasonPaths(_root).Stamp!;

        Assert.Equal(LayoutStampState.Read, stamp.State);
        Assert.Equal("\U0001F600", stamp.At);
    }

    [Fact]
    public void A_folder_at_the_name_is_unreadable()
    {
        Directory.CreateDirectory(StampPath);

        Assert.Equal("it is a folder", new JasonPaths(_root).Stamp!.Problem);
    }

    [Fact]
    public void A_file_larger_than_a_stamp_is_unreadable_without_being_read()
    {
        File.WriteAllBytes(StampPath, new byte[LayoutStamp.MaxBytes + 1]);

        Assert.Equal("it is larger than a stamp", new JasonPaths(_root).Stamp!.Problem);
    }

    [Fact]
    public void A_byte_order_mark_is_read_past()
    {
        File.WriteAllText(StampPath, """{"layout":1,"written_by":"0.43.0"}""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(LayoutStampState.Read, new JasonPaths(_root).Stamp!.State);
    }

    /// <summary>A reader shares the file for writing and deleting, so a writer holding it, as a rename into place does, never fails the read.</summary>
    [Fact]
    public void A_stamp_held_open_by_its_writer_still_reads()
    {
        Write("""{"layout":2,"written_by":"0.43.0"}""");
        using var held = new FileStream(StampPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(2, new JasonPaths(_root).Stamp!.Layout);
    }

    /// <summary>A file another program holds without sharing is said to be unreadable in a fixed phrase, never by the exception's name.</summary>
    [Fact]
    public void A_stamp_that_cannot_be_opened_could_not_be_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses an open that the holder does not share.");
        Write("""{"layout":2,"written_by":"0.43.0"}""");
        using var held = new FileStream(StampPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var stamp = new JasonPaths(_root).Stamp!;

        Assert.Equal(LayoutStampState.Unreadable, stamp.State);
        Assert.Equal("it could not be read", stamp.Problem);
        Assert.DoesNotContain("Exception", stamp.RefusalFor("0.42.0")!.Value.Message, StringComparison.Ordinal);
    }

    /// <summary>The stamp is read when the paths are made, once: a stamp written after is not seen by them.</summary>
    [Fact]
    public void The_stamp_is_read_once_when_the_paths_are_made()
    {
        var paths = new JasonPaths(_root);
        Write("""{"layout":2,"written_by":"0.43.0"}""");

        Assert.Equal(LayoutStampState.Missing, paths.Stamp!.State);
        Assert.Equal(1, paths.Layout);
        Assert.Equal(Path.Combine(paths.Root, "plugins"), paths.PluginsDirectory);
    }

    /// <summary>Paths given their layout read nothing and refuse nothing, whatever stamp is there.</summary>
    [Fact]
    public void Paths_given_a_layout_read_no_stamp_and_refuse_nothing()
    {
        Write("{");

        var paths = new JasonPaths(_root, 2);

        Assert.Null(paths.Stamp);
        Assert.Null(LayoutStamp.Refusal(paths, "0.42.0"));
    }
}
