using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;
using static Jason.Cli.Tests.Logs.LogFixtures;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// <c>jason doctor</c>: where a bundle is written, that it is listed before it is written and never written over, what is
/// printed after, and that a runtime which does not answer is something a bundle reports rather than a reason it fails.
/// The runtime exists only in a handler and the names are made up, as in the composer's own tests; what is held here is
/// the verb around the composer.
/// </summary>
public sealed class DoctorCommandTests
{
    private const string Token = "SENTINEL-TOKEN-5b1e9c";
    private const string BaseUrl = "http://127.0.0.1:47811";
    private const string InstanceId = "rt_01JDOCTOR";

    /// <summary>
    /// The exact words a bundle that carries the journal's entries says, wherever it is shown: typed out here rather than
    /// read from the constant, so that a change to the sentence is a change somebody makes on purpose.
    /// </summary>
    private const string JournalWarning =
        "This bundle includes what the journal entries say: campaign names and fields as they changed, project names as they changed, the folders a project link recorded, campaign and work-item contexts as they changed, result formats, the addresses and numbers put on or taken off the do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered with, the errors attempts ended with, provider identifiers, the verdicts roles recorded on replies and the decisions whose answers they carry, and the standing approvals people set: their categories, reactions, goals and limits, the effect, tool and operation a report names, the failure text and receipts notification channels reported, entries people and agents wrote themselves, the names actors gave, and the reasons they typed. Read it before you share it.";

    /// <summary>
    /// What a written bundle says on stderr about where a problem goes, typed out for the same reason: the form, with the
    /// file attached, and the support address for a file made with the journal's entries.
    /// </summary>
    private const string ToAnIssue = "Report a problem as an issue, with this file attached: ";

    private const string MadeToBeShared =
        "The file is made to be shared; read it first. A file made with --include-journal goes only to the support address under Getting help, https://github.com/reply-team/jason-ai#getting-help, never to an issue.";

    private const string JournalToSupport =
        "This file was made with --include-journal, so it goes only to the support address under Getting help, https://github.com/reply-team/jason-ai#getting-help, never to an issue.";

    private const string JournalThenAnIssue =
        "A problem is reported as an issue with a file made without --include-journal, which is made to be shared; read it first: ";

    /// <summary>Two in the afternoon two hours east of Greenwich, which is noon in UTC: the name is written in UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 14, 0, 0, TimeSpan.FromHours(2));

    /// <summary>The name a bundle composed at <see cref="Now"/> is given, without its extension.</summary>
    private const string DefaultName = "jason-doctor-20261004T120000Z";

    /// <summary>The installation's names, made up, so that nothing here learns the names of the machine running the suite.</summary>
    private static readonly IdentityFacts MadeUp = new(@"C:\Users\Quillon", "Quillon", "WORKSTATION-7", "CORP", "S-1-5-21-1111111111-2222222222-3333333333-1001");

    /// <summary>What a run prints after it wrote a bundle, in its order.</summary>
    private static readonly string[] WrittenKeys = ["path", "bytes", "sha256", "problem_report_url", "contents", "values_carried", "never_carried", "projected", "warnings"];

    /// <summary>What a dry run prints, in its order.</summary>
    private static readonly string[] ListedKeys = ["would_write", "contents", "values_carried", "never_carried", "projected", "warnings"];

    /// <summary>The lists a dry run and a plain run print alike.</summary>
    private static readonly string[] Listing = ["contents", "values_carried", "never_carried", "projected", "warnings"];

    private static readonly DoctorOptions Plain = new(DryRun: false, IncludeJournal: false, Output: null, Human: false);

    private static readonly DoctorOptions DryRun = Plain with { DryRun = true };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- where it is written -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_bundle_is_written_under_the_data_directory_named_by_its_second_in_utc_exactly_as_composed()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var composed = await DoctorComposer.ComposeAsync(Machine(dir, runtime, new StringWriter(), new StringWriter()), includeJournal: false, new FixedClock(Now.ToUniversalTime()), Ct);

        var (exit, output, error) = await RunAsync(dir, runtime, Plain);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([ToAnIssue + IssueUrl(), MadeToBeShared], Lines(error));

        var path = Path.Combine(dir.Paths.LogsDirectory, "doctor", DefaultName + ".json");
        Assert.True(File.Exists(path), $"Nothing at {path}; the data directory holds: {string.Join(", ", Tree(dir.Paths.Root))}");

        // The file is the composer's text, byte for byte: compact, one line, plain ASCII.
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(composed.Serialize(), Encoding.UTF8.GetString(bytes));
        Assert.All(bytes, b => Assert.InRange(b, (byte)0x20, (byte)0x7E));

