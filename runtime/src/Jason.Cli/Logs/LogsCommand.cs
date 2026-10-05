using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Commands;
using Jason.Contracts.Ids;

namespace Jason.Cli.Logs;

/// <summary>One log file as the answer lists it.</summary>
/// <param name="Name">The file's name, which carries the day and the roll.</param>
/// <param name="Bytes">The size the directory listing showed; a file still being written can be longer.</param>
/// <param name="ModifiedAt">When the file was last written.</param>
public sealed record LogFileEntry(string Name, long Bytes, DateTimeOffset ModifiedAt);

/// <summary>
/// The answer of <c>jason logs</c>: where it looked, what was there, and the events, oldest first, exactly as the
/// runtime wrote them.
/// </summary>
/// <param name="Directory">The directory it looked in.</param>
/// <param name="Exists">False when there is no such directory: no runtime has started on this data directory.</param>
/// <param name="Files">Every log file in the directory, oldest first, whether or not an event was taken from it.</param>
/// <param name="Events">The newest events that matched, oldest first. Their keys and properties are the runtime's, not renamed.</param>
/// <param name="Returned">How many events there are in <paramref name="Events"/>.</param>
/// <param name="LimitReached">True when more matched than the limit allowed, so older ones were left out.</param>
/// <param name="SkippedLines">Lines that were read and are not events.</param>
public sealed record LogsDocument(
    string Directory,
    bool Exists,
    IReadOnlyList<LogFileEntry> Files,
    IReadOnlyList<JsonObject> Events,
    int Returned,
    bool LimitReached,
    int SkippedLines);

/// <summary>
/// <c>jason logs</c>: what the runtime has written to this installation's log files, filtered, without asking the
/// runtime anything.
/// </summary>
/// <remarks>
/// <para>
/// One of the names in this CLI that stand for no API operation, and it cannot stand for one: the answer it exists to
/// give is the one a person needs when the runtime will not start, and a verb that asked the runtime where its logs
/// are, or whether it is running, would fail exactly then. The files are where the data-directory rule puts them —
/// the same rule that finds the runtime — and the runtime's descriptor is never read.
/// </para>
/// <para>
/// So it never exits 3. That code means the runtime could not be asked, and this verb does not ask it; "no log
/// directory" is an answer (<c>exists</c> is false) and a directory that is there and cannot be read is a refusal
/// (exit 1, <c>logs_unreadable</c>) that shows nothing rather than less than was written.
/// </para>
/// </remarks>
public static partial class LogsCommand
{
    /// <summary>What <c>--limit</c> is when it is not given.</summary>
    public const int DefaultLimit = 100;

    /// <summary>The most <c>--limit</c> accepts.</summary>
    public const int MaxLimit = 10_000;

    /// <summary>The width of a time in a human line, <c>2026-10-03T20:05:31Z</c>.</summary>
    private const int TimeWidth = 20;

    /// <summary>
    /// The contract, printed in help: an agent reading a failure, or choosing a filter, has this page and nothing
    /// else, so it says where the files are, what each word means, what comes back and what each exit code is.
    /// </summary>
    private const string Contract =
        """
        Reads the log files this installation's runtime writes. It never asks the runtime and never reads its descriptor,
        so it answers the same when the runtime is stopped, will not start, or has never run.

        Where: the logs directory of the data directory — ~/.jason/logs, or logs under JASON_DATA_DIR when that is set.
        The files read are the runtime's own, directly in that directory: runtime-YYYYMMDD.jsonl, and
        runtime-YYYYMMDD_001.jsonl, _002 and so on when a day's file reached its size limit. Nothing else in the
        directory is read.

        Filters — every one given must hold, and none is required; with none, everything written is shown:
          --level <level>   at or above: verbose, debug, information, warning, error, fatal, in any case (the names
                            Logging:MinimumLevel uses). A line that names no level is information.
          --campaign <id>   lines whose CampaignId is this campaign, as cmp_….
          --work-item <id>  lines whose WorkItemId is this work item, as wi_….
          --since <time>    lines stamped at or after this moment: ISO-8601 with Z or an offset, for example
                            2026-09-18T09:00:00Z. A time with neither is a usage error, never a guess at your time zone.
        Every line inside an attempt carries CampaignId, WorkItemId and AttemptId. Lines written by an older build name
        what they named then, so on an old log a campaign or work-item filter may find fewer lines than happened.

        Order and limit: the newest --limit matching events are returned, oldest first. --limit defaults to 100 and is
        at most 10000 (a value outside that is a usage error); limit_reached says when more matched than it allowed.

        Output: one JSON document — directory, exists, files, events, returned, limit_reached and skipped_lines.
        files lists each log file as name, bytes and modified_at. events are the lines exactly as the runtime wrote
        them: its own keys (@t, @mt, @l, @x) and every property, none renamed, so a reader can match "@mt".
        The JSON is plain ASCII: every character above ~ is a \uXXXX escape, which a parser reads back as the same
        text and which no console, whatever its code page, can change. skipped_lines counts lines that were not
        events. --human prints one line per event instead — the time in UTC, the level, and the message put back
        together from its template — with an exception's first line beneath it, and a trailer when the limit was
        reached. --human writes text as it is, so a console that is not UTF-8 may show some characters as ?; the JSON
        is the exact form. Control and format characters in a value — line breaks, escape sequences, text-direction
        overrides — are written out as \n, \uXXXX and the like, never sent to the terminal.

        Exit codes: 0 whenever it read, including when there is nothing to show and when there is no logs directory
        (exists is false: no runtime has started on this data directory). 1 when the logs directory exists and cannot
        be read, which is one of three things: the path is a file, not a directory; the directory cannot be listed;
        a log file in it cannot be opened. The error is logs_unreadable, and nothing is shown rather than less.
        2 for a usage error. Never 3: it never asks the runtime, so it cannot fail to reach it.
        """;

