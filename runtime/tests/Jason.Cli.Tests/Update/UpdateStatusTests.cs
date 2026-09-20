using System.Text.Json;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// <c>jason update status</c> reads the ledger and nothing else. It is the verb a person types when an update
/// stopped halfway and they want to know where it stopped, which is exactly the moment the runtime may be down
/// — so it asks no runtime anything, and the handler here fails the test if it is asked.
/// </summary>
public class UpdateStatusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Status_says_what_is_in_flight_and_which_step_it_reached()
    {
        using var dir = new TempPaths();
        Ledger(dir, UpdateStep.Kept).Write(new UpdatePaths(dir.Paths).Ledger);
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "status"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        var answer = Answer(stdout.ToString());
        Assert.True(answer.InFlight);
        Assert.Equal("kept", answer.Step);
        Assert.Equal("0.1.0", answer.From);
        Assert.Equal("9.9.9", answer.To);
        Assert.Equal(DateTimeOffset.Parse("2026-09-19T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture), answer.StartedAt);

        // One line of compact snake_case JSON, like every other verb's answer.
        Assert.DoesNotContain('\n', stdout.ToString().TrimEnd());
        Assert.Contains("\"in_flight\":true", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_on_an_installation_with_nothing_in_flight_says_so_and_exits_zero()
    {
        using var dir = new TempPaths();
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "status"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        var answer = Answer(stdout.ToString());
        Assert.False(answer.InFlight);

        // No ledger at all is not the same as a finished one: the step is absent rather than "complete".
        Assert.Null(answer.Step);
        Assert.Null(answer.From);
        Assert.Null(answer.To);
        Assert.Empty(answer.NewlyApplied);
    }

    /// <summary>
    /// The ledger stays behind when an update finishes, because <c>jason update rollback</c> an hour later has
    /// nothing else to read. A record of something that is over is not work in progress, and a person reading
    /// "in flight" about an update that ended yesterday would go looking for a process that does not exist.
    /// </summary>
    [Fact]
    public async Task Status_reports_a_completed_update_as_completed_and_not_as_in_flight()
    {
        using var dir = new TempPaths();
        var backup = Path.Combine(dir.Paths.BackupsDirectory, "jason-20260919T120000Z-before-20260920000000_Next.db");
        (Ledger(dir, UpdateStep.Complete) with
        {
            StoppedAt = DateTimeOffset.Parse("2026-09-19T08:01:00Z", System.Globalization.CultureInfo.InvariantCulture),
            BackupFile = backup,
            NewlyApplied = ["20260920000000_Next"],
            ChronicleId = "jrn_01M2XVJ84TFRG54VKC291A53F2",
        }).Write(new UpdatePaths(dir.Paths).Ledger);
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "status"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = Answer(stdout.ToString());
        Assert.False(answer.InFlight);
        Assert.Equal("complete", answer.Step);
        Assert.Equal("0.1.0", answer.From);
        Assert.Equal("9.9.9", answer.To);

        // And it carries the record the finished update left, which is what a rollback reads.
        Assert.Equal(backup, answer.BackupFile);
        Assert.Equal(["20260920000000_Next"], answer.NewlyApplied);
    }

    /// <summary>
    /// Exit 3 is the CLI saying a step needed the runtime and could not have it. Reading a file is not such a
    /// step, and an update that went wrong is the likeliest reason there is no runtime to ask.
    /// </summary>
    [Fact]
    public async Task Status_reads_the_ledger_without_a_runtime()
    {
        using var dir = new TempPaths();
        Ledger(dir, UpdateStep.Swapped).Write(new UpdatePaths(dir.Paths).Ledger);

        // No descriptor on disk, and a handler that fails the test if anything is sent anywhere.
        Assert.False(File.Exists(dir.Paths.DescriptorFile));
        var (env, stdout, stderr) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "status"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.Equal("swapped", Answer(stdout.ToString()).Step);
    }

    [Fact]
    public async Task Human_mode_says_in_a_sentence_where_the_update_stands()
    {
        using var dir = new TempPaths();
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "status", "--human"], env, Ct));
        Assert.Contains("No update", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("{", stdout.ToString(), StringComparison.Ordinal);

        stdout.GetStringBuilder().Clear();
        Ledger(dir, UpdateStep.Kept).Write(new UpdatePaths(dir.Paths).Ledger);
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "status", "--human"], env, Ct));
        var inFlight = stdout.ToString();
        Assert.Contains("kept", inFlight, StringComparison.Ordinal);
        Assert.Contains("0.1.0", inFlight, StringComparison.Ordinal);
        Assert.Contains("9.9.9", inFlight, StringComparison.Ordinal);

        // And it says what to type, because "kept" alone tells a person nothing they can do.
        Assert.Contains("jason update apply", inFlight, StringComparison.Ordinal);
        Assert.DoesNotContain("{", inFlight, StringComparison.Ordinal);

        stdout.GetStringBuilder().Clear();
        Ledger(dir, UpdateStep.Complete).Write(new UpdatePaths(dir.Paths).Ledger);
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["update", "status", "--human"], env, Ct));
        Assert.Contains("complete", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("in flight", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file that is not a ledger is said so, with the code the ledger's own reader raises. Guessing at half a
    /// document is how a person is told an update reached a step it never began.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_not_a_ledger_is_refused_with_its_code()
    {
        using var dir = new TempPaths();
        var ledger = new UpdatePaths(dir.Paths).Ledger;
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        await File.WriteAllTextAsync(ledger, "{ not json", Ct);
        var (env, stdout, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["update", "status"], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(UpdateLedgerException.Invalid, RuntimeVerbs.Envelope(stdout.ToString()).Code);
    }

    private static UpdateLedger Ledger(TempPaths dir, UpdateStep step)
    {
        var update = new UpdatePaths(dir.Paths);
        var to = SemanticVersion.Parse("9.9.9");
        return new UpdateLedger(
            SemanticVersion.Parse("0.1.0"),
            to,
            step,
            DateTimeOffset.Parse("2026-09-19T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Path.Combine(dir.Paths.Root, "install", "jason"),
            update.StagedExecutable(to),
            update.PreviousExecutable);
    }

    private static UpdateStatusResponse Answer(string stdout) =>
        JsonSerializer.Deserialize<UpdateStatusResponse>(stdout, JasonJson.Options)!;
}
