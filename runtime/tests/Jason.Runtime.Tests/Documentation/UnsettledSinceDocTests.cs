using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A later attempt of a provider operation is told since when an earlier one may have acted unsettled, and three pages
/// say so: the work-execution contract, which says which attempts count; the plugin guide, where the context a plugin
/// receives is listed and where a repeat under a key is explained; and the contracts README, whose field table says a
/// recovery read that rests on something the provider keeps for a while is bounded by that while.
/// </summary>
public sealed partial class UnsettledSinceDocTests
{
    [Fact]
    public void The_work_execution_contract_says_which_attempts_leave_an_item_unsettled()
    {
        var section = Section(Flat(Source("docs/work-execution.md")), "## Provider operations", "## Provider blocks");

        Assert.Contains(
            "**A later attempt is told since when an earlier one is unsettled.** The plugin's `context` carries `unsettled_since`: the moment the earliest earlier attempt of the item that may have acted was claimed. It is absent where none did, and a plugin reads absent and null alike.",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            "An attempt may have acted when it ended `ambiguous`: the plugin said so, or no answer of the protocol came back — a timeout, a crash, a host failure, a lease that ran out or a restart. A provider's own answer of any other class settled its attempt.",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            "It is never in the composed input, so what a person approved does not change with it.",
            section,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_work_execution_contract_says_which_send_a_later_attempt_is_told_of()
    {
        var section = Section(Flat(Source("docs/work-execution.md")), "## Provider operations", "## Provider blocks");

        Assert.Contains(
            "**A later attempt is told which send an earlier one may have made.** Where the operation declares a `send` identifier for the work item itself, the plugin's `context` also carries `unsettled_reference`: the reference the latest earlier attempt that may have acted returned for it, at or after `unsettled_since`. It is absent where none returned one, and a plugin reads absent and null alike.",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            "The claim reads it from the identifiers each attempt's provenance kept as returned, and it is never in the composed input either. It is a provider identifier and nothing else.",
            section,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_plugin_guide_lists_the_send_in_the_context_and_reads_it_before_a_repeat()
    {
        var guide = Flat(Source("docs/plugins.md"));
        var context = Section(guide, "**`context`** is the third argument:", "## 6. The Host SDK");
        var repeating = Section(guide, "### Repeating after a lost answer", "### What to return");

        Assert.DoesNotContain("\"unsettled_reference\"", context, StringComparison.Ordinal);
        Assert.Contains(
            "`unsettled_reference` is there beside it where such an attempt returned the work item's own `send` identifier: the reference the latest one returned. It is absent where none did; read absent and `null` alike.",
            context,
            StringComparison.Ordinal);
        Assert.Contains(
            "Where `context.unsettled_reference` names the send an earlier attempt returned, read that send first, inside the window and past it: a read never makes a send twice, and what the provider says became of the send answers the work item without the key. A read the provider answers as another account's send is not settled either. Only where the read finds nothing, or gets no answer, does the window decide.",
            repeating,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_plugin_guide_lists_the_moment_in_the_context_and_bounds_a_repeat_under_a_key_by_it()
    {
        var guide = Flat(Source("docs/plugins.md"));
        var context = Section(guide, "**`context`** is the third argument:", "## 6. The Host SDK");
        var repeating = Section(guide, "### Repeating after a lost answer", "### What to return");
        var envelope = Section(guide, "\"operation_contract_version\": 1,", "\"grants\":");

        Assert.DoesNotContain("\"unsettled_since\"", context, StringComparison.Ordinal);
        Assert.Contains(
            "`unsettled_since` is there only on a later attempt of a provider operation after one that may have acted: the moment the earliest such attempt of the work item was claimed, in UTC. It is absent where none did; read absent and `null` alike.",
            context,
            StringComparison.Ordinal);
        Assert.Contains(
            "A provider keeps such a key for a bounded time only, so count that time from `context.unsettled_since`: a repeat after the provider's window from it is not settled either.",
            repeating,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"unsettled_since\"", envelope, StringComparison.Ordinal);
    }

    [Fact]
    public void The_contracts_readme_bounds_a_recovery_read_by_the_provider_s_own_window()
    {
        var table = Section(Flat(Source("docs/contracts/README.md")), "| `recovery_read` |", "| `repeat_after_ambiguous` |");

        Assert.Contains(
            "a reading that rests on something the provider keeps for a while, such as an idempotency key, is bounded by the provider's own window, counted from `context.unsettled_since`, the moment the earliest attempt that may have acted was claimed",
            table,
            StringComparison.Ordinal);
    }

    private static string Section(string text, string heading, string next)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page has no '{heading}' section.");
        var end = text.IndexOf(next, start + heading.Length, StringComparison.Ordinal);
        return end > start ? text[start..end] : text[start..];
    }

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
