using System.Text;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Execution;

public class OutputPumpTests
{
    private const string Token = "tok-4f3a-canary";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TokenRedactor Redactor => new(new RuntimeSecrets(Token));

    /// <summary>More than any of these streams says: these tests are about the other rules.</summary>
    private const int Unbounded = 1024 * 1024;

    [Fact]
    public async Task Every_line_is_redacted_on_its_way_to_the_file()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stdout.log");

        await OutputPump.PumpAsync(Source($"first {Token} line\nplain line\n{Token}\n"), file, Redactor, null, 4096, Unbounded);

        var written = await File.ReadAllTextAsync(file, Ct);
        Assert.DoesNotContain(Token, written, StringComparison.Ordinal);
        Assert.Contains($"first {TokenRedactor.Mask} line", written, StringComparison.Ordinal);
        Assert.Contains("plain line", written, StringComparison.Ordinal);
        Assert.Equal(3, written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task A_last_line_without_a_newline_still_reaches_the_file()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stderr.log");

        await OutputPump.PumpAsync(Source("one\ntwo"), file, Redactor, null, 4096, Unbounded);

        var written = await File.ReadAllTextAsync(file, Ct);
        Assert.Contains("one", written, StringComparison.Ordinal);
        Assert.Contains("two", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tail_keeps_the_end_rather_than_the_beginning()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stderr.log");
        var tail = new StringBuilder();

        await OutputPump.PumpAsync(Source("aaaaaaaaaa\nbbbbbbbbbb\nthe last word\n"), file, Redactor, tail, 16, Unbounded);

        Assert.True(tail.Length <= 16, $"the tail kept {tail.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} characters");
        Assert.EndsWith("word", tail.ToString().TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("aaaa", tail.ToString(), StringComparison.Ordinal);

        // The file keeps everything; only what travels onto the attempt is cut short.
        Assert.Contains("aaaaaaaaaa", await File.ReadAllTextAsync(file, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tail_is_redacted_like_the_file()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stderr.log");
        var tail = new StringBuilder();

        await OutputPump.PumpAsync(Source($"leaked {Token}\n"), file, Redactor, tail, 4096, Unbounded);

        Assert.Contains(TokenRedactor.Mask, tail.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, tail.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stream_that_says_nothing_still_leaves_a_file_behind()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stdout.log");

        await OutputPump.PumpAsync(Source(string.Empty), file, Redactor, null, 4096, Unbounded);

        Assert.True(File.Exists(file));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(file, Ct));
    }

    [Fact]
    public async Task A_child_that_talks_past_the_cap_is_truncated_and_says_so()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stdout.log");
        var text = string.Join('\n', Enumerable.Range(0, 200).Select(line => $"line {line} of a very talkative agent")) + "\nthe last word\n";

        var truncated = await OutputPump.PumpAsync(Source(text), file, Redactor, null, 4096, maxBytes: 256);

        Assert.True(truncated);
        var written = await File.ReadAllTextAsync(file, Ct);
        Assert.Contains("line 0 of", written, StringComparison.Ordinal);
        Assert.DoesNotContain("the last word", written, StringComparison.Ordinal);

        // The file says where it stops and which setting decided, because a transcript that simply ends reads
        // like a host that simply stopped.
        Assert.Contains("Roles:MaxStdoutBytes", written, StringComparison.Ordinal);
        Assert.EndsWith(OutputPump.CutMarker(256) + Environment.NewLine, written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pipe_is_drained_to_the_end_long_after_the_file_stopped_growing()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stdout.log");
        var tail = new StringBuilder();
        var text = string.Join('\n', Enumerable.Range(0, 200).Select(line => $"line {line}")) + "\nthe last word\n";

        // A pipe nobody empties blocks the child writing into it, which is the bug this file exists to prevent.
        // The tail is the proof that the reading went on: it can only hold the end if the end was read.
        await OutputPump.PumpAsync(Source(text), file, Redactor, tail, 4096, maxBytes: 128);

        Assert.EndsWith("the last word", tail.ToString().TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_stays_inside_the_cap_is_kept_whole_and_says_nothing_about_it()
    {
        using var directory = new TempDataDir();
        var file = Path.Combine(directory.Paths.Root, "stdout.log");

        var truncated = await OutputPump.PumpAsync(Source("a short answer\n"), file, Redactor, null, 4096, maxBytes: 4096);

        Assert.False(truncated);
        Assert.Equal("a short answer" + Environment.NewLine, await File.ReadAllTextAsync(file, Ct));
    }

    private static StreamReader Source(string text) => new(new MemoryStream(Encoding.UTF8.GetBytes(text)), Encoding.UTF8);
}
