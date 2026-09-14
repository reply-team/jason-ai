using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the work-item verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class WorkItemRenderers
{
    private const int Label = 14;

    /// <summary>
    /// One work item: what it is, where it stands and what it has tried. The context and the result are named
    /// rather than printed — the values themselves are in the JSON output.
    /// </summary>
    public static string? WorkItem(string json)
    {
        var item = RenderText.Read<WorkItemDto>(json);
        if (item?.Id is null || item.CampaignId is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Id:", item.Id),
            Line("Campaign:", item.CampaignId),
            Line("Contact:", item.ContactId),
            Line("Kind:", Kind(item.Kind, item.Role, item.Operation)),
            Line("Status:", RenderText.Snake(item.Status) + (item.Eligible ? " (eligible)" : string.Empty)),
            Line("Priority:", item.Priority.ToString(CultureInfo.InvariantCulture)),
            Line("Not before:", RenderText.Moment(item.NotBefore)),
            Line("Due:", RenderText.Moment(item.DueAt)),
            Line("Retry after:", RenderText.Moment(item.RetryAfter)),
            Line("Attempts:", Attempts(item.AttemptCount, item.CurrentAttemptId)),
            Line("Last error:", Error(item.LastError)),
            Line("Created:", RenderText.Moment(item.CreatedAt)),
            Line("Updated:", RenderText.Moment(item.UpdatedAt)),
            Line("Finished:", RenderText.Moment(item.FinishedAt)),
            Line("Context keys:", RenderText.Keys(item.Context)),
            Line("Result:", item.Result is null ? "none" : "present"),
        };

        var rendered = RenderText.Lines(lines);
        return item.Attempts is null ? rendered : rendered + Environment.NewLine + Environment.NewLine + AttemptTable(item.Attempts);
    }

    /// <summary>A page of work items, and the cursor that continues it.</summary>
    public static string? WorkItemList(string json)
    {
        var page = RenderText.Read<Page<WorkItemSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "CAMPAIGN", "KIND", "ROLE/OP", "STATUS", "ELIGIBLE", "PRIO", "ATTEMPTS", "CREATED");
        foreach (var item in page.Items)
        {
            table.Row(
                item.Id,
                item.CampaignId,
                RenderText.Snake(item.Kind),
                item.Role ?? item.Operation,
                RenderText.Snake(item.Status),
                item.Eligible ? "yes" : "no",
                item.Priority.ToString(CultureInfo.InvariantCulture),
                item.AttemptCount.ToString(CultureInfo.InvariantCulture),
                RenderText.Moment(item.CreatedAt));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>The answer to a heartbeat: one line, because that is all an executor needs to read in a loop.</summary>
    public static string? Heartbeat(string json)
    {
        var response = RenderText.Read<HeartbeatResponse>(json);
        if (response?.AttemptId is null)
        {
            return null;
        }

        var line = $"attempt {response.AttemptId} alive; lease until {RenderText.Moment(response.LockUntil)}";
        return response.HeartbeatDueBy is null ? line : line + $"; next heartbeat due by {RenderText.Moment(response.HeartbeatDueBy.Value)}";
    }

    private static string AttemptTable(IReadOnlyList<AttemptDto> attempts)
    {
        var table = new HumanTable("#", "ID", "STATUS", "CLAIMED", "FINISHED", "ERROR");
        foreach (var attempt in attempts)
        {
            table.Row(
                attempt.Number.ToString(CultureInfo.InvariantCulture),
                attempt.Id,
                RenderText.Snake(attempt.Status),
                RenderText.Moment(attempt.ClaimedAt),
                RenderText.Moment(attempt.FinishedAt),
                attempt.Error?.Code);
        }

        return table.Render();
    }

    private static string Kind(WorkItemKind kind, string? role, string? operation)
    {
        var name = RenderText.Snake(kind);
        if (role is not null)
        {
            return $"{name} (role {role})";
        }

        return operation is null ? name : $"{name} (operation {operation})";
    }

    private static string Attempts(int count, string? currentAttemptId)
    {
        var made = count.ToString(CultureInfo.InvariantCulture);
        return currentAttemptId is null ? made : $"{made}, current {currentAttemptId}";
    }

    private static string? Error(AttemptErrorDto? error) =>
        error?.Code is null ? null : error.Message is null ? error.Code : error.Code + " — " + error.Message;

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
