using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Update;

/// <summary>
/// The record an update writes before every step it takes, so that a kill anywhere leaves a state the next
/// invocation can finish or revert. It is the one thing in an update that has to survive the process performing
/// it, which is why it is read as strictly as a manifest: half a ledger would send an applier down a path the
/// installation is not on.
/// </summary>
public class UpdateLedgerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_ledger_is_read_back_exactly_as_it_was_written()
    {
        var written = Ledger() with
        {
            Step = UpdateStep.Healthy,
            StoppedAt = Noon.AddMinutes(1),
            BackupFile = @"C:\data\state\backups\jason-20260919T120100Z-before-X.db",
            NewlyApplied = ["20260919T000000_Something"],
            ChronicleId = "jrn_01M2XVJ84TFRG54VKC291A53F2",
        };

        var read = UpdateLedger.Read(written.ToJson());

        // Written out again rather than compared as records: a record compares its list member by reference, so
        // Assert.Equal on the two would be asserting that a round trip returns the same object, which it cannot.
        Assert.Equal(written.ToJson(), read.ToJson());
        Assert.Equal(written.StoppedAt, read.StoppedAt);
        Assert.Equal(written.BackupFile, read.BackupFile);
        Assert.Equal(written.NewlyApplied, read.NewlyApplied);
        Assert.Equal(written.ChronicleId, read.ChronicleId);
        Assert.Equal(written.InstallPath, read.InstallPath);
        Assert.Equal(written.StagedPath, read.StagedPath);
        Assert.Equal(written.PreviousPath, read.PreviousPath);

        // And it is a file a person can read when an update has gone wrong: no escaped plus signs in the moments.
        Assert.Contains("\"started_at\": \"2026-09-19T12:00:00.000Z\"", written.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_ledger_names_the_version_it_came_from_and_the_one_it_is_going_to()
    {
        var read = UpdateLedger.Read(Ledger().ToJson());

        Assert.Equal(SemanticVersion.Parse("0.1.0"), read.FromVersion);
        Assert.Equal(SemanticVersion.Parse("0.2.0"), read.ToVersion);
        Assert.Equal(UpdateStep.Staged, read.Step);
        Assert.Null(read.StoppedAt);
        Assert.Null(read.BackupFile);
        Assert.Empty(read.NewlyApplied);
        Assert.Null(read.ChronicleId);
    }

    /// <summary>Every step has a name on the wire, and the names are the ones an update's messages use.</summary>
    [Theory]
    [InlineData(UpdateStep.Staged, "staged")]
    [InlineData(UpdateStep.Drained, "drained")]
    [InlineData(UpdateStep.Stopped, "stopped")]
    [InlineData(UpdateStep.Kept, "kept")]
    [InlineData(UpdateStep.Swapped, "swapped")]
    [InlineData(UpdateStep.Started, "started")]
    [InlineData(UpdateStep.Healthy, "healthy")]
    [InlineData(UpdateStep.Complete, "complete")]
    public void Every_step_is_written_under_the_name_the_messages_use(UpdateStep step, string name)
    {
        var json = (Ledger() with { Step = step }).ToJson();

        Assert.Contains($"\"step\": \"{name}\"", json, StringComparison.Ordinal);
        Assert.Equal(step, UpdateLedger.Read(json).Step);
    }

    [Theory]
    [MemberData(nameof(NotLedgers))]
    public void A_document_that_is_not_a_ledger_is_refused_rather_than_half_read(string what, string json)
    {
        var refused = Assert.Throws<UpdateLedgerException>(() => UpdateLedger.Read(json));

        Assert.Equal(UpdateLedgerException.Invalid, refused.Code);
        Assert.False(string.IsNullOrWhiteSpace(refused.Message), $"{what} was refused without saying why");
    }

    public static TheoryData<string, string> NotLedgers()
    {
        var good = Ledger().ToJson();
        return new TheoryData<string, string>
        {
            { "not json at all", "half a fi" },
            { "an empty document", "{}" },
            { "an array", "[]" },
            { "a step this build does not know", good.Replace("\"staged\"", "\"teleported\"", StringComparison.Ordinal) },
            { "a version that is not one", good.Replace("\"0.2.0\"", "\"soon\"", StringComparison.Ordinal) },
            { "no install path", good.Replace("\"install_path\"", "\"install\"", StringComparison.Ordinal) },
            { "a moment with no zone", good.Replace("2026-09-19T12:00:00.000Z", "2026-09-19T12:00:00.000", StringComparison.Ordinal) },
            { "a truncated document", good[..(good.Length / 2)] },
        };
    }

    /// <summary>
    /// A ledger is written whole or not at all. The step it names is taken after it is on disk, so a torn file
    /// is a state no applier could read and no update could recover from.
    /// </summary>
    [Fact]
    public void A_half_written_ledger_never_replaces_a_whole_one()
    {
        using var dir = new TempTree();
        var path = Path.Combine(dir.Root, "ledger.json");
        Ledger().Write(path);

        // The temporary file the write goes through first, if it is left behind by a kill, is not the ledger.
        var strays = Directory.EnumerateFiles(dir.Root).Where(f => !f.EndsWith("ledger.json", StringComparison.Ordinal));
        Assert.Empty(strays);

        var second = Ledger() with { Step = UpdateStep.Swapped };
        second.Write(path);

        Assert.Equal(UpdateStep.Swapped, UpdateLedger.Read(File.ReadAllText(path)).Step);
    }

    /// <summary>
    /// A reader does not stop the update it is reading about. <c>jason update status</c> opens this file at
    /// whatever moment a person types it, and an update replaces it by renaming over it — which on Windows a
    /// reader holding it with ordinary sharing forbids.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted here because both are claimed: the writer's temporary-file-and-rename, and the
    /// reader's share mode. A hundred replacements against a reader in a tight loop is enough to catch it: the
    /// first attempt that lands mid-read throws.
    /// </remarks>
    [Fact]
    public async Task A_ledger_is_replaced_while_somebody_is_reading_it()
    {
        using var dir = new TempTree();
        var path = Path.Combine(dir.Root, "ledger.json");
        Ledger().Write(path);

        using var reading = new CancellationTokenSource();
        var reader = Task.Run(
            () =>
            {
                var read = 0;
                while (!reading.IsCancellationRequested)
                {
                    // Not swallowed: a read that throws here is the other half of the same defect.
                    _ = UpdateLedger.ReadFile(path);
                    read++;
                }

                return read;
            },
            TestContext.Current.CancellationToken);

        for (var write = 0; write < 100; write++)
        {
            (Ledger() with { Step = write % 2 == 0 ? UpdateStep.Swapped : UpdateStep.Started }).Write(path);
        }

        await reading.CancelAsync();
        Assert.True(await reader > 0, "the reader never managed a single read, so this proved nothing");
        Assert.NotNull(UpdateLedger.ReadFile(path));
    }

    /// <summary>
    /// And a write that fails before its rename leaves the ledger that was there. The step a ledger names is
    /// taken after the file is on disk, so a half-written one is the state nothing could recover from.
    /// </summary>
    [Fact]
    public void A_write_that_fails_before_its_rename_leaves_the_last_ledger_readable()
    {
        using var dir = new TempTree();
        var path = Path.Combine(dir.Root, "ledger.json");
        Ledger().Write(path);

        // The temporary file cannot be written, because a directory has its name.
        Directory.CreateDirectory(path + ".writing");

        // The kind of refusal differs by platform — a denied access here, a directory-in-the-way there — and what
        // matters is only that the write did not happen.
        Assert.ThrowsAny<SystemException>(() => (Ledger() with { Step = UpdateStep.Swapped }).Write(path));

        var survived = UpdateLedger.ReadFile(path);
        Assert.NotNull(survived);
        Assert.Equal(UpdateStep.Staged, survived.Step);
    }

    [Fact]
    public void A_missing_ledger_is_nothing_in_flight_rather_than_a_failure()
    {
        using var dir = new TempTree();

        Assert.Null(UpdateLedger.ReadFile(Path.Combine(dir.Root, "ledger.json")));
    }

    /// <summary>Moving to the next step changes the step and nothing else.</summary>
    [Fact]
    public void Taking_the_next_step_leaves_everything_else_alone()
    {
        var staged = Ledger();

        var drained = staged.At(UpdateStep.Drained);

        Assert.Equal(UpdateStep.Drained, drained.Step);
        Assert.Equal(staged with { Step = UpdateStep.Drained }, drained);
    }

    private static UpdateLedger Ledger() => new(
        SemanticVersion.Parse("0.1.0"),
        SemanticVersion.Parse("0.2.0"),
        UpdateStep.Staged,
        Noon,
        @"C:\Program Files\jason\jason.exe",
        @"C:\data\update\staged\0.2.0\jason.exe",
        @"C:\data\update\previous\jason.exe");
}
