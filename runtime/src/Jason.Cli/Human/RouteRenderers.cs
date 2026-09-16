using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the route verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class RouteRenderers
{
    private const int Label = 12;

    /// <summary>
    /// How a route with no operation is shown in the operation column. Operations are dotted names, so nothing
    /// published can be mistaken for it, and the brackets say it is a fallback rather than a name.
    /// </summary>
    private const string DefaultRoute = "(default)";

    /// <summary>
    /// One resolution: the package a run would reach, the level that decided it, and — when the route cannot be
    /// run — every problem between the two. The verdict alone would send an operator looking; the problems say
    /// where to look.
    /// </summary>
    public static string? Resolution(string json)
    {
        var resolution = RenderText.Read<RouteResolutionDto>(json);
        if (resolution?.Operation is null || resolution.CampaignId is null || resolution.RoutingSnapshotId is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Operation:", string.Create(CultureInfo.InvariantCulture, $"{resolution.Operation} v{resolution.OperationVersion}")),
            Line("Campaign:", resolution.CampaignId),
            Line("Plugin:", Plugin(resolution)),
            Line("Scope:", resolution.Scope is null ? null : RenderText.Snake(resolution.Scope.Value)),
            Line("Binding:", RenderText.Compact(resolution.Binding)),
            Line("Snapshots:", $"routes {resolution.RoutingSnapshotId} · plugins {resolution.PluginSnapshotId}"),
            Line("Usable:", resolution.Usable ? "yes" : "no"),
        };

        if (resolution.Problems is { Count: > 0 } problems)
        {
            lines.Add("problems:");
            lines.AddRange(problems.Select(problem => $"{problem.Field}: {problem.Code} — {problem.Message}"));
        }

        return RenderText.Lines(lines);
    }

    /// <summary>
    /// Every route in one table, most general last, under the two snapshot ids it was frozen with. The same
    /// rendering answers a write, because what an operator wants to see after moving a route is where all of
    /// them now point.
    /// </summary>
    public static string? Routes(string json)
    {
        var routes = RenderText.Read<RoutesDto>(json);
        if (routes?.RoutingSnapshotId is null || routes.Global is null || routes.Campaigns is null)
        {
            return null;
        }

        var table = new HumanTable("SCOPE", "CAMPAIGN", "OPERATION", "PLUGIN", "BINDING");
        var rows = 0;
        rows += Rows(table, "global", campaign: null, routes.Global.Default, routes.Global.Operations);
        foreach (var campaign in routes.Campaigns)
        {
            rows += Rows(table, "campaign", campaign.CampaignId, campaign.Default, campaign.Operations);
        }

        var snapshots = $"snapshot {routes.RoutingSnapshotId} over plugins {routes.PluginSnapshotId}";
        return rows == 0
            ? RenderText.Lines(["no routes: nothing is sent to a plugin yet", snapshots])
            : RenderText.Lines([table.Render(), string.Empty, snapshots]);
    }

    private static int Rows(HumanTable table, string scope, string? campaign, RouteDto? fallback, IReadOnlyDictionary<string, RouteDto>? operations)
    {
        var rows = 0;
        if (fallback is not null)
        {
            table.Row(scope, campaign, DefaultRoute, fallback.PluginId, RenderText.Compact(fallback.Binding));
            rows++;
        }

        foreach (var (operation, route) in operations ?? new Dictionary<string, RouteDto>(StringComparer.Ordinal))
        {
            table.Row(scope, campaign, operation, route.PluginId, RenderText.Compact(route.Binding));
            rows++;
        }

        return rows;
    }

    /// <summary>The package as a person compares packages: which one, which version, and what state it is in.</summary>
    private static string? Plugin(RouteResolutionDto resolution)
    {
        if (resolution.PluginId is null)
        {
            return null;
        }

        var status = resolution.PluginStatus is null ? "not loaded" : RenderText.Snake(resolution.PluginStatus.Value);
        return $"{resolution.PluginId} {resolution.PluginVersion ?? "-"} · {status}";
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
