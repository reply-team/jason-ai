using System.Text.Json.Nodes;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using static Jason.Cli.Tests.Logs.LogFixtures;

namespace Jason.Cli.Tests.Logs;

/// <summary>
/// <c>jason logs</c>: the installation's own log files, read by the program that is asked and never through the runtime
/// that wrote them. The one answer a person needs when the runtime will not start is in these files, so the verb is held
/// to the same standard in every test here: no descriptor, no runtime, and a handler that fails the test if anything
/// is asked of one.
/// </summary>
public sealed class LogsCommandTests
{
    private const string Campaign = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string OtherCampaign = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE";
    private const string WorkItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string OtherWorkItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string At(int minute, int second = 0) => $"2026-10-03T20:{minute:D2}:{second:D2}.0000000Z";

    private static string LogsIn(TempPaths dir) => Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;

    private static async Task<(int Exit, string Out, string Error)> RunAsync(TempPaths dir, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment(output, error, dir.Paths, RuntimeVerbs.NeverCalled());
        var exit = await CliApp.RunAsync(["logs", .. args], env, Ct);
        return (exit, output.ToString(), error.ToString());
    }

    private static JsonObject Document(string output) => (JsonObject)JsonNode.Parse(output)!;

    private static string[] Messages(JsonObject document) =>
        [.. document["events"]!.AsArray().Select(evt => (string)evt!["@mt"]!)];

