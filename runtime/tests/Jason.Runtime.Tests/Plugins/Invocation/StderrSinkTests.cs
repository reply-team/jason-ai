using System.Text;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The copy of a child's stderr, driven directly rather than through a process. What matters is that a child
/// cannot decide how much of this runtime's memory its diagnostics take: the stream arrives in fixed chunks and
/// a line is assembled only up to a bound. Whole lines still reach the file and the tail, because the stream is
/// JSON Lines by contract and because the redactor masks a value only where it sees the whole of it.
/// </summary>
public class StderrSinkTests : IDisposable
{
    private const string Secret = "token-0123456789abcdef";

    private readonly string _file = Path.Combine(Path.GetTempPath(), "jason-stderr-" + Guid.NewGuid().ToString("N") + ".log");

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        File.Delete(_file);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Whole_lines_survive_a_stream_that_arrives_a_few_characters_at_a_time()
    {
        // Every line ending the platform can produce, an empty line, and a last line the child never terminated.
        var sink = Sink();

        await sink.PumpAsync(Reader("first\nsecond\r\n\rfourth\nunterminated", step: 3));

        Assert.Equal(["first", "second", string.Empty, "fourth", "unterminated"], await LinesAsync());
        Assert.Contains("unterminated", sink.Tail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_split_between_two_reads_is_still_masked()
    {
        var sink = Sink();

        await sink.PumpAsync(Reader("the token is " + Secret + " and that is all\n", step: 4));

        var written = await File.ReadAllTextAsync(_file, Ct);
        Assert.DoesNotContain(Secret, written, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, written, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, sink.Tail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_longer_than_one_line_may_be_is_dropped_whole_and_noted()
    {
        var sink = Sink(maxLineChars: 64);

        await sink.PumpAsync(Reader(new string('x', 4096) + "\nshort line\n", step: 512));

        // Dropped, not cut: half a JSON line is not a line, and half a secret is a secret.
        Assert.Equal([StderrSink.DroppedLineNotice, "short line"], await LinesAsync());
        Assert.DoesNotContain("xxxx", sink.Tail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_inside_a_dropped_line_reaches_neither_the_file_nor_the_tail()
    {
        var sink = Sink(maxLineChars: 64);

        await sink.PumpAsync(Reader(new string('x', 200) + Secret + new string('y', 200) + "\n", step: 7));

        Assert.DoesNotContain(Secret, await File.ReadAllTextAsync(_file, Ct), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, sink.Tail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Everything_past_the_cap_is_replaced_by_one_notice()
    {
        var sink = Sink(maxBytes: 32);

        await sink.PumpAsync(Reader("kept\n" + new string('a', 64) + "\nalso dropped\n", step: 16));

        Assert.Equal(["kept", StderrSink.TruncationNotice], await LinesAsync());
        Assert.True(sink.Truncated);
    }

    [Fact]
    public async Task A_flood_with_no_line_ending_at_all_is_capped_rather_than_held()
    {
        var sink = Sink();

        await sink.PumpAsync(Reader(new string('x', 8 * 1024 * 1024), step: 64 * 1024));

        // Nothing of the flood is kept anywhere: not in the file, not in the tail the failure would travel with.
        Assert.DoesNotContain('x', await File.ReadAllTextAsync(_file, Ct));
        Assert.DoesNotContain('x', sink.Tail);
        Assert.True(sink.Tail.Length <= 4096, $"the tail grew to {sink.Tail.Length} characters");
    }

    /// <summary>
    /// The invoker reads the tail on every protocol-failure branch, and by then it may have abandoned the pump
    /// at the end of the kill grace — so the read and the append run at the same time. A <c>StringBuilder</c>
    /// read while it is being written does not merely tear: the read can throw, out of the invocation, which in
    /// this runtime strands the handler slot the attempt was holding rather than spoiling one diagnostic string.
    /// </summary>
    [Fact]
    public async Task Reading_the_tail_while_a_pump_is_still_writing_never_throws()
    {
        var sink = Sink();
        var pump = Task.Run(() => sink.PumpAsync(Reader(Flood(20_000), step: 4096)), Ct);

        Exception? thrown = null;
        while (!pump.IsCompleted)
        {
            try
            {
                _ = sink.Tail;
            }
            catch (Exception exception)
            {
                thrown = exception;
                break;
            }
        }

        await pump;
        Assert.True(thrown is null, $"reading the tail threw {thrown?.GetType().Name}: {thrown?.Message}");
    }

    /// <summary>
    /// What the invoker actually needs: the trace that travels with a protocol failure is the trace of this
    /// invocation. The pump is abandoned at the end of the kill grace, and a child can leave something behind
    /// holding its stderr open — so without a moment of letting go, the answer would go on changing under the
    /// caller that already acted on it.
    /// </summary>
    [Fact]
    public async Task A_tail_the_caller_has_let_go_of_does_not_move_under_it()
    {
        var sink = Sink(maxBytes: 4096);
        using var held = new HeldOpenStream("a line the helper goes on writing after the invocation has its answer\n");
        var pump = Task.Run(() => sink.PumpAsync(new StreamReader(held, Encoding.UTF8)), Ct);

        // The state the invoker finds the sink in: lines assembled, and a pump that has not finished — this one
        // cannot finish, because the stream ends only when this test says so.
        while (sink.Tail.Length == 0)
        {
            await Task.Yield();
        }

        sink.Freeze();
        var frozen = sink.Tail;
        Assert.NotEmpty(frozen);

        var served = held.Reads;
        while (held.Reads < served + 50)
        {
            await Task.Yield();
        }

        Assert.Equal(frozen, sink.Tail);

        held.Finish();
        await pump;
        Assert.Equal(frozen, sink.Tail);
    }

    /// <summary>
    /// A stream that never ends until it is told to, so a test can hold a pump in the middle of writing. This is
    /// the shape a helper a child left behind presents to the runtime: the pipe stays open and data keeps coming
    /// long after the invocation has its answer.
    /// </summary>
    private sealed class HeldOpenStream(string line) : Stream
    {
        private readonly byte[] _payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, 8)));
        private readonly ManualResetEventSlim _finished = new(false);
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Finish() => _finished.Set();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Interlocked.Increment(ref _reads);
            if (_finished.IsSet)
            {
                return 0;
            }

            var take = Math.Min(count, _payload.Length);
            Array.Copy(_payload, 0, buffer, offset, take);
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _finished.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static string Flood(int lines)
    {
        var text = new StringBuilder();
        for (var line = 0; line < lines; line++)
        {
            text.Append("a diagnostic line long enough that the tail has to be trimmed on every one of them ")
                .Append(line)
                .Append('\n');
        }

        return text.ToString();
    }

    private StderrSink Sink(int maxBytes = 4_194_304, int maxLineChars = StderrSink.DefaultLineChars) =>
        new(_file, new Redactor([Secret]), maxBytes, tailChars: 4096, maxLineChars: maxLineChars);

    /// <summary>Reads the file back as lines, which is how a JSON Lines consumer of it would.</summary>
    private async Task<string[]> LinesAsync() =>
        (await File.ReadAllTextAsync(_file, Ct)).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private static StreamReader Reader(string text, int step) =>
        new(new TrickleStream(Encoding.UTF8.GetBytes(text), step), Encoding.UTF8);

    /// <summary>
    /// A stream that hands over a few bytes at a time. A pipe behaves this way whenever the child is still
    /// writing, so every boundary the sink has to survive — inside a line, inside a secret, between a carriage
    /// return and its line feed — actually happens here rather than by luck.
    /// </summary>
    private sealed class TrickleStream(byte[] bytes, int step) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => bytes.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var take = Math.Min(step, Math.Min(count, bytes.Length - _position));
            Array.Copy(bytes, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
