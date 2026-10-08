using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A real <c>jason runtime start</c> for a test whose subject is not the start: the runtime has to be up, and how long a
/// loaded machine took to bring it up is not what the test is about.
/// </summary>
/// <remarks>
/// <para>
/// The verb waits 30 s for the runtime it launched to publish and answer. When that wait runs out with the runtime still
/// running it says so — "it may still be starting" — and exits 1, retryable. Runs of the whole suite have held a real start
/// past those 30 s, and once past the 60 s a test gave the verb's own process, with nothing wrong with the runtime. So the
/// verb's process is given <see cref="DefaultPatience"/>, and that one answer is followed the way a person would follow
/// it: wait for the descriptor and for <c>jason runtime status</c> to answer, within the same patience. Any other answer is
/// handed back as it came, for the caller's own assertion to fail on.
/// </para>
/// <para>
/// A start that needed patience says so in the test's output, with how long it took, so a start creeping past its
/// documented wait shows in the logs without failing a run. The one start held to the documented wait is the walkthrough's,
/// <see cref="GoldenPath.StartAsDocumentedAsync"/>, which is where such a creep should still fail.
/// </para>
/// </remarks>
internal static class PatientStart
{
    /// <summary>What the verb promises: its own wait for the runtime it launched to publish and answer.</summary>
    public static TimeSpan Documented { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The suite's own bound for a slow step.</summary>
    public static TimeSpan DefaultPatience => GoldenPath.Patience;

    private static TimeSpan Poll { get; } = TimeSpan.FromMilliseconds(200);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Starts the runtime, following "it may still be starting" and nothing else.</summary>
    /// <param name="paths">Where the runtime this starts publishes its descriptor.</param>
    /// <param name="start">The caller's own <c>jason runtime start</c>, given how long its process may take.</param>
    /// <param name="status">The caller's own <c>jason runtime status</c>.</param>
    /// <param name="read">The exit code and standard output of one of the caller's results.</param>
    /// <param name="patience">How long the whole start may take: <see cref="DefaultPatience"/> outside this helper's own tests.</param>
    /// <param name="say">Where a start that needed patience says so: the test's output outside this helper's own tests.</param>
    /// <returns>The start's own answer, or, for a runtime that was still starting, the status it then answered with.</returns>
    public static async Task<T> RunAsync<T>(
        JasonPaths paths,
        Func<TimeSpan, Task<T>> start,
        Func<Task<T>> status,
        Func<T, (int ExitCode, string Output)> read,
        TimeSpan? patience = null,
        Action<string>? say = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(read);
        var within = patience ?? DefaultPatience;
        say ??= line => TestContext.Current.TestOutputHelper?.WriteLine(line);

        var clock = Stopwatch.StartNew();
        var started = await start(within);
        var (exit, output) = read(started);
        if (exit != ExitCodes.ApiError || !StillStarting(output))
        {
            return started;
        }

        while (clock.Elapsed < within)
        {
            if (GoldenPath.ReadDescriptor(paths) is not null)
            {
                var answered = await status();
                if (read(answered).ExitCode == ExitCodes.Success)
                {
                    say(string.Create(
                        CultureInfo.InvariantCulture,
                        $"jason runtime start: the runtime answered after {clock.Elapsed.TotalSeconds:F1} s; the verb itself gave up at its documented {Documented.TotalSeconds:F0} s and said it may still be starting."));
                    return answered;
                }
            }

            await Task.Delay(Poll, Ct);
        }

        return started;
    }

    /// <summary>Whether the verb's answer is its own "it may still be starting": a start failure it marks as worth asking again.</summary>
    private static bool StillStarting(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var code)
                && code.GetString() == CliErrors.RuntimeStartFailed
                && error.TryGetProperty("retryable", out var retryable)
                && retryable.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
