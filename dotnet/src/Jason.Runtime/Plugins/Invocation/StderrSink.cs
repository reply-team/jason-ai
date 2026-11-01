using System.Text;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// The child's whole stderr, kept in its working directory and read to the end whatever it holds. Four things
/// matter. The reading never stops, because a pipe nobody empties fills up and blocks the child writing into it.
/// It arrives in fixed chunks and is assembled into lines only up to a bound, so how much of this runtime's
/// memory a child's diagnostics take is this runtime's decision and not the child's. Every line goes through the
/// redactor, so a plugin that echoes a granted credential cannot leave it in a file the user keeps. And the last
/// of it is remembered, because a protocol failure with no trace explains nothing.
/// </summary>
/// <remarks>
/// A line is not assumed to be JSON: the host writes JSON Lines, but the one exit code that reports a host
/// failure writes a plain-text exception instead, and that is exactly the text worth keeping.
/// </remarks>
public sealed class StderrSink(
    string file,
    Redactor redactor,
    int maxBytes,
    int tailChars = 4096,
    int maxLineChars = StderrSink.DefaultLineChars)
{
    /// <summary>Written once, in place of everything past the cap, so the file never lies by omission.</summary>
    public const string TruncationNotice = "[stderr truncated]";

    /// <summary>Written in place of a line too long to assemble, so the gap is visible rather than silent.</summary>
    public const string DroppedLineNotice = "[stderr line dropped: longer than a line may be]";

    /// <summary>
    /// How much of one line is assembled before the line is given up on. It is far above the longest line the
    /// host can legitimately write — a message of 4096 characters beside a data payload of the log-line cap,
    /// each escaped again on its way into the line's JSON — and far below anything that matters as memory. The
    /// point of the bound is only that the child cannot choose it.
    /// </summary>
    public const int DefaultLineChars = 131_072;

    /// <summary>Read in fixed pieces, the way every other pipe in the protocol is read.</summary>
    private const int ChunkChars = 16 * 1024;

    private readonly StringBuilder _line = new();
    private readonly StringBuilder _tail = new();

    /// <summary>Held over the tail's append and its read, which happen on different threads. See <see cref="Freeze"/>.</summary>
    private readonly Lock _tailGate = new();

    private string? _frozen;
    private long _written;
    private bool _silenced;
    private bool _dropping;
    private bool _afterCarriageReturn;

    /// <summary>The last of the child's stderr, redacted — the trace a failed invocation travels with.</summary>
    public string Tail
    {
        get
        {
            lock (_tailGate)
            {
                return _frozen ?? _tail.ToString();
            }
        }
    }

    /// <summary>Whether the child wrote more than the cap allowed to be kept.</summary>
    public bool Truncated => _silenced;

    /// <summary>
    /// Says that the caller has let go: the tail stops moving and every later read answers with what stood here
    /// at this moment. The caller is the invoker, which waits for this pump only as long as the kill grace and
    /// then abandons it — a child can leave something behind holding its stderr open, and that helper goes on
    /// writing into this sink long after the invocation has its answer. Freezing is what makes the trace that
    /// travels with a protocol failure the trace of the invocation rather than of whatever outlived it.
    /// </summary>
    /// <remarks>
    /// The file is a separate matter and keeps its copy of everything: a durable record of what a child said is
    /// worth more than a tidy end, and nothing reads it back into an answer.
    /// </remarks>
    public void Freeze()
    {
        lock (_tailGate)
        {
            _frozen ??= _tail.ToString();
        }
    }

    public async Task PumpAsync(StreamReader source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(redactor);

        await using var writer = new StreamWriter(file, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            // A file that survives a killed runtime is worth more than a buffered one.
            AutoFlush = true,
        };

        var buffer = new char[ChunkChars];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            // The cap counts what the child wrote rather than what survives the rules below: a child that floods
            // this stream is stopped from filling the file whether or not any of the flood looks like a line.
            _written += Encoding.UTF8.GetByteCount(buffer, 0, read);

            for (var index = 0; index < read; index++)
            {
                await TakeAsync(writer, buffer[index]).ConfigureAwait(false);
            }
        }

        // A last line the child never terminated is still a line; nothing is lost to a missing newline.
        if (_line.Length > 0 || _dropping)
        {
            await EmitAsync(writer).ConfigureAwait(false);
        }
    }

    /// <summary>One character of the stream, ending a line the way <c>ReadLine</c> would: on CR, LF or CRLF.</summary>
    private async Task TakeAsync(StreamWriter writer, char character)
    {
        var afterCarriageReturn = _afterCarriageReturn;
        _afterCarriageReturn = false;

        switch (character)
        {
            case '\n' when afterCarriageReturn:
                // The second half of a line ending the carriage return already ended.
                return;

            case '\n':
                await EmitAsync(writer).ConfigureAwait(false);
                return;

            case '\r':
                _afterCarriageReturn = true;
                await EmitAsync(writer).ConfigureAwait(false);
                return;

            default:
                if (_line.Length < maxLineChars)
                {
                    _line.Append(character);
                }
                else
                {
                    _dropping = true;
                }

                return;
        }
    }

    /// <summary>
    /// One finished line, to the file and to the tail. A line is kept or dropped whole: a cut line hands a JSON
    /// Lines consumer a fragment, and the redactor masks a value only where it can see the whole of it, so the
    /// half of a long line that fitted could be the half that carries a secret.
    /// </summary>
    private async Task EmitAsync(StreamWriter writer)
    {
        var text = _dropping ? DroppedLineNotice : redactor.Redact(_line.ToString());
        _line.Clear();
        _dropping = false;

        // Under the gate, because the reader of the tail is another thread entirely and a builder trimmed while
        // it is being read does not merely hand back a torn string: the read throws.
        lock (_tailGate)
        {
            if (_frozen is null)
            {
                _tail.AppendLine(text);
                if (_tail.Length > tailChars)
                {
                    _tail.Remove(0, _tail.Length - tailChars);
                }
            }
        }

        if (_silenced)
        {
            return;
        }

        if (_written > maxBytes)
        {
            _silenced = true;
            await writer.WriteLineAsync(TruncationNotice).ConfigureAwait(false);
            return;
        }

        await writer.WriteLineAsync(text).ConfigureAwait(false);
    }
}