    private static string[] Lines(string output) =>
        output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    // ---- the document ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_document_names_the_directory_the_files_and_the_events_exactly_as_the_runtime_wrote_them()
    {
        using var dir = new TempPaths();
        var logs = LogsIn(dir);
        var first = Event("2026-10-02T09:00:00.0000000Z", "Older {Name}", null, ("Name", "ada"));
        var second = Event(At(5, 31), "Disk is {Percent} full", "Warning", ("Percent", "91"));
        var third = Event(At(6), "Back to normal");
        var older = Write(logs, "runtime-20261002.jsonl", first);
        var newer = Write(logs, "runtime-20261003.jsonl", second, third);
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 10, 3, 20, 6, 0, DateTimeKind.Utc));

        var (exit, output, error) = await RunAsync(dir);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(error);
        Assert.Single(Lines(output));

        var document = Document(output);
        Assert.Equal(
            ["directory", "exists", "files", "events", "returned", "limit_reached", "skipped_lines"],
            document.Select(property => property.Key));
        Assert.Equal(logs, (string?)document["directory"]);
        Assert.True((bool)document["exists"]!);

        var files = document["files"]!.AsArray();
        Assert.Equal(2, files.Count);
        Assert.Equal(["name", "bytes", "modified_at"], files[0]!.AsObject().Select(property => property.Key));
        Assert.Equal("runtime-20261002.jsonl", (string?)files[0]!["name"]);
        Assert.Equal(new FileInfo(older).Length, (long)files[0]!["bytes"]!);
        Assert.Equal("2026-10-02T09:30:00.000Z", (string?)files[0]!["modified_at"]);
        Assert.Equal("runtime-20261003.jsonl", (string?)files[1]!["name"]);
        Assert.Equal(new FileInfo(newer).Length, (long)files[1]!["bytes"]!);
        Assert.Equal("2026-10-03T20:06:00.000Z", (string?)files[1]!["modified_at"]);

        // Oldest first, and each event is the line the runtime wrote: the format's own keys, the properties, nothing
        // renamed — a reader matches "@mt", and a renamed copy would be a second format of the same lines.
        Assert.Equal(["Older {Name}", "Disk is {Percent} full", "Back to normal"], Messages(document));
        Assert.Contains(first, output, StringComparison.Ordinal);
        Assert.Contains(second, output, StringComparison.Ordinal);
        Assert.Contains(third, output, StringComparison.Ordinal);
        Assert.Equal(3, (int)document["returned"]!);
        Assert.False((bool)document["limit_reached"]!);
        Assert.Equal(0, (int)document["skipped_lines"]!);
    }

    [Fact]
    public async Task An_event_keeps_everything_it_was_written_with_including_text_that_is_not_ascii()
    {
        using var dir = new TempPaths();
        var logs = LogsIn(dir);
        var failure = """{"@t":"2026-10-03T20:07:00.0000000Z","@mt":"Attempt {AttemptId} failed: {Reason}","@l":"Error","@x":"System.InvalidOperationException: boom\r\n   at Jason.Thing.Run()","@i":"1a2b3c4d","AttemptId":"att_X","Reason":"naïve — 日本語","Count":3,"Ratio":0.5,"Done":true,"Nothing":null,"Who":{"Name":"ada","Tags":["a","b"]}}""";
        Write(logs, "runtime-20261003.jsonl", failure);

        var (exit, output, _) = await RunAsync(dir);

        // The same event, compared as values: how a character is spelled in the output is the next test's business.
        Assert.Equal(ExitCodes.Success, exit);
        var evt = Assert.Single(Document(output)["events"]!.AsArray())!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(failure), evt), $"The event came back changed: {evt.ToJsonString()}");
        Assert.Equal("naïve — 日本語", (string?)evt["Reason"]);
    }

    [Fact]
    public async Task The_json_is_plain_ascii_so_that_a_console_that_is_not_utf_8_cannot_change_what_it_says()
    {
        // The directory is part of the answer, and a user's profile path is where it can be non-ASCII.
        using var dir = new TempPaths();
        var paths = new Jason.Contracts.Discovery.JasonPaths(Path.Combine(dir.Paths.Root, "Привіт 日本語 é"));
        Directory.CreateDirectory(paths.LogsDirectory);
        var astral = char.ConvertFromUtf32(0x1F600);
        Write(
            paths.LogsDirectory,
            "runtime-20261003.jsonl",
            """{"@t":"2026-10-03T20:07:00.0000000Z","@mt":"Привіт, {Ім_я}: naïve — 日本語 \ud83d\ude00","Ім_я":"Ада","Path":"C:\\Users\\Артем\\.jason","Quote":"a \"q\" and \\ and \u0007 and \u007f"}""",
            """{"@t":"2026-10-03T20:08:00.0000000Z","@mt":"a+b <tag> & 'quoted' {X}","X":"1 < 2 > 0 & 'ok' + 1"}""");
        var output = new StringWriter();
        var env = new CliEnvironment(output, new StringWriter(), paths, RuntimeVerbs.NeverCalled());

        var exit = await CliApp.RunAsync(["logs"], env, Ct);

        // Every character a console could mangle is an escape: all that is left is printable ASCII and the line break.
        var text = output.ToString();
        Assert.Equal(ExitCodes.Success, exit);
        Assert.All(text, character => Assert.True(character is '\n' or '\r' or (>= ' ' and <= '~'), $"U+{(int)character:X4} was written as it is."));

        // And nothing was lost by it: parsed, the document says what the files said.
        var document = Document(text);
        Assert.Equal(paths.LogsDirectory, (string?)document["directory"]);
        var events = document["events"]!.AsArray();
        Assert.Equal("Привіт, {Ім_я}: naïve — 日本語 " + astral, (string?)events[0]!["@mt"]);
        Assert.Equal("Ада", (string?)events[0]!["Ім_я"]);
        Assert.Equal(@"C:\Users\Артем\.jason", (string?)events[0]!["Path"]);
        Assert.Equal("a \"q\" and \\ and \u0007 and \u007f", (string?)events[0]!["Quote"]);

        // The characters a template or a value is matched on stay as they are, so a reader can still match "@mt" verbatim.
        Assert.Contains("a+b <tag> & 'quoted' {X}", text, StringComparison.Ordinal);
        Assert.Contains("1 < 2 > 0 & 'ok' + 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_with_an_unpaired_surrogate_is_counted_as_damaged_and_the_answer_is_still_given()
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            Event(At(1), "before"),
            """{"@t":"2026-10-03T20:02:00.0000000Z","@mt":"damaged","Value":"a\ud800b"}""",
            """{"@t":"2026-10-03T20:03:00.0000000Z","@mt":"damaged too","\udc00":1}""",
            Event(At(4), "after"));

        var json = await RunAsync(dir);
        var human = await RunAsync(dir, "--human", "--level", "information");

        Assert.Equal(ExitCodes.Success, json.Exit);
        Assert.Equal(["before", "after"], Messages(Document(json.Out)));
        Assert.Equal(2, (int)Document(json.Out)["skipped_lines"]!);
        Assert.Equal(ExitCodes.Success, human.Exit);
        Assert.Equal(
            ["2026-10-03T20:01:00Z  INF  before", "2026-10-03T20:04:00Z  INF  after", "-- 2 lines were not events and were skipped."],
            Lines(human.Out));
    }

    [Fact]
    public async Task Lines_that_are_not_events_are_counted_and_never_shown()
    {
        using var dir = new TempPaths();
        var logs = LogsIn(dir);
        Write(logs, "runtime-20261003.jsonl", "not json at all", Event(At(1), "kept"), "[1,2,3]", "\"a string\"");

        var (exit, output, _) = await RunAsync(dir);

        Assert.Equal(ExitCodes.Success, exit);
        var document = Document(output);
        Assert.Equal(["kept"], Messages(document));
        Assert.Equal(3, (int)document["skipped_lines"]!);
    }

    [Fact]
    public async Task A_directory_with_no_log_in_it_is_an_empty_answer_and_not_an_error()
    {
        using var dir = new TempPaths();
        var logs = LogsIn(dir);
        Write(logs, "notes.txt", "{}"); // not the runtime's

        var (exit, output, _) = await RunAsync(dir);

        Assert.Equal(ExitCodes.Success, exit);
        var document = Document(output);
        Assert.True((bool)document["exists"]!);
        Assert.Empty(document["files"]!.AsArray());
        Assert.Empty(document["events"]!.AsArray());
        Assert.Equal(0, (int)document["returned"]!);
    }

    [Fact]
    public async Task No_directory_at_all_means_no_runtime_has_started_here_and_is_still_an_answer()
    {
        using var dir = new TempPaths();

        var (exit, output, error) = await RunAsync(dir);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(error);
        var document = Document(output);
        Assert.Equal(dir.Paths.LogsDirectory, (string?)document["directory"]);
        Assert.False((bool)document["exists"]!);
        Assert.Empty(document["files"]!.AsArray());
        Assert.Empty(document["events"]!.AsArray());
        Assert.Equal(0, (int)document["returned"]!);
        Assert.False((bool)document["limit_reached"]!);
        Assert.Equal(0, (int)document["skipped_lines"]!);
    }

    // ---- the filters -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("warning")]
    [InlineData("Warning")]
    [InlineData("WARNING")]
    public async Task A_level_keeps_the_events_at_that_level_and_above_in_any_case(string level)
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            Event(At(1), "verbose", "Verbose"),
            Event(At(2), "debug", "Debug"),
            Event(At(3), "information"),
            Event(At(4), "warning", "Warning"),
            Event(At(5), "error", "Error"),
            Event(At(6), "fatal", "Fatal"));

        var (exit, output, _) = await RunAsync(dir, "--level", level);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["warning", "error", "fatal"], Messages(Document(output)));
    }

    [Fact]
    public async Task Nothing_is_hidden_unless_a_level_is_asked_for()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", Event(At(1), "verbose", "Verbose"), Event(At(2), "fatal", "Fatal"));

        var (_, output, _) = await RunAsync(dir);

        Assert.Equal(["verbose", "fatal"], Messages(Document(output)));
    }

    [Fact]
    public async Task A_campaign_and_a_work_item_each_keep_only_the_events_that_name_them_and_together_both_must_hold()
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            Event(At(1), "startup"),
            Event(At(2), "campaign only", null, ("CampaignId", Campaign)),
            Event(At(3), "both", null, ("CampaignId", Campaign), ("WorkItemId", WorkItem), ("AttemptId", "att_X")),
            Event(At(4), "other campaign", null, ("CampaignId", OtherCampaign), ("WorkItemId", WorkItem)),
            Event(At(5), "other work item", null, ("CampaignId", Campaign), ("WorkItemId", OtherWorkItem)));

        var byCampaign = await RunAsync(dir, "--campaign", Campaign);
        var byWorkItem = await RunAsync(dir, "--work-item", WorkItem);
        var byBoth = await RunAsync(dir, "--campaign", Campaign, "--work-item", WorkItem);

        Assert.Equal(["campaign only", "both", "other work item"], Messages(Document(byCampaign.Out)));
        Assert.Equal(["both", "other campaign"], Messages(Document(byWorkItem.Out)));
        Assert.Equal(["both"], Messages(Document(byBoth.Out)));
    }

    [Theory]
    [InlineData("2026-10-03T20:03:00Z")]
    [InlineData("2026-10-03T20:03:00.000Z")]
    [InlineData("2026-10-03T22:03:00+02:00")]
    [InlineData("2026-10-03T15:33:00-04:30")]
    [InlineData("2026-10-03T20:03Z")]
    public async Task A_time_with_a_Z_or_an_offset_keeps_the_events_stamped_at_that_instant_or_later(string since)
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            Event(At(1), "before"),
            Event(At(2, 59), "just before"),
            Event(At(3), "at"),
            Event(At(4), "after"));

        var (exit, output, error) = await RunAsync(dir, "--since", since);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(error);
        Assert.Equal(["at", "after"], Messages(Document(output)));
    }

    [Fact]
    public async Task The_limit_keeps_the_newest_events_and_says_so_when_older_ones_were_left_out()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", Event(At(1), "one"), Event(At(2), "two"), Event(At(3), "three"), Event(At(4), "four"));

        var exact = await RunAsync(dir, "--limit", "4");
        var short1 = await RunAsync(dir, "--limit", "2");

        var all = Document(exact.Out);
        Assert.Equal(["one", "two", "three", "four"], Messages(all));
        Assert.False((bool)all["limit_reached"]!);

        var newest = Document(short1.Out);
        Assert.Equal(["three", "four"], Messages(newest));
        Assert.Equal(2, (int)newest["returned"]!);
        Assert.True((bool)newest["limit_reached"]!);
    }

    [Fact]
    public async Task With_no_limit_given_a_hundred_events_are_returned()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", [.. Enumerable.Range(1, 101).Select(number => Event($"2026-10-03T21:{number / 60:D2}:{number % 60:D2}.0000000Z", "event " + number))]);

        var (_, output, _) = await RunAsync(dir);

        var document = Document(output);
        var messages = Messages(document);
        Assert.Equal(100, messages.Length);
        Assert.Equal("event 2", messages[0]);
        Assert.Equal("event 101", messages[^1]);
        Assert.True((bool)document["limit_reached"]!);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10000")]
    public async Task The_limits_at_either_end_are_accepted(string limit)
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", Event(At(1), "one"));

        var (exit, output, _) = await RunAsync(dir, "--limit", limit);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["one"], Messages(Document(output)));
    }

    // ---- what a person got wrong -------------------------------------------------------------------------------

    [Theory]
    [InlineData("--level", "warn")]
    [InlineData("--level", "2")]
    [InlineData("--level", "warning,error")]
    [InlineData("--campaign", WorkItem)]
    [InlineData("--campaign", "cmp_")]
    [InlineData("--campaign", "cmp_nope")]
    [InlineData("--campaign", "cmp_01jb6k8tq2w9v4mz0c3y7h5nrd")]
    [InlineData("--campaign", Campaign + "0")]
    [InlineData("--campaign", "01JB6K8TQ2W9V4MZ0C3Y7H5NRD")]
    [InlineData("--campaign", "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRU")]
    [InlineData("--work-item", Campaign)]
    [InlineData("--work-item", "wi_")]
    [InlineData("--work-item", "wi_short")]
    [InlineData("--work-item", WorkItem + " ")]
    [InlineData("--since", "2026-10-03T20:05:31")]
    [InlineData("--since", "2026-10-03T20:05:31.123")]
    [InlineData("--since", "2026-10-03 20:05:31")]
    [InlineData("--since", "2026-10-03")]
    [InlineData("--since", "yesterday")]
    [InlineData("--since", "2026-13-45T20:05:31Z")]
    [InlineData("--since", "1759521931")]
    [InlineData("--limit", "0")]
    [InlineData("--limit", "-1")]
    [InlineData("--limit", "10001")]
    [InlineData("--limit", "many")]
    [InlineData("--limit", "1.5")]
    public async Task A_filter_that_cannot_be_what_it_names_is_a_usage_error_before_anything_is_read(string option, string value)
    {
        // No log directory at all: the refusal comes from the arguments, not from what was or was not there to read.
        using var dir = new TempPaths();

        var (exit, output, error) = await RunAsync(dir, option, value);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task A_usage_error_names_the_option_it_is_about()
    {
        using var dir = new TempPaths();

        Assert.Contains("--since", (await RunAsync(dir, "--since", "2026-10-03T20:05:31")).Error, StringComparison.Ordinal);
        Assert.Contains("--level", (await RunAsync(dir, "--level", "warn")).Error, StringComparison.Ordinal);
        Assert.Contains("--campaign", (await RunAsync(dir, "--campaign", "x")).Error, StringComparison.Ordinal);
        Assert.Contains("--work-item", (await RunAsync(dir, "--work-item", "x")).Error, StringComparison.Ordinal);
        Assert.Contains("--limit", (await RunAsync(dir, "--limit", "0")).Error, StringComparison.Ordinal);
    }

    // ---- for people --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Human_output_is_one_line_per_event_oldest_first_with_the_message_put_back_together()
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            Event(At(1), "A {Thing} very quiet", "Verbose", ("Thing", "v")),
            Event(At(2), "debug", "Debug"),
            Event("2026-10-03T20:03:00.9999999Z", "Jason runtime {Version} starting as {InstanceId}", null, ("Version", "0.3.0"), ("InstanceId", "inst_01")),
            Event(At(4), "Disk is {Percent} full ({Missing})", "Warning", ("Percent", "91")),
            Event(At(5), "error", "Error"),
            Event(At(6), "fatal {{braces}}", "Fatal"));

        var (exit, output, error) = await RunAsync(dir, "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(error);
        Assert.Equal(
            [
                "2026-10-03T20:01:00Z  VRB  A v very quiet",
                "2026-10-03T20:02:00Z  DBG  debug",
                "2026-10-03T20:03:00Z  INF  Jason runtime 0.3.0 starting as inst_01",
                "2026-10-03T20:04:00Z  WRN  Disk is 91 full ({Missing})",
                "2026-10-03T20:05:00Z  ERR  error",
                "2026-10-03T20:06:00Z  FTL  fatal {braces}",
            ],
            Lines(output));
    }

    [Fact]
    public async Task Human_output_shows_the_time_in_utc_whatever_zone_the_line_was_stamped_in()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", Event("2026-10-03T22:05:31.0000000+02:00", "stamped in another zone"));

        var (_, output, _) = await RunAsync(dir, "--human");

        Assert.Equal(["2026-10-03T20:05:31Z  INF  stamped in another zone"], Lines(output));
    }

    [Fact]
    public async Task Human_output_puts_an_exceptions_first_line_beneath_its_event_and_no_more_of_it()
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            """{"@t":"2026-10-03T20:07:00.0000000Z","@mt":"Attempt {AttemptId} failed","@l":"Error","@x":"System.InvalidOperationException: boom\r\n   at Jason.Thing.Run()\r\n   at Jason.Other()","AttemptId":"att_X"}""",
            Event(At(8), "after"));

        var (_, output, _) = await RunAsync(dir, "--human");

        Assert.Equal(
            [
                "2026-10-03T20:07:00Z  ERR  Attempt att_X failed",
                "    System.InvalidOperationException: boom",
                "2026-10-03T20:08:00Z  INF  after",
            ],
            Lines(output));
        Assert.DoesNotContain("Jason.Thing.Run", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_output_shows_what_is_in_a_value_and_never_lets_a_value_become_a_second_line_or_a_terminal_command()
    {
        using var dir = new TempPaths();
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            """{"@t":"2026-10-03T20:07:00.0000000Z","@mt":"Said {Detail}","Detail":"first\nsecond\r\n\t\u001b[31mred\u009b\u007f end"}""",
            """{"@t":"2026-10-03T20:08:00.0000000Z","@mt":"Failed","@l":"Error","@x":"Boom\u001b[2J here\nnext"}""");

        var (_, output, _) = await RunAsync(dir, "--human");

        Assert.Equal(
            [
                @"2026-10-03T20:07:00Z  INF  Said first\nsecond\r\n\t\u001B[31mred\u009B\u007F end",
                "2026-10-03T20:08:00Z  ERR  Failed",
                @"    Boom\u001B[2J here",
            ],
            Lines(output));
        Assert.False(output.Contains('\u001b', StringComparison.Ordinal), "A raw escape character reached the terminal.");
    }

    [Fact]
    public async Task Human_output_writes_out_the_characters_that_reorder_or_hide_text_and_keeps_the_ones_a_person_reads()
    {
        using var dir = new TempPaths();
        var girl = char.ConvertFromUtf32(0x1F467); // a symbol, not a format character: it stays
        Write(
            LogsIn(dir),
            "runtime-20261003.jsonl",
            """{"@t":"2026-10-03T20:09:00.0000000Z","@mt":"Said {Detail}","Detail":"RLO \u202e abc \u2066 x \u200b y \u00ad z \ufeff w \ud83d\udc67 tag \udb40\udc67 sep \u2028 and \u2029 end"}""",
            """{"@t":"2026-10-03T20:10:00.0000000Z","@mt":"Failed","@l":"Error","@x":"Boom \u202e here"}""",
            """{"@t":"2026-10-03T20:11:00.0000000Z","@mt":"Привіт, {Ім_я}: naïve — 日本語","Ім_я":"Ада"}""");

        var (_, output, _) = await RunAsync(dir, "--human");

        Assert.Equal(
            [
                $@"2026-10-03T20:09:00Z  INF  Said RLO \u202E abc \u2066 x \u200B y \u00AD z \uFEFF w {girl} tag \uDB40\uDC67 sep \u2028 and \u2029 end",
                "2026-10-03T20:10:00Z  ERR  Failed",
                @"    Boom \u202E here",
                "2026-10-03T20:11:00Z  INF  Привіт, Ада: naïve — 日本語",
            ],
            Lines(output));
    }

    [Fact]
    public async Task Human_output_says_when_the_limit_left_older_events_out_and_only_then()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", Event(At(1), "one"), Event(At(2), "two"), Event(At(3), "three"));

        var cut = Lines((await RunAsync(dir, "--human", "--limit", "2")).Out);
        var whole = Lines((await RunAsync(dir, "--human", "--limit", "3")).Out);

        Assert.Equal(3, cut.Length);
        Assert.EndsWith("  two", cut[0], StringComparison.Ordinal);
        Assert.EndsWith("  three", cut[1], StringComparison.Ordinal);
        Assert.StartsWith("-- ", cut[2], StringComparison.Ordinal);
        Assert.Contains("2", cut[2], StringComparison.Ordinal);
        Assert.Contains("--limit", cut[2], StringComparison.Ordinal);
        Assert.Contains("older", cut[2], StringComparison.Ordinal);
        Assert.Equal(3, whole.Length);
        Assert.EndsWith("  three", whole[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_output_says_how_many_lines_were_not_events()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", "garbage", Event(At(1), "kept"), "more garbage");

        var lines = Lines((await RunAsync(dir, "--human")).Out);

        Assert.Equal("2026-10-03T20:01:00Z  INF  kept", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("-- ", lines[1], StringComparison.Ordinal);
        Assert.Contains("2 lines were not events", lines[1], StringComparison.Ordinal);

        Write(dir.Paths.LogsDirectory, "runtime-20261003.jsonl", "garbage", Event(At(1), "kept"));
        var single = Lines((await RunAsync(dir, "--human")).Out);
        Assert.Contains("1 line was not an event", single[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_output_says_when_there_is_nothing_to_show_and_why()
    {
        using var empty = new TempPaths();
        using var missing = new TempPaths();
        using var narrowed = new TempPaths();
        LogsIn(empty);
        Write(LogsIn(narrowed), "runtime-20261003.jsonl", Event(At(1), "quiet"));

        var noLogs = await RunAsync(empty, "--human");
        var noDirectory = await RunAsync(missing, "--human");
        var noMatch = await RunAsync(narrowed, "--human", "--level", "error");

        Assert.Equal(ExitCodes.Success, noLogs.Exit);
        Assert.Equal(ExitCodes.Success, noDirectory.Exit);
        Assert.Equal(ExitCodes.Success, noMatch.Exit);
        Assert.Contains("No log files", noLogs.Out, StringComparison.Ordinal);
        Assert.Contains(missing.Paths.LogsDirectory, noDirectory.Out, StringComparison.Ordinal);
        Assert.Contains("no runtime has started", noDirectory.Out, StringComparison.Ordinal);
        Assert.Contains("No event matches", noMatch.Out, StringComparison.Ordinal);
    }

    // ---- when the files cannot be read -------------------------------------------------------------------------

    [Fact]
    public async Task A_logs_path_that_is_not_a_directory_is_a_refusal_with_the_error_envelope_on_stdout()
    {
        using var dir = new TempPaths();
        File.WriteAllText(dir.Paths.LogsDirectory, "a file where the log directory goes");

        var (exit, output, _) = await RunAsync(dir);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = Document(output)["error"]!.AsObject();
        Assert.Equal(["code", "message", "retryable"], error.Select(property => property.Key));
        Assert.Equal("logs_unreadable", (string?)error["code"]);
        Assert.False((bool)error["retryable"]!);
        Assert.Contains(dir.Paths.LogsDirectory, (string?)error["message"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_log_file_that_cannot_be_opened_is_a_refusal_and_never_a_quietly_shorter_answer()
    {
        using var dir = new TempPaths();
        var logs = LogsIn(dir);
        Write(logs, "runtime-20261002.jsonl", Event("2026-10-02T09:00:00.0000000Z", "readable"));
        var held = Write(logs, "runtime-20261003.jsonl", Event(At(1), "held"));

        // Another process holding the file without sharing it: neither a reader nor a deleter is let in.
        using var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var (exit, output, _) = await RunAsync(dir);

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = Document(output)["error"]!.AsObject();
        Assert.Equal("logs_unreadable", (string?)error["code"]);
        Assert.Contains(logs, (string?)error["message"], StringComparison.Ordinal);
        Assert.DoesNotContain("\"events\"", output, StringComparison.Ordinal);
    }

    // ---- the runtime is never asked ----------------------------------------------------------------------------

    [Fact]
    public async Task The_runtime_is_never_asked_and_its_descriptor_is_never_needed()
    {
        using var dir = new TempPaths();
        Write(LogsIn(dir), "runtime-20261003.jsonl", Event(At(1), "written before it stopped answering"));

        // A descriptor naming a runtime that is up, and a handler that would answer for it by failing every request.
        // Anything that reached for either would be exit 3, not an answer.
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("inst_1"));
        var requests = new List<Uri?>();
        var handler = new FakeHandler(request =>
        {
            requests.Add(request.RequestUri);
            throw new HttpRequestException("connection refused");
        });

        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment(output, error, dir.Paths, handler);

        var json = await CliApp.RunAsync(["logs"], env, Ct);
        var human = await CliApp.RunAsync(["logs", "--human", "--level", "information", "--limit", "5"], env, Ct);

        Assert.Equal(ExitCodes.Success, json);
        Assert.Equal(ExitCodes.Success, human);
        Assert.Empty(requests);
        Assert.Empty(error.ToString());
        Assert.Contains("written before it stopped answering", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_runtime_ever_started_and_no_descriptor_it_still_answers_and_never_exits_three()
    {
        using var dir = new TempPaths();
        Assert.False(File.Exists(dir.Paths.DescriptorFile));

        var (exit, _, _) = await RunAsync(dir);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.NotEqual(ExitCodes.RuntimeUnavailable, exit);
    }

    // ---- its own help is the contract --------------------------------------------------------------------------

    [Fact]
    public async Task Its_help_publishes_the_directory_rule_the_vocabulary_the_limit_the_output_and_the_exit_codes()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["logs", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var help = output.ToString();

        // Where, and that nothing else is read.
        Assert.Contains("JASON_DATA_DIR", help, StringComparison.Ordinal);
        Assert.Contains("runtime-YYYYMMDD.jsonl", help, StringComparison.Ordinal);
        Assert.Contains("never asks the runtime", help, StringComparison.Ordinal);
        Assert.Contains("never reads its descriptor", help, StringComparison.Ordinal);

        // The filters and the words they take.
        Assert.Contains("verbose, debug, information, warning, error, fatal", help, StringComparison.Ordinal);
        Assert.Contains("cmp_", help, StringComparison.Ordinal);
        Assert.Contains("wi_", help, StringComparison.Ordinal);
        Assert.Contains("2026-09-18T09:00:00Z", help, StringComparison.Ordinal);
        Assert.Contains("older build", help, StringComparison.Ordinal);

        // The order, the limit, the shape of the answer.
        Assert.Contains("oldest first", help, StringComparison.Ordinal);
        Assert.Contains("defaults to 100", help, StringComparison.Ordinal);
        Assert.Contains("at most 10000", help, StringComparison.Ordinal);
        Assert.Contains("limit_reached", help, StringComparison.Ordinal);
        Assert.Contains("skipped_lines", help, StringComparison.Ordinal);
        Assert.Contains("exactly as the runtime wrote", help, StringComparison.Ordinal);
        Assert.Contains("--human", help, StringComparison.Ordinal);

        // The JSON is plain ASCII, and what that means for a console and for --human.
        Assert.Contains("plain ASCII", help, StringComparison.Ordinal);
        Assert.Contains("\\uXXXX", help, StringComparison.Ordinal);
        Assert.Contains("--human writes text as it is", help, StringComparison.Ordinal);

        // And the codes, including the one it never uses, and every way of exiting 1.
        Assert.Contains("logs_unreadable", help, StringComparison.Ordinal);
        Assert.Contains("exists is false", help, StringComparison.Ordinal);
        Assert.Contains("is a file, not a directory", help, StringComparison.Ordinal);
        Assert.Contains("a log file in it cannot be opened", help, StringComparison.Ordinal);
        Assert.Contains("the directory cannot be listed", help, StringComparison.Ordinal);
        Assert.Contains("Never 3", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_takes_no_subcommands_and_none_of_the_options_other_verbs_carry_for_the_api()
    {
        using var dir = new TempPaths();

        var (tail, _, tailError) = await RunAsync(dir, "tail");
        var (file, _, _) = await RunAsync(dir, "--file", "x.json");
        var (reason, _, _) = await RunAsync(dir, "--reason", "why");

        Assert.Equal(ExitCodes.Usage, tail);
        Assert.Contains("tail", tailError, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Usage, file);
        Assert.Equal(ExitCodes.Usage, reason);
    }

    /// <summary>The error envelope is the API's shape, so an agent parses one error format whichever verb refused.</summary>
    [Fact]
    public async Task The_refusal_is_the_same_envelope_the_api_uses()
    {
        using var dir = new TempPaths();
        File.WriteAllText(dir.Paths.LogsDirectory, "x");

        var (_, output, _) = await RunAsync(dir);

        var envelope = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(output, JasonJson.Options)!;
        Assert.Equal("logs_unreadable", envelope.Error.Code);
        Assert.False(envelope.Error.Retryable);
    }
}
