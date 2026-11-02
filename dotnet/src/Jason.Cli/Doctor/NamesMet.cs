using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Skills;

namespace Jason.Cli.Doctor;

/// <summary>
/// The names of plugins, roles and skill packs a bundle met while it was composed: in the answers it and the status
/// checks read, in the sections before they are written, in the plugins directory and in the records of the skills it
/// deployed. Every one of them is a candidate for an alias; <see cref="NameAliases"/> decides which are the person's.
/// </summary>
internal sealed partial class NamesMet
{
    private readonly Dictionary<AliasKind, HashSet<string>> _names = Enum.GetValues<AliasKind>()
        .ToDictionary(kind => kind, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _refs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The skill sources of the person's own the records name: every source but the public repository's own address.</summary>
    public IReadOnlyCollection<string> SkillSources => _sources;

    /// <summary>
    /// The refs those sources were taken at, unless a ref is a release tag, which says nothing of anybody, or the sentence
    /// a record holds for a directory read where it stands, which is Jason's own.
    /// </summary>
    public IReadOnlyCollection<string> SkillRefs => _refs;

    /// <summary>The names met, by kind.</summary>
    public IReadOnlyDictionary<AliasKind, IEnumerable<string>> All =>
        _names.ToDictionary(entry => entry.Key, entry => (IEnumerable<string>)[.. entry.Value]);

    public void Add(AliasKind kind, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _names[kind].Add(name);
        }
    }

    public void AddAll(AliasKind kind, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            Add(kind, name);
        }
    }

    /// <summary>
    /// The names an answer the status checks read holds: the roster's roles, the registry's plugins and the plugin each
    /// route names, and nothing else of the routes. The answer was buffered by the latch every request passes, so reading
    /// it here leaves it to be read again by whoever asked.
    /// </summary>
    public async Task ReadAsync(string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        JsonNode? answer;
        try
        {
            answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return;
        }

        if (path.EndsWith(Operations.Route(Operations.RoleList), StringComparison.Ordinal) && answer?["items"] is JsonArray roles)
        {
            AddAll(AliasKind.Role, roles.OfType<JsonObject>().Select(role => Text(role["name"])).OfType<string>());
        }
        else if (path.EndsWith(Operations.Route(Operations.PluginList), StringComparison.Ordinal) && answer?["plugins"] is JsonArray plugins)
        {
            AddAll(AliasKind.Plugin, plugins.OfType<JsonObject>().Select(plugin => Text(plugin["id"])).OfType<string>());
        }
        else if (path.EndsWith(Operations.Route(Operations.RouteList), StringComparison.Ordinal))
        {
            // Not walked: a route's binding holds whatever keys the plugin's binding names, and the rest of the answer is
            // ids, so of the whole answer only the plugin each route names is a name.
            AddAll(AliasKind.Plugin, Routes(answer).Select(route => Text(route["plugin_id"])).OfType<string>());
        }
        else if (answer is not null)
        {
            Walk(answer);
        }
    }

    /// <summary>
    /// The names a section holds before it is written: a plugin wherever a <c>plugin_id</c> or a plugin channel names
    /// one, the runtime's plugins and the packages its release carries, a role wherever a <c>role</c> names one, and in a
    /// log event a plugin wherever the runtime's lines name one (<see cref="LoggedNames"/>).
    /// </summary>
    public void Read(JsonNode? section)
    {
        if (section is not JsonObject members)
        {
            return;
        }

        if (members["plugins"] is JsonArray registry)
        {
            AddAll(AliasKind.Plugin, registry.OfType<JsonObject>().Select(plugin => Text(plugin["id"])).OfType<string>());
        }

        if (members["system_info"] is JsonObject info)
        {
            if (info["released_plugins"] is JsonArray released)
            {
                AddAll(AliasKind.Plugin, released.OfType<JsonObject>().Select(plugin => Text(plugin["id"])).OfType<string>());
            }

            if (info["notifications"]?["channels"] is JsonArray channels)
            {
                AddAll(AliasKind.Plugin, channels.OfType<JsonObject>().Where(channel => Text(channel["kind"]) == "plugin").Select(channel => Text(channel["channel"])).OfType<string>());
            }
        }

        Walk(section);
    }

    /// <summary>
    /// The packs the skills records in these roots name, and the sources of the person's own they came from. A record that
    /// cannot be read names nothing here.
    /// </summary>
    public void ReadPacks(IEnumerable<string> roots)
    {
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var packs = SkillsRecord.Read(root)?.Packs ?? [];
                AddAll(AliasKind.SkillPack, packs.Select(pack => pack.Pack));
                foreach (var pack in packs.Where(pack => !string.IsNullOrWhiteSpace(pack.Source) && !IsPublicRepository(pack.Source)))
                {
                    _sources.Add(pack.Source);
                    if (!string.IsNullOrWhiteSpace(pack.Ref) && !ReleaseTag().IsMatch(pack.Ref) && pack.Ref != SkillsSource.DirectoryRef)
                    {
                        _refs.Add(pack.Ref);
                    }
                }
            }
            catch (Exception unreadable) when (unreadable is SkillsRecordUnreadable or IOException or UnauthorizedAccessException or SecurityException)
            {
                // The status check says the record cannot be read; it names no pack, so there is none to write as an alias.
            }
        }
    }

    /// <summary>Every route of a <c>route.list</c> answer: the global default and operations, then each campaign's.</summary>
    private static IEnumerable<JsonObject> Routes(JsonNode? answer)
    {
        JsonNode?[] sets = [answer?["global"], .. answer?["campaigns"] is JsonArray campaigns ? campaigns : []];
        foreach (var set in sets.OfType<JsonObject>())
        {
            if (set["default"] is JsonObject fallback)
            {
                yield return fallback;
            }

            if (set["operations"] is JsonObject operations)
            {
                foreach (var route in operations.Select(operation => operation.Value).OfType<JsonObject>())
                {
                    yield return route;
                }
            }
        }
    }

    /// <summary>The public repository's own address, with or without <c>.git</c> and a trailing slash, in any case.</summary>
    private static bool IsPublicRepository(string source)
    {
        var address = source.Trim().TrimEnd('/');
        address = address.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? address[..^4] : address;
        return string.Equals(address, PublicRepository.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A release's tag: <c>v</c>, a version, and a pre-release after a dash where there is one.</summary>
    [GeneratedRegex(@"^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTag();

    /// <summary>
    /// Every <c>plugin_id</c>, every <c>role</c> and, in a log event, every plugin the runtime's line names. A route's
    /// binding, wherever a section carries one, is not read: its keys are whatever the plugin's binding names, and a key
    /// called <c>role</c> there names no role.
    /// </summary>
    private void Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                if (Text(members[LogTemplateKey]) is { } template && LoggedNames.PluginTemplates.TryGetValue(template, out var property)
                    && Text(members[property]) is { } named && LoggedNames.NamesAPlugin(template, named))
                {
                    Add(AliasKind.Plugin, named);
                }

                foreach (var (key, value) in members)
                {
                    if (key is "plugin_id" or LoggedNames.PluginIdProperty)
                    {
                        Add(AliasKind.Plugin, Text(value));
                    }
                    else if (key == "role")
                    {
                        Add(AliasKind.Role, Text(value));
                    }

                    if (key != Binding)
                    {
                        Walk(value);
                    }
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    Walk(item);
                }

                break;
        }
    }

    /// <summary>Where a line of the log carries its message template.</summary>
    private const string LogTemplateKey = "@mt";

    /// <summary>A route's binding, whose keys are the plugin's.</summary>
    private const string Binding = "binding";

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
