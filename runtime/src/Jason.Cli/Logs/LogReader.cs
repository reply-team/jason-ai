using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace Jason.Cli.Logs;

/// <summary>What reading the end of the log came to.</summary>
/// <param name="Events">The events that matched, oldest first, at most the limit that was asked for.</param>
/// <param name="LimitReached">True when more events matched than the limit allowed, so older ones were left out.</param>
/// <param name="SkippedLines">
/// Lines that were read and are not events: not a JSON object, or longer than <see cref="LogReader.MaxLineBytes"/>.
/// A line a writer has not finished is not among them, and neither is a line a byte budget cut short.
/// </param>
/// <param name="BytesExhausted">
/// True when the byte budget ended the reading before the limit did and log was left unread: older events may exist
/// that this does not show.
/// </param>
public sealed record LogTail(IReadOnlyList<JsonObject> Events, bool LimitReached, int SkippedLines, bool BytesExhausted);

/// <summary>
/// Reads the end of the runtime's log, newest first, without ever contending with the writer that is appending to it.
/// </summary>
/// <remarks>
/// <para>
/// The runtime holds its live file open for writing and shares it for reading only. A reader that does not share
/// writing is refused such a file on Windows ("being used by another process"), so every file here is opened for
/// reading with writing and deletion shared: the runtime never waits for a reader, and a retention sweep never waits
/// for one either. The length is read once, when the file is opened, and what the writer appends afterwards is left
/// for the next read.
/// </para>
/// <para>
/// A file is read in blocks from its end, which is where the answer to "the latest N events" is, rather than from its
/// start, so a file of hundreds of megabytes costs what the events asked for cost. Lines are split on the line break
/// as bytes and decoded afterwards, so a block boundary falling inside a multi-byte character cannot damage one.
/// </para>
/// <para>
/// Nothing in a log file is trusted to be a line the runtime wrote. A line that is not a JSON object is skipped and
/// counted; a line longer than <see cref="MaxLineBytes"/> is skipped and counted without being held in memory; what a
/// writer has not yet finished is not a damaged line, and is left for the next read.
/// </para>
/// </remarks>
public static class LogReader
{
    /// <summary>How much of a file is read at a time.</summary>
    private const int BlockSize = 64 * 1024;

    /// <summary>
    /// The longest line that is read. An event the runtime writes is a few hundred bytes; a line this long is a
    /// damaged or foreign file, and holding it would cost more than anything it could tell.
    /// </summary>
    public const int MaxLineBytes = 1024 * 1024;

