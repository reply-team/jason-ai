using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Update;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// <c>jason runtime run</c> around an update that stopped after moving the installed build aside, the shipped program
/// itself: what a runtime registered to start at sign-in runs. Without the update's mark it refuses before it touches
/// anything; with it the start goes on, and the mark reaches the runtime from the environment the update's launch sets.
/// </summary>
public class UnfinishedUpdateRunTests
{
    [Fact]
    public async Task A_run_that_is_not_the_updates_own_is_refused_with_the_starts_envelope_and_leaves_nothing()
    {
        using var it = GoldenPath.Create("run-around-update");
        Kept(it);

        var result = await GoldenPath.JasonAsync(it, "runtime", "run");

        Assert.Equal(1, result.ExitCode);
        var error = JsonNode.Parse(result.Error)?["error"]?.AsObject() ?? throw new Xunit.Sdk.XunitException($"Not an envelope: {result.Error}");
        Assert.Equal("update_in_progress", (string?)error["code"]);
        Assert.EndsWith("Nothing was started.", (string?)error["message"], StringComparison.Ordinal);
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
        Assert.False(File.Exists(it.Paths.DatabaseFile), "A refused run made or migrated a database.");
    }

    /// <summary>
    /// With the mark in its environment the same run gets past the update and on to the rest of its start, which a
    /// directory in the descriptor's place then refuses: the refusal it ends with is the descriptor's, and not the update's.
    /// </summary>
    [Fact]
    public async Task The_updates_own_run_goes_on_past_the_update()
    {
        using var it = GoldenPath.Create("run-marked");
        Kept(it);
        Directory.CreateDirectory(Path.Combine(it.Paths.DescriptorFile, "in-the-way"));

        var result = await GoldenPath.JasonAsync(
            it,
            new Dictionary<string, string> { [UpdateLaunch.Variable] = UpdateLaunch.At(UpdateStep.Started) },
            "runtime",
            "run");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"The runtime could not publish its endpoint descriptor at '{it.Paths.DescriptorFile}'", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("update_in_progress", result.Error, StringComparison.Ordinal);
    }

    private static void Kept(Installation it)
    {
        var root = it.Paths.Root;
        new UpdateLedger(
            SemanticVersion.Parse("0.1.0"),
            SemanticVersion.Parse("9.9.9"),
            UpdateStep.Kept,
            DateTimeOffset.Parse("2026-09-19T08:00:00Z", CultureInfo.InvariantCulture),
            Path.Combine(root, "install", "jason"),
            Path.Combine(root, "update", "staged", "9.9.9", "jason"),
            Path.Combine(root, "update", "previous", "jason")).Write(UpdateLedger.In(it.Paths));
    }
}