    /// <summary>
    /// A public id: its prefix, an underscore, and a ULID, which is what the runtime writes into a line and what a
    /// filter compares it with, exactly. What a ULID is has one definition, the one that makes them.
    /// </summary>
    private static bool IsPublicId(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) && Ulid.IsWellFormed(text.AsSpan(prefix.Length));

    /// <summary>
    /// ISO-8601 with a zone: a date, a <c>T</c>, a time to the minute, second or fraction, and then <c>Z</c> or an
    /// offset. The zone is what the shape insists on, because a parser given a time without one would read it as the
    /// reader's own, and the log is stamped in UTC.
    /// </summary>
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimeWithAZone();

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        const string Summary = "Read this installation's log files, without asking the runtime.";
        var command = new Command("logs", Summary)
        {
            Description = Summary + Environment.NewLine + Environment.NewLine + Contract,
        };

        var level = new Option<string?>("--level")
        {
            Description = "Only events at this level or above: verbose, debug, information, warning, error or fatal.",
        };
        var campaign = new Option<string?>("--campaign") { Description = "Only events of this campaign, as cmp_…." };
        var workItem = new Option<string?>("--work-item") { Description = "Only events about this work item, as wi_…." };
        var since = new Option<string?>("--since")
        {
            Description = "Only events stamped at or after this moment, as ISO-8601 with Z or an offset — for example 2026-09-18T09:00:00Z.",
        };
        var limit = new Option<int?>("--limit")
        {
            Description = $"How many events to return, the newest that match (the default is {DefaultLimit}, at most {MaxLimit}).",
        };
        var human = VerbOptions.Human();

        command.Options.Add(level);
        command.Options.Add(campaign);
        command.Options.Add(workItem);
        command.Options.Add(since);
        command.Options.Add(limit);
        command.Options.Add(human);

