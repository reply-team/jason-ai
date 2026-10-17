using System.Globalization;
using System.Text.RegularExpressions;
using Jason.PluginHost.Sdk;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// The plugin page publishes the kill grace a plugin author plans around, and its examples are the host's own
/// arithmetic: the numbers on the page are computed here from the constant and the function the host runs, so the
/// page and the host cannot come to say different things.
/// </summary>
public sealed partial class KillGracePageTests
{
    [Fact]
    public void The_page_states_the_kill_grace_and_its_examples_as_the_host_computes_them()
    {
        var page = Flat(Source("docs/plugins.md"));
        var (twenty, twentyGrace) = ExecService.Budget(TimeSpan.FromSeconds(20), null);
        var (four, fourGrace) = ExecService.Budget(TimeSpan.FromSeconds(4), null);

        Assert.Contains(
            $"A call's **kill grace** is the smaller of {Seconds(ExecService.KillGraceCeiling)} and half of what is left of the budget when the call is made — "
            + $"with 20 s left, a call without `timeout_ms` runs for {Seconds(twenty)} and keeps a grace of {Seconds(twentyGrace)}; "
            + $"with 4 s left, {Seconds(four)} and {Seconds(fourGrace)}.",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_exec_options_table_says_the_default_keeps_the_grace_back()
    {
        var page = Source("docs/plugins.md");

        Assert.Contains(
            "| `timeout_ms` | 1 .. the invocation's `timeout_ms`, then clamped to what is left of the budget; default: what is left, less the kill grace |",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_s_own_messages_include_the_unconfirmed_kill()
    {
        var page = Flat(Source("docs/plugins.md"));
        var list = OwnMessages().Match(page);
        Assert.True(list.Success, "docs/plugins.md no longer has its \"The host's own messages are …\" sentence.");

        Assert.Contains("`exec_kill_unconfirmed`", list.Value, StringComparison.Ordinal);
        Assert.Contains("an `exec_kill_unconfirmed` line with its process id and the grace in milliseconds", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page says what a plugin author meets when a kill outlives its call: the call answers in time, the host waits for
    /// the kill before it exits, and past the invocation's deadline the line the host writes and the code the invocation
    /// ends with.
    /// </summary>
    [Fact]
    public void The_page_says_the_host_waits_for_a_kill_and_names_the_unfinished_kill()
    {
        var page = Flat(Source("docs/plugins.md"));
        var list = OwnMessages().Match(page);
        Assert.True(list.Success, "docs/plugins.md no longer has its \"The host's own messages are …\" sentence.");

        Assert.Contains($"`{UnfinishedKills.Message}`", list.Value, StringComparison.Ordinal);
        Assert.Contains(
            "A kill that is itself slow does not hold the call past its grace, and the host does not exit before a kill it started has ended: "
            + $"one still running at the invocation's deadline is said once, as an `{UnfinishedKills.Message}` line with its process id and the deadline, "
            + "and the host goes on waiting; should the runtime's own kill come first, it ends the host with the program, "
            + "and the invocation ends `plugin_timeout` rather than with your answer.",
            page,
            StringComparison.Ordinal);
    }

    private static string Seconds(TimeSpan span) => string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0.###} s");

    [GeneratedRegex(@"The host's own messages are [^.]+\.")]
    private static partial Regex OwnMessages();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
