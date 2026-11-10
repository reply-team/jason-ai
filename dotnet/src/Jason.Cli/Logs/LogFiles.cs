using System.Globalization;
using System.Text.RegularExpressions;

namespace Jason.Cli.Logs;

/// <summary>Which program wrote a log file: the runtime, or the desktop application beside it.</summary>
public enum LogSource
{
    /// <summary>The runtime's own log, <c>runtime-YYYYMMDD.jsonl</c>.</summary>
    Runtime,

    /// <summary>The desktop application's log, <c>desktop-YYYYMMDD.jsonl</c>, written in the runtime log's shape.</summary>
    Desktop,
}

/// <summary>One log file, as the directory listing showed it.</summary>
/// <param name="Path">The full path.</param>
/// <param name="Name">The file's name, which carries the day and the roll.</param>
/// <param name="Day">The date in the name: the local date of the machine when the file was started.</param>
/// <param name="Sequence">
/// Zero for the file a day starts in, then 1, 2 and so on for each file a size limit rolled it into.
/// </param>
/// <param name="Bytes">
/// The size the listing showed. A file the runtime is still writing can be longer than this says: a directory entry is
/// not updated on every write.
/// </param>
/// <param name="ModifiedAt">When the file was last written, as the listing showed it.</param>
/// <param name="Source">Which program wrote it, as its name says.</param>
public sealed record LogFile(
    string Path,
    string Name,
    DateOnly Day,
    int Sequence,
    long Bytes,
    DateTimeOffset ModifiedAt,
    LogSource Source = LogSource.Runtime);

/// <summary>
/// Which files under the log directory are a source's own, and the order they were written in.
/// </summary>
/// <remarks>
/// The runtime's log sink names its files <c>runtime-YYYYMMDD.jsonl</c> and, when a day's file reaches its size
/// limit, <c>runtime-YYYYMMDD_001.jsonl</c>, <c>_002</c> and so on. The desktop application names its own the same
/// way, with <c>desktop-</c> in front. Only a name of exactly that shape directly in the directory is a log, and each
/// source is listed apart. A diagnostic bundle kept in a directory beside them, a backup somebody made, a file another
/// program left that merely starts with the same letters: none of them is read as a log.
/// </remarks>
public static partial class LogFiles
{
    // The roll sequence is the sink's own: three digits or more, since it numbers with a width of three and a
    // thousandth roll is _1000. The name ends at the end of the string, never just before a final line break.
    [GeneratedRegex(@"^(?<source>runtime|desktop)-(?<day>[0-9]{8})(?:_(?<sequence>[0-9]{3,9}))?\.jsonl\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// One source's log files in the directory, the runtime's unless another is named, oldest first: by the date in
    /// the name, then by the roll sequence compared as a number, because after <c>_999</c> the number grows a digit
    /// and the text no longer sorts. A directory that does not exist has none.
    /// </summary>
    public static IReadOnlyList<LogFile> In(string directory, LogSource source = LogSource.Runtime)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var prefix = source switch
        {
            LogSource.Runtime => "runtime",
            LogSource.Desktop => "desktop",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not a log source."),
        };

        var files = new List<LogFile>();
        try
        {
            foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var match = NamePattern().Match(info.Name);
                if (!match.Success
                    || !string.Equals(match.Groups["source"].Value, prefix, StringComparison.Ordinal)
                    || !DateOnly.TryParseExact(match.Groups["day"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    continue;
                }

                var sequence = match.Groups["sequence"].Success ? int.Parse(match.Groups["sequence"].Value, CultureInfo.InvariantCulture) : 0;
                try
                {
                    files.Add(new LogFile(info.FullName, info.Name, day, sequence, info.Length, info.LastWriteTimeUtc, source));
                }
                catch (FileNotFoundException)
                {
                    // Gone since it was listed: not a file to read. Windows lists a file's size with its name, but
                    // elsewhere the listing carries the name alone and the size is read from the file when it is asked
                    // for, so the oldest file, removed by the runtime's retention between the two, is not there to
                    // measure. The gap is between two calls to the system, which no test can open on purpose.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        return [.. files.OrderBy(file => file.Day).ThenBy(file => file.Sequence)];
    }
}
