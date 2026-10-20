namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// .NET in globalization invariant mode composes no text, so a runtime started in it would key an accent written as one
/// letter and the same accent written as a letter and a mark as two profiles. The start checks before it writes anything —
/// before the backup and the migration, which keys every stored link — and the shipped program at both ends shows that the
/// check is made and is heard. The mode is given to the started program alone, through its own environment, and never to
/// this process, so nothing else this assembly runs meanwhile sees it.
/// </summary>
public class InvariantModeStartTests
{
    [Fact]
    public async Task A_runtime_in_invariant_mode_refuses_to_start_and_writes_nothing()
    {
        using var it = GoldenPath.Create("start-invariant");
        GoldenPath.Delete(it.Root);

        var result = await GoldenPath.JasonAsync(it, new Dictionary<string, string> { ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1" }, "runtime", "start");
        if (result.ExitCode == 0)
        {
            // A runtime that skipped the check started; nothing this test began is left running.
            await GoldenPath.StopAsync(it);
        }

        Assert.True(result.ExitCode == 1, $"The start answered {result.ExitCode}: {result.Output} {result.Error}");
        var error = GoldenPath.Json(result)["error"];
        Assert.True((string?)error?["code"] == "runtime_start_failed", $"The start was refused as '{error?["code"]}'.");
        var message = (string?)error?["message"] ?? string.Empty;
        Assert.Contains("It said: The runtime did not start: this process does not compose text", message, StringComparison.Ordinal);
        Assert.Contains("Install libicu", message, StringComparison.Ordinal);
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
        Assert.False(Directory.Exists(it.Paths.StateDirectory), $"The start wrote {it.Paths.StateDirectory} before it refused.");
        Assert.False(Directory.Exists(it.Paths.LogsDirectory), $"The start wrote {it.Paths.LogsDirectory} before it refused.");
        Assert.False(Directory.Exists(it.Root), $"The start made the data directory {it.Root} before it refused.");
    }
}
