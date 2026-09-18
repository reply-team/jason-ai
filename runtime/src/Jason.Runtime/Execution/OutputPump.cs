using System.Globalization;
using System.Text;

namespace Jason.Runtime.Execution;

/// <summary>
/// Drains one of a child's output pipes into a file in its work directory. Three things matter here. The
/// reading never stops until end of file, because a pipe nobody empties fills up and blocks the child writing
/// into it — a hung executor caused by its own diagnostics would be indistinguishable from a hung agent. Every
/// line goes through the redactor on its way out, so a host that echoes its own credentials cannot leave the
/// capability token in an artifact the user keeps. And the file is bounded: an agent that streams its reasoning
/// writes a great deal and nothing cleans a work directory up, so past the maximum the file stops growing and
/// says where it was cut — while the reading goes on exactly as before.
/// <para>
/// A cut transcript never changes an outcome. The runtime reads no result from standard output: a result
/// reaches it through <c>workitem.set_result</c> and <c>workitem.complete</c>, and this file is only the record
/// of what a run looked like.
/// </para>
/// </summary>
public static class OutputPump
{
    /// <summary>
    /// The one line a cut file ends with: what stopped it, and which setting decided. A transcript that simply
    /// ends reads like a host that simply stopped, which is a different thing entirely.
    /// </summary>
    public static string CutMarker(int maxBytes) => string.Create(
        CultureInfo.InvariantCulture,
        $"[jason] The transcript passed {maxBytes} bytes and is cut here; Roles:MaxStdoutBytes says how much is kept. The child kept running, and no outcome depends on this file.");

    /// <summary>
    /// Reads until end of file, redacts each line, appends it to <paramref name="file"/> as UTF-8 flushed line
    /// by line — a file that survives a killed runtime is worth more than a buffered one — and, when
    /// <paramref name="tail"/> is given, keeps the last <paramref name="tailLimit"/> characters of the redacted
    /// text for whoever has to explain the exit.
    /// </summary>
    /// <param name="maxBytes">
    /// How much redacted text is written to the file. Past it the file is closed off with
    /// <see cref="CutMarker"/> and nothing more is written to it, while the pipe is still drained to the end and
    /// the tail still follows what the child says.
    /// </param>
    /// <returns>True when the file was cut short, which is what the attempt records about it.</returns>
    public static async Task<bool> PumpAsync(StreamReader source, string file, TokenRedactor redactor, StringBuilder? tail, int tailLimit, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(redactor);

        await using var writer = new StreamWriter(file, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        var written = 0L;
        var cut = false;

        while (await source.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var redacted = redactor.Redact(line);

            if (!cut)
            {
                var size = Encoding.UTF8.GetByteCount(redacted) + Environment.NewLine.Length;
                if (written + size > maxBytes)
                {
                    // The marker itself is written past the maximum on purpose: a file that says nothing about
                    // its own end would be the one thing worse than a long one.
                    cut = true;
                    await writer.WriteLineAsync(CutMarker(maxBytes)).ConfigureAwait(false);
                }
                else
                {
                    written += size;
                    await writer.WriteLineAsync(redacted).ConfigureAwait(false);
                }
            }

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

        return cut;
    }
}