    /// <summary>
    /// The newest <paramref name="limit"/> events that match <paramref name="filter"/>, oldest first.
    /// </summary>
    /// <param name="files">The files to read, in any order: they are read newest first by the day in the name and then the roll.</param>
    /// <param name="filter">What the events must match.</param>
    /// <param name="limit">How many events to return, at least one.</param>
    /// <param name="maxBytes">
    /// The most bytes to read in all, or null for no bound. A line is returned only when the reader has seen where it
    /// begins — the line break that ends the line before it, or the start of the file — so a budget that ends exactly
    /// at a line's first byte does not return that line.
    /// </param>
    public static LogTail Tail(IReadOnlyList<LogFile> files, LogFilter filter, int limit, long? maxBytes = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "A byte budget cannot be negative.");
        }

        var scan = new Scan(filter, limit, maxBytes);
        foreach (var file in Newest(files, filter.Since))
        {
            if (scan.Done)
            {
                break;
            }

            scan.Read(file);
        }

        return scan.Result();
    }

    /// <summary>
    /// The files that could hold what <paramref name="since"/> asks for, newest first. A file is named for the local
    /// date of the machine that started it, which is within one day of the date in UTC wherever that machine is, so a
    /// file named for a day more than one before the date of <paramref name="since"/> holds nothing since then.
    /// </summary>
    private static IEnumerable<LogFile> Newest(IReadOnlyList<LogFile> files, DateTimeOffset? since)
    {
        var earliestDay = since is { } at ? DateOnly.FromDateTime(at.UtcDateTime).DayNumber - 1 : int.MinValue;
        return files
            .Where(file => file.Day.DayNumber >= earliestDay)
            .OrderByDescending(file => file.Day)
            .ThenByDescending(file => file.Sequence);
    }

    private static readonly JsonDocumentOptions NoDuplicateProperties = new() { AllowDuplicateProperties = false };

    /// <summary>One read: the events collected so far, newest first, and how much has been spent.</summary>
    private sealed class Scan(LogFilter filter, int limit, long? maxBytes)
    {
        private readonly List<JsonObject> newestFirst = [];
        private readonly byte[] block = new byte[BlockSize];
        private readonly Line line = new();
        private long bytesRead;
        private int skipped;
        private bool exhausted;

        /// <summary>One more event than the limit proves that older ones were left out; the byte budget ends it too.</summary>
        public bool Done => exhausted || newestFirst.Count > limit;

        public LogTail Result() =>
            new([.. newestFirst.Take(limit).Reverse()], newestFirst.Count > limit, skipped, exhausted);

        public void Read(LogFile file)
        {
            SafeFileHandle handle;
            try
            {
                // Sharing writing and deletion is the point: the runtime holds this file for writing.
                handle = File.OpenHandle(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                return; // removed between the listing and now
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            using (handle)
            {
                ReadBackwards(handle);
            }
        }

        private void ReadBackwards(SafeFileHandle handle)
        {
            line.Clear();
            var position = RandomAccess.GetLength(handle);

            // Whatever follows the last line break is a line its writer has not finished.
            var unfinished = true;

            while (position > 0 && !Done)
            {
                var want = (int)Math.Min(BlockSize, position);
                if (maxBytes is { } budget)
                {
                    var left = budget - bytesRead;
                    if (left <= 0)
                    {
                        exhausted = true;
                        return;
                    }

                    want = (int)Math.Min(want, left);
                }

                position -= want;
                if (!Fill(handle, block.AsSpan(0, want), position))
                {
                    return; // the file shrank under the reader: what is left of it is not there to read
                }

                bytesRead += want;

                var bytes = block.AsSpan(0, want);
                if (unfinished)
                {
                    var lastBreak = bytes.LastIndexOf((byte)'\n');
                    if (lastBreak < 0)
                    {
                        continue; // still inside the unfinished line
                    }

                    unfinished = false;
                    bytes = bytes[..lastBreak];
                }

                Feed(bytes, startOfFile: position == 0);
            }
        }

        private static bool Fill(SafeFileHandle handle, Span<byte> into, long position)
        {
            while (!into.IsEmpty)
            {
                var read = RandomAccess.Read(handle, into, position);
                if (read == 0)
                {
                    return false;
                }

                into = into[read..];
                position += read;
            }

            return true;
        }

        /// <summary>
        /// Takes the bytes of one block, which end where a line ends, newest line last: every line in it that begins
        /// inside it is finished, and what comes before its first line break is the end of a line that began in an
        /// earlier block.
        /// </summary>
        private void Feed(ReadOnlySpan<byte> bytes, bool startOfFile)
        {
            while (!Done)
            {
                var lineBreak = bytes.LastIndexOf((byte)'\n');
                if (lineBreak < 0)
                {
                    break;
                }

                Finish(bytes[(lineBreak + 1)..]);
                bytes = bytes[..lineBreak];
            }

            if (Done)
            {
                return;
            }

            line.Prepend(bytes);
            if (startOfFile)
            {
                Finish([]);
            }
        }

        /// <summary>A line is complete: <paramref name="beginning"/> is its first bytes and the rest is held in <see cref="line"/>.</summary>
        private void Finish(ReadOnlySpan<byte> beginning)
        {
            if (line.TooLong || line.Length + beginning.Length > MaxLineBytes)
            {
                skipped++;
            }
            else if (line.IsEmpty)
            {
                Accept(beginning);
            }
            else
            {
                Accept(line.Assemble(beginning));
            }

            line.Clear();
        }

        private void Accept(ReadOnlySpan<byte> raw)
        {
            // Windows writes the line break as a carriage return and a line feed.
            raw = raw.TrimEnd((byte)'\r');
            if (raw.IsEmpty)
            {
                return;
            }

            if (Parse(Encoding.UTF8.GetString(raw)) is not { } evt)
            {
                skipped++;
            }
            else if (filter.Matches(evt))
            {
                newestFirst.Add(evt);
            }
        }

        /// <summary>
        /// A line that is a JSON object whose text is text, or null. A property named twice is refused here, at the
        /// parse: the parser accepts it, and the object then throws on the first property anybody reads from it — a
        /// failure a caller would meet far from the line that caused it. So is an unpaired surrogate written as a JSON
        /// escape: it parses, and is no text that anybody can read, filter on or write out. In a property name the
        /// duplicate check is where it throws, and in a value it is the first read of the value.
        /// </summary>
        private static JsonObject? Parse(string text)
        {
            try
            {
                var evt = JsonNode.Parse(text, documentOptions: NoDuplicateProperties) as JsonObject;
                return evt is not null && HoldsOnlyText(text, evt) ? evt : null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether every string in the event can be read. Only a line that spells a surrogate as an escape can hold one
        /// that cannot, so every other line — nearly all of them — is not walked.
        /// </summary>
        private static bool HoldsOnlyText(string line, JsonNode node)
        {
            if (line.AsSpan().IndexOf(@"\ud", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }

            try
            {
                Read(node);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void Read(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject members:
                    foreach (var member in members)
                    {
                        Read(member.Value);
                    }

                    break;
                case JsonArray items:
                    foreach (var item in items)
                    {
                        Read(item);
                    }

                    break;
                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    _ = value.GetValue<string>();
                    break;
            }
        }
    }

    /// <summary>
    /// A line being put back together from its end towards its start, as the blocks that hold it are read. Once it is
    /// longer than <see cref="MaxLineBytes"/> what has been gathered is let go and the rest of it is only passed over.
    /// </summary>
    private sealed class Line
    {
        /// <summary>The pieces, the one nearest the end of the line first.</summary>
        private readonly List<byte[]> pieces = [];

        public int Length { get; private set; }

        public bool TooLong { get; private set; }

        public bool IsEmpty => pieces.Count == 0;

        /// <summary>Adds the next piece towards the start of the line.</summary>
        public void Prepend(ReadOnlySpan<byte> piece)
        {
            if (TooLong || piece.IsEmpty)
            {
                return;
            }

            if (Length + piece.Length > MaxLineBytes)
            {
                TooLong = true;
                pieces.Clear();
                Length = 0;
                return;
            }

            pieces.Add(piece.ToArray());
            Length += piece.Length;
        }

        /// <summary>The whole line: its first bytes, then every piece gathered, nearest the start first.</summary>
        public byte[] Assemble(ReadOnlySpan<byte> beginning)
        {
            var whole = new byte[beginning.Length + Length];
            beginning.CopyTo(whole);
            var offset = beginning.Length;
            for (var i = pieces.Count - 1; i >= 0; i--)
            {
                pieces[i].CopyTo(whole, offset);
                offset += pieces[i].Length;
            }

            return whole;
        }

        public void Clear()
        {
            pieces.Clear();
            Length = 0;
            TooLong = false;
        }
    }
}
