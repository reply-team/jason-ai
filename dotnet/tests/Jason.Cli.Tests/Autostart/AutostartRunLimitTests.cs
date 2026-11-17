using System.Diagnostics;
using Jason.Cli.Autostart;

namespace Jason.Cli.Tests.Autostart;

/// <summary>
/// A system tool waited for with a bound, as the uninstall waits for <c>launchctl unload</c>: one that does not answer in
/// time is ended and refused, so the caller goes on without its answer instead of waiting for ever.
/// </summary>
public sealed class AutostartRunLimitTests
{
    [Fact]
    public void A_command_that_outlives_its_bound_is_ended_and_refused()
    {
        IReadOnlyList<string> slow = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n", "60", "127.0.0.1"]
            : ["/bin/sleep", "60"];
        var watch = Stopwatch.StartNew();

        var refused = Assert.Throws<AutostartException>(() => AutostartRegistrars.Run(slow, TimeSpan.FromSeconds(1)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"the bound was not kept: {watch.Elapsed}");
        Assert.Contains("did not answer within 1 seconds", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_that_ends_within_its_bound_is_answered()
    {
        IReadOnlyList<string> quick = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "exit 3"]
            : ["/bin/sh", "-c", "exit 3"];

        var (exit, _, _) = AutostartRegistrars.Run(quick, TimeSpan.FromSeconds(30));

        Assert.Equal(3, exit);
    }
}
