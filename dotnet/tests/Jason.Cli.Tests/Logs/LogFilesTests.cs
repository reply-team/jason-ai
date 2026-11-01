using Jason.Cli.Logs;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Logs;

/// <summary>
/// Which files in the log directory are the runtime's own, and the order they were written in. Anything else
/// there — a diagnostic bundle, a backup somebody made, a file another program left — is never read as a log.
/// </summary>
public sealed class LogFilesTests
{
    [Fact]
    public void The_runtime_s_files_are_listed_by_the_date_in_the_name_and_then_by_the_roll_sequence()
    {
        using var tree = new TempTree();
        // The file a day starts in has no suffix; a size roll adds _001, _002 and so on, and after _999 the number
        // simply grows a digit, so ordering by the text of the name would put _1000 before _999.
        foreach (var name in new[]
                 {
                     "runtime-20261003.jsonl", "runtime-20261002_1000.jsonl", "runtime-20261002_002.jsonl", "runtime-20261002.jsonl",
                     "runtime-20261002_999.jsonl", "runtime-20261002_001.jsonl", "runtime-20260930.jsonl",
                 })
        {
            LogFixtures.Write(tree.Root, name, "{}");
        }

        var files = LogFiles.In(tree.Root);

        Assert.Equal(
            ["runtime-20260930.jsonl", "runtime-20261002.jsonl", "runtime-20261002_001.jsonl", "runtime-20261002_002.jsonl", "runtime-20261002_999.jsonl", "runtime-20261002_1000.jsonl", "runtime-20261003.jsonl"],
            files.Select(file => file.Name));
        Assert.Equal([0, 0, 1, 2, 999, 1000, 0], files.Select(file => file.Sequence));
        Assert.Equal(new DateOnly(2026, 10, 2), files[3].Day);
        Assert.Equal(new DateOnly(2026, 9, 30), files[0].Day);
    }

    [Fact]
    public void A_file_is_described_by_where_it_is_how_big_it_is_and_when_it_was_last_written()
    {
        using var tree = new TempTree();
        var path = LogFixtures.Write(tree.Root, "runtime-20261002.jsonl", "{\"a\":1}", "{\"a\":2}");
        var written = new DateTime(2026, 10, 2, 8, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var file = Assert.Single(LogFiles.In(tree.Root));

        Assert.Equal(path, file.Path);
        Assert.Equal(16, file.Bytes);
        Assert.Equal(new DateTimeOffset(written), file.ModifiedAt);
    }

    [Fact]
    public void Nothing_else_in_the_directory_is_a_log()
    {
        using var tree = new TempTree();
        LogFixtures.Write(tree.Root, "runtime-20261002.jsonl", "{}");

        // Names that look like the runtime's without being them, and names that are not even close.
        foreach (var name in new[]
                 {
                     "runtime-x.jsonl", "runtime-.jsonl", "runtime-2026100.jsonl", "runtime-202610021.jsonl", "runtime-20261302.jsonl",
                     "runtime-20261002_.jsonl", "runtime-20261002_a.jsonl", "runtime-20261002_-1.jsonl", "runtime-20261002_99999999999.jsonl",
                     "runtime-20261002.jsonl.bak", "runtime-20261002.json", "runtime-20261002.log", "other-20261002.jsonl",
                     "xruntime-20261002.jsonl", "runtime-２０２６１００２.jsonl", "notes.txt",
                 })
        {
            LogFixtures.Write(tree.Root, name, "{}");
        }

        // A directory with a log's name is not a log, and neither is whatever a diagnostic bundle keeps beside the logs.
        Directory.CreateDirectory(Path.Combine(tree.Root, "runtime-20261005.jsonl"));
        var doctor = Directory.CreateDirectory(Path.Combine(tree.Root, "doctor")).FullName;
        LogFixtures.Write(doctor, "runtime-20261001.jsonl", "{}");

        Assert.Equal(["runtime-20261002.jsonl"], LogFiles.In(tree.Root).Select(file => file.Name));
    }

    /// <summary>
    /// The sink numbers a roll with at least three digits — <c>_001</c>, and <c>_1000</c> once it passes 999 — so a
    /// shorter number is a name the runtime never wrote.
    /// </summary>
    [Theory]
    [InlineData("runtime-20261002_1.jsonl")]
    [InlineData("runtime-20261002_01.jsonl")]
    public void A_roll_sequence_of_fewer_than_the_three_digits_the_runtime_writes_is_not_a_log(string name)
    {
        using var tree = new TempTree();
        LogFixtures.Write(tree.Root, "runtime-20261002.jsonl", "{}");
        LogFixtures.Write(tree.Root, "runtime-20261002_001.jsonl", "{}");
        LogFixtures.Write(tree.Root, name, "{}");

        Assert.Equal(["runtime-20261002.jsonl", "runtime-20261002_001.jsonl"], LogFiles.In(tree.Root).Select(file => file.Name));
    }

    [Fact]
    public void A_directory_that_does_not_exist_has_no_logs()
    {
        using var tree = new TempTree();

        Assert.Empty(LogFiles.In(Path.Combine(tree.Root, "never-created")));
    }
}