        // And what is printed says what was written: where, how many bytes and their digest, and the bundle's own listing.
        var printed = Json(output);
        Assert.Equal(WrittenKeys, Keys(printed));
        Assert.Equal(path, Text(printed["path"]));
        Assert.Equal(bytes.LongLength, printed["bytes"]!.GetValue<long>());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), Text(printed["sha256"]));
        var header = JsonNode.Parse(bytes)!["bundle"]!;
        foreach (var item in Listing)
        {
            Assert.True(JsonNode.DeepEquals(header[item], printed[item]), $"{item}: the file says {header[item]?.ToJsonString()}, the run printed {printed[item]?.ToJsonString()}");
        }
    }

    /// <summary>
    /// Two runs in one second, after a file somebody else put at the first name: each run takes the next free name, and no
    /// file that was there is written over — not theirs, and not the first run's.
    /// </summary>
    [Fact]
    public async Task A_name_that_is_taken_is_never_written_over_and_the_next_run_takes_the_next_number()
    {
        using var dir = new TempPaths();
        var doctor = Path.Combine(dir.Paths.LogsDirectory, "doctor");
        Directory.CreateDirectory(doctor);
        var taken = Path.Combine(doctor, DefaultName + ".json");
        File.WriteAllText(taken, "somebody's own file");

        var listed = await RunAsync(dir, null, DryRun);
        var first = await RunAsync(dir, null, Plain);
        var second = await RunAsync(dir, null, Plain);

        Assert.Equal(Path.Combine(doctor, DefaultName + "-1.json"), Text(Json(listed.Output)["would_write"]));
        Assert.Equal(Path.Combine(doctor, DefaultName + "-1.json"), Text(Json(first.Output)["path"]));
        Assert.Equal(Path.Combine(doctor, DefaultName + "-2.json"), Text(Json(second.Output)["path"]));
        Assert.Equal("somebody's own file", File.ReadAllText(taken));
        Assert.Equal(Text(Json(first.Output)["sha256"]), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(doctor, DefaultName + "-1.json")))));
    }

    [Theory]
    // Critical: never writes over a file.
    [InlineData(false, false, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S4)])]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task An_output_that_is_already_there_is_refused_before_anything_is_read_and_left_as_it_was(bool dryRun, bool aDirectory)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var existing = Path.Combine(dir.Paths.Root, "bundle.json");
        if (aDirectory)
        {
            Directory.CreateDirectory(existing);
        }
        else
        {
            File.WriteAllText(existing, "mine");
        }

        var (exit, output, error) = await RunAsync(dir, runtime, Plain with { DryRun = dryRun, Output = existing });

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = JsonSerializer.Deserialize<ErrorResponse>(output, JasonJson.Options)!;
        Assert.Equal("doctor_output_exists", envelope.Error.Code);
        Assert.False(envelope.Error.Retryable);
        Assert.Equal(string.Empty, error);
        Assert.Empty(runtime.Requests);
        Assert.False(Directory.Exists(Path.Combine(dir.Paths.LogsDirectory, "doctor")));
        if (!aDirectory)
        {
            Assert.Equal("mine", File.ReadAllText(existing));
        }
    }

    /// <summary>
    /// A path that cannot be written is refused, as --output names it or as the default place is: a file stands where its
    /// directory belongs. A dry run says the same as the run it stands for.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task A_path_that_cannot_be_written_is_refused_and_nothing_is_written(bool dryRun, bool named)
    {
        using var dir = new TempPaths();
        string? output = null;
        if (named)
        {
            var blocker = Path.Combine(dir.Paths.Root, "a-file");
            File.WriteAllText(blocker, "not a directory");
            output = Path.Combine(blocker, "bundle.json");
        }
        else
        {
            File.WriteAllText(dir.Paths.LogsDirectory, "not a directory");
        }

        var before = Tree(dir.Paths.Root);

        var (exit, printed, _) = await RunAsync(dir, null, Plain with { DryRun = dryRun, Output = output });

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("doctor_output_unwritable", JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error.Code);
        Assert.Equal(before, Tree(dir.Paths.Root));
    }

    /// <summary>
    /// An --output that ends in a separator names a directory, not a file — a person who types <c>--output reports/</c>
    /// means "put it in reports" — and it is refused as a path that cannot be written, by the dry run as by the plain run,
    /// before anything is read or made. It is not "already there": nothing was.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task An_output_that_names_no_file_is_refused_in_both_modes_and_nothing_is_made(bool dryRun, bool alternateSeparator)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        var output = Path.Combine(dir.Paths.Root, "newdir") + (alternateSeparator ? Path.AltDirectorySeparatorChar : Path.DirectorySeparatorChar);
        var before = Tree(dir.Paths.Root);

        var (exit, printed, _) = await RunAsync(dir, runtime, Plain with { DryRun = dryRun, Output = output });

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("doctor_output_unwritable", JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error.Code);
        Assert.Equal(before, Tree(dir.Paths.Root));
        Assert.Empty(runtime.Requests);
    }

    /// <summary>
    /// A name no file can have is refused before anything is made, by the dry run as by the plain run: a character this
    /// system does not allow in a name, under directories that are not there yet, leaves the tree as it was.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_name_no_file_can_have_is_refused_before_anything_is_made(bool dryRun)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);

        // A character the system refuses in a name and that is not a separator: '<' on Windows. Elsewhere a name may hold
        // anything but a separator and the null character, so it is the null character there.
        var refused = Path.GetInvalidFileNameChars().Where(c => c != '\0' && c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar).DefaultIfEmpty('\0').First();
        var output = Path.Combine(dir.Paths.Root, "x", "y", $"bad{refused}name.json");
        var before = Tree(dir.Paths.Root);

        var (exit, printed, _) = await RunAsync(dir, runtime, Plain with { DryRun = dryRun, Output = output });

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("doctor_output_unwritable", JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error.Code);
        Assert.Equal(before, Tree(dir.Paths.Root));

        // Before anything is made, and before anything is read: a plain run that went on to compose would ask the runtime,
        // and its refusal to create the file would leave the same tree behind.
        Assert.Empty(runtime.Requests);
    }

    /// <summary>
    /// A device's name — <c>NUL</c>, <c>CON</c> — is no file, and is refused as a device rather than as a directory, which
    /// it is not either.
    /// </summary>
    [Theory]
    [InlineData("NUL", false)]
    [InlineData("NUL", true)]
    [InlineData("CON", false)]
    public async Task A_device_name_is_refused_as_a_device_in_both_modes(string device, bool dryRun)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows answers for a device's name in every directory.");
        using var dir = new TempPaths();

        var (exit, printed, _) = await RunAsync(dir, null, Plain with { DryRun = dryRun, Output = device });

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error;
        Assert.Equal("doctor_output_unwritable", error.Code);
        Assert.Contains($"'{device}' names a device, not a file", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("directory", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run beside this one that refuses takes away the empty directories it made, and in the moment between this run's
    /// making its directory and creating its file there, that can be this run's directory. It is made again and the bundle
    /// is written; a run that says it wrote has a file where it says.
    /// </summary>
    [Fact]
    public async Task A_directory_another_run_takes_away_before_the_file_is_created_is_made_again_and_written_into()
    {
        using var dir = new TempPaths();
        var output = Path.Combine(dir.Paths.Root, "x", "y", "bundle.json");
        var calls = 0;
        DoctorCommand.BeforeCreate.Value = path =>
        {
            if (calls++ == 0)
            {
                // As the other run's cleanup would: the deepest first, each while it is empty.
                Directory.Delete(Path.GetDirectoryName(path)!);
                Directory.Delete(Path.Combine(dir.Paths.Root, "x"));
            }
        };

        try
        {
            var (exit, printed, _) = await RunAsync(dir, null, Plain with { Output = output });

            Assert.True(exit == ExitCodes.Success, printed);
            Assert.Equal(output, Text(Json(printed)["path"]));
            Assert.True(File.Exists(output));
            Assert.Equal(2, calls);
        }
        finally
        {
            DoctorCommand.BeforeCreate.Value = null;
        }
    }

    /// <summary>
    /// The directories a refused run takes away are only the empty ones, and that is all that keeps it from taking a bundle
    /// another run wrote into the same new directory in the meantime: that bundle, and its directory, are left as they are.
    /// </summary>
    [Fact]
    public async Task A_refused_run_leaves_a_directory_another_run_has_written_into_as_it_is()
    {
        using var dir = new TempPaths();
        var output = Path.Combine(dir.Paths.Root, "x", "y", new string('a', 300) + ".json");
        var other = Path.Combine(dir.Paths.Root, "x", "y", "another-runs-bundle.json");
        DoctorCommand.BeforeCreate.Value = _ => File.WriteAllText(other, "another run's bundle");

        try
        {
            var (exit, printed, _) = await RunAsync(dir, null, Plain with { Output = output });

            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.Equal("doctor_output_unwritable", JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error.Code);
            Assert.Equal("another run's bundle", File.ReadAllText(other));
        }
        finally
        {
            DoctorCommand.BeforeCreate.Value = null;
        }
    }

    /// <summary>
    /// When every name a second can have is taken — the default and the thousand numbered after it — a run refuses rather
    /// than write over one, the dry run as the plain run, and leaves every file and directory as it was.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_every_name_of_the_second_taken_both_modes_refuse_and_nothing_is_written_over(bool dryRun)
    {
        using var dir = new TempPaths();
        var doctor = Path.Combine(dir.Paths.LogsDirectory, "doctor");
        Directory.CreateDirectory(doctor);
        File.WriteAllText(Path.Combine(doctor, DefaultName + ".json"), "taken");
        for (var number = 1; number <= DoctorCommand.MaxNumber; number++)
        {
            File.WriteAllText(Path.Combine(doctor, $"{DefaultName}-{number}.json"), "taken");
        }

        var before = Tree(dir.Paths.Root);

        var (exit, printed, _) = await RunAsync(dir, null, Plain with { DryRun = dryRun });

        Assert.Equal(ExitCodes.ApiError, exit);
        var error = JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error;
        Assert.Equal("doctor_output_unwritable", error.Code);
        Assert.Contains($"{DefaultName}-{DoctorCommand.MaxNumber}.json is taken", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Tree(dir.Paths.Root));
    }

    /// <summary>
    /// A file the system refuses to make for a reason other than its being there — a name longer than any directory
    /// entry may be — is refused as a path that cannot be written, not as one already taken, and the directories made for
    /// it are taken away again: the run that says nothing was written leaves the tree as it was. Only writing can find this
    /// out, so the dry run lists the path and the plain run refuses it, as the help says.
    /// </summary>
    [Fact]
    public async Task A_file_the_system_will_not_make_is_refused_and_the_directories_made_for_it_are_taken_away()
    {
        using var dir = new TempPaths();
        var output = Path.Combine(dir.Paths.Root, "x", "y", new string('a', 300) + ".json");
        var before = Tree(dir.Paths.Root);

        var listed = await RunAsync(dir, null, DryRun with { Output = output });
        var (exit, printed, _) = await RunAsync(dir, null, Plain with { Output = output });

        Assert.Equal(ExitCodes.Success, listed.Exit);
        Assert.Equal(output, Text(Json(listed.Output)["would_write"]));
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("doctor_output_unwritable", JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error.Code);
        Assert.Equal(before, Tree(dir.Paths.Root));
    }

    /// <summary>And a path on a volume this machine does not have is refused by both, before anything is read.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_path_on_a_volume_that_is_not_there_is_refused_in_both_modes(bool dryRun)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows names a path by a volume that may not be there.");
        var taken = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = Enumerable.Range('D', 'Z' - 'D' + 1).Select(letter => (char)letter).Reverse().Where(letter => !taken.Contains(letter)).ToList();
        Assert.SkipWhen(free.Count == 0, "Every drive letter is in use on this machine.");
        using var dir = new TempPaths();
        var runtime = Running(dir);

        var (exit, printed, _) = await RunAsync(dir, runtime, Plain with { DryRun = dryRun, Output = $@"{free[0]}:\bundle.json" });

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("doctor_output_unwritable", JsonSerializer.Deserialize<ErrorResponse>(printed, JasonJson.Options)!.Error.Code);
        Assert.Empty(runtime.Requests);
    }

    /// <summary>With --human a refusal is a sentence on stderr for the person, and stdout carries nothing.</summary>
    [Fact]
    public async Task With_human_a_refusal_is_a_sentence_on_stderr_and_nothing_on_stdout()
    {
        using var dir = new TempPaths();
        var existing = Path.Combine(dir.Paths.Root, "bundle.json");
        File.WriteAllText(existing, "mine");

        var (exit, output, error) = await RunAsync(dir, null, Plain with { Output = existing, Human = true });

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(string.Empty, output);
        Assert.Contains(existing, error, StringComparison.Ordinal);
        Assert.Contains("never written over", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The clock is read once: a clock that moves a second each time it is read, a tenth of a second before a minute
    /// turns, still gives a name and a stamp of the same second.
    /// </summary>
    [Fact]
    public async Task The_name_and_the_stamp_are_one_second_however_the_clock_moves_between_reads()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await DoctorCommand.RunAsync(Machine(dir, null, output, new StringWriter()), Plain, new Ticking(new DateTimeOffset(2026, 10, 4, 11, 59, 59, 900, TimeSpan.Zero)), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var path = Text(Json(output.ToString())["path"]);
        Assert.Equal(Path.Combine(dir.Paths.LogsDirectory, "doctor", "jason-doctor-20261004T115959Z.json"), path);
        Assert.StartsWith("2026-10-04T11:59:59", Text(JsonNode.Parse(File.ReadAllBytes(path))!["bundle"]!["created_at"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_output_is_written_exactly_there_and_its_directory_is_created()
    {
        using var dir = new TempPaths();
        var output = Path.Combine(dir.Paths.Root, "elsewhere", "deeper", "for-support.json");

        var (exit, printed, _) = await RunAsync(dir, null, Plain with { Output = output });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(output, Text(Json(printed)["path"]));
        Assert.True(File.Exists(output));
        Assert.False(Directory.Exists(Path.Combine(dir.Paths.LogsDirectory, "doctor")));
    }

    // ---- listed before it is written -----------------------------------------------------------------------------------

    /// <summary>
    /// A dry run composes the whole bundle and writes nothing — not the file, not its directory — and what it lists is what
    /// the plain run after it writes, item for item, and what the written file's own header says.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "--dry-run lists and writes nothing")]
    public async Task A_dry_run_writes_nothing_and_lists_what_a_plain_run_writes_item_for_item()
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);
        Directory.CreateDirectory(dir.Paths.LogsDirectory);
        Write(dir.Paths.LogsDirectory, "runtime-20261004.jsonl", Event("2026-10-04T11:59:00.0000000Z", "Сбой {Code}", "Error", ("Code", "provider_down")));
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{ "Dispatcher": { "Enabled": true }, "Plugins": { "Exec": { "Command": "SENTINEL" } } }""");
        var before = Tree(dir.Paths.Root);

        var listed = await RunAsync(dir, runtime, DryRun with { IncludeJournal = true });

        Assert.Equal(ExitCodes.Success, listed.Exit);
        Assert.Equal(before, Tree(dir.Paths.Root));
        var listing = Json(listed.Output);
        Assert.Equal(ListedKeys, Keys(listing));
        Assert.Equal(Path.Combine(dir.Paths.LogsDirectory, "doctor", DefaultName + ".json"), Text(listing["would_write"]));

        var written = await RunAsync(dir, runtime, Plain with { IncludeJournal = true });

        var result = Json(written.Output);
        Assert.Equal(Text(listing["would_write"]), Text(result["path"]));
        var header = JsonNode.Parse(File.ReadAllBytes(Text(result["path"])))!["bundle"]!;
        foreach (var item in Listing)
        {
            Assert.True(JsonNode.DeepEquals(listing[item], result[item]), $"{item}: listed {listing[item]?.ToJsonString()}, written {result[item]?.ToJsonString()}");
            Assert.True(JsonNode.DeepEquals(header[item], result[item]), $"{item}: the file says {header[item]?.ToJsonString()}, the run printed {result[item]?.ToJsonString()}");
        }

        // Each list is the bundle's: the sections in order, the one setting the list carries, the fixed lists.
        Assert.Equal(
            ["versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items", "journal_entries"],
            listing["contents"]!.AsArray().Select(section => Text(section!["name"])));
        Assert.Equal(["Dispatcher:Enabled"], Strings(listing["values_carried"]));
        Assert.Equal(DoctorComposer.NeverCarried, Strings(listing["never_carried"]));
        Assert.Equal(
            DoctorComposer.Projections.Select(projection => projection.Field),
            listing["projected"]!.AsArray().Select(projection => Text(projection!["field"])));
        Assert.Equal(
            DoctorComposer.Projections.SelectMany(projection => projection.Withheld),
            listing["projected"]!.AsArray().SelectMany(projection => Strings(projection!["withheld"])));
        Assert.DoesNotContain("SENTINEL", listed.Output, StringComparison.Ordinal);
    }

    // ---- the journal's warning ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task With_the_journal_asked_for_its_warning_is_printed_once_on_stderr_and_in_the_result(bool dryRun, bool includeJournal)
    {
        using var dir = new TempPaths();

        var (exit, output, error) = await RunAsync(dir, Running(dir), Plain with { DryRun = dryRun, IncludeJournal = includeJournal });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            (dryRun, includeJournal) switch
            {
                (true, false) => string.Empty,
                (true, true) => string.Join(Environment.NewLine, JournalWarning, JournalToSupport, string.Empty),
                (false, false) => string.Join(Environment.NewLine, ToAnIssue + IssueUrl(), MadeToBeShared, string.Empty),
                (false, true) => string.Join(Environment.NewLine, JournalWarning, JournalToSupport, JournalThenAnIssue + IssueUrl(), string.Empty),
            },
            error);
        IReadOnlyList<string> warnings = includeJournal ? [JournalWarning, JournalToSupport] : [];
        Assert.Equal(warnings, Strings(Json(output)["warnings"]));
        if (!dryRun)
        {
            Assert.Equal(warnings, Strings(JsonNode.Parse(File.ReadAllBytes(Text(Json(output)["path"])))!["bundle"]!["warnings"]));
        }
    }

    // ---- where a problem goes ----------------------------------------------------------------------------------------

    /// <summary>
    /// A written bundle ends with where a problem is reported: the repository's issue form, with the two fields this
    /// command knows, the version and the platform, filled in through the form's own field ids and encoded as a query
    /// is, so a version's plus sign stays one and a space never splits the address. It goes on stderr, where the
    /// journal's warning goes, and into the result on stdout as problem_report_url, since an agent reads the body and not
    /// the diagnostics; --human prints it. Nothing is sent, the person opens it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_written_bundle_points_to_the_issue_form_with_the_version_and_the_platform_filled_in(bool human)
    {
        using var dir = new TempPaths();
        var runtime = Running(dir);

        var (exit, output, error) = await RunAsync(dir, runtime, Plain with { Human = human });

        Assert.Equal(ExitCodes.Success, exit);
        var lines = Lines(error);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(ToAnIssue, lines[0], StringComparison.Ordinal);
        Assert.Equal(MadeToBeShared, lines[1]);

        var address = new Uri(lines[0][ToAnIssue.Length..]);
        Assert.Equal("https://github.com/reply-team/jason-ai/issues/new", address.GetLeftPart(UriPartial.Path));
        Assert.DoesNotContain(' ', address.OriginalString);
        Assert.DoesNotContain('+', address.Query);
        var query = System.Web.HttpUtility.ParseQueryString(address.Query);
        Assert.Equal(["template", "version", "platform"], query.AllKeys);
        Assert.Equal("problem.yml", query["template"]);
        Assert.Equal(Jason.Contracts.JasonVersion.Current, query["version"]);
        Assert.Equal(Platform(), query["platform"]);

        if (human)
        {
            Assert.Contains("Problem form: " + IssueUrl(), Lines(output));
        }
        else
        {
            Assert.Equal(IssueUrl(), Text(Json(output)["problem_report_url"]));
        }
    }

    /// <summary>
    /// A bundle made with the journal's entries carries what people typed, and an issue is public the moment it is
    /// submitted: so after the journal's warning the first thing said is that this file goes to the support address only,
    /// and the form follows on stderr for a file made without the option. An agent reads the body, not stderr, so the
    /// body never leads it to the form with this file: problem_report_url is null, and that the file goes to support only
    /// is among the result's warnings and the bundle's own. --human prints no form beside it either.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_bundle_with_the_journal_says_first_that_it_goes_to_support_only_and_its_result_names_no_form(bool human)
    {
        using var dir = new TempPaths();

        var (exit, output, error) = await RunAsync(dir, Running(dir), Plain with { IncludeJournal = true, Human = human });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([JournalWarning, JournalToSupport, JournalThenAnIssue + IssueUrl()], Lines(error));
        if (human)
        {
            Assert.DoesNotContain(Lines(output), line => line.Contains("Problem form", StringComparison.Ordinal));
            Assert.DoesNotContain(IssueUrl(), output, StringComparison.Ordinal);
            return;
        }

        var result = Json(output);
        Assert.True(result.AsObject().ContainsKey("problem_report_url"));
        Assert.Null(result["problem_report_url"]);
        Assert.Equal([JournalWarning, JournalToSupport], Strings(result["warnings"]));
        Assert.Equal([JournalWarning, JournalToSupport], Strings(JsonNode.Parse(File.ReadAllBytes(Text(result["path"])))!["bundle"]!["warnings"]));
    }

    /// <summary>A dry run writes no file, so it has none to report and says nothing about where one goes.</summary>
    [Fact]
    public async Task A_dry_run_says_nothing_about_where_a_problem_goes()
    {
        using var dir = new TempPaths();

        var (exit, output, error) = await RunAsync(dir, Running(dir), DryRun with { Human = true });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, error);
        Assert.DoesNotContain("issues/new", output, StringComparison.Ordinal);
    }

    // ---- for people --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task With_human_a_dry_run_is_a_table_of_the_sections_and_then_where_it_would_be_written()
    {
        using var dir = new TempPaths();
        var path = Path.Combine(dir.Paths.LogsDirectory, "doctor", DefaultName + ".json");

        var (exit, output, _) = await RunAsync(dir, Running(dir), DryRun with { Human = true, IncludeJournal = true });

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(path));
        var lines = Lines(output);
        Assert.StartsWith("SECTION", lines[0], StringComparison.Ordinal);
        foreach (var section in (string[])["versions", "status", "runtime", "routes", "config", "logs", "journal", "work_items", "journal_entries"])
        {
            Assert.NotNull(Row(lines, section));
        }

        Assert.Contains(lines, line => line.Contains(DoctorComposer.NeverCarried[0], StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(DoctorComposer.SystemInfoWithheld[0].Described, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("<data>", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(JournalWarning, StringComparison.Ordinal));
        Assert.Equal("Would write " + path, lines[^1]);
    }

    [Fact]
    public async Task With_human_a_written_bundle_says_where_it_was_written()
    {
        using var dir = new TempPaths();
        var path = Path.Combine(dir.Paths.LogsDirectory, "doctor", DefaultName + ".json");

        var (exit, output, _) = await RunAsync(dir, Running(dir), Plain with { Human = true });

        Assert.Equal(ExitCodes.Success, exit);
        var bytes = File.ReadAllBytes(path);
        Assert.StartsWith(
            $"Written to {path} ({bytes.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(bytes))})",
            Lines(output)[^2],
            StringComparison.Ordinal);
        Assert.Equal("Problem form: " + IssueUrl(), Lines(output)[^1]);
    }

    /// <summary>
    /// A section that is not whole says why, in the words the bundle uses: a runtime that did not answer and why, a section
    /// it would have answered for, and one too large to write.
    /// </summary>
    [Fact]
    public async Task With_human_a_section_that_is_not_whole_says_why_in_the_bundles_own_words()
    {
        using var dir = new TempPaths();
        var variables = Enumerable.Range(0, 30_000).ToDictionary(n => $"JASON_Variable{n}", _ => "x");

        var (exit, output, _) = await RunAsync(dir, null, DryRun with { Human = true }, variables);

        Assert.Equal(ExitCodes.Success, exit);
        var lines = Lines(output);
        Assert.Contains(CliErrors.NoDescriptor, Row(lines, "runtime"), StringComparison.Ordinal);
        Assert.Contains(DoctorComposer.RuntimeDidNotAnswer, Row(lines, "journal"), StringComparison.Ordinal);
        Assert.Contains(DoctorComposer.RuntimeDidNotAnswer, Row(lines, "work_items"), StringComparison.Ordinal);
        Assert.Contains(DoctorComposer.TooLarge, Row(lines, "config"), StringComparison.Ordinal);
    }

    /// <summary>
    /// And a section that stopped partway says where and why, a list cut short says which, and the newest failures say
    /// where they were found — each read from the bundle by the composer's own key.
    /// </summary>
    [Fact]
    public async Task With_human_a_read_that_stopped_a_list_cut_short_and_the_failures_source_are_said()
    {
        using var dir = new TempPaths();
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        File.WriteAllLines(
            Path.Combine(logs, "runtime-20261004.jsonl"),
            Enumerable.Range(0, DoctorComposer.SliceEvents + 10).Select(n => $$"""{"@t":"2026-10-04T10:00:00.000Z","@mt":"line {{n}}"}"""));
        var runtime = Running(dir);
        runtime.Fails = (operation, _) => operation == Operations.JournalList
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"error":{"code":"internal_error","message":"m","retryable":true}}""", System.Text.Encoding.UTF8, "application/json"),
            }
            : null;

        var (exit, output, _) = await RunAsync(dir, runtime, DryRun with { Human = true });

        Assert.Equal(ExitCodes.Success, exit);
        var lines = Lines(output);
        Assert.Contains($"incomplete: internal_error", Row(lines, "journal"), StringComparison.Ordinal);
        Assert.Contains("cut short: ", Row(lines, "logs"), StringComparison.Ordinal);
        Assert.Contains("slice_truncated", Row(lines, "logs"), StringComparison.Ordinal);
        Assert.Contains($"recent failures from: {DoctorComposer.FromListOldestFirst}", Row(lines, "work_items"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing about the account a command runs as is read unless a bundle is composed: every other command, and doctor's
    /// own help, says the marks without making a rewriter or reading this machine's names. Read off the sources, since a
    /// name read leaves nothing behind to observe.
    /// </summary>
    [Fact]
    public void Only_composing_a_bundle_reads_the_accounts_names()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var sources = Directory.EnumerateFiles(Path.Combine(root.FullName, "dotnet", "src", "Jason.Cli"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(sources);

        foreach (var reader in (string[])["new IdentityRewriter(", "IdentityFacts.ForThisMachine()"])
        {
            Assert.Equal(
                ["DoctorComposer.cs"],
                sources.Where(file => File.ReadAllText(file).Contains(reader, StringComparison.Ordinal)).Select(Path.GetFileName));
        }
    }

    // ---- exit codes ----------------------------------------------------------------------------------------------------

    /// <summary>A bundle is most wanted when the runtime does not answer, so not answering is in the bundle and the exit is 0.</summary>
    [Theory]
    [Critical(Family.R2, "doctor never exits 3")]
    [InlineData("no_descriptor")]
    [InlineData("runtime_unreachable")]
    [InlineData("unauthorized")]
    public async Task It_never_exits_3_whatever_the_runtime_does(string runtimeIs)
    {
        using var dir = new TempPaths();
        HttpMessageHandler handler = runtimeIs == "unauthorized" ? new Answering(HttpStatusCode.Unauthorized) : new Refusing();
        if (runtimeIs != "no_descriptor")
        {
            dir.WriteDescriptor(Descriptor());
        }

        var output = new StringWriter();
        var exit = await CliApp.RunAsync(["doctor"], Machine(dir, handler, output, new StringWriter()), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var bundle = JsonNode.Parse(File.ReadAllBytes(Text(Json(output.ToString())["path"])))!;
        Assert.Equal(runtimeIs, Text(bundle["runtime"]!["reason"]));
    }

    [Theory]
    [InlineData("doctor", "--output")]
    [InlineData("doctor", "--output", "")]
    [InlineData("doctor", "send")]
    [InlineData("doctor", "--limit", "3")]
    public async Task A_usage_error_exits_2_and_writes_nothing(params string[] args)
    {
        using var dir = new TempPaths();
        var before = Tree(dir.Paths.Root);

        var exit = await CliApp.RunAsync(args, Machine(dir, null, new StringWriter(), new StringWriter()), Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(before, Tree(dir.Paths.Root));
    }

    // ---- the contract, published ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Help_publishes_what_is_carried_what_never_is_what_is_rewritten_where_it_goes_and_the_exit_codes()
    {
        var help = await HelpAsync();

        // How it is typed, and the order a person is told to type it in.
        foreach (var option in (string[])["--dry-run", "--include-journal", "--output <file>", "--human"])
        {
            Assert.Contains(option, help, StringComparison.Ordinal);
        }

        Assert.Contains("Run --dry-run first", help, StringComparison.Ordinal);
        Assert.Contains("never reaches the network", help, StringComparison.Ordinal);
        Assert.Contains("with no descriptor it makes none", Flat(help), StringComparison.Ordinal);

        // What a dry run cannot know, said where it is promised.
        Assert.Contains(
            $"a dry run cannot know whether the place may be written, so a plain run may still refuse with {CliErrors.DoctorOutputUnwritable}",
            Flat(help),
            StringComparison.Ordinal);
        Assert.Contains("a console that is not UTF-8 may show some characters differently; the JSON is the exact form", Flat(help), StringComparison.Ordinal);

        // The allowlist and the never-list, each line as the code holds it, the inbound read's three settings among them.
        Assert.All(SettingsAllowlist.Default.Published, line => Assert.Contains(line, help, StringComparison.Ordinal));
        Assert.Contains("Inbound:Enabled (true or false)", help, StringComparison.Ordinal);
        Assert.Contains("Inbound:SyncSeconds (a 32-bit integer)", help, StringComparison.Ordinal);
        Assert.Contains("Inbound:LookbackSeconds (a 32-bit integer)", help, StringComparison.Ordinal);
        Assert.All(DoctorComposer.NeverCarried, item => Assert.Contains(item, help, StringComparison.Ordinal));

        // What the root is in a bundle: the runtime's report of it, and nothing under it.
        Assert.Contains("the harness root's path and state among it, as the runtime reports them, and nothing under the root", Flat(help), StringComparison.Ordinal);

        // The rewrites: the names, the unknown key, the exception text, the projected report.
        foreach (var mark in (string[])["~", "<data>", "<runtime_data>", "<harness_root>", "<install_dir>", "<plugin-N>", "<role-N>", "<skill-pack-N>", "<skill-source>", "<user>", "<machine>", "<domain>", "<sid>", SettingKey.Hidden])
        {
            Assert.Contains(mark, help, StringComparison.Ordinal);
        }

        // Where each directory's mark applies: wherever it stands, the package managers' prefixes apart.
        Assert.Contains("a data directory other than ~/.jason as <data> wherever it stands", Flat(help), StringComparison.Ordinal);
        Assert.Contains("the harness root as <harness_root> wherever it stands", Flat(help), StringComparison.Ordinal);
        Assert.Contains("the directory the executable is in, outside the home directory, as <install_dir>", Flat(help), StringComparison.Ordinal);
        Assert.Contains("/opt/homebrew, /usr/local and /home/linuxbrew/.linuxbrew", Flat(help), StringComparison.Ordinal);
        Assert.Contains("install_dir_written_as", Flat(help), StringComparison.Ordinal);

        // A skill source's mark, wherever it stands, and the ref beside it.
        Assert.Contains("are written as <skill-source> wherever they stand", Flat(help), StringComparison.Ordinal);
        Assert.Contains("the ref only where a fact quotes a deployment", Flat(help), StringComparison.Ordinal);
        Assert.Contains("(a directory, read where it stands)", Flat(help), StringComparison.Ordinal);
        Assert.DoesNotContain("in the facts that quote a deployment, which the header lists", Flat(help), StringComparison.Ordinal);

        // A file with the journal names no form in its result.
        Assert.Contains("With --include-journal, problem_report_url is null", Flat(help), StringComparison.Ordinal);

        Assert.Contains($"{ExceptionText.MaxLineCharacters} characters a line", help, StringComparison.Ordinal);
        Assert.Contains($"{ExceptionText.MaxMessageCharacters} for its message", help, StringComparison.Ordinal);
        Assert.Contains($"{ExceptionText.MaxFrames} frames", help, StringComparison.Ordinal);
        foreach (var projection in DoctorComposer.Projections)
        {
            Assert.Contains(projection.Field, help, StringComparison.Ordinal);
            Assert.All(projection.Withheld, field => Assert.Contains(field, help, StringComparison.Ordinal));
        }

        // The journal's flag and its warning, word for word.
        Assert.Contains(JournalWarning, help, StringComparison.Ordinal);

        // Where, never over anything, and for how long: the words the code uses, read from it.
        Assert.Contains($"logs/{DoctorCommand.DirectoryName}", help, StringComparison.Ordinal);
        Assert.Contains($"{DoctorCommand.NamePrefix}YYYYMMDDTHHMMSSZ{DoctorCommand.Extension}", help, StringComparison.Ordinal);
        Assert.Contains("the time in UTC", help, StringComparison.Ordinal);
        Assert.Contains(
            $"-1, -2 and so on, up to -{DoctorCommand.MaxNumber}; past that it refuses with {CliErrors.DoctorOutputUnwritable}",
            Flat(help),
            StringComparison.Ordinal);
        Assert.Contains("nothing is ever overwritten", help, StringComparison.Ordinal);
        Assert.Contains("Bundles in the data directory are not rolled", Flat(help), StringComparison.Ordinal);
        Assert.Contains("they stay until 'jason uninstall --purge-data' or until a person deletes them", help, StringComparison.Ordinal);

        // The exit codes, the one it never uses among them.
        Assert.Contains(CliErrors.DoctorOutputExists, help, StringComparison.Ordinal);
        Assert.Contains(CliErrors.DoctorOutputUnwritable, help, StringComparison.Ordinal);
        Assert.Contains("Never 3", help, StringComparison.Ordinal);

        // The names Jason did not ship, and how their aliases are numbered.
        Assert.Contains("plugin_id_written_as, role_written_as, skill_pack_written_as and aliases_numbered", Flat(help), StringComparison.Ordinal);
        Assert.Contains(NameAliases.Numbering, Flat(help), StringComparison.Ordinal);

        // Where a problem goes, said where the contract is.
        Assert.Contains("https://github.com/reply-team/jason-ai/issues/new?template=problem.yml&version=<version>&platform=<platform>", help, StringComparison.Ordinal);
        Assert.Contains("A file made with --include-journal goes only to the support address under Getting help, https://github.com/reply-team/jason-ai#getting-help, never to an issue.", Flat(help), StringComparison.Ordinal);
        Assert.Contains("Opening the issue is the person's act: nothing is sent.", Flat(help), StringComparison.Ordinal);
    }

    /// <summary>The bounds the help states are the composer's, each in the bundle's own bytes, and the bundle's ceiling.</summary>
    [Fact]
    public async Task Help_states_every_bound_the_composer_holds()
    {
        var help = await HelpAsync();

        Assert.Contains($"at most {DoctorComposer.RecentErrors},", help, StringComparison.Ordinal);
        Assert.Contains($"last {Mebibytes(DoctorComposer.ErrorSearchBytes)} of the log", help, StringComparison.Ordinal);
        Assert.Contains($"at most {DoctorComposer.SliceEvents} events", help, StringComparison.Ordinal);
        Assert.Contains($"last {DoctorComposer.JournalWindowDays} days", help, StringComparison.Ordinal);
        Assert.Contains($"newest {DoctorComposer.RecentEntries} entries", help, StringComparison.Ordinal);
        Assert.Contains($"newest {DoctorComposer.RecentFailures} failed or expired", help, StringComparison.Ordinal);
        Assert.Contains($"{DoctorComposer.PageSize} to a page and never past {DoctorComposer.MaxPages} pages", help, StringComparison.Ordinal);
        Assert.Contains($"recent_errors at most {Mebibytes(DoctorComposer.RecentErrorsBytes)}", help, StringComparison.Ordinal);
        Assert.Contains($"slice at most {Mebibytes(DoctorComposer.SliceBytes)}", help, StringComparison.Ordinal);
        Assert.Contains($"journal_entries at most {Mebibytes(DoctorComposer.JournalEntriesBytes)}", help, StringComparison.Ordinal);
        Assert.Contains($"the rest of every section at most {Mebibytes(DoctorComposer.SectionBytes)}", Flat(help), StringComparison.Ordinal);
        Assert.Contains($"the header at most {DoctorComposer.HeaderBytes / 1024} KiB", Flat(help), StringComparison.Ordinal);
        Assert.Contains($"A bundle is at most {DoctorComposer.MaxBundleBytes} bytes", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// The words a bundle says when a part of it is not whole, each one in the help: the reasons and codes from the
    /// composer's constants, and every section, every flag that says a list was cut short, and every member of the header's
    /// listing read off bundles composed here, so a word added to the bundle and not to the help is a red test.
    /// </summary>
    [Fact]
    public async Task Help_knows_every_word_a_bundle_uses_to_say_it_is_not_whole()
    {
        var help = await HelpAsync();

        foreach (var word in (string[])
                 [
                     DoctorComposer.Format, DoctorComposer.RuntimeDidNotAnswer, DoctorComposer.UnreadableAnswer, DoctorComposer.TimedOut,
                     DoctorComposer.NotAttempted, DoctorComposer.TooLarge, DoctorComposer.FromJournal, DoctorComposer.FromListOldestFirst,
                     CliErrors.NoDescriptor, CliErrors.RuntimeUnreachable, CliErrors.Unauthorized, CliErrors.StaleDescriptor, CliErrors.LogsUnreadable,
                     "not_read", "incomplete", "omitted", "recent_failures_from",
                 ])
        {
            Assert.Contains(word, help, StringComparison.Ordinal);
        }

        using var dir = new TempPaths();
        var running = await DoctorComposer.ComposeAsync(Machine(dir, Running(dir), new StringWriter(), new StringWriter()), includeJournal: true, new FixedClock(Now), Ct);
        using var down = new TempPaths();
        var silent = await DoctorComposer.ComposeAsync(Machine(down, null, new StringWriter(), new StringWriter()), includeJournal: true, new FixedClock(Now), Ct);

        var lines = Lines(help);
        foreach (var section in running.Document.Select(member => member.Key))
        {
            Assert.True(
                lines.Any(line => line.TrimStart().StartsWith(section + " ", StringComparison.Ordinal)),
                $"The help names no section '{section}' at the start of a line.");
        }

        foreach (var bundle in new[] { running, silent })
        {
            foreach (var key in Members(bundle.Document).Where(key => key == "truncated" || key.EndsWith("_truncated", StringComparison.Ordinal) || key.EndsWith("_written_as", StringComparison.Ordinal)))
            {
                Assert.Contains(key, help, StringComparison.Ordinal);
            }

            foreach (var key in bundle.Document["bundle"]!.AsObject().Select(member => member.Key).SkipWhile(key => key != "contents"))
            {
                Assert.Contains(key, help, StringComparison.Ordinal);
            }
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private static async Task<(int Exit, string Output, string Error)> RunAsync(
        TempPaths dir,
        HttpMessageHandler? runtime,
        DoctorOptions options,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await DoctorCommand.RunAsync(Machine(dir, runtime, output, error, variables), options, new FixedClock(Now), Ct);
        return (exit, output.ToString(), error.ToString());
    }

    private static async Task<string> HelpAsync()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(["doctor", "--help"], Machine(dir, null, output, new StringWriter()), Ct);
        Assert.Equal(ExitCodes.Success, exit);
        return output.ToString();
    }

    /// <summary>The machine a run is typed against, and every way in which it is not the one running the suite.</summary>
    private static CliEnvironment Machine(
        TempPaths dir,
        HttpMessageHandler? runtime,
        StringWriter output,
        StringWriter error,
        IReadOnlyDictionary<string, string>? variables = null) =>
        new(
            output,
            error,
            dir.Paths,
            runtime ?? new Refusing(),
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "no-harness-here")),
            Programs: new FakeProgramRunner(),
            SearchPath: Path.Combine(dir.Paths.Root, "nowhere"),
            Identity: MadeUp,
            Variables: variables ?? new Dictionary<string, string>());

    /// <summary>A descriptor on disk and the runtime it names behind the handler.</summary>
    private static StubRuntime Running(TempPaths dir)
    {
        dir.WriteDescriptor(Descriptor());
        return new StubRuntime(dir.Paths.Root);
    }

    private static RuntimeDescriptor Descriptor() => new("v1", "0.3.0-dev", InstanceId, 4242, BaseUrl, Token, Now.AddHours(-1));

    /// <summary>Every entry under a directory, with each file's size: what a run that writes nothing leaves as it was.</summary>
    private static List<string> Tree(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(entry => File.Exists(entry) ? $"{entry} ({new FileInfo(entry).Length})" : entry + Path.DirectorySeparatorChar)
            .Order(StringComparer.Ordinal)];

    /// <summary>The row of a human table that names a section, or null.</summary>
    private static string Row(IReadOnlyList<string> lines, string section) =>
        lines.SingleOrDefault(line => line.StartsWith(section + " ", StringComparison.Ordinal))
        ?? throw new Xunit.Sdk.XunitException($"No row for '{section}' in:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");

    /// <summary>The platform as a bundle's versions name it: the system's description and its architecture.</summary>
    private static string Platform() => $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    /// <summary>The issue form's address with this build's version and this machine's platform filled in.</summary>
    private static string IssueUrl() =>
        "https://github.com/reply-team/jason-ai/issues/new?template=problem.yml&version=" + Uri.EscapeDataString(Jason.Contracts.JasonVersion.Current)
        + "&platform=" + Uri.EscapeDataString(Platform());

    private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    private static string Mebibytes(long bytes) => $"{bytes / (1024 * 1024)} MiB";

    /// <summary>A text with every run of white space, line breaks included, made one space: help as a sentence reads it.</summary>
    private static string Flat(string text) => string.Join(' ', text.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Every key of a document, at any depth.</summary>
    private static IEnumerable<string> Members(JsonNode? node) =>
        node switch
        {
            JsonObject members => members.SelectMany(member => Members(member.Value).Prepend(member.Key)),
            JsonArray items => items.SelectMany(Members),
            _ => [],
        };

    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    private static List<string> Keys(JsonNode node) => [.. node.AsObject().Select(member => member.Key)];

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static List<string> Strings(JsonNode? node) => [.. node!.AsArray().Select(Text)];

    /// <summary>Every request refused before it leaves the process, as on a machine whose runtime is not listening.</summary>
    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no runtime is listening in this test");
    }

    /// <summary>A clock that moves a second on from each moment it is asked for: whoever reads it twice gets two seconds.</summary>
    private sealed class Ticking(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _next = start;

        public override DateTimeOffset GetUtcNow()
        {
            var now = _next;
            _next = _next.AddSeconds(1);
            return now;
        }
    }

    /// <summary>Every request answered with one status and no body, as a runtime that refuses this descriptor's token does.</summary>
    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
