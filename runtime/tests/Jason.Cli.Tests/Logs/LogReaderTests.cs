using System.Text;
using Jason.Cli.Logs;
using Jason.Runtime.Tests;
using static Jason.Cli.Tests.Logs.LogFixtures;

namespace Jason.Cli.Tests.Logs;

/// <summary>
/// Reading the runtime's log files back, newest first, from files a writer may still be holding. The reader never
/// refuses a file the runtime is writing, never trusts a line to be an event, and never reads more of a hostile file
/// than it has to.
/// </summary>
public sealed class LogReaderTests
{
    private const string Noon = "2026-10-02T12:00:00.0000000Z";

    private static string At(int minute) => $"2026-10-02T12:{minute:D2}:00.0000000Z";

    private static DateTimeOffset Utc(int minute) => new(2026, 10, 2, 12, minute, 0, TimeSpan.Zero);

    // ---- what a line is ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(LogLevelName.Verbose, "verbose debug information warning error fatal")]
    [InlineData(LogLevelName.Debug, "debug information warning error fatal")]
    [InlineData(LogLevelName.Information, "information warning error fatal")]
    [InlineData(LogLevelName.Warning, "warning error fatal")]
    [InlineData(LogLevelName.Error, "error fatal")]
    [InlineData(LogLevelName.Fatal, "fatal")]
    public void A_level_keeps_the_events_at_that_level_and_above_and_an_event_with_no_level_is_information(LogLevelName atLeast, string expected)
    {
        using var tree = new TempTree();
        Write(
            tree.Root,
            "runtime-20261002.jsonl",
            Event(At(1), "verbose", "Verbose"),
            Event(At(2), "debug", "Debug"),
            Event(At(3), "information"), // the runtime leaves the level out at Information
            Event(At(4), "warning", "Warning"),
            Event(At(5), "error", "Error"),
            Event(At(6), "fatal", "Fatal"));

        var tail = Tail(tree.Root, new LogFilter(AtLeast: atLeast));

        Assert.Equal(expected.Split(' '), Messages(tail));
    }

