using System.Text;

namespace Jason.Runtime.Execution;

/// <summary>
/// Drains one of a child's output pipes into a file in its work directory. Two things matter here. The reading
/// never stops until end of file, because a pipe nobody empties fills up and blocks the child writing into it —
/// a hung executor caused by its own diagnostics would be indistinguishable from a hung agent. And every line
/// goes through the redactor on its way out, so a host that echoes its own credentials cannot leave the
/// capability token in an artifact the user keeps.
/// </summary>
public static class OutputPump
{
    /// <summary>
    /// Reads until end of file, redacts each line, appends it to <paramref name="file"/> as UTF-8 flushed line
    /// by line — a file that survives a killed runtime is worth more than a buffered one — and, when
    /// <paramref name="tail"/> is given, keeps the last <paramref name="tailLimit"/> characters of the redacted
    /// text for whoever has to explain the exit.
    /// </summary>
    public static async Task PumpAsync(StreamReader source, string file, TokenRedactor redactor, StringBuilder? tail, int tailLimit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(redactor);

        await using var writer = new StreamWriter(file, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        while (await source.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var redacted = redactor.Redact(line);
            await writer.WriteLineAsync(redacted).ConfigureAwait(false);

            if (tail is null)
            {
                continue;
            }

            tail.AppendLine(redacted);
            if (tail.Length > tailLimit)
            {
                tail.Remove(0, tail.Length - tailLimit);
            }
        }
    }
}
