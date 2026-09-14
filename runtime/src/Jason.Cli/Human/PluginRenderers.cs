using System.Globalization;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the plugin verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class PluginRenderers
{
    private const string DigestPrefix = "sha256:";

    /// <summary>What a capability the plugin never asked for looks like: nothing to grant, nothing to count.</summary>
    private const string Absent = "-";

    /// <summary>How much of the digest a person needs to tell two packages apart at a glance.</summary>
    private const int DigestShown = 12;

    /// <summary>How many operations fit a table cell before the rest becomes an ellipsis.</summary>
    private const int OperationsShown = 2;

    /// <summary>
    /// The active snapshot as a table, and under it the diagnostics of a load that was refused — the same
    /// problems the rejected reload answered with, so the question "why is it not there" is answered here too.
    /// </summary>
    public static string? Registry(string json)
    {
        var registry = RenderText.Read<PluginRegistryDto>(json);
        if (registry?.Snapshot is null || registry.Plugins is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "VERSION", "KIND", "STATUS", "OPERATIONS", "GRANTS", "DIGEST");
        foreach (var plugin in registry.Plugins)
        {
            table.Row(
                plugin.Id,
                plugin.Version,
                RenderText.Snake(plugin.Kind),
                RenderText.Snake(plugin.Status),
                Operations(plugin.Operations),
                Grants(plugin.Capabilities),
                Digest(plugin.Digest));
        }

        var lines = new List<string> { table.Render() };
        if (registry.LastReload is { Activated: false } rejected)
        {
            lines.Add(string.Empty);
            lines.Add($"last reload rejected at {RenderText.Moment(rejected.At)}:");
            foreach (var candidate in rejected.Candidates ?? [])
            {
                foreach (var problem in candidate.Problems ?? [])
                {
                    lines.Add($"{candidate.Directory}/{problem.Path}: {problem.Code} — {problem.Message}");
                }
            }
        }

        return RenderText.Lines(lines);
    }

    /// <summary>
    /// What the reload activated, and which plugins the machine cannot run. A refused reload never reaches
    /// here: it is an API error, printed verbatim with exit code 1.
    /// </summary>
    public static string? Reload(string json)
    {
        var registry = RenderText.Read<PluginRegistryDto>(json);
        if (registry?.Snapshot?.Id is null || registry.Plugins is null || !registry.Activated)
        {
            return null;
        }

        var count = registry.Plugins.Count;
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"activated snapshot {registry.Snapshot.Id} with {count} {(count == 1 ? "plugin" : "plugins")}"),
        };

        foreach (var plugin in registry.Plugins.Where(plugin => plugin.Status == PluginStatus.Unavailable))
        {
            lines.Add($"{plugin.Id} unavailable: {string.Join(", ", (plugin.Problems ?? []).Select(problem => problem.Code))}");
        }

        return RenderText.Lines(lines);
    }

    /// <summary>The operations the plugin implements, two of them at most: the full list is in the JSON.</summary>
    private static string? Operations(IReadOnlyList<string>? operations)
    {
        if (operations is not { Count: > 0 })
        {
            return null;
        }

        return operations.Count <= OperationsShown
            ? string.Join(", ", operations)
            : string.Join(", ", operations.Take(OperationsShown)) + " …";
    }

    /// <summary>Granted against requested for each capability: declaration is not permission, and it shows.</summary>
    private static string? Grants(PluginCapabilitiesDto? capabilities)
    {
        if (capabilities is null || (capabilities.Exec is null && capabilities.Http is null && capabilities.Env is null))
        {
            return null;
        }

        var exec = capabilities.Exec is null ? Absent : Counts(capabilities.Exec.Granted?.Count ?? 0, capabilities.Exec.Requested?.Count ?? 0);
        var http = capabilities.Http is null ? Absent : Counts(capabilities.Http.Granted?.Count ?? 0, capabilities.Http.Requested?.Count ?? 0);
        var env = capabilities.Env is null ? Absent : Counts(capabilities.Env.Granted?.Count ?? 0, capabilities.Env.Requested?.Count ?? 0);

        return $"exec {exec} · http {http} · env {env}";
    }

    private static string Counts(int granted, int requested) =>
        string.Create(CultureInfo.InvariantCulture, $"{granted}/{requested}");

    /// <summary>The head of the content digest, which is what people compare; the whole value is in the JSON.</summary>
    private static string? Digest(string? digest)
    {
        if (string.IsNullOrEmpty(digest))
        {
            return null;
        }

        var hex = digest.StartsWith(DigestPrefix, StringComparison.Ordinal) ? digest[DigestPrefix.Length..] : digest;
        return hex.Length <= DigestShown ? hex : hex[..DigestShown];
    }
}