    [Fact]
    public void An_event_that_names_its_level_in_other_letters_is_still_at_that_level()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "loud", "ERROR"), Event(At(2), "quiet", "information"));

        Assert.Equal(["loud"], Messages(Tail(tree.Root, new LogFilter(AtLeast: LogLevelName.Warning))));
    }

    [Theory]
    [InlineData("verbose", LogLevelName.Verbose)]
    [InlineData("DEBUG", LogLevelName.Debug)]
    [InlineData("Information", LogLevelName.Information)]
    [InlineData("wArNiNg", LogLevelName.Warning)]
    [InlineData("error", LogLevelName.Error)]
    [InlineData("FATAL", LogLevelName.Fatal)]
    public void A_level_name_is_read_without_regard_to_case(string text, LogLevelName expected)
    {
        Assert.True(LogLevels.TryParse(text, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("warn")]
    [InlineData("info")]
    [InlineData("critical")]
    [InlineData("3")] // an enum would take a number for a name
    [InlineData("Error,Fatal")]
    [InlineData(" error")]
    public void Anything_that_is_not_a_level_s_name_is_not_a_level(string? text)
    {
        Assert.False(LogLevels.TryParse(text, out _));
    }

    // ---- which files, in which order ---------------------------------------------------------------------------

    [Fact]
    public void Files_are_read_newest_first_across_days_and_rolls_and_the_result_is_oldest_first()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", Event(At(1), "day1-a"), Event(At(2), "day1-b"));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(3), "day2-first-a"), Event(At(4), "day2-first-b"));
        Write(tree.Root, "runtime-20261002_001.jsonl", Event(At(5), "day2-second"));
        Write(tree.Root, "runtime-20261002_002.jsonl", Event(At(6), "day2-third"));
        Write(tree.Root, "runtime-20261003.jsonl", Event(At(7), "day3"));
        var files = LogFiles.In(tree.Root);

        // The order the files are handed over in decides nothing.
        var shuffled = new[] { files[3], files[0], files[4], files[2], files[1] }.ToList();
        Assert.Equal(
            ["day1-a", "day1-b", "day2-first-a", "day2-first-b", "day2-second", "day2-third", "day3"],
            LogReader.Tail(shuffled, new LogFilter(), 100).Events.Select(Message));
    }

    /// <summary>
    /// After <c>_999</c> a roll's number grows a digit, so its text sorts before the file it follows. The reader sorts
    /// the files again itself, newest first, whatever order it is handed them in, and the newest of the two is the
    /// thousandth roll.
    /// </summary>
    [Fact]
    public void The_thousandth_roll_of_a_day_is_read_as_newer_than_the_nine_hundred_and_ninety_ninth()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002_999.jsonl", Event(At(1), "roll-999"));
        Write(tree.Root, "runtime-20261002_1000.jsonl", Event(At(2), "roll-1000"));
        var files = LogFiles.In(tree.Root);

        foreach (var handed in new[] { files, [.. files.Reverse()], [.. files.OrderBy(file => file.Name, StringComparer.Ordinal)] })
        {
            var tail = LogReader.Tail(handed, new LogFilter(), 1);
            Assert.Equal(["roll-1000"], tail.Events.Select(Message));
            Assert.True(tail.LimitReached);
        }
    }

    [Fact]
    public void When_there_are_more_events_than_the_limit_the_newest_are_kept_and_the_result_says_so()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", Event(At(1), "old-a"), Event(At(2), "old-b"));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(3), "new-a"), Event(At(4), "new-b"), Event(At(5), "new-c"));

        var two = Tail(tree.Root, limit: 2);
        Assert.Equal(["new-b", "new-c"], Messages(two));
        Assert.True(two.LimitReached);

        var four = Tail(tree.Root, limit: 4);
        Assert.Equal(["old-b", "new-a", "new-b", "new-c"], Messages(four));
        Assert.True(four.LimitReached);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(1000)]
    public void A_limit_that_holds_everything_leaves_nothing_out_and_says_nothing_was(int limit)
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", Event(At(1), "a"), Event(At(2), "b"));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(3), "c"), Event(At(4), "d"), Event(At(5), "e"));

        var tail = Tail(tree.Root, limit: limit);

        Assert.Equal(["a", "b", "c", "d", "e"], Messages(tail));
        Assert.False(tail.LimitReached);
        Assert.Equal(0, tail.SkippedLines);
        Assert.False(tail.BytesExhausted);
    }

    [Fact]
    public void A_limit_is_counted_in_events_that_match_not_in_lines_read()
    {
        using var tree = new TempTree();
        Write(
            tree.Root,
            "runtime-20261002.jsonl",
            Event(At(1), "keep-1", "Error"),
            Event(At(2), "noise-1"),
            Event(At(3), "keep-2", "Error"),
            Event(At(4), "noise-2"),
            Event(At(5), "noise-3"),
            Event(At(6), "keep-3", "Error"),
            Event(At(7), "noise-4"));

        var tail = Tail(tree.Root, new LogFilter(AtLeast: LogLevelName.Error), limit: 2);

        Assert.Equal(["keep-2", "keep-3"], Messages(tail));
        Assert.True(tail.LimitReached);
    }

    [Fact]
    public void No_files_is_no_events()
    {
        var tail = LogReader.Tail([], new LogFilter(), 10);

        Assert.Empty(tail.Events);
        Assert.False(tail.LimitReached);
        Assert.False(tail.BytesExhausted);
        Assert.Equal(0, tail.SkippedLines);
    }

    // ---- reading backwards in blocks ---------------------------------------------------------------------------

    /// <summary>
    /// How many bytes the reader takes from the end of a file at a time, read from the reader rather than copied: the
    /// tests below put a block's first byte on a chosen byte of the file, so they need to know where the blocks begin,
    /// and a copy that drifted from the reader would stage every edge somewhere else and still pass.
    /// </summary>
    private const int BlockBytes = LogReader.BlockSize;

    /// <summary>
    /// A file of a thousand lines of one size, each a message made of <paramref name="unit"/> repeated, ended by
    /// <paramref name="lineBreak"/>, whose last line is padded with as many ASCII bytes as it takes for the first byte of
    /// the block that starts <see cref="BlockBytes"/> from the end to be one <paramref name="wanted"/> accepts, given the
    /// file's bytes and that byte's index. The reader's first block boundary is then exactly where the case needs it, not
    /// wherever the sizes happen to put it.
    /// </summary>
    private static (string[] Messages, byte[] Bytes, int BlockStart) FileCut(string lineBreak, string unit, Func<byte[], int, bool> wanted)
    {
        var encoding = new UTF8Encoding(false);
        var messages = Enumerable.Range(0, 1000).Select(i => string.Concat(Enumerable.Repeat(unit, 40)) + i.ToString("D5")).ToArray();
        var head = encoding.GetBytes(string.Concat(messages[..^1].Select(message => Event(Noon, message) + lineBreak)));

        for (var pad = 0; pad < 2000; pad++)
        {
            var last = messages[^1] + new string('x', pad);
            var tail = encoding.GetBytes(Event(Noon, last) + lineBreak);
            var blockStart = head.Length + tail.Length - BlockBytes;
            if (blockStart > 0 && blockStart < head.Length && wanted(head, blockStart))
            {
                messages[^1] = last;
                return (messages, [.. head, .. tail], blockStart);
            }
        }

        throw new InvalidOperationException("No padding of the last line puts a block boundary where the case needs it.");
    }

    /// <summary>How many bytes into a UTF-8 character the byte at the index is: zero for a byte that begins one.</summary>
    private static int BytesIntoCharacter(byte[] bytes, int index)
    {
        var into = 0;
        while ((bytes[index - into] & 0xC0) == 0x80)
        {
            into++;
        }

        return into;
    }

    /// <summary>
    /// A character that is cut between two blocks is put back together before it is decoded. The file holds two-, three-
    /// and four-byte characters as the runtime writes them, raw, and the block the reader takes first — the one that
    /// starts a block's length from the end — starts at each position inside them in turn: on the second byte of a
    /// two-byte one, the second or the third of a three-byte one, and the second, third or fourth of a four-byte one.
    /// The block it takes second ends mid-character.
    /// The test says what it stages — a byte above 0x7F in the file, and a block that begins on a continuation byte —
    /// so a fixture that quietly went back to ASCII would fail here rather than pass for nothing.
    /// </summary>
    [Theory]
    // Critical: block boundaries in UTF-8.
    [InlineData("é", 1, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X1)])]
    [InlineData("日", 1)]
    [InlineData("日", 2)]
    [InlineData("\U0001F642", 1)]
    [InlineData("\U0001F642", 2)]
    [InlineData("\U0001F642", 3)]
    public void A_character_cut_between_two_blocks_comes_back_whole(string unit, int bytesIntoIt)
    {
        using var tree = new TempTree();
        var (messages, bytes, blockStart) = FileCut("\n", unit, (file, index) => BytesIntoCharacter(file, index) == bytesIntoIt);
        File.WriteAllBytes(Path.Combine(tree.Root, "runtime-20261002.jsonl"), bytes);

        Assert.Contains(bytes, b => b > 0x7F);
        Assert.Equal(bytesIntoIt, BytesIntoCharacter(bytes, blockStart));
        Assert.True(bytes.Length > BlockBytes);

        var tail = Tail(tree.Root, limit: 5000);

        Assert.Equal(messages, Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
        Assert.False(tail.LimitReached);
    }

    /// <summary>
    /// A block can begin on a line's first byte, on the line break that ends the line before it, or, in a file written
    /// with carriage returns, on the carriage return or between it and the line feed. Each leaves one line whole in one
    /// block and the end of the line before it in the other, and none may lose a line, join two, or count a skipped one.
    /// </summary>
    [Theory]
    [InlineData("\n", "after the break")]
    [InlineData("\n", "on the break")]
    [InlineData("\r\n", "after the pair")]
    [InlineData("\r\n", "on the return")]
    [InlineData("\r\n", "between the pair")]
    public void A_block_that_begins_at_a_line_break_loses_no_line_and_joins_none(string lineBreak, string where)
    {
        Func<byte[], int, bool> wanted = where switch
        {
            "after the break" or "after the pair" => (file, index) => file[index - 1] == '\n',
            "on the break" => (file, index) => file[index] == '\n',
            "on the return" => (file, index) => file[index] == '\r',
            _ => (file, index) => file[index] == '\n' && file[index - 1] == '\r',
        };
        using var tree = new TempTree();
        var (messages, bytes, blockStart) = FileCut(lineBreak, "l", wanted);
        File.WriteAllBytes(Path.Combine(tree.Root, "runtime-20261002.jsonl"), bytes);
        Assert.True(wanted(bytes, blockStart));

        var tail = Tail(tree.Root, limit: 5000);

        Assert.Equal(messages, Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_line_longer_than_a_block_is_read_whole()
    {
        using var tree = new TempTree();
        var big = new string('b', 200 * 1024);
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "before"), Event(At(2), big), Event(At(3), "after"));

        var tail = Tail(tree.Root);

        Assert.Equal(["before", big, "after"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_file_written_with_windows_line_breaks_reads_the_same_and_a_blank_line_is_nothing()
    {
        using var tree = new TempTree();
        WriteWithBreaks(tree.Root, "runtime-20261002.jsonl", "\r\n", Event(At(1), "one"), string.Empty, Event(At(2), "two"));

        var tail = Tail(tree.Root);

        Assert.Equal(["one", "two"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    // ---- since -------------------------------------------------------------------------------------------------

    [Fact]
    public void Since_keeps_the_events_stamped_at_or_after_it()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "before"), Event(At(2), "exactly"), Event(At(3), "after"));

        var tail = Tail(tree.Root, new LogFilter(Since: Utc(2)));

        Assert.Equal(["exactly", "after"], Messages(tail));
    }

    [Fact]
    public void Since_is_an_instant_whatever_offset_it_is_written_in()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(10), "ten"), Event(At(20), "twenty"));

        // 14:15 at UTC+2 is 12:15 UTC, so only the event stamped 12:20 UTC is after it.
        var since = new DateTimeOffset(2026, 10, 2, 14, 15, 0, TimeSpan.FromHours(2));

        Assert.Equal(["twenty"], Messages(Tail(tree.Root, new LogFilter(Since: since))));
    }

    /// <summary>
    /// A file is named by the local date of the machine that wrote it, which is within a day of the date in UTC
    /// wherever it is. A file named for a day more than one before the date of <c>since</c> cannot hold an event
    /// since then, so it is not opened: the file here holds an event stamped inside the window to show it was not.
    /// </summary>
    [Fact]
    public void Since_does_not_open_a_file_named_for_a_day_more_than_one_before_it()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", Event("2026-10-03T01:00:00.0000000Z", "two days before"));
        Write(tree.Root, "runtime-20261002.jsonl", Event("2026-10-02T23:00:00.0000000Z", "too early"), Event("2026-10-03T00:45:00.0000000Z", "the day before, local time"));
        Write(tree.Root, "runtime-20261003.jsonl", Event("2026-10-03T00:10:00.0000000Z", "too early too"), Event("2026-10-03T01:00:00.0000000Z", "the day itself"));

        var tail = Tail(tree.Root, new LogFilter(Since: new DateTimeOffset(2026, 10, 3, 0, 30, 0, TimeSpan.Zero)));

        Assert.Equal(["the day before, local time", "the day itself"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void Since_at_the_earliest_moment_there_is_reads_everything_without_failing()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", Event(At(1), "a"));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(2), "b"));

        Assert.Equal(["a", "b"], Messages(Tail(tree.Root, new LogFilter(Since: DateTimeOffset.MinValue))));
    }

    [Fact]
    public void An_event_that_carries_no_time_cannot_be_shown_to_be_since_one()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", "{\"@mt\":\"timeless\"}", "{\"@t\":\"not a time\",\"@mt\":\"garbled\"}", Event(At(5), "stamped"));

        Assert.Equal(["timeless", "garbled", "stamped"], Messages(Tail(tree.Root)));
        Assert.Equal(["stamped"], Messages(Tail(tree.Root, new LogFilter(Since: Utc(0)))));
    }

    // ---- campaign and work item --------------------------------------------------------------------------------

    [Fact]
    public void A_campaign_or_a_work_item_matches_only_the_exact_id_in_its_own_property()
    {
        using var tree = new TempTree();
        Write(
            tree.Root,
            "runtime-20261002.jsonl",
            Event(At(1), "a", null, ("CampaignId", "cmp_1"), ("WorkItemId", "wi_1")),
            Event(At(2), "b", null, ("CampaignId", "cmp_10"), ("WorkItemId", "wi_1")),
            Event(At(3), "c", null, ("CampaignId", "cmp_1"), ("WorkItemId", "wi_2")),
            Event(At(4), "d"),
            Event(At(5), "e", null, ("CampaignId", "CMP_1")),
            Event(At(6), "f", null, ("Other", "cmp_1")),
            "{\"@t\":\"" + At(7) + "\",\"@mt\":\"g\",\"CampaignId\":1}");

        Assert.Equal(["a", "c"], Messages(Tail(tree.Root, new LogFilter(CampaignId: "cmp_1"))));
        Assert.Equal(["a", "b"], Messages(Tail(tree.Root, new LogFilter(WorkItemId: "wi_1"))));
        Assert.Equal(["a"], Messages(Tail(tree.Root, new LogFilter(CampaignId: "cmp_1", WorkItemId: "wi_1"))));
        Assert.Equal(7, Tail(tree.Root).Events.Count);
    }

    // ---- a writer mid-line, and lines that are not events -------------------------------------------------------

    [Fact]
    [Critical(Family.X1, "a line still being written")]
    public void A_final_line_with_no_line_break_is_the_writer_mid_line_and_is_skipped_without_being_counted()
    {
        using var tree = new TempTree();
        var path = Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "whole"));
        File.AppendAllText(path, "{\"@t\":\"" + At(2) + "\",\"@mt\":\"half a li", new UTF8Encoding(false));

        var tail = Tail(tree.Root);

        Assert.Equal(["whole"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_final_line_that_is_complete_json_but_not_yet_ended_is_left_for_the_next_read()
    {
        using var tree = new TempTree();
        var path = Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "whole"));
        File.AppendAllText(path, Event(At(2), "not ended yet"), new UTF8Encoding(false));

        Assert.Equal(["whole"], Messages(Tail(tree.Root)));

        File.AppendAllText(path, "\n", new UTF8Encoding(false));

        Assert.Equal(["whole", "not ended yet"], Messages(Tail(tree.Root)));
    }

    [Fact]
    public void A_file_with_only_an_unfinished_line_has_no_events_and_nothing_skipped()
    {
        using var tree = new TempTree();
        File.WriteAllText(Path.Combine(tree.Root, "runtime-20261002.jsonl"), "{\"@t\":\"" + At(1) + "\",\"@mt\":\"just begun");

        var tail = Tail(tree.Root);

        Assert.Empty(tail.Events);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void An_unfinished_line_longer_than_several_blocks_is_still_only_skipped()
    {
        using var tree = new TempTree();
        var path = Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "whole"));
        File.AppendAllText(path, "{\"@mt\":\"" + new string('u', 300 * 1024), new UTF8Encoding(false));

        var tail = Tail(tree.Root);

        Assert.Equal(["whole"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_line_that_is_not_a_json_object_is_skipped_and_counted_and_the_lines_around_it_are_read()
    {
        using var tree = new TempTree();
        Write(
            tree.Root,
            "runtime-20261002.jsonl",
            Event(At(1), "first"),
            "this is not json at all",
            "[1,2,3]",
            "\"a string\"",
            "null",
            "42",
            "{\"@t\":\"" + At(2) + "\",\"@mt\":\"never closed",
            Event(At(3), "second"),
            "    ",
            Event(At(4), "third"));

        var tail = Tail(tree.Root);

        Assert.Equal(["first", "second", "third"], Messages(tail));
        Assert.Equal(7, tail.SkippedLines);
    }

    [Fact]
    public void A_line_that_is_json_followed_by_something_else_is_not_an_event()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "fine"), Event(At(2), "joined") + Event(At(3), "twice"), Event(At(4), "trailing") + " and more");

        var tail = Tail(tree.Root);

        Assert.Equal(["fine"], Messages(tail));
        Assert.Equal(2, tail.SkippedLines);
    }

    [Fact]
    public void A_line_nested_far_deeper_than_any_event_is_skipped_rather_than_read()
    {
        using var tree = new TempTree();
        var deep = string.Concat(Enumerable.Repeat("[", 5000)) + string.Concat(Enumerable.Repeat("]", 5000));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "fine"), "{\"@mt\":\"deep\",\"x\":" + deep + "}");

        var tail = Tail(tree.Root);

        Assert.Equal(["fine"], Messages(tail));
        Assert.Equal(1, tail.SkippedLines);
    }

    [Fact]
    public void A_line_that_names_a_property_twice_is_skipped_rather_than_handed_on_to_throw_later()
    {
        using var tree = new TempTree();
        Write(
            tree.Root,
            "runtime-20261002.jsonl",
            Event(At(1), "fine"),
            "{\"@t\":\"" + At(2) + "\",\"@mt\":\"first\",\"@mt\":\"second\"}",
            "{\"@t\":\"" + At(3) + "\",\"@mt\":\"nested\",\"inner\":{\"a\":1,\"a\":2}}",
            Event(At(4), "also fine"));

        // A filter that reads a property would be the first to meet the line, so ask for one that does.
        var tail = Tail(tree.Root, new LogFilter(AtLeast: LogLevelName.Information));

        Assert.Equal(["fine", "also fine"], Messages(tail));
        Assert.Equal(2, tail.SkippedLines);
    }

    [Fact]
    public void A_line_with_an_unpaired_surrogate_escape_is_skipped_rather_than_handed_on_to_throw_later()
    {
        using var tree = new TempTree();
        Write(
            tree.Root,
            "runtime-20261002.jsonl",
            Event(At(1), "fine"),
            """{"@t":"2026-10-02T12:02:00.0000000Z","@mt":"lone high","Value":"a\ud800b"}""",
            """{"@t":"2026-10-02T12:03:00.0000000Z","@mt":"lone low","Value":"\udc00"}""",
            """{"@t":"2026-10-02T12:04:00.0000000Z","@mt":"high then a letter","Value":"\ud83dx"}""",
            """{"@t":"2026-10-02T12:05:00.0000000Z","@mt":"deep in an array","Value":["ok",{"Deep":"\ud83d"}]}""",
            """{"@t":"2026-10-02T12:06:00.0000000Z","@mt":"in a name","\ud800":1}""",
            """{"@t":"2026-10-02T12:07:00.0000000Z","@mt":"in the template \udfff"}""",
            """{"@t":"2026-10-02T12:08:00.0000000Z","@mt":"a pair","Value":"\ud83d\ude00 and \\ud800 are text"}""",
            Event(At(9), "also fine"));

        // Every kind of read meets the line: with no filter, with one that reads a property, and with one that reads the time.
        foreach (var filter in new[] { new LogFilter(), new LogFilter(AtLeast: LogLevelName.Information), new LogFilter(Since: Utc(0)) })
        {
            var tail = Tail(tree.Root, filter);

            Assert.Equal(["fine", "a pair", "also fine"], Messages(tail));
            Assert.Equal(6, tail.SkippedLines);

            // Everything handed on can be written out: no event holds text that is not text.
            foreach (var evt in tail.Events)
            {
                Assert.NotEmpty(evt.ToJsonString());
            }
        }
    }

    [Fact]
    public void The_lines_skipped_are_counted_across_every_file_read()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", "junk", Event(At(1), "old"));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(2), "new"), "more junk", "and more");

        var tail = Tail(tree.Root);

        Assert.Equal(["old", "new"], Messages(tail));
        Assert.Equal(3, tail.SkippedLines);
    }

    // ---- a file a writer holds ---------------------------------------------------------------------------------

    /// <summary>
    /// The log is held exactly as the runtime's sink holds it — open for writing, shared for reading only — and is
    /// still read, line by line as it grows. A reader that does not share writing is refused on Windows with "being
    /// used by another process", which is how the suite once failed reading a log the runtime had only just stopped
    /// writing to.
    /// </summary>
    [Fact]
    [Critical(Family.F5, "a held log is read, not refused")]
    public void A_file_a_writer_still_holds_is_read_while_it_is_written()
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "runtime-20261002.jsonl");
        using var held = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(held, new UTF8Encoding(false));

        writer.Write(Event(At(1), "first") + "\n" + Event(At(2), "second") + "\n");
        writer.Flush();
        Assert.Equal(["first", "second"], Messages(Tail(tree.Root)));

        // The writer is in the middle of a line: what it has not finished is not read, and nothing is refused.
        writer.Write("{\"@t\":\"" + At(3) + "\",\"@mt\":\"thi");
        writer.Flush();
        var midLine = Tail(tree.Root);
        Assert.Equal(["first", "second"], Messages(midLine));
        Assert.Equal(0, midLine.SkippedLines);

        writer.Write("rd\"}\n");
        writer.Flush();
        Assert.Equal(["first", "second", "third"], Messages(Tail(tree.Root)));
    }

    // ---- a hostile directory -----------------------------------------------------------------------------------

    [Fact]
    public void Only_the_runtime_s_own_files_are_read_whatever_else_the_directory_holds()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "ours"));
        Write(tree.Root, "runtime-x.jsonl", Event(At(2), "a foreign file with a log's look"));
        Write(tree.Root, "runtime-20261002.jsonl.bak", Event(At(3), "a backup"));
        Write(tree.Root, "notes.txt", Event(At(4), "a note"));
        var doctor = Directory.CreateDirectory(Path.Combine(tree.Root, "doctor")).FullName;
        Write(doctor, "runtime-20261002.jsonl", Event(At(5), "a diagnostic bundle's copy"));

        Assert.Equal(["ours"], Messages(Tail(tree.Root)));
    }

    [Theory]
    [InlineData("first")]
    [InlineData("middle")]
    [InlineData("last")]
    public void A_single_line_of_five_mebibytes_is_skipped_and_counted_wherever_it_is_and_the_rest_is_read(string where)
    {
        using var tree = new TempTree();
        var huge = Event(At(2), new string('h', 5 * 1024 * 1024));
        string[] lines = where switch
        {
            "first" => [huge, Event(At(3), "a"), Event(At(4), "b")],
            "middle" => [Event(At(3), "a"), huge, Event(At(4), "b")],
            _ => [Event(At(3), "a"), Event(At(4), "b"), huge],
        };
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        var tail = Tail(tree.Root);

        Assert.Equal(["a", "b"], Messages(tail));
        Assert.Equal(1, tail.SkippedLines);
    }

    [Fact]
    public void A_line_of_exactly_one_mebibyte_is_read_and_one_byte_more_is_not()
    {
        using var tree = new TempTree();
        var frame = Event(Noon, string.Empty).Length;
        var exactly = Event(Noon, new string('e', 1024 * 1024 - frame));
        var over = Event(Noon, new string('o', 1024 * 1024 - frame + 1));
        Assert.Equal(1024 * 1024, Encoding.UTF8.GetByteCount(exactly));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "a"), exactly, over, Event(At(2), "b"));

        var tail = Tail(tree.Root);

        Assert.Equal(["a", new string('e', 1024 * 1024 - frame), "b"], Messages(tail));
        Assert.Equal(1, tail.SkippedLines);
    }

    [Fact]
    public void A_file_of_bytes_that_are_not_text_yields_nothing_and_fails_nothing()
    {
        using var tree = new TempTree();
        var noise = new byte[400 * 1024];
        new Random(20261002).NextBytes(noise);
        File.WriteAllBytes(Path.Combine(tree.Root, "runtime-20261001.jsonl"), noise);
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "readable"));

        var tail = Tail(tree.Root);

        Assert.Equal(["readable"], Messages(tail));
        Assert.True(tail.SkippedLines > 0);
    }

    [Fact]
    public void A_file_with_no_line_break_at_all_is_only_an_unfinished_line()
    {
        using var tree = new TempTree();
        File.WriteAllBytes(Path.Combine(tree.Root, "runtime-20261002.jsonl"), Enumerable.Repeat((byte)'{', 3 * 1024 * 1024).ToArray());
        Write(tree.Root, "runtime-20261001.jsonl", Event(At(1), "elsewhere"));

        var tail = Tail(tree.Root);

        Assert.Equal(["elsewhere"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_file_that_is_not_there_when_it_is_opened_is_skipped_and_the_others_are_read()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261001.jsonl", Event(At(1), "old"));
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(2), "vanishing"));
        Write(tree.Root, "runtime-20261003.jsonl", Event(At(3), "new"));
        var files = LogFiles.In(tree.Root);

        // Retention, or a person, removes it between the listing and the read.
        File.Delete(files[1].Path);

        var tail = LogReader.Tail(files, new LogFilter(), 10);

        Assert.Equal(["old", "new"], Messages(tail));
        Assert.Equal(0, tail.SkippedLines);
        Assert.False(tail.BytesExhausted);
    }

    [Fact]
    public void A_file_whose_directory_is_gone_is_skipped_too()
    {
        using var tree = new TempTree();
        var gone = new LogFile(Path.Combine(tree.Root, "never", "runtime-20261002.jsonl"), "runtime-20261002.jsonl", new DateOnly(2026, 10, 2), 0, 10, DateTimeOffset.UnixEpoch);

        var tail = LogReader.Tail([gone], new LogFilter(), 10);

        Assert.Empty(tail.Events);
        Assert.Equal(0, tail.SkippedLines);
    }

    // ---- a budget of bytes -------------------------------------------------------------------------------------

    /// <summary>Events of one size, so that a budget can be spelled in lines: the size of a line, line break included.</summary>
    private static (string[] Lines, int LineBytes) Uniform(string prefix, int count, int startMinute = 0)
    {
        var lines = Enumerable.Range(0, count).Select(i => Event(At(startMinute + i), $"{prefix}{i:D2}")).ToArray();
        return (lines, lines[0].Length + 1);
    }

    /// <summary>
    /// A line is read only when the reader has seen where it begins, which is the line break that ends the line
    /// before it. So three lines and the break before them are what it takes to read three lines.
    /// </summary>
    [Fact]
    public void A_byte_budget_stops_the_reading_and_says_the_older_events_were_left_unread()
    {
        using var tree = new TempTree();
        var (lines, size) = Uniform("m", 10);
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        var tail = Tail(tree.Root, maxBytes: 3 * size + 1);

        Assert.Equal(["m07", "m08", "m09"], Messages(tail));
        Assert.True(tail.BytesExhausted);
        Assert.False(tail.LimitReached);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_line_the_budget_cuts_through_is_neither_read_nor_counted_as_damaged()
    {
        using var tree = new TempTree();
        var (lines, size) = Uniform("m", 10);
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        var tail = Tail(tree.Root, maxBytes: 3 * size + 8);

        Assert.Equal(["m07", "m08", "m09"], Messages(tail));
        Assert.True(tail.BytesExhausted);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_line_whose_beginning_the_budget_did_not_reach_is_not_read_even_when_all_of_it_was_read()
    {
        using var tree = new TempTree();
        var (lines, size) = Uniform("m", 10);
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        // The last three lines are all there, but nothing says the oldest of them did not begin earlier.
        var tail = Tail(tree.Root, maxBytes: 3 * size);

        Assert.Equal(["m08", "m09"], Messages(tail));
        Assert.True(tail.BytesExhausted);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public void A_budget_that_covers_the_whole_file_leaves_nothing_unread(int spare)
    {
        using var tree = new TempTree();
        var (lines, size) = Uniform("m", 10);
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        var tail = Tail(tree.Root, maxBytes: 10 * size + spare);

        Assert.Equal(10, tail.Events.Count);
        Assert.False(tail.BytesExhausted);
    }

    [Fact]
    public void A_budget_one_byte_short_of_the_file_loses_only_the_oldest_event()
    {
        using var tree = new TempTree();
        var (lines, size) = Uniform("m", 10);
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        var tail = Tail(tree.Root, maxBytes: 10 * size - 1);

        Assert.Equal(9, tail.Events.Count);
        Assert.Equal("m01", Message(tail.Events[0]));
        Assert.True(tail.BytesExhausted);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_budget_is_spent_across_files_and_an_older_file_it_never_reached_counts_as_unread()
    {
        using var tree = new TempTree();
        var (older, size) = Uniform("old", 10);
        var (newer, _) = Uniform("new", 4, startMinute: 20);
        Write(tree.Root, "runtime-20261001.jsonl", older);
        Write(tree.Root, "runtime-20261002.jsonl", newer);

        var exactlyTheNewerFile = Tail(tree.Root, maxBytes: 4 * size);
        Assert.Equal(["new00", "new01", "new02", "new03"], Messages(exactlyTheNewerFile));
        Assert.True(exactlyTheNewerFile.BytesExhausted);

        var everything = Tail(tree.Root, maxBytes: 14 * size);
        Assert.Equal(14, everything.Events.Count);
        Assert.False(everything.BytesExhausted);

        var intoTheOlderFile = Tail(tree.Root, maxBytes: 6 * size + 1);
        Assert.Equal(["old08", "old09", "new00", "new01", "new02", "new03"], Messages(intoTheOlderFile));
        Assert.True(intoTheOlderFile.BytesExhausted);
    }

    /// <summary>Lines of one size, many enough to fill several blocks: the size of a line, line break included.</summary>
    private static (string[] Lines, int LineBytes) Many(string prefix, int count)
    {
        var lines = Enumerable.Range(0, count).Select(i => Event(Noon, $"{prefix}{i:D5}")).ToArray();
        return (lines, lines[0].Length + 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void A_budget_that_ends_in_a_later_block_keeps_exactly_the_lines_it_covers(int pastTheBreak)
    {
        using var tree = new TempTree();
        var (lines, size) = Many("m", 3000);
        Write(tree.Root, "runtime-20261002.jsonl", lines);
        var budget = 2500L * size + pastTheBreak;
        Assert.True(budget > 2 * BlockBytes && budget < 3 * BlockBytes + 3 * size, "The budget should end in the third block it reads.");

        var tail = Tail(tree.Root, limit: 5000, maxBytes: budget);

        Assert.Equal(Enumerable.Range(500, 2500).Select(i => $"m{i:D5}"), Messages(tail));
        Assert.True(tail.BytesExhausted);
        Assert.False(tail.LimitReached);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_budget_that_runs_out_in_the_middle_of_an_older_file_of_several_blocks_keeps_the_lines_it_covers()
    {
        using var tree = new TempTree();
        var (older, size) = Many("old", 3000);
        var (newer, _) = Many("new", 3000);
        Write(tree.Root, "runtime-20261001.jsonl", older);
        Write(tree.Root, "runtime-20261002.jsonl", newer);

        var tail = Tail(tree.Root, limit: 10000, maxBytes: 4500L * size + 1);

        Assert.Equal(
            Enumerable.Range(1500, 1500).Select(i => $"old{i:D5}").Concat(Enumerable.Range(0, 3000).Select(i => $"new{i:D5}")),
            Messages(tail));
        Assert.True(tail.BytesExhausted);
        Assert.Equal(0, tail.SkippedLines);
    }

    [Fact]
    public void A_budget_never_reached_because_the_limit_came_first_is_not_exhausted()
    {
        using var tree = new TempTree();
        var (lines, size) = Uniform("m", 10);
        Write(tree.Root, "runtime-20261002.jsonl", lines);

        var tail = Tail(tree.Root, limit: 2, maxBytes: 100 * size);

        Assert.Equal(["m08", "m09"], Messages(tail));
        Assert.True(tail.LimitReached);
        Assert.False(tail.BytesExhausted);
    }

    [Fact]
    public void A_budget_of_nothing_reads_nothing_and_says_so_when_there_was_something_to_read()
    {
        using var tree = new TempTree();
        Write(tree.Root, "runtime-20261002.jsonl", Event(At(1), "a"));
        File.WriteAllText(Path.Combine(tree.Root, "runtime-20261001.jsonl"), string.Empty);

        var nothing = Tail(tree.Root, maxBytes: 0);

        Assert.Empty(nothing.Events);
        Assert.True(nothing.BytesExhausted);

        var empty = Directory.CreateDirectory(Path.Combine(tree.Root, "empty")).FullName;
        Assert.False(Tail(empty, maxBytes: 0).BytesExhausted);
    }

    // ---- what is asked -----------------------------------------------------------------------------------------

    [Fact]
    public void A_limit_that_asks_for_nothing_and_a_budget_below_nothing_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogReader.Tail([], new LogFilter(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogReader.Tail([], new LogFilter(), -5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogReader.Tail([], new LogFilter(), 10, -1));
    }
}
