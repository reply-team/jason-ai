using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Harness;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jason.Runtime.Tests;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// <c>host.appendLine</c>: the one way a plugin writes a file. It appends one line to a file directly in a directory a
/// person granted, refuses everything else as a rule the plugin cannot catch, and hands back as a failure it can catch
/// only what the disk did.
/// </summary>
public sealed class FilesServiceTests : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TempPackage _package = new();
    private readonly string _directory;

    public FilesServiceTests()
    {
        _directory = Path.Combine(_package.Outside, "lines");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => _package.Dispose();

    private string File(string name) => Path.Combine(_directory, name);

    private SdkHarness Granted(
        Action<InvocationBuilder>? configure = null,
        TimeSpan? sharingWait = null,
        TimeSpan? remaining = null,
        IReadOnlyList<FileDirectoryGrant>? directories = null,
        Action<FileStream, byte[]>? lineWrite = null) =>
        new(
            _package.Root,
            builder =>
            {
                builder.Grants = new InvocationGrants(null, null, null, new FilesGrants(directories ?? [new FileDirectoryGrant(_directory, _directory)]));
                configure?.Invoke(builder);
            },
            remaining: remaining,
            sharingWait: sharingWait,
            lineWrite: lineWrite);

    private static string Call(string path, string text) =>
        $"host.appendLine({JsonSerializer.Serialize(path)}, {JsonSerializer.Serialize(text)})";

    private static HostRuleException Refused(SdkHarness harness, string path, string text = "one line") =>
        Assert.Throws<HostRuleException>(() => harness.Evaluate(Call(path, text)));

    /// <summary>What a plugin's own <c>catch</c> holds after the call, or null where nothing was thrown.</summary>
    private static JsonObject? Caught(SdkHarness harness, string path, string text = "one line")
    {
        var answer = harness.Evaluate($$"""
            (() => {
              try { {{Call(path, text)}}; return null; }
              catch (e) { return JSON.stringify({ name: e.name, class: e.class, code: e.code, message: e.message, details: e.details }); }
            })()
            """);

        return answer.IsNull() ? null : (JsonObject)JsonNode.Parse(answer.AsString())!;
    }

    private static byte[] Bytes(string file) => System.IO.File.ReadAllBytes(file);

    private static string Text(string file) => Utf8.GetString(Bytes(file));

    [Fact]
    public void A_line_lands_after_what_the_file_holds_in_UTF8_with_no_mark_and_one_line_feed()
    {
        System.IO.File.WriteAllBytes(File("a.jsonl"), Utf8.GetBytes("first\n"));
        using var harness = Granted();

        var answer = harness.Evaluate(Call(File("a.jsonl"), "вторая строка"));

        Assert.True(answer.IsUndefined());
        Assert.Equal([.. Utf8.GetBytes("first\nвторая строка\n")], Bytes(File("a.jsonl")));
    }

    [Fact]
    public void The_file_and_the_directory_are_made_at_the_first_line()
    {
        var directory = Path.Combine(_package.Outside, "not", "yet");
        using var harness = Granted(directories: [new FileDirectoryGrant(directory, directory)]);

        harness.Evaluate(Call(Path.Combine(directory, "a.log"), "here"));

        Assert.Equal("here\n", Text(Path.Combine(directory, "a.log")));
    }

    [Fact]
    public void Lines_of_one_invocation_keep_their_order()
    {
        using var harness = Granted();

        harness.Evaluate($"{Call(File("a.log"), "1")}; {Call(File("a.log"), "2")}; {Call(File("a.log"), "3")};");

        Assert.Equal("1\n2\n3\n", Text(File("a.log")));
    }

    [Fact]
    public void A_plugin_without_a_grant_is_stopped_and_cannot_catch_it()
    {
        using var harness = new SdkHarness(_package.Root);

        var refused = Refused(harness, File("a.log"));

        Assert.Equal(HostOutcomeCodes.CapabilityNotGranted, refused.Code);
        Assert.Equal("files", refused.Details!["capability"]!.GetValue<string>());
        Assert.False(System.IO.File.Exists(File("a.log")));
    }

    [Theory]
    [InlineData("host.appendLine()")]
    [InlineData("host.appendLine(1, 'text')")]
    [InlineData("host.appendLine('/a')")]
    [InlineData("host.appendLine('/a', 1)")]
    [InlineData("host.appendLine('/a', null)")]
    public void Arguments_that_are_not_two_strings_are_a_type_error(string expression)
    {
        using var harness = Granted();

        Assert.Equal("TypeError", harness.Caught(expression));
    }

    [Fact]
    public void Paths_outside_the_granted_directory_are_refused_by_rule()
    {
        using var harness = Granted();
        string[] paths =
        [
            Path.Combine(_package.Outside, "other", "a.log"),
            Path.Combine(_directory, "deeper", "a.log"),
            Path.Combine(_package.Outside, "a.log"),
            Path.Combine(_directory, "..", "other", "a.log"),
            _directory + Path.DirectorySeparatorChar,
            _directory,
            "a.log",
            Path.Combine("lines", "a.log"),
            string.Empty,
            "lines\0a.log",
        ];

        foreach (var path in paths)
        {
            Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, path).Code);
        }

        Assert.Equal(0, harness.Budget.FileCalls);
        Assert.DoesNotContain(
            Directory.GetFiles(_package.Outside, "*", SearchOption.AllDirectories),
            file => !file.StartsWith(_package.Root, StringComparison.Ordinal));
    }

    [Fact]
    public void A_path_is_compared_after_it_is_made_full()
    {
        using var harness = Granted();

        harness.Evaluate(Call(Path.Combine(_directory, "..", "lines", "a.log"), "here"));

        Assert.Equal("here\n", Text(File("a.log")));
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("-dash")]
    [InlineData("_under")]
    [InlineData("a b")]
    [InlineData(" a")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\\b")]
    [InlineData("café.log")]
    [InlineData("а.log")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL.txt")]
    [InlineData("nul.txt")]
    [InlineData("Com1.log")]
    [InlineData("COM9")]
    [InlineData("LPT9")]
    [InlineData("lpt1.a.b")]
    [InlineData("AUX.tar.gz")]
    [InlineData("PRN")]
    [InlineData("a.")]
    [InlineData("a.log.")]
    [InlineData("a.log ")]
    public void A_file_name_outside_the_whitelist_is_refused_on_every_platform(string name)
    {
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, _directory + Path.DirectorySeparatorChar + name).Code);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public void A_file_name_of_one_hundred_and_one_is_refused()
    {
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, File(new string('a', 101))).Code);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("7")]
    [InlineData("a.b-c_d")]
    [InlineData("COM10.log")]
    [InlineData("COM0")]
    [InlineData("LPT10")]
    [InlineData("console.log")]
    [InlineData("nully.txt")]
    [InlineData("A.JSONL")]
    public void A_name_the_whitelist_takes_is_written(string name)
    {
        using var harness = Granted();

        harness.Evaluate(Call(File(name), "here"));

        Assert.Equal("here\n", Text(File(name)));
    }

    [Fact]
    public void A_file_name_of_one_hundred_is_written()
    {
        using var harness = Granted();
        var name = new string('a', 100);

        harness.Evaluate(Call(File(name), "here"));

        Assert.Equal("here\n", Text(File(name)));
    }

    [Fact]
    public void A_directory_at_the_end_of_the_path_is_refused()
    {
        Directory.CreateDirectory(File("sub.log"));
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, File("sub.log")).Code);
    }

    [Fact]
    public void A_link_at_the_end_of_the_path_is_refused_and_nothing_lands_where_it_leads()
    {
        var target = Path.Combine(_package.Outside, "target");
        Directory.CreateDirectory(target);
        Link(File("link.log"), target);
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, File("link.log")).Code);
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    [Critical(Family.X2, "no line leaves the granted directory by a link at the end of its path")]
    public void A_link_to_a_file_at_the_end_of_the_path_is_refused_and_nothing_lands_where_it_leads()
    {
        var target = Path.Combine(_package.Outside, "target.log");
        System.IO.File.WriteAllText(target, "first\n");
        LinkToAFile(File("link.log"), target);
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, File("link.log")).Code);
        Assert.Equal("first\n", Text(target));
    }

    [Fact]
    public void A_link_that_leads_nowhere_is_refused()
    {
        var target = Path.Combine(_package.Outside, "gone");
        Directory.CreateDirectory(target);
        Link(File("link.log"), target);
        Directory.Delete(target);
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, File("link.log")).Code);
        Assert.False(Directory.Exists(target));
    }

    /// <summary>A named pipe would hold the line for whoever reads it, and with nobody there it must not stop the invocation.</summary>
    [Fact]
    public async Task A_named_pipe_at_the_end_of_the_path_is_refused_without_waiting_for_a_reader()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a named pipe is no file on Windows.");
        using (var make = Process.Start(new ProcessStartInfo("mkfifo") { ArgumentList = { File("pipe.log") } })!)
        {
            make.WaitForExit();
            Assert.Equal(0, make.ExitCode);
        }

        using var harness = Granted();
        var ct = TestContext.Current.CancellationToken;

        var refused = await Task.Run(() => Refused(harness, File("pipe.log")), ct).WaitAsync(TimeSpan.FromSeconds(30), ct);

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, refused.Code);
    }

    [Fact]
    public void A_line_that_fits_to_the_byte_with_its_line_feed_is_written_and_one_more_is_refused()
    {
        using var harness = Granted(builder => builder.Limits = InvocationFactory.DefaultLimits with { Files = new FileLimits(16, 64) });

        harness.Evaluate(Call(File("a.log"), new string('a', 15)));
        var refused = Refused(harness, File("a.log"), new string('a', 16));

        Assert.Equal(HostOutcomeCodes.FileLineTooLarge, refused.Code);
        Assert.Equal(new string('a', 15) + "\n", Text(File("a.log")));
        Assert.Equal(1, harness.Budget.FileCalls);
    }

    [Fact]
    public void The_size_of_a_line_is_its_UTF8_bytes()
    {
        using var harness = Granted(builder => builder.Limits = InvocationFactory.DefaultLimits with { Files = new FileLimits(16, 64) });

        harness.Evaluate(Call(File("a.log"), new string('ж', 7) + "a"));

        Assert.Equal(HostOutcomeCodes.FileLineTooLarge, Refused(harness, File("a.log"), new string('ж', 8)).Code);
        Assert.Equal(16, Bytes(File("a.log")).Length);
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    [InlineData("two\r\nlines")]
    [InlineData("\n")]
    [InlineData("ends in one\n")]
    public void A_line_break_inside_the_text_is_its_own_refusal(string text)
    {
        using var harness = Granted();

        Assert.Equal(HostOutcomeCodes.FileLineMultiline, Refused(harness, File("a.log"), text).Code);
        Assert.False(System.IO.File.Exists(File("a.log")));
        Assert.Equal(0, harness.Budget.FileCalls);
    }

    [Fact]
    public void A_line_break_over_the_limit_is_still_named_as_a_line_break()
    {
        using var harness = Granted(builder => builder.Limits = InvocationFactory.DefaultLimits with { Files = new FileLimits(16, 64) });

        Assert.Equal(HostOutcomeCodes.FileLineMultiline, Refused(harness, File("a.log"), new string('a', 32) + "\n" + new string('a', 32)).Code);
    }

    [Fact]
    public void An_empty_line_is_a_line()
    {
        using var harness = Granted();

        harness.Evaluate(Call(File("a.log"), string.Empty));

        Assert.Equal("\n", Text(File("a.log")));
    }

    [Fact]
    public void Calls_past_the_limit_are_refused_and_a_refused_call_spends_none()
    {
        using var harness = Granted(builder => builder.Limits = InvocationFactory.DefaultLimits with { Files = new FileLimits(1024, 2) });

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, File(".refused")).Code);
        harness.Evaluate($"{Call(File("a.log"), "1")}; {Call(File("a.log"), "2")};");
        var refused = Refused(harness, File("a.log"), "3");

        Assert.Equal(HostOutcomeCodes.FileLimit, refused.Code);
        Assert.Equal("files.max_calls", refused.Details!["limit"]!.GetValue<string>());
        Assert.Equal(2, refused.Details!["max"]!.GetValue<int>());
        Assert.Equal(2, harness.Budget.FileCalls);
        Assert.Equal("1\n2\n", Text(File("a.log")));
    }

    [Fact]
    public void A_file_held_past_the_wait_is_a_failure_the_plugin_can_catch()
    {
        using var held = Hold(File("a.log"));
        using var harness = Granted(sharingWait: TimeSpan.FromMilliseconds(100));

        var failure = Caught(harness, File("a.log"));

        Assert.NotNull(failure);
        Assert.Equal(HostFailure.Name, failure["name"]!.GetValue<string>());
        Assert.Equal("transient", failure["class"]!.GetValue<string>());
        Assert.Equal(HostOutcomeCodes.FileWriteFailed, failure["code"]!.GetValue<string>());
        Assert.Equal("a.log", failure["details"]!["file"]!.GetValue<string>());
        Assert.DoesNotContain(_directory, failure.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Budget.FileCalls);
        Assert.True(harness.Budget.ExternalCallsStarted > 0, "a started append is a call that may have acted.");
    }

    [Fact]
    public void A_file_held_for_a_moment_is_waited_for()
    {
        var held = Hold(File("a.log"));
        using var release = new Timer(_ => held.Dispose(), null, TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);
        using var harness = Granted();

        harness.Evaluate(Call(File("a.log"), "here"));

        Assert.Equal("here\n", Text(File("a.log")));
    }

    [Fact]
    public void The_wait_stays_inside_what_is_left_of_the_invocation()
    {
        using var held = Hold(File("a.log"));
        using var harness = Granted(remaining: TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();

        var failure = Caught(harness, File("a.log"));

        Assert.Equal(HostOutcomeCodes.FileWriteFailed, failure!["code"]!.GetValue<string>());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the door waited {watch.Elapsed} with 50 ms left.");
    }

    /// <summary>What the door may do beside the rule is end, not refuse: a read-only file is the disk's answer, the same as a full disk.</summary>
    [Fact]
    public void A_file_the_disk_refuses_is_a_failure_the_plugin_can_catch()
    {
        Assert.SkipWhen(Environment.UserName == "root", "root writes a read-only file all the same.");
        System.IO.File.WriteAllBytes(File("a.log"), []);
        System.IO.File.SetAttributes(File("a.log"), FileAttributes.ReadOnly);
        try
        {
            using var harness = Granted();

            var failure = Caught(harness, File("a.log"));

            Assert.Equal(HostOutcomeCodes.FileWriteFailed, failure!["code"]!.GetValue<string>());
        }
        finally
        {
            System.IO.File.SetAttributes(File("a.log"), FileAttributes.Normal);
        }
    }

    /// <summary>
    /// How a write fails with no disk to fill: a range another handle keeps locked past the end, which stops a write on
    /// Windows alone. The failure stands, what the file held stays, and the next line takes up where it was.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void A_write_the_disk_refuses_leaves_the_file_as_it_was()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "elsewhere a lock on a range is advisory and stops no write.");
        System.IO.File.WriteAllText(File("a.log"), "first\n");
        using var harness = Granted();

        using (var holder = new FileStream(File("a.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            holder.Lock(holder.Length, 4096);

            var failure = Caught(harness, File("a.log"));

            Assert.Equal(HostOutcomeCodes.FileWriteFailed, failure!["code"]!.GetValue<string>());
        }

        Assert.Equal("first\n", Text(File("a.log")));
        harness.Evaluate(Call(File("a.log"), "second"));
        Assert.Equal("first\nsecond\n", Text(File("a.log")));
    }

    /// <summary>
    /// A file at its size limit takes part of the line and then refuses the rest, which .NET on Linux and macOS reports as
    /// an <see cref="ArgumentOutOfRangeException"/>: the part comes off, and the plugin hears what the disk did.
    /// </summary>
    [Fact]
    public void A_write_that_stops_part_of_the_way_comes_off_and_fails_as_the_disk_failing()
    {
        System.IO.File.WriteAllText(File("a.log"), "first\n");
        var reached = 0L;
        using var harness = Granted(lineWrite: (stream, line) =>
        {
            stream.Write(line, 0, line.Length / 2);
            stream.Flush();
            reached = stream.Length;
            throw new ArgumentOutOfRangeException(nameof(line));
        });

        var failure = Caught(harness, File("a.log"));

        Assert.True(reached > "first\n".Length, "the write took nothing, so nothing shows the part coming off.");
        Assert.Equal(HostOutcomeCodes.FileWriteFailed, failure!["code"]!.GetValue<string>());
        Assert.Equal("first\n", Text(File("a.log")));
    }

    [Fact]
    public void A_grant_is_taken_as_written_and_as_it_led_at_the_load()
    {
        var real = Path.Combine(_package.Outside, "real");
        var link = Path.Combine(_package.Outside, "link");
        Directory.CreateDirectory(real);
        Link(link, real);
        var leads = FolderPaths.Real(link);
        using var harness = Granted(directories: [new FileDirectoryGrant(link, leads)]);

        harness.Evaluate(Call(Path.Combine(link, "a.log"), "written"));
        harness.Evaluate(Call(Path.Combine(leads, "a.log"), "resolved"));

        Assert.Equal("written\nresolved\n", Text(Path.Combine(real, "a.log")));
    }

    [Fact]
    public void A_grant_written_with_a_separator_at_its_end_takes_the_same_directory()
    {
        using var harness = Granted(directories: [new FileDirectoryGrant(_directory + Path.DirectorySeparatorChar, _directory)]);

        harness.Evaluate(Call(File("a.log"), "here"));

        Assert.Equal("here\n", Text(File("a.log")));
    }

    [Fact]
    public void A_link_that_leads_elsewhere_since_the_load_is_refused()
    {
        var real = Path.Combine(_package.Outside, "real");
        var elsewhere = Path.Combine(_package.Outside, "elsewhere");
        var link = Path.Combine(_package.Outside, "link");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(elsewhere);
        Link(link, real);
        var leads = FolderPaths.Real(link);
        Unlink(link);
        Link(link, elsewhere);
        using var harness = Granted(directories: [new FileDirectoryGrant(link, leads)]);

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, Path.Combine(link, "a.log")).Code);
        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, Path.Combine(leads, "a.log")).Code);
        Assert.Empty(Directory.GetFileSystemEntries(real));
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
    }

    /// <summary>A directory not there at the load was frozen as written; a link put at its place since leads somewhere nobody granted.</summary>
    [Fact]
    public void A_directory_made_a_link_since_the_load_is_refused()
    {
        var elsewhere = Path.Combine(_package.Outside, "elsewhere");
        var directory = Path.Combine(_package.Outside, "not-yet");
        Directory.CreateDirectory(elsewhere);
        var frozen = FolderPaths.Real(directory);
        Link(directory, elsewhere);
        using var harness = Granted(directories: [new FileDirectoryGrant(directory, frozen)]);

        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, Path.Combine(directory, "a.log")).Code);
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
    }

    [Fact]
    public void A_path_is_compared_as_the_platform_compares_folders()
    {
        using var harness = Granted();
        var cased = Path.Combine(_directory.ToUpperInvariant(), "a.log");

        if (FolderPaths.Comparison == StringComparison.Ordinal)
        {
            Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, Refused(harness, cased).Code);
            return;
        }

        harness.Evaluate(Call(cased, "here"));

        Assert.Equal("here\n", Text(File("a.log")));
    }

    /// <summary>A directory set through a plugin's configuration is a known secret, so a path in a record would read as a mask.</summary>
    [Fact]
    public void The_diagnostics_carry_the_name_and_the_bytes_and_never_the_directory()
    {
        var harness = Granted(builder => builder.Config = new Dictionary<string, string> { ["file"] = _directory });
        using (harness)
        {
            harness.Evaluate(Call(File("a.log"), "жа"));

            var line = Assert.Single(harness.Lines, line => line["message"]?.GetValue<string>() == "append");
            Assert.Equal("a.log", line["data"]!["file"]!.GetValue<string>());
            Assert.Equal(5, line["data"]!["bytes"]!.GetValue<int>());
            Assert.Null(line["data"]!["path"]);
            Assert.DoesNotContain(_directory, harness.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[redacted]", harness.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("жа", harness.Stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_failed_append_says_so_in_the_diagnostics_by_name_alone()
    {
        using var held = Hold(File("a.log"));
        using var harness = Granted(sharingWait: TimeSpan.FromMilliseconds(50));

        _ = Caught(harness, File("a.log"));

        var line = Assert.Single(harness.Lines, line => line["message"]?.GetValue<string>() == "append_failed");
        Assert.Equal("warn", line["level"]!.GetValue<string>());
        Assert.Equal("a.log", line["data"]!["file"]!.GetValue<string>());
        Assert.DoesNotContain(_directory, harness.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The line is data, as a request's body is: what the plugin was given to write lands as it was given.</summary>
    [Fact]
    public void A_line_is_not_masked()
    {
        using var harness = Granted(builder => builder.Config = new Dictionary<string, string> { ["token"] = "known-secret-value" });

        harness.Evaluate(Call(File("a.log"), "known-secret-value"));

        Assert.Equal("known-secret-value\n", Text(File("a.log")));
    }

    [Fact]
    public void A_refusal_does_not_say_the_path_back()
    {
        using var harness = Granted();
        var path = Path.Combine(_package.Outside, "other", "secret-name.log");

        var refused = Refused(harness, path);

        Assert.DoesNotContain("secret-name", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-name", refused.Details?.ToJsonString() ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(_package.Outside, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_write_the_plugin_does_not_catch_ends_the_invocation_transient_and_counts_as_a_call()
    {
        using var held = Hold(File("a.log"));

        var outcome = Run($$"""
            export function invoke() {
              {{Call(File("a.log"), "here")}};
              return { result: null };
            }
            """, timeoutMs: 500, deadlineMs: 10_000);

        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Equal(FailureClass.Transient, outcome.Error!.Class);
        Assert.Equal(HostOutcomeCodes.FileWriteFailed, outcome.Error.Code);
        Assert.Equal(1, outcome.Diagnostics.FileCalls);
    }

    [Fact]
    public void A_refused_path_ends_the_invocation_permanent_though_the_plugin_tries_to_catch_it()
    {
        var outcome = Run($$"""
            export function invoke() {
              try { {{Call(Path.Combine(_package.Outside, "a.log"), "here")}}; } catch (e) { return { result: "caught" }; }
              return { result: null };
            }
            """);

        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Equal(FailureClass.Permanent, outcome.Error!.Class);
        Assert.Equal(HostOutcomeCodes.FilePathNotAllowed, outcome.Error.Code);
    }

    [Fact]
    public void An_outcome_counts_the_lines_appended()
    {
        var outcome = Run($$"""
            export function invoke() {
              {{Call(File("a.log"), "1")}};
              {{Call(File("a.log"), "2")}};
              return { result: null };
            }
            """);

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(2, outcome.Diagnostics.FileCalls);
    }

    /// <summary>The line may be on the disk when the time runs out, so a repeat is not known to be safe.</summary>
    [Fact]
    public void A_plugin_that_runs_out_of_time_after_an_append_may_have_acted()
    {
        var outcome = Run(
            $$"""
            export function invoke() {
              {{Call(File("a.log"), "here")}};
              for (;;) { }
            }
            """,
            timeoutMs: 1_000,
            deadlineMs: 1_000);

        Assert.Equal(FailureClass.Ambiguous, outcome.Error!.Class);
        Assert.Equal(HostOutcomeCodes.PluginTimeout, outcome.Error.Code);
    }

    /// <summary>A real run. The two clocks part only for a row where the wait, which reads the first, has to end before the second.</summary>
    private PluginOutcome Run(string mainJs, int timeoutMs = 10_000, int deadlineMs = 10_000)
    {
        using var package = new TempPackage(mainJs);
        var invocation = InvocationFactory.Create(package.Root, "echo.run", configure: builder =>
        {
            builder.Grants = new InvocationGrants(null, null, null, new FilesGrants([new FileDirectoryGrant(_directory, _directory)]));
            builder.Limits = InvocationFactory.DefaultLimits with { TimeoutMs = timeoutMs, MaxStatements = 2_000_000_000 };
        });
        using var stderr = new StringWriter();
        var diagnostics = SdkHarness.DiagnosticsFor(stderr, invocation);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(deadlineMs));

        return new InvocationRunner().Run(invocation, diagnostics, new UnfinishedKills(), deadline.Token);
    }

    /// <summary>A program that keeps the file to itself, as a second host does for the length of one line.</summary>
    private static FileStream Hold(string file) => new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>A link to a folder: a junction on Windows, which an account without the link privilege may still make.</summary>
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        catch (Win32Exception refused)
        {
            Assert.Skip($"this machine would not make a junction: {refused.Message}");
        }
    }

    /// <summary>A link to a file, which on Windows only an account with the link privilege may make.</summary>
    private static void LinkToAFile(string link, string target)
    {
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
        }
        catch (Exception refused) when (OperatingSystem.IsWindows() && refused is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine would not make a link to a file: {refused.Message}");
        }
    }

    private static void Unlink(string link) => Directory.Delete(link);
}
