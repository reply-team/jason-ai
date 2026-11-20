using System.Collections;
using System.Diagnostics;
using System.Globalization;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The launcher behind the toast channel, end to end through the one door every child goes through: the payload
/// written on stdin, the bound, and the exit code. Nothing is shown — the toast host is asked in a protocol it refuses
/// before it touches anything.
/// </summary>
public sealed class ProcessToastLauncherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>How long a row waits for a program it closed to end before it fails rather than hangs: a guard, never a measure.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    private static Dictionary<string, string> Environment()
    {
        var source = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            source[(string)entry.Key] = (string)entry.Value!;
        }

        return ToastCommands.Environment(source, OperatingSystem.IsWindows() ? DesktopPlatform.Windows : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOs : DesktopPlatform.Linux);
    }

    [Fact]
    public async Task The_toast_host_s_refusal_comes_back_through_the_one_door()
    {
        var launch = new ToastLaunch([.. new JasonDllLocator().Command, "toast-host", "--protocol", "2"], "{\"protocol_version\":2}", Environment());

        var result = await new ProcessToastLauncher().LaunchAsync(launch, TimeSpan.FromSeconds(60), Ct);

        Assert.True(result.Started);
        Assert.False(result.TimedOut);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("--protocol 1", result.StderrTail, StringComparison.Ordinal);
        Assert.Equal(ToastOutcomes.RejectedCode, ToastOutcomes.Read(result).Code);
    }

    /// <summary>
    /// The launcher writes stdin as UTF-8 without a byte-order mark. The child reads the bytes itself, decodes them as
    /// UTF-8 and answers with their code points in ASCII, so neither its console's code page nor the reader's encoding
    /// can make another encoding look right. The toast host's own payload is escaped JSON, ASCII on the wire: this holds
    /// the launcher, whatever it is handed.
    /// </summary>
    [Fact]
    public async Task The_launcher_writes_stdin_as_utf_8_without_a_byte_order_mark()
    {
        const string Payload = "Jason — 3 approvals ✓ · €5 · naïve";
        var launch = new ToastLaunch([Jason.Runtime.Tests.Plugins.FakeProviderCli.ExecutablePath, "stdin-codepoints"], Payload, Environment());

        var result = await new ProcessToastLauncher().LaunchAsync(launch, TimeSpan.FromSeconds(30), Ct);

        var written = string.Join(' ', Payload.EnumerateRunes().Select(rune => rune.Value.ToString("X4", CultureInfo.InvariantCulture)));
        Assert.Equal((0, written), (result.ExitCode, result.Stdout.Trim()));
    }

    /// <summary>
    /// A child that ends without reading its stdin breaks the pipe under the write. That is its answer, not a timeout:
    /// its exit code and stderr come back as they would have.
    /// </summary>
    [Fact]
    public async Task A_child_that_ends_without_reading_its_stdin_is_not_a_timeout()
    {
        var launch = new ToastLaunch([.. new JasonDllLocator().Command, "toast-host", "--protocol", "2"], new string('x', 4 * 1024 * 1024), Environment());

        var result = await new ProcessToastLauncher().LaunchAsync(launch, TimeSpan.FromSeconds(60), Ct);

        Assert.True(result.Started);
        Assert.False(result.TimedOut);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ToastOutcomes.RejectedCode, ToastOutcomes.Read(result).Code);
    }

    /// <summary>A child still running at its bound is killed, with its tree, and said to have timed out.</summary>
    [Fact]
    public async Task A_child_that_outlives_its_bound_is_killed()
    {
        var launch = new ToastLaunch([Muxer.Path, .. Jason.Runtime.Tests.Execution.FakeAgentHost.EntryCommand("linger").Skip(1)], null, Environment());
        var watch = Stopwatch.StartNew();

        var result = await new ProcessToastLauncher().LaunchAsync(launch, TimeSpan.FromSeconds(2), Ct);

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the bound was not kept: {watch.Elapsed}");
    }

    [Fact]
    public async Task A_program_that_is_not_there_never_started()
    {
        var launch = new ToastLaunch(["jason-no-such-program-" + Guid.NewGuid().ToString("N")], null, Environment());

        var result = await new ProcessToastLauncher().LaunchAsync(launch, TimeSpan.FromSeconds(5), Ct);

        Assert.False(result.Started);
        Assert.NotNull(result.StartError);
        Assert.Equal(ToastOutcomes.LaunchFailed, ToastOutcomes.Read(result).Code);
    }
    /// <summary>
    /// A program that stays — notify-send waiting on the bus — is answered as soon as it says the line asked for, never
    /// killed at the bound, and handed back with its end to come, which closing it brings, with all it said. Breaks if the
    /// launcher kills a waiting toast at five seconds, which ends the click with it, or loses what it said.
    /// </summary>
    [Fact]
    public async Task A_program_that_said_its_line_is_handed_back_waiting_and_ends_when_closed()
    {
        var launch = new ToastLaunch([Jason.Runtime.Tests.Plugins.FakeProviderCli.ExecutablePath, "say-and-hold", "42", "600000"], null, Environment());

        var waiting = await new ProcessToastLauncher().LaunchWaitingAsync(launch, TimeSpan.FromSeconds(60), line => line == "42", Ct);

        Assert.Equal((true, false, (int?)null), (waiting.Said.Started, waiting.Said.TimedOut, waiting.Said.ExitCode));
        Assert.Equal("42\n", waiting.Said.Stdout);
        var end = Assert.IsType<Task<LaunchResult>>(waiting.End, exactMatch: false);
        Assert.False(end.IsCompleted, "the program still waits");
        waiting.Close();
        var ended = await end.WaitAsync(Hang, Ct);
        Assert.NotEqual(0, ended.ExitCode);
        Assert.Equal("42\n", ended.Stdout);
    }

    /// <summary>
    /// A program that ends within the bound is answered by its end, with no end to come; one that says nothing it is asked
    /// for is answered at the bound as still there, and not killed. Breaks if notify-send 0.8.3, which keeps its id until it
    /// ends, is killed at the bound, or one that ended is held as if it waited.
    /// </summary>
    [Fact]
    public async Task A_program_that_ended_is_its_end_and_one_silent_past_the_bound_still_waits()
    {
        var ended = await new ProcessToastLauncher().LaunchWaitingAsync(
            new ToastLaunch([Jason.Runtime.Tests.Plugins.FakeProviderCli.ExecutablePath, "echo-args", "7"], null, Environment()), TimeSpan.FromSeconds(60), line => line == "never", Ct);
        Assert.Null(ended.End);
        Assert.Equal((0, "7\n"), (ended.Said.ExitCode, ended.Said.Stdout.Replace("\r", string.Empty, StringComparison.Ordinal)));

        var silent = await new ProcessToastLauncher().LaunchWaitingAsync(
            new ToastLaunch([Jason.Runtime.Tests.Plugins.FakeProviderCli.ExecutablePath, "hold", "600000"], null, Environment()), TimeSpan.FromMilliseconds(200), _ => true, Ct);
        var end = Assert.IsType<Task<LaunchResult>>(silent.End, exactMatch: false);
        Assert.False(end.IsCompleted, "the program was not killed at the bound");
        silent.Close();
        Assert.NotEqual(0, (await end.WaitAsync(Hang, Ct)).ExitCode);
    }
}
