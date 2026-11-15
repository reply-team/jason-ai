using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Files;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// A crash between a settings writer's temporary file and its rename leaves that file, the settings' secrets in it,
/// beside the settings file. The runtime's start removes such a file once it is older than any write takes and no
/// writer holds it, keeps every other file of the folder, says what it did by counts alone, and never fails a start
/// over a file it could not remove.
/// </summary>
public sealed class SettingsTemporarySweepTests
{
    /// <summary>An obviously made-up value standing for a secret a stray file holds; it must never reach the log.</summary>
    private const string PlantedSecret = "fake-token-0000-not-a-real-secret";

    private const string RuntimeShape = "settings.json.0123456789abcdef0123456789abcdef.tmp";

    private const string DesktopShape = "settings.json.4242.tmp";

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTime LongAgo = Now.UtcDateTime - TimeSpan.FromHours(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_stray_of_either_writer_s_shape_is_removed_and_every_other_file_of_the_folder_is_kept()
    {
        using var dir = new TempDataDir();
        string[] kept = ["settings.json", "secrets.json", "settings.json.bak", "settings.json.notes.tmp", "other.json.4242.tmp", "notes.tmp"];
        foreach (var name in kept.Append(RuntimeShape).Append(DesktopShape))
        {
            Plant(dir, name, LongAgo);
        }

        var report = SettingsTemporarySweep.Sweep(dir.Paths.UserSettingsFile, Now);

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 2, Kept: 0, Refused: 0), report);
        Assert.False(File.Exists(Path.Combine(dir.Paths.ConfigDirectory, RuntimeShape)));
        Assert.False(File.Exists(Path.Combine(dir.Paths.ConfigDirectory, DesktopShape)));
        Assert.Equal(kept.Order(StringComparer.Ordinal), Directory.GetFiles(dir.Paths.ConfigDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The rule every platform has: a file younger than the age is a writer's that may still be at work, and is kept.
    /// A write takes well under a second; the age is ten minutes.
    /// </summary>
    [Theory]
    [InlineData(RuntimeShape)]
    [InlineData(DesktopShape)]
    public void A_file_younger_than_the_age_is_kept(string name)
    {
        using var dir = new TempDataDir();
        var file = Plant(dir, name, Now.UtcDateTime - SettingsTemporarySweep.MinimumAge + TimeSpan.FromSeconds(1));

        var report = SettingsTemporarySweep.Sweep(dir.Paths.UserSettingsFile, Now);

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 0, Kept: 1, Refused: 0), report);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void A_file_whose_time_is_ahead_of_the_clock_is_kept()
    {
        using var dir = new TempDataDir();
        var file = Plant(dir, DesktopShape, Now.UtcDateTime + TimeSpan.FromDays(1));

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 0, Kept: 1, Refused: 0), SettingsTemporarySweep.Sweep(dir.Paths.UserSettingsFile, Now));
        Assert.True(File.Exists(file));
    }

    /// <summary>
    /// Windows' own rule on top of the age: a file another handle holds open is refused an exclusive open, so it is kept
    /// however old it looks. Held as the desktop's writer holds it (Node opens sharing read, write and delete) and as the
    /// runtime's holds it (sharing nothing).
    /// </summary>
    [Theory]
    [InlineData(FileShare.ReadWrite | FileShare.Delete)]
    [InlineData(FileShare.None)]
    public void On_Windows_a_file_a_writer_holds_open_is_kept_however_old(FileShare writersShare)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses an exclusive open of a file another handle holds; elsewhere the age alone decides.");
        using var dir = new TempDataDir();
        var file = Plant(dir, DesktopShape, LongAgo);

        SettingsTemporarySweepReport report;
        using (new FileStream(file, FileMode.Open, FileAccess.Write, writersShare))
        {
            report = SettingsTemporarySweep.Sweep(dir.Paths.UserSettingsFile, Now);
        }

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 0, Kept: 1, Refused: 0), report);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void A_link_of_the_shape_is_left_and_what_it_leads_to_is_not_touched()
    {
        using var dir = new TempDataDir();
        var target = Plant(dir, "elsewhere.json", LongAgo);
        var link = Path.Combine(dir.Paths.ConfigDirectory, DesktopShape);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception refused) when (refused is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This account may not make a file link here (on Windows that takes developer mode).");
        }

        var report = SettingsTemporarySweep.Sweep(dir.Paths.UserSettingsFile, Now);

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 0, Kept: 1, Refused: 0), report);
        Assert.True(File.Exists(target));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void A_removal_that_fails_is_counted_and_the_sweep_goes_on()
    {
        using var dir = new TempDataDir();
        var refused = Plant(dir, RuntimeShape, LongAgo);
        var other = Plant(dir, DesktopShape, LongAgo);

        var report = SettingsTemporarySweep.Sweep(
            dir.Paths.UserSettingsFile,
            Now,
            path =>
            {
                if (path == refused)
                {
                    throw new UnauthorizedAccessException("a stand-in refusal");
                }

                SettingsTemporarySweep.RemoveUnheld(path);
            });

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 1, Kept: 0, Refused: 1), report);
        Assert.True(File.Exists(refused));
        Assert.False(File.Exists(other));
    }

    [Fact]
    public void A_folder_that_is_not_there_is_nothing_to_sweep()
    {
        using var dir = new TempDataDir();

        Assert.Equal(new SettingsTemporarySweepReport(0, 0, 0), SettingsTemporarySweep.Sweep(dir.Paths.UserSettingsFile, Now));
    }

    /// <summary>
    /// In layout 2 the secrets are in <c>config/secrets.json</c>, apart from <c>setup/settings.json</c>, and a write of
    /// either leaves its temporary file beside it: both are swept, each in its own folder, and nothing else of either.
    /// </summary>
    [Fact]
    public void In_layout_two_the_strays_beside_the_settings_file_and_beside_the_secrets_file_are_both_removed()
    {
        using var dir = new TempDataDir();
        var paths = new JasonPaths(dir.Paths.Root, 2);
        var setup = Path.GetDirectoryName(paths.UserSettingsFile)!;
        var config = Path.GetDirectoryName(paths.SecretsFile)!;
        var settingsStray = PlantAt(setup, "settings.json.4242.tmp", LongAgo);
        var secretsStray = PlantAt(config, "secrets.json." + "0123456789abcdef0123456789abcdef" + ".tmp", LongAgo);
        var keptBesideSecrets = PlantAt(config, "settings.json.4242.tmp", LongAgo);
        PlantAt(setup, "settings.json", LongAgo);
        PlantAt(config, "secrets.json", LongAgo);

        var report = SettingsTemporarySweep.Sweep(paths, Now);

        Assert.Equal(new SettingsTemporarySweepReport(Removed: 2, Kept: 0, Refused: 0), report);
        Assert.False(File.Exists(settingsStray));
        Assert.False(File.Exists(secretsStray));
        Assert.True(File.Exists(keptBesideSecrets));
        Assert.True(File.Exists(paths.UserSettingsFile));
        Assert.True(File.Exists(paths.SecretsFile));
    }

    /// <summary>The runtime's own writer makes its temporary file by the rule's name, so the sweep and export know it.</summary>
    [Fact]
    public void The_runtime_s_writer_names_its_temporary_file_by_the_rule()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);

        var temporary = JasonConfiguration.WriteTemporary(dir.Paths.UserSettingsFile, Encoding.UTF8.GetBytes("{}"), replacing: false, out _);

        Assert.NotNull(temporary);
        Assert.Equal(dir.Paths.ConfigDirectory, Path.GetDirectoryName(temporary));
        Assert.True(SettingsTemporaryFiles.IsOne("settings.json", Path.GetFileName(temporary)), "The runtime's writer made a name the rule does not know.");
        File.Delete(temporary);
    }

    // ---- at the runtime's start --------------------------------------------------------------------------------------

    /// <summary>
    /// A second runtime the instance lock refuses owns nothing of the installation another runtime runs on: it removes no
    /// file, however stale, and the running runtime's next start is the one that sweeps.
    /// </summary>
    [Fact]
    public async Task A_start_the_instance_lock_refuses_removes_nothing()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var stray = Plant(dir, RuntimeShape, DateTime.UtcNow - TimeSpan.FromHours(1));
        using var held = InstanceLock.Acquire(dir.Paths);

        await Assert.ThrowsAsync<RuntimeAlreadyRunningException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.True(File.Exists(stray));
    }

    /// <summary>A start that fails before it holds the lock, on a settings file it cannot read, removes nothing either.</summary>
    [Fact]
    public async Task A_start_that_fails_on_an_unreadable_settings_file_removes_nothing()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, "{ \"Dispatcher\": ");
        var stray = Plant(dir, DesktopShape, DateTime.UtcNow - TimeSpan.FromHours(1));

        await Assert.ThrowsAnyAsync<Exception>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.True(File.Exists(stray));
    }

    [Fact]
    public async Task A_start_removes_a_stray_keeps_the_settings_file_and_logs_counts_without_a_byte_of_the_file()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var stray = Plant(dir, RuntimeShape, DateTime.UtcNow - TimeSpan.FromHours(1));

        var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        await runtime.StopAsync();

        Assert.False(File.Exists(stray));
        Assert.Equal(RuntimeApiFixture.DispatcherOff, await File.ReadAllTextAsync(dir.Paths.UserSettingsFile, Ct));
        var logs = await LogsAsync(dir);
        var line = Assert.Single(SweepLines(logs));
        Assert.Equal(("Information", 1, 0, 0), line);
        Assert.DoesNotContain(PlantedSecret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_with_nothing_to_sweep_says_nothing_of_it()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);

        var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        await runtime.StopAsync();

        Assert.Empty(SweepLines(await LogsAsync(dir)));
    }

    [Fact]
    public async Task A_removal_that_fails_does_not_fail_the_start_and_is_said_without_the_file_s_content()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var stray = Plant(dir, DesktopShape, DateTime.UtcNow - TimeSpan.FromHours(1));
        var options = TestRuntimeOptions.Quiet with
        {
            SettingsTemporaryRemoval = _ => throw new IOException("a stand-in failure " + PlantedSecret),
        };

        var runtime = await RuntimeHost.StartAsync(dir.Paths, options, Ct);
        HttpStatusCode answer;
        try
        {
            answer = await SystemInfoAsync(runtime);
        }
        finally
        {
            await runtime.StopAsync();
        }

        Assert.Equal(HttpStatusCode.OK, answer);
        Assert.True(File.Exists(stray));
        var logs = await LogsAsync(dir);
        Assert.Equal(("Warning", 0, 0, 1), Assert.Single(SweepLines(logs)));
        Assert.DoesNotContain(PlantedSecret, logs, StringComparison.Ordinal);
    }

    /// <summary>A file of the folder by its name, holding the planted secret, its write time set.</summary>
    private static string Plant(TempDataDir dir, string name, DateTime lastWriteUtc) => PlantAt(dir.Paths.ConfigDirectory, name, lastWriteUtc);

    /// <summary>A file of <paramref name="folder"/> by its name, holding the planted secret, its write time set.</summary>
    private static string PlantAt(string folder, string name, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, name);
        File.WriteAllText(file, "{\"Plugins\":{\"Config\":{\"made-up\":{\"token\":\"" + PlantedSecret + "\"}}}}");
        File.SetLastWriteTimeUtc(file, lastWriteUtc);
        return file;
    }

    /// <summary>Every log file's text; read after the runtime stopped, which flushes and closes the rolling sink.</summary>
    private static async Task<string> LogsAsync(TempDataDir dir) =>
        string.Concat(await Task.WhenAll(
            Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(file => File.ReadAllTextAsync(file, Ct))));

    /// <summary>The sweep's lines in the log, each by its level and its three counts.</summary>
    private static List<(string Level, int Removed, int Kept, int Refused)> SweepLines(string logs) =>
        [
            .. logs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(text => JsonDocument.Parse(text).RootElement)
                .Where(line => line.TryGetProperty("@mt", out var template) && template.GetString()!.StartsWith(SweepTemplateStart, StringComparison.Ordinal))
                .Select(line => (
                    line.TryGetProperty("@l", out var level) ? level.GetString()! : "Information",
                    line.GetProperty("Removed").GetInt32(),
                    line.GetProperty("Kept").GetInt32(),
                    line.GetProperty("Refused").GetInt32())),
        ];

    private const string SweepTemplateStart = "Temporary files writes of the settings left beside their files (settings.json.<id>.tmp, and secrets.json.<id>.tmp where the secrets are a file of their own) swept at start:";

    private static async Task<HttpStatusCode> SystemInfoAsync(RunningRuntime runtime)
    {
        using var http = new HttpClient { BaseAddress = runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token);
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), body, Ct);
        return response.StatusCode;
    }
}
