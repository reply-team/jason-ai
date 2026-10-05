using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Cli.Logs;

namespace Jason.Cli.Tests.Logs;

/// <summary>
/// Log files the way the runtime writes them: one compact JSON event per line, UTF-8 without a byte-order mark, the
/// level left out at Information, non-ASCII text written as it is rather than escaped.
/// </summary>
internal static class LogFixtures
{
    /// <summary>One event's line, without its line break.</summary>
    public static string Event(string time, string message, string? level = null, params (string Name, string Value)[] properties)
    {
        var line = new StringBuilder().Append("{\"@t\":\"").Append(time).Append("\",\"@mt\":").Append(Quote(message));
        if (level is not null)
        {
            line.Append(",\"@l\":\"").Append(level).Append('"');
        }

        foreach (var (name, value) in properties)
        {
            line.Append(',').Append(Quote(name)).Append(':').Append(Quote(value));
        }

        return line.Append('}').ToString();
    }

    /// <summary>
    /// A JSON string with only what JSON requires escaped: the quote, the backslash and the control characters. Every
    /// other character is written as itself, as the runtime's sink writes it, so a file made here holds the multi-byte
    /// UTF-8 a real log does. A serializer's own escaping would turn a character outside the Basic Multilingual Plane
    /// into an ASCII escape pair, and a file with no byte above 0x7F tests nothing about bytes.
    /// </summary>
    public static string Quote(string text)
    {
        var quoted = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case < ' ':
                    quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>A file of whole lines, each ended by a line break, in the directory.</summary>
    public static string Write(string directory, string name, params string[] lines) => WriteWithBreaks(directory, name, "\n", lines);

    /// <summary>The same, ending each line with the given line break: Windows writes <c>\r\n</c>.</summary>
    public static string WriteWithBreaks(string directory, string name, string lineBreak, params string[] lines)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, string.Concat(lines.Select(line => line + lineBreak)), new UTF8Encoding(false));
        return path;
    }

    public static string Message(JsonObject evt) => evt["@mt"]!.GetValue<string>();

    public static string[] Messages(LogTail tail) => [.. tail.Events.Select(Message)];

    public static LogTail Tail(string directory, LogFilter? filter = null, int limit = 1000, long? maxBytes = null) =>
        LogReader.Tail(LogFiles.In(directory), filter ?? new LogFilter(), limit, maxBytes);
}
