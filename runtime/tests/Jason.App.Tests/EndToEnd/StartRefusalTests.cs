namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A runtime that will not start, heard by the command that started it — the shipped program at both ends.
/// </summary>
/// <remarks>
/// <c>jason runtime start</c> launches the runtime detached, and a detached runtime points its standard streams at
/// the null device before it does anything else. So when it refused to start — a data directory it could not
/// prepare — it wrote the reason to nowhere, the CLI read an exit code, and it sent the operator to an empty log
/// directory with advice to run the runtime in the foreground. Only the two real processes together show whether
/// the sentence arrives.
/// </remarks>
public class StartRefusalTests
{
    [Fact]
    public async Task A_detached_runtime_that_will_not_start_is_heard_in_its_own_words()
    {
        using var it = GoldenPath.Create("start-refused");

        // A file where the runtime's first directory goes, so the data directory cannot be prepared.
        await File.WriteAllTextAsync(it.Paths.StateDirectory, "a file where the state directory goes", TestContext.Current.CancellationToken);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("It said: The data directory", result.Output, StringComparison.Ordinal);
        Assert.Contains("could not be prepared", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("jason runtime run", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A settings file that is not valid JSON, on an installation that has run before. The runtime refuses before it has
    /// a log, so its sentence — the file, the line and the byte — is the only account there is; it reached nobody, and
    /// the log directory, holding an earlier day's file, was where the person was sent instead.
    /// </summary>
    [Fact]
    public async Task A_settings_file_that_is_not_valid_JSON_is_heard_by_its_position_on_an_installation_that_has_logged_before()
    {
        using var it = GoldenPath.Create("start-malformed");
        await EarlierDaysLogAsync(it);
        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, "{\"Dispatcher\":{\"TickSeconds\":1,\n  " + Sentinel, TestContext.Current.CancellationToken);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");

        AssertHeard(
            it,
            result,
            $"It said: Invalid Jason configuration: the settings file at '{it.Paths.UserSettingsFile}' could not be read (JsonException at line 2, byte 3).");
    }

    /// <summary>The same for a value of the wrong type in a section the host is built from: the setting's path and its type.</summary>
    [Fact]
    public async Task A_value_of_the_wrong_type_is_heard_by_its_setting_on_an_installation_that_has_logged_before()
    {
        using var it = GoldenPath.Create("start-mistyped");
        await EarlierDaysLogAsync(it);
        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, "{\"Dispatcher\":{\"TickSeconds\":\"" + Sentinel + "\"}}", TestContext.Current.CancellationToken);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");

        AssertHeard(it, result, "It said: Invalid Jason configuration: Dispatcher:TickSeconds could not be read as System.Int32.");
    }

    /// <summary>
    /// A value of the wrong type in a section checked as the host starts, long after this start began its log. The
    /// reason was left to the log, and the person was told only to go and read it; it is heard in the runtime's own words
    /// now, and the log this start wrote is still pointed at.
    /// </summary>
    [Fact]
    public async Task A_value_of_the_wrong_type_in_a_section_checked_late_is_heard_though_this_start_has_logged()
    {
        using var it = GoldenPath.Create("start-late");
        await EarlierDaysLogAsync(it);
        await File.WriteAllTextAsync(
            it.Paths.UserSettingsFile,
            "{\"Dispatcher\":{\"Enabled\":false},\"Plugins\":{\"Limits\":{\"TimeoutMs\":\"" + Sentinel + "\"}}}",
            TestContext.Current.CancellationToken);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");

        Assert.Equal(1, result.ExitCode);
        var message = GoldenPath.Json(result)["error"]?["message"]?.GetValue<string>() ?? string.Empty;
        Assert.Contains("It said: Invalid Jason configuration: Plugins:Limits:TimeoutMs could not be read as System.Int32.", message, StringComparison.Ordinal);
        Assert.Contains($"See the log files under '{it.Paths.LogsDirectory}'.", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, result.Error, StringComparison.Ordinal);
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
    }

    /// <summary>What a person typed into the settings file, which no answer may repeat.</summary>
    private const string Sentinel = "SENTINEL_START_SETTINGS";

    /// <summary>A file the runtime wrote on an earlier day, so the log directory is not empty when this start begins.</summary>
    private static async Task EarlierDaysLogAsync(Installation it)
    {
        Directory.CreateDirectory(it.Paths.LogsDirectory);
        var log = Path.Combine(it.Paths.LogsDirectory, "runtime-20261003.jsonl");
        await File.WriteAllTextAsync(log, "{\"@mt\":\"an earlier day's runtime\"}\n", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-1));
    }

    /// <summary>
    /// The verb refused, carried the runtime's own sentence, quoted nothing that was typed, did not send the person to
    /// log files this start never wrote, and guessed at no cause beside the one it was told.
    /// </summary>
    private static void AssertHeard(Installation it, CliResult result, string said)
    {
        Assert.Equal(1, result.ExitCode);
        var message = GoldenPath.Json(result)["error"]?["message"]?.GetValue<string>() ?? string.Empty;
        Assert.Contains(said, message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("See the log files", message, StringComparison.Ordinal);
        Assert.Contains($"No log file under '{it.Paths.LogsDirectory}' was written by this start.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("which is what happens when", message, StringComparison.Ordinal);
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
    }
}
