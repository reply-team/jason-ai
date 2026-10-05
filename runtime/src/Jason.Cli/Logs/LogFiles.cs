using System.Globalization;
using System.Text.RegularExpressions;

namespace Jason.Cli.Logs;

/// <summary>One of the runtime's log files, as the directory listing showed it.</summary>
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
public sealed record LogFile(string Path, string Name, DateOnly Day, int Sequence, long Bytes, DateTimeOffset ModifiedAt);

/// <summary>
/// Which files under the log directory are the runtime's own, and the order they were written in.
/// </summary>
/// <remarks>
/// The runtime's log sink names its files <c>runtime-YYYYMMDD.jsonl</c> and, when a day's file reaches its size
/// limit, <c>runtime-YYYYMMDD_001.jsonl</c>, <c>_002</c> and so on. Only a name of exactly that shape directly in the
/// directory is a log. A diagnostic bundle kept in a directory beside them, a backup somebody made, a file another
/// program left that merely starts with the same letters: none of them is read as a log.
/// </remarks>
public static partial class LogFiles
{
    // The roll sequence is the sink's own: three digits or more, since it numbers with a width of three and a
    // thousandth roll is _1000. The name ends at the end of the string, never just before a final line break.
    [GeneratedRegex(@"^runtime-(?<day>[0-9]{8})(?:_(?<sequence>[0-9]{3,9}))?\.jsonl\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// The runtime's log files in the directory, oldest first: by the date in the name, then by the roll sequence
    /// compared as a number, because after <c>_999</c> the number grows a digit and the text no longer sorts. A
    /// directory that does not exist has none.
    /// </summary>
    public static IReadOnlyList<LogFile> In(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        var files = new List<LogFile>();
        try
        {
            foreach (var info in new DirectoryInfo(directory).EnumerateFiles("runtime-*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var match = NamePattern().Match(info.Name);
                if (!match.Success
                    || !DateOnly.TryParseExact(match.Groups["day"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    continue;
                }

                var sequence = match.Groups["sequence"].Success ? int.Parse(match.Groups["sequence"].Value, CultureInfo.InvariantCulture) : 0;
                try
                {
                    files.Add(new LogFile(info.FullName, info.Name, day, sequence, info.Length, info.LastWriteTimeUtc));
                }
                catch (FileNotFoundException)
                {
                    // Gone since it was listed, or a link to nothing: not a file to read.
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