        command.SetAction(parse => Run(
            env,
            Filter(parse.GetValue(level), parse.GetValue(campaign), parse.GetValue(workItem), parse.GetValue(since)),
            Limit(parse.GetValue(limit)),
            parse.GetValue(human)));
        return command;
    }

    private static int Run(CliEnvironment env, LogFilter filter, int limit, bool human)
    {
        var directory = env.Paths.LogsDirectory;

        LogsDocument document;
        try
        {
            document = Read(directory, filter, limit);
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
        {
            return Unreadable(env, directory, failure.Message);
        }

        if (human)
        {
            Render(env.Out, document, limit);
        }
        else
        {
            env.Out.WriteLine(AsciiJson.Serialize(document));
        }

        return ExitCodes.Success;
    }

    private static LogsDocument Read(string directory, LogFilter filter, int limit)
    {
        if (File.Exists(directory))
        {
            throw new IOException("it is a file, not a directory.");
        }

        var exists = Directory.Exists(directory);
        var files = exists ? LogFiles.In(directory) : [];
        var tail = LogReader.Tail(files, filter, limit);

        return new LogsDocument(
            directory,
            exists,
            [.. files.Select(file => new LogFileEntry(file.Name, file.Bytes, file.ModifiedAt))],
            tail.Events,
            tail.Events.Count,
            tail.LimitReached,
            tail.SkippedLines);
    }

    private static int Unreadable(CliEnvironment env, string directory, string reason)
    {
        env.Out.WriteLine(CliErrors.Serialize(
            CliErrors.LogsUnreadable,
            $"The log directory {directory} exists and could not be read: {reason}",
            retryable: false));
        return ExitCodes.ApiError;
    }

    // ---- the arguments -----------------------------------------------------------------------------------------

    private static LogFilter Filter(string? level, string? campaign, string? workItem, string? since)
    {
        LogLevelName? atLeast = null;
        if (level is not null)
        {
            atLeast = LogLevels.TryParse(level, out var parsed)
                ? parsed
                : throw new UsageException($"--level must be verbose, debug, information, warning, error or fatal, in any case; '{level}' is none of them.");
        }

        if (campaign is not null && !IsPublicId(campaign, "cmp_"))
        {
            throw new UsageException($"--campaign must be a campaign id: cmp_ and 26 characters, as the campaign commands print it; '{campaign}' is not one.");
        }

        if (workItem is not null && !IsPublicId(workItem, "wi_"))
        {
            throw new UsageException($"--work-item must be a work item id: wi_ and 26 characters, as the work item commands print it; '{workItem}' is not one.");
        }

        return new LogFilter(atLeast, Since(since), campaign, workItem);
    }

    private static DateTimeOffset? Since(string? since)
    {
        if (since is null)
        {
            return null;
        }

        if (TimeWithAZone().IsMatch(since)
            && DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return moment;
        }

        throw new UsageException($"--since must be an ISO-8601 time with Z or an offset, for example 2026-09-18T09:00:00Z; '{since}' is not one.");
    }

    private static int Limit(int? limit)
    {
        var chosen = limit ?? DefaultLimit;
        return chosen is >= 1 and <= MaxLimit
            ? chosen
            : throw new UsageException($"--limit must be between 1 and {MaxLimit}; {chosen} is not.");
    }

    // ---- for people --------------------------------------------------------------------------------------------

    private static void Render(TextWriter output, LogsDocument document, int limit)
    {
        if (!document.Exists)
        {
            output.WriteLine($"There is no log directory at {document.Directory}: no runtime has started on this data directory.");
            return;
        }

        if (document.Files.Count == 0)
        {
            output.WriteLine($"No log files in {document.Directory}.");
            return;
        }

        if (document.Events.Count == 0)
        {
            output.WriteLine("No event matches.");
        }

        foreach (var evt in document.Events)
        {
            output.WriteLine(Line(evt));
            if (Visible(FirstLine(LogLevels.StringProperty(evt, "@x"))) is { Length: > 0 } exception)
            {
                output.WriteLine("    " + exception);
            }
        }

        if (document.LimitReached)
        {
            output.WriteLine(
                $"-- Limit reached: the newest {limit} matching events are shown and older ones were left out. "
                + $"Raise --limit (at most {MaxLimit}) or narrow the filters.");
        }

        if (document.SkippedLines > 0)
        {
            output.WriteLine(document.SkippedLines == 1
                ? "-- 1 line was not an event and was skipped."
                : $"-- {document.SkippedLines} lines were not events and were skipped.");
        }
    }

    /// <summary>The time in UTC, the level, and the message put back together; no more than one line, whatever the values hold.</summary>
    private static string Line(JsonObject evt)
    {
        var time = LogFilter.Time(evt) is { } at
            ? at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : "-".PadRight(TimeWidth);
        var message = LogLevels.StringProperty(evt, "@mt") is { } template ? MessageTemplate.Render(template, evt) : string.Empty;

        return $"{time}  {Abbreviation(LogLevels.Of(evt))}  {Visible(message)}".TrimEnd();
    }

    private static string Abbreviation(LogLevelName level) => level switch
    {
        LogLevelName.Verbose => "VRB",
        LogLevelName.Debug => "DBG",
        LogLevelName.Information => "INF",
        LogLevelName.Warning => "WRN",
        LogLevelName.Error => "ERR",
        _ => "FTL",
    };

    private static string FirstLine(string? text)
    {
        if (text is null)
        {
            return string.Empty;
        }

        var end = text.AsSpan().IndexOfAny('\r', '\n');
        return end < 0 ? text : text[..end];
    }

    /// <summary>
    /// The text with every character that does something other than show written out as the escape that names it:
    /// control characters, and the format characters that reorder or hide text \u2014 a right-to-left override, a zero-width
    /// space, a byte-order mark. A value in a log line is whatever a program put there, and a person's terminal obeys
    /// what it is sent: a line break in a value would be a second line that no event wrote, an escape sequence would
    /// be a command to the terminal, and an override would make a line read in a different order than it was written.
    /// A character outside the Basic Multilingual Plane is escaped as the two halves of its surrogate pair.
    /// </summary>
    private static string Visible(string text)
    {
        var visible = new StringBuilder(text.Length + 8);
        Span<char> units = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            if (!NeedsEscaping(rune))
            {
                visible.Append(rune.ToString());
                continue;
            }

            switch (rune.Value)
            {
                case '\n':
                    visible.Append("\\n");
                    break;
                case '\r':
                    visible.Append("\\r");
                    break;
                case '\t':
                    visible.Append("\\t");
                    break;
                default:
                    foreach (var unit in units[..rune.EncodeToUtf16(units)])
                    {
                        visible.Append("\\u").Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return visible.ToString();
    }

    private static bool NeedsEscaping(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
