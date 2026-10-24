using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The work-execution contract's account of a waiting item, held to the code: the moves the transition table allows, what
/// counts as an item that has ended, and the bounds of a wait with the codes it is refused with. A page that drifted from
/// any of them would teach a role's author a wait the runtime does not keep.
/// </summary>
public partial class WaitingDocTests
{
    [Fact]
    public void The_lifecycle_names_every_move_into_and_out_of_waiting()
    {
        var page = Flattened(Read("work-execution.md"));

        Assert.Contains("processing ──► waiting ──► created", page, StringComparison.Ordinal);
        Assert.Contains("| waiting | expired ──► cancelled", page, StringComparison.Ordinal);
        Assert.Contains("| waiting ──► expired", page, StringComparison.Ordinal);
        Assert.Equal(
            [WorkItemStatus.Created, WorkItemStatus.Cancelled, WorkItemStatus.Expired],
            Enum.GetValues<WorkItemStatus>().Where(to => WorkItemTransitions.IsLegal(WorkItemStatus.Waiting, to)));
    }

    [Fact]
    public void The_resume_step_names_the_statuses_that_end_an_item_as_the_runtime_counts_them()
    {
        var page = Flattened(Read("work-execution.md"));
        var ended = string.Join(", ", WorkItemTransitions.Terminal.Order().Select(s => $"`{SnakeCaseEnumConverter<WorkItemStatus>.Format(s)}`"));

        Assert.Contains($"every item it named has ended — {ended.Replace(", `expired`", " or `expired`", StringComparison.Ordinal)};", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_that_bound_a_wait_are_the_ones_the_runtime_enforces()
    {
        var page = Flattened(Read("work-execution.md"));
        var defaults = new DispatcherOptions().AiRole;

        Assert.Contains(
            $"| `Dispatcher:AiRole:MaxWaitSeconds` | `{defaults.MaxWaitSeconds}` | {AiRoleDefaults.MinimumWaitSeconds}..{AiRoleDefaults.MaximumWaitSeconds} |",
            page,
            StringComparison.Ordinal);
        Assert.Contains($"| `Dispatcher:AiRole:MaxWaits` | `{defaults.MaxWaits}` | 1..{AiRoleDefaults.MaximumWaits} |", page, StringComparison.Ordinal);

        // Each bound is held where the wait is written, and the page names the code it is refused with.
        var text = Read("work-execution.md");
        Assert.Contains($"`{DomainErrors.WaitTooLong(defaults.MaxWaitSeconds).Code}`", Row(text, "Dispatcher:AiRole:MaxWaitSeconds"), StringComparison.Ordinal);
        Assert.Contains($"`{DomainErrors.WaitLimitReached("wi_x", defaults.MaxWaits).Code}`", Row(text, "Dispatcher:AiRole:MaxWaits"), StringComparison.Ordinal);
        Assert.Contains(
            $"a wait on more than {WorkItemWaitRequest.MaxWaitItems} items with `{DomainErrors.WaitTargetInvalid(WorkItemWaitRequest.MaxWaitItems + 1).Code}`",
            page,
            StringComparison.Ordinal);
    }

    private static string Row(string text, string setting) =>
        Assert.Single(text.Split('\n'), line => line.StartsWith($"| `{setting}` |", StringComparison.Ordinal));

    private static string Read(string page) => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", page));

    private static string Flattened(string text) => Whitespace().Replace(text, " ");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
