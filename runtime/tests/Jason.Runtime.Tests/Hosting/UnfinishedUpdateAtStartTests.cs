using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Integration;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// <c>jason runtime run</c> under an update that stopped part-way. A runtime registered to start at sign-in runs it, and
/// from the step that moves the installed build aside on, a sign-in in that window started the new build outside the
/// update, whose last steps were then never taken. It refuses now, with the start's own envelope, unless the launch is
/// the update's own; before that step nothing has been replaced and it starts as it always did.
/// </summary>
public sealed class UnfinishedUpdateAtStartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(UpdateStep.Kept)]
    [InlineData(UpdateStep.Swapped)]
    [InlineData(UpdateStep.Plugins)]
    [InlineData(UpdateStep.Started)]
    [InlineData(UpdateStep.Healthy)]
    public async Task A_run_that_is_not_the_updates_own_is_refused_from_kept_on_and_starts_nothing(UpdateStep step)
    {
        using var data = new TempDataDir();
        Ledger(data.Paths, step).Write(UpdateLedger.In(data.Paths));

        var (exit, said) = await RunAsync(data, mark: null);

        Assert.Equal(1, exit);
        var error = Envelope(said);
        Assert.Equal("update_in_progress", (string?)error["code"]);
        Assert.Equal(
            $"An update from 0.1.0 to 9.9.9 stopped at '{step.ToString().ToLowerInvariant()}' and is not finished. `jason update apply` "
            + "finishes it and starts the runtime as part of it, and `jason update rollback` goes back to 0.1.0. Nothing was started.",
            (string?)error["message"]);
        Assert.False((bool?)error["retryable"]);
        Assert.False(File.Exists(data.Paths.DescriptorFile), "A refused run published an endpoint descriptor.");
        Assert.False(File.Exists(data.Paths.DatabaseFile), "A refused run made or migrated a database.");

        var line = Assert.Single(Lines(data), line => (string?)line["@mt"] == RefusedInFlight);
        Assert.Equal(step.ToString().ToLowerInvariant(), (string?)line["Step"]);
        Assert.Equal("update_in_progress", (string?)line["Code"]);
        Assert.Equal(UpdateLedger.In(data.Paths), (string?)line["Ledger"]);

        // Nothing of the refused run is held: the update's own run, which comes next, starts on the same data directory.
        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet with { UpdateLaunch = UpdateLaunch.At(step) }, Ct);
        Assert.True(File.Exists(data.Paths.DescriptorFile));
    }

    [Fact]
    public async Task A_run_beside_a_ledger_that_cannot_be_read_is_refused_with_the_file_named()
    {
        using var data = new TempDataDir();
        var ledger = UpdateLedger.In(data.Paths);
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        await File.WriteAllTextAsync(ledger, "{ \"from\": \"" + Sentinel + "\"", Ct);

        var (exit, said) = await RunAsync(data, mark: null);

        Assert.Equal(1, exit);
        var error = Envelope(said);
        Assert.Equal("update_ledger_invalid", (string?)error["code"]);
        Assert.StartsWith($"The update ledger at '{ledger}' is not one this build can act on: ", (string?)error["message"], StringComparison.Ordinal);
        Assert.EndsWith("Nothing was started.", (string?)error["message"], StringComparison.Ordinal);
        Assert.False(File.Exists(data.Paths.DescriptorFile), "A refused run published an endpoint descriptor.");
        Assert.False(File.Exists(data.Paths.DatabaseFile), "A refused run made or migrated a database.");

        var line = Assert.Single(Lines(data), line => (string?)line["@mt"] == RefusedUnreadable);
        Assert.Equal("update_ledger_invalid", (string?)line["Code"]);
        Assert.Equal(ledger, (string?)line["Ledger"]);
        Assert.DoesNotContain(Sentinel, Log(data), StringComparison.Ordinal);

        // Nothing of the refused run is held here either: a run the update marked starts on the same data directory.
        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet with { UpdateLaunch = UpdateLaunch.At(UpdateStep.Started) }, Ct);
        Assert.True(File.Exists(data.Paths.DescriptorFile));
    }

    [Theory]
    [InlineData(UpdateStep.Started)]
    [InlineData(UpdateStep.Kept)]
    public async Task The_updates_own_run_starts(UpdateStep step)
    {
        using var data = new TempDataDir();
        Ledger(data.Paths, step).Write(UpdateLedger.In(data.Paths));

        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet with { UpdateLaunch = UpdateLaunch.At(step) }, Ct);

        Assert.True(File.Exists(data.Paths.DescriptorFile));
        var line = Assert.Single(Lines(data), line => (string?)line["@mt"] == StartedByTheUpdate);
        Assert.Equal(step.ToString().ToLowerInvariant(), (string?)line["Step"]);
    }

    /// <summary>
    /// The update's rollback starts the build it put back beside a ledger that build may not be able to read, one a newer
    /// build wrote: its mark is enough, whatever the file says.
    /// </summary>
    [Fact]
    public async Task The_updates_own_run_starts_beside_a_ledger_it_cannot_read()
    {
        using var data = new TempDataDir();
        var ledger = UpdateLedger.In(data.Paths);
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        await File.WriteAllTextAsync(ledger, "not a ledger", Ct);

        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet with { UpdateLaunch = "healthy" }, Ct);

        Assert.True(File.Exists(data.Paths.DescriptorFile));
        var line = Assert.Single(Lines(data), line => (string?)line["@mt"] == StartedByTheUpdate);
        Assert.Equal("unknown", (string?)line["Step"]);
    }

    [Theory]
    [InlineData(UpdateStep.Staged)]
    [InlineData(UpdateStep.Drained)]
    [InlineData(UpdateStep.Stopped)]
    public async Task A_run_before_kept_starts_without_the_mark(UpdateStep step)
    {
        using var data = new TempDataDir();
        Ledger(data.Paths, step).Write(UpdateLedger.In(data.Paths));

        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);

        Assert.True(File.Exists(data.Paths.DescriptorFile));
        var line = Assert.Single(Lines(data), line => (string?)line["@mt"] == StartedBeforeKept);
        Assert.Equal(step.ToString().ToLowerInvariant(), (string?)line["Step"]);
    }

    [Fact]
    public async Task A_run_after_a_completed_update_starts_without_the_mark_and_says_nothing_of_it()
    {
        using var data = new TempDataDir();
        Ledger(data.Paths, UpdateStep.Complete).Write(UpdateLedger.In(data.Paths));

        await using var runtime = await RuntimeHost.StartAsync(data.Paths, TestRuntimeOptions.Quiet, Ct);

        Assert.True(File.Exists(data.Paths.DescriptorFile));
        Assert.DoesNotContain(Lines(data), line => (string?)line["@mt"] is StartedBeforeKept or StartedByTheUpdate);
    }

    private const string RefusedInFlight = "The runtime did not start: an update stopped at {Step} and this start is not its own ({Code}); its ledger is {Ledger}";

    private const string RefusedUnreadable = "The runtime did not start: the update ledger {Ledger} cannot be read ({Code})";

    private const string StartedByTheUpdate = "Started by the update's own launch; its ledger is at {Step}";

    private const string StartedBeforeKept = "An update stopped at {Step}, before anything was replaced; the installed build starts";

    /// <summary>What a ledger that is not one held, which neither the refusal's log line nor any other may repeat.</summary>
    private const string Sentinel = "SENTINEL_LEDGER_TEXT";

    private static UpdateLedger Ledger(JasonPaths paths, UpdateStep step) => new(
        SemanticVersion.Parse("0.1.0"),
        SemanticVersion.Parse("9.9.9"),
        step,
        DateTimeOffset.Parse("2026-09-19T08:00:00Z", CultureInfo.InvariantCulture),
        Path.Combine(paths.Root, "install", "jason"),
        Path.Combine(paths.Root, "update", "staged", "9.9.9", "jason"),
        Path.Combine(paths.Root, "update", "previous", "jason"));

    /// <summary>What a foreground run said where it was started from, and its exit code; nothing may have escaped it.</summary>
    private static async Task<(int Exit, string Said)> RunAsync(TempDataDir data, string? mark)
    {
        using var refusals = new StringWriter();
        var exit = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // A run that starts after all serves until it is stopped; it is stopped at once, so the row fails on its exit code
        // rather than waiting forever.
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        var escaped = await Record.ExceptionAsync(async () => exit = await RuntimeHost.RunAsync(
            data.Paths,
            TestRuntimeOptions.Quiet with { Refusals = refusals, UpdateLaunch = mark },
            stop.Token));

        Assert.Null(escaped);
        return (exit, refusals.ToString().Trim());
    }

    private static JsonObject Envelope(string said) =>
        JsonNode.Parse(said)?["error"]?.AsObject() ?? throw new Xunit.Sdk.XunitException($"Not an error envelope: {said}");

    private static string Log(TempDataDir data) =>
        string.Concat(Directory.GetFiles(data.Paths.LogsDirectory).Select(FakeHostRuntime.ReadShared));

    private static List<JsonObject> Lines(TempDataDir data) => [.. Log(data)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => JsonNode.Parse(line)!.AsObject())];
}
