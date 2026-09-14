using System.Text;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// The child's whole stderr, kept in its working directory and read to the end whatever it holds. Three things
/// matter. The reading never stops, because a pipe nobody empties fills up and blocks the child writing into it.
/// Every line goes through the redactor, so a plugin that echoes a granted credential cannot leave it in a file
/// the user keeps. And the last of it is remembered, because a protocol failure with no trace explains nothing.
/// </summary>
/// <remarks>
/// A line is not assumed to be JSON: the host writes JSON Lines, but the one exit code that reports a host
/// failure writes a plain-text exception instead, and that is exactly the text worth keeping.
/// </remarks>
public sealed class StderrSink(string file, Redactor redactor, int maxBytes, int tailChars = 4096)
{
    /// <summary>Written once, in place of everything past the cap, so the file never lies by omission.</summary>
    public const string TruncationNotice = "[stderr truncated]";

    private readonly StringBuilder _tail = new();
    private long _written;
    private bool _silenced;

    /// <summary>The last of the child's stderr, redacted — the trace a failed invocation travels with.</summary>
    public string Tail => _tail.ToString();

    /// <summary>Whether the child wrote more than the cap allowed to be kept.</summary>
    public bool Truncated => _silenced;

    public async Task PumpAsync(StreamReader source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(redactor);

        await using var writer = new StreamWriter(file, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            // A file that survives a killed runtime is worth more than a buffered one.
            AutoFlush = true,
        };

        while (await source.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var redacted = redactor.Redact(line);
            _tail.AppendLine(redacted);
            if (_tail.Length > tailChars)
            {
                _tail.Remove(0, _tail.Length - tailChars);
            }

            if (_silenced)
            {
                continue;
            }

            _written += Encoding.UTF8.GetByteCount(redacted) + Environment.NewLine.Length;
            if (_written > maxBytes)
            {
                _silenced = true;
                await writer.WriteLineAsync(TruncationNotice).ConfigureAwait(false);
                continue;
            }

            await writer.WriteLineAsync(redacted).ConfigureAwait(false);
        }
    }
}
