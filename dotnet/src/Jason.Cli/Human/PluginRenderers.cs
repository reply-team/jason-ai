using System.Globalization;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the plugin verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class PluginRenderers
{
    /// <summary>What a capability the plugin never asked for looks like: nothing to grant, nothing to count.</summary>
    private const string Absent = "-";

    /// <summary>How many operations fit a table cell before the rest becomes an ellipsis.</summary>
    private const int OperationsShown = 2;

    /// <summary>
    /// The active snapshot as a table; under it, what each plugin still needs (<see cref="Needs"/>); and under that the
    /// diagnostics of a load that was refused — the same problems the rejected reload answered with, so the question "why
    /// is it not there" is answered here too.
    /// </summary>
    /// <param name="json">The body <c>plugin.list</c> answered.</param>
    /// <param name="settingsFile">The settings file this CLI uses, which is where a grant is written.</param>
    public static string? Registry(string json, string settingsFile)
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
                Operations(plugin.Operations, plugin.Contracts?.Events),
                Grants(plugin.Capabilities),
                RenderText.Digest(plugin.Digest));
        }

        var lines = new List<string> { table.Render() };
        var needs = Needs(registry.Plugins, settingsFile).ToList();
        if (needs.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(needs);
        }

        if (registry.LastReload is { Activated: false } rejected)
        {
            lines.Add(string.Empty);
            lines.Add($"last reload rejected at {RenderText.Moment(rejected.At)}:");
            foreach (var candidate in rejected.Candidates ?? [])
            {
                foreach (var problem in candidate.Problems ?? [])
                {
                    lines.Add(RenderText.Shown($"{Location(candidate.Directory, problem.Path)}: {problem.Code} — {problem.Message}")!);
                }
            }

            // A route already names itself the way the operator wrote it, so there is no location to build.
            foreach (var route in rejected.Routes ?? [])
            {
                lines.Add(RenderText.Shown($"{route.Route}: {route.Code} — {route.Message}")!);
            }

            // And so does the order of a kind's subscribers: the setting is its location.
            foreach (var problem in rejected.Subscriptions ?? [])
            {
                lines.Add(RenderText.Shown($"{problem.Setting}: {problem.Code} — {problem.Message}")!);
            }
        }

        return RenderText.Lines(lines);
    }

    /// <summary>
    /// What <c>plugin grant</c> or <c>plugin revoke</c> left in force, said from the body the runtime answered, then what
    /// the reload activated as <see cref="Reload"/> says it, where one ran.
    /// </summary>
    public static string? Granted(string json, string id, string noun, string entry, bool revoke, string settingsFile)
    {
        var registry = RenderText.Read<PluginRegistryDto>(json);
        if (registry?.Plugins is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            revoke
                ? $"{id} is not granted the {noun} {entry} now; work already running keeps what it was started with."
                : $"{id} is granted the {noun} {entry}, in force now.",
        };
        if (Reload(json, settingsFile) is { } reloaded)
        {
            lines.Add(reloaded);
        }

        return RenderText.Lines(lines);
    }

    /// <summary>
    /// What the reload activated, and what each plugin still needs (<see cref="Needs"/>). A refused reload never reaches
    /// here: it is an API error, printed verbatim with exit code 1.
    /// </summary>
    /// <param name="json">The body <c>plugin.reload</c> answered.</param>
    /// <param name="settingsFile">The settings file this CLI uses, which is where a grant is written.</param>
    public static string? Reload(string json, string settingsFile)
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

        lines.AddRange(Needs(registry.Plugins, settingsFile));
        return RenderText.Lines(lines);
    }

    /// <summary>
    /// What each plugin that cannot do its work yet still needs, in the snapshot's order: for a plugin the machine cannot
    /// run, its problems (<see cref="Problems"/>); then, for it and for one that loaded <c>valid</c>, the capabilities it
    /// asks for and is granted nothing of (<see cref="Ungranted"/>), as <c>jason plugin install</c> says them. The runtime
    /// resolves a plugin's grants before its status, so they are as true of a plugin it holds back. A plugin granted what
    /// it asks for, and able to run, has no line.
    /// </summary>
    private static IEnumerable<string> Needs(IEnumerable<PluginDto> plugins, string settingsFile) =>
        plugins
            .SelectMany(plugin => plugin.Status switch
            {
                PluginStatus.Unavailable => new[] { Problems(plugin), Ungranted(plugin.Id, plugin.Capabilities, settingsFile) },
                PluginStatus.Valid => new[] { Ungranted(plugin.Id, plugin.Capabilities, settingsFile) },
                _ => Array.Empty<string?>(),
            })
            .OfType<string>();

    /// <summary>
    /// Each problem that makes a plugin unavailable, by its code and the runtime's message, never the code alone: the
    /// message is where the runtime says what to do. Null for a plugin with none. The message is the runtime's words about
    /// what a manifest names, so it is shown inert.
    /// </summary>
    public static string? Problems(PluginDto plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (plugin.Problems is not { Count: > 0 } problems || plugin.Status != PluginStatus.Unavailable)
        {
            return null;
        }

        return RenderText.Shown($"{plugin.Id} unavailable: {string.Join(" ", problems.Select(problem => $"{problem.Code} — {problem.Message}"))}");
    }

    /// <summary>
    /// What a loaded plugin asks for and is granted nothing of, as one sentence a person or an agent can act on: what it
    /// asks for, the key that grants it, the settings file, and the reload that reads it, in that order and per capability
    /// in the manifest's order. A capability granted part of what it asks has no sentence: it is granted something, and the
    /// table counts the rest. The CLI compares granted with requested, as the table's counts do, and reads no manifest.
    /// Null when there is nothing to say. What the manifest asks for is its author's text, so the sentence is shown inert.
    /// </summary>
    /// <param name="id">The plugin's id.</param>
    /// <param name="capabilities">Its capabilities, as the runtime answered them.</param>
    /// <param name="settingsFile">The settings file this CLI uses.</param>
    public static string? Ungranted(string id, PluginCapabilitiesDto? capabilities, string settingsFile)
    {
        if (capabilities is null)
        {
            return null;
        }

        var asked = Asked(capabilities);
        if (asked.Count == 0)
        {
            return null;
        }

        var grantedAnything = new[]
        {
            capabilities.Exec?.Granted, capabilities.Http?.Granted, capabilities.Env?.Granted, capabilities.Api?.Granted,
            capabilities.Subscriptions?.Granted, capabilities.Files?.Granted,
        }.Any(granted => granted is { Count: > 0 });

        var sentence = new System.Text.StringBuilder(grantedAnything
            ? $"{id} is loaded and granted only part of what it asks for."
            : $"{id} is loaded and granted nothing.");
        for (var at = 0; at < asked.Count; at++)
        {
            var (key, verb, names) = asked[at];
            var last = at == asked.Count - 1;
            sentence.Append(verb is null
                ? $" It asks for directories to write to; grant them under Plugins:Grants:{id}:{key} in {settingsFile}, each an absolute path.{(last ? " Then run `jason plugin reload`." : string.Empty)}"
                : $" It asks to {verb}: {string.Join(", ", names)}. Grant {(names.Count == 1 ? "it" : "them")} under Plugins:Grants:{id}:{key} in {settingsFile}{(last ? ", then run `jason plugin reload`." : ".")}");
        }

        return RenderText.Shown(sentence.ToString());
    }

    /// <summary>
    /// Each capability a plugin asks for and is granted nothing of, in the manifest's order: its key under
    /// <c>Plugins:Grants:&lt;id&gt;</c>, the verb that says it, and what it asks for. A capability granted part of what it
    /// asks for is not one of them. <c>Files</c> names nothing requested, since a manifest names no directory.
    /// </summary>
    internal static IReadOnlyList<(string Key, string? Verb, IReadOnlyList<string> Names)> Asked(PluginCapabilitiesDto capabilities)
    {
        var asked = new List<(string Key, string? Verb, IReadOnlyList<string> Names)>();
        void Ask(string key, string verb, IReadOnlyList<string>? requested, IReadOnlyList<string>? granted)
        {
            if (requested is { Count: > 0 } && granted is not { Count: > 0 })
            {
                asked.Add((key, verb, requested));
            }
        }

        Ask("Exec", "run", capabilities.Exec?.Requested?.Select(executable => executable.Name).ToList(), capabilities.Exec?.Granted);
        Ask("Http", "reach", capabilities.Http?.Requested, capabilities.Http?.Granted);
        Ask("Env", "read", capabilities.Env?.Requested, capabilities.Env?.Granted);
        Ask("Api", "call", capabilities.Api?.Requested, capabilities.Api?.Granted);
        Ask("Subscriptions", "be told of", capabilities.Subscriptions?.Requested, capabilities.Subscriptions?.Granted);

        // A manifest names no directory, so there is nothing requested to name: it asks for places, and the person names them.
        if (capabilities.Files is { } files && files.Granted is not { Count: > 0 })
        {
            asked.Add(("Files", null, []));
        }

        return asked;
    }

    /// <summary>
    /// The operations the plugin implements, two of them at most — the full list is in the JSON — and whether it reads
    /// its accounts' events, which is no operation a route carries.
    /// </summary>
    private static string? Operations(IReadOnlyList<string>? operations, IReadOnlyList<int>? events)
    {
        var listed = operations is not { Count: > 0 } ? null
            : operations.Count <= OperationsShown ? string.Join(", ", operations)
            : string.Join(", ", operations.Take(OperationsShown)) + " ...";
        return events is not { Count: > 0 } ? listed
            : listed is null ? "events"
            : listed + " + events";
    }

    /// <summary>Granted against requested for each capability: declaration is not permission, and it shows.</summary>
    private static string? Grants(PluginCapabilitiesDto? capabilities)
    {
        if (capabilities is null
            || (capabilities.Exec is null && capabilities.Http is null && capabilities.Env is null && capabilities.Api is null
                && capabilities.Subscriptions is null && capabilities.Files is null))
        {
            return null;
        }

        var exec = capabilities.Exec is null ? Absent : Counts(capabilities.Exec.Granted?.Count ?? 0, capabilities.Exec.Requested?.Count ?? 0);
        var http = capabilities.Http is null ? Absent : Counts(capabilities.Http.Granted?.Count ?? 0, capabilities.Http.Requested?.Count ?? 0);
        var env = capabilities.Env is null ? Absent : Counts(capabilities.Env.Granted?.Count ?? 0, capabilities.Env.Requested?.Count ?? 0);
        var subscriptions = capabilities.Subscriptions is null
            ? Absent
            : Counts(capabilities.Subscriptions.Granted?.Count ?? 0, capabilities.Subscriptions.Requested?.Count ?? 0);

        // The door is a handler's alone, so a plugin that asks for none keeps the line it always had.
        var line = $"exec {exec} | http {http} | env {env}";
        if (capabilities.Api is not null)
        {
            line += $" | api {Counts(capabilities.Api.Granted?.Count ?? 0, capabilities.Api.Requested?.Count ?? 0)}";
        }

        line += $" | subscriptions {subscriptions}";

        // A manifest names no place, so the directories granted stand alone, and a plugin that asks for none keeps its line.
        if (capabilities.Files is not null)
        {
            line += string.Create(CultureInfo.InvariantCulture, $" | files {capabilities.Files.Granted?.Count ?? 0}");
        }

        // A host the grant names beyond the declared ones counts as any grant does; the word that allows it stands beside them.
        return capabilities.AnyGrantedHost == true ? line + " | any_granted_host" : line;
    }

    private static string Counts(int granted, int requested) =>
        string.Create(CultureInfo.InvariantCulture, $"{granted}/{requested}");

    /// <summary>
    /// Where a problem is, the way the runtime's own error details spell it: the file is named exactly once. A
    /// field path (<c>kind</c>, <c>capabilities.http.hosts[1]</c>) gets the manifest in front of it; a YAML
    /// syntax error already carries <c>plugin.yaml#line:col</c> and is left alone.
    /// </summary>
    private static string Location(string directory, string path) =>
        path.StartsWith("plugin.yaml#", StringComparison.Ordinal) ? $"{directory}/{path}" : $"{directory}/plugin.yaml#{path}";
}
