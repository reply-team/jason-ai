using System.Globalization;
using System.Text.Json.Nodes;
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
            Line("Input:", Input(item.Context)),
            Line("Result:", item.Result is null ? "none" : "present"),
        };

        var rendered = RenderText.Lines(lines);
        if (item.Attempts is null)
        {
            return rendered;
        }

        return RenderText.Lines(
            [rendered, string.Empty, AttemptTable(item.Attempts), .. Provenance(item.Attempts), .. ExternalReports(item.ExternalReports)]);
    }

    /// <summary>
    /// Effects somebody performed outside Jason, under the attempts and never mixed into them. The two sit on
    /// one page because a person working out what happened to a contact needs both, and they are kept apart
    /// because only one of them is something this runtime did. An item nobody has reported anything about
    /// prints no table at all rather than an empty one.
    /// </summary>
    private static IEnumerable<string> ExternalReports(IReadOnlyList<ReportSummaryDto>? reports)
    {
        if (reports is not { Count: > 0 })
        {
            yield break;
        }

        var table = new HumanTable("RECEIVED", "EFFECT", "REPORTER", "TOOL", "ID");
        foreach (var report in reports)
        {
            table.Row(
                RenderText.Moment(report.ReceivedAt),
                report.Effect,
                RenderText.Actor(report.Reporter),
                report.Tool,
                report.Id);
        }

        yield return string.Empty;
        yield return ReportRenderers.Heading;
        yield return string.Empty;
        yield return table.Render();
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

    /// <summary>
    /// The arguments of a provider operation, named rather than printed like the context around them. They earn
    /// their own line because they are the half of the context a plugin will actually be given.
    /// </summary>
    private static string? Input(JsonObject? context) => context?["input"] switch
    {
        null => null,
        JsonObject arguments => RenderText.Keys(arguments),
        _ => "present",
    };

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

    /// <summary>
    /// What ran each attempt that has a record of it: one block per provider attempt, and nothing at all for an
    /// agent attempt, which has no provenance and must print exactly what it printed before.
    /// </summary>
    private static IReadOnlyList<string> Provenance(IReadOnlyList<AttemptDto> attempts)
    {
        var lines = new List<string>();
        foreach (var attempt in attempts.Where(attempt => attempt.Provenance is not null))
        {
            var ran = attempt.Provenance!;
            lines.Add(string.Empty);
            lines.Add($"ATTEMPT {attempt.Number.ToString(CultureInfo.InvariantCulture)} ({attempt.Id}) RAN");
            lines.Add(Line("Plugin:", Plugin(ran)));
            lines.Add(Line("Operation:", Operation(ran.Operation, ran.OperationVersion)));
            lines.Add(Line("Route:", Route(ran)));
            lines.Add(Line("Snapshots:", Snapshots(ran)));
            lines.Add(Line("Invocation:", Invocation(ran)));
        }

        return lines;
    }

    /// <summary>The package, as it was when it was chosen: a route that named nothing renders nothing.</summary>
    private static string? Plugin(AttemptProvenanceDto ran) => ran.PluginId is null
        ? null
        : Parts(ran.PluginId + (ran.PluginVersion is null ? string.Empty : " " + ran.PluginVersion), RenderText.Digest(ran.PluginDigest));

    private static string? Operation(string? operation, int? version) => operation is null
        ? null
        : version is null ? operation : operation + " v" + version.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Which level chose the plugin, and which account of it — by identity, never by value.</summary>
    private static string? Route(AttemptProvenanceDto ran) => Parts(
        ran.RouteScope is { } scope ? RenderText.Snake(scope) : null,
        RenderText.Digest(ran.BindingIdentity) is { } binding ? "binding " + binding : null);

    private static string? Snapshots(AttemptProvenanceDto ran) => Parts(
        ran.PluginSnapshotId is null ? null : "plugins " + ran.PluginSnapshotId,
        ran.RoutingSnapshotId is null ? null : "routes " + ran.RoutingSnapshotId);

    /// <summary>The invocation and what it cost, which is the line an operator reads when something was slow.</summary>
    private static string? Invocation(AttemptProvenanceDto ran) => Parts(
        ran.InvocationId,
        ran.Diagnostics is { } cost
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{cost.DurationMs} ms · exec {cost.ExecCalls} · http {cost.HttpCalls} · log {cost.LogLines}")
            : null);

    private static string? Parts(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrEmpty(part)).ToList();
        return present.Count == 0 ? null : string.Join(" · ", present);
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
