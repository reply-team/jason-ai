using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Manifest;

/// <summary>
/// One program a plugin declares it needs. The name is what <c>host.exec</c> may start; the version command is
/// the only thing a reload will run on the user's machine, and only for an executable the user granted.
/// </summary>
public sealed record ExecutableRequest(string Name, string? MinVersion, IReadOnlyList<string>? VersionCommand);

public sealed record ExecRequestSpec(IReadOnlyList<ExecutableRequest> Executables);

/// <summary>
/// The hosts a plugin asks for. <c>AnyGrantedHost</c> asks for more: that a person may grant a host the manifest could not
/// know, the receiver of a plugin whose address is theirs. It names no host; what it opens is what a grant names.
/// </summary>
public sealed record HttpRequestSpec(IReadOnlyList<string> Hosts, bool AnyGrantedHost = false);

public sealed record EnvRequestSpec(IReadOnlyList<string> Variables);

/// <summary>The Runtime API families a handler of this plugin may be granted through its door (<c>host.call</c>).</summary>
public sealed record ApiRequestSpec(IReadOnlyList<string> Families);

/// <summary>
/// That a handler of this plugin may be granted the host's one file door (<c>host.appendLine</c>). It names no path: the
/// directories are a person's to name, in the grant.
/// </summary>
public sealed record FilesRequestSpec(bool Append);

/// <summary>What the manifest asks for. Asking is not being given it: the grants decide that, at every reload.</summary>
public sealed record CapabilityRequests(ExecRequestSpec? Exec, HttpRequestSpec? Http, EnvRequestSpec? Env, ApiRequestSpec? Api = null, FilesRequestSpec? Files = null);

/// <summary>
/// The contract versions a plugin speaks: the plugin-host protocol, the canonical operations (for a notification plugin,
/// the delivery contract), and the events contract, which only a provider that reads its accounts' events declares —
/// empty for every other.
/// </summary>
public sealed record ManifestContracts(IReadOnlyList<int> Protocol, IReadOnlyList<int> Operations)
{
    public IReadOnlyList<int> Events { get; init; } = [];
}

/// <summary>
/// The arguments a provider takes on some of an operation's channels, by operation, argument and channel: what the claim
/// reads before an argument a document names per channel reaches the plugin. An operation, argument or channel it does
/// not list is one the plugin does not take.
/// </summary>
public sealed record ManifestArguments(IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> ByOperation)
{
    /// <summary>A manifest that lists nothing: the plugin takes no argument per channel anywhere.</summary>
    public static ManifestArguments None { get; } = new(new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal));

    /// <summary>Whether the plugin takes <paramref name="argument"/> of <paramref name="operation"/> on <paramref name="channel"/>.</summary>
    public bool Takes(string operation, string argument, string channel) =>
        ByOperation.TryGetValue(operation, out var arguments)
        && arguments.TryGetValue(argument, out var channels)
        && channels.Contains(channel, StringComparer.Ordinal);
}

/// <summary>
/// The channels a provider writes an operation's message as a template on, by operation: what the claim reads before a
/// template reaches the plugin. An operation or channel it does not list is one the plugin writes no template on.
/// </summary>
public sealed record ManifestTemplates(IReadOnlyDictionary<string, IReadOnlyList<string>> ByOperation)
{
    /// <summary>A manifest that lists nothing: the plugin writes a template nowhere.</summary>
    public static ManifestTemplates None { get; } = new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>Whether the plugin writes the message of <paramref name="operation"/> as a template on <paramref name="channel"/>.</summary>
    public bool Writes(string operation, string channel) =>
        ByOperation.TryGetValue(operation, out var channels) && channels.Contains(channel, StringComparer.Ordinal);
}

/// <summary>
/// One annotation of a property of an audience's definition, beside the schema because the dialect refuses a keyword it
/// does not publish inside one: which side of a person the property filters on, and the filter whose values the
/// provider lists for it. The runtime reads neither; it answers them as declared.
/// </summary>
public sealed record DefinitionKey(string? Side, string? Typeahead);

/// <summary>
/// The schema a provider's audience definition satisfies, as <c>audience_definition</c> declares it, and the annotations
/// of its properties. The envelope of an audience operation is the contract's; the definition inside it is the
/// provider's, checked against this at the claim and at the preview.
/// </summary>
public sealed record AudienceDefinition(JsonObject Schema, IReadOnlyDictionary<string, DefinitionKey> Keys)
{
    /// <summary>The declaration as the manifest wrote it, a fresh copy each time: <c>{schema, keys}</c>.</summary>
    public JsonObject ToJson()
    {
        var keys = new JsonObject();
        foreach (var (name, key) in Keys)
        {
            var annotation = new JsonObject();
            if (key.Side is not null)
            {
                annotation["side"] = key.Side;
            }

            if (key.Typeahead is not null)
            {
                annotation["typeahead"] = key.Typeahead;
            }

            keys[name] = annotation;
        }

        return new JsonObject { ["schema"] = Schema.DeepClone(), ["keys"] = keys };
    }
}

/// <summary>What the plugin asks its invocations to run under; null means "whatever the installation decides".</summary>
public sealed record ManifestLimits(int? TimeoutMs, int? MemoryMb);

/// <summary>
/// A <c>plugin.yaml</c> that passed every rule, as the rest of the runtime reads it. <c>Binding</c> is the schema
/// a route to this plugin must satisfy — what the plugin needs to know about the installation before it can act,
/// such as which account to work in — and null when the plugin asks a route for nothing.
/// </summary>
public sealed record PluginManifest(
    string Id,
    string Version,
    PluginKind Kind,
    string? Name,
    string? Description,
    string? Homepage,
    ManifestContracts Contracts,
    IReadOnlyList<string> Operations,
    PluginEntry Entry,
    CapabilityRequests Capabilities,
    ManifestLimits Limits,
    JsonObject? Binding)
{
    /// <summary>The arguments the plugin takes per channel; none unless the manifest lists them.</summary>
    public ManifestArguments Arguments { get; init; } = ManifestArguments.None;

    /// <summary>The channels the plugin writes an operation's message as a template on; none unless the manifest lists them.</summary>
    public ManifestTemplates Templates { get; init; } = ManifestTemplates.None;

    /// <summary>
    /// The operations whose lost answer the plugin settles by the work item's key, as <c>settles_by_key</c> lists them;
    /// none unless the manifest lists them.
    /// </summary>
    public IReadOnlyList<string> KeyedOperations { get; init; } = [];

    /// <summary>
    /// Whether the plugin settles a lost answer of <paramref name="operation"/> by the work item's key: what an ambiguous
    /// end of an operation whose recovery read rests on the key needs before the runtime repeats it.
    /// </summary>
    public bool SettlesByKey(string operation) => KeyedOperations.Contains(operation, StringComparer.Ordinal);

    /// <summary>
    /// The journal kinds the plugin subscribes to, as <c>subscriptions</c> declares them, in the manifest's order; none
    /// unless the manifest lists them. Declaring a kind is not being given it: the grants decide that, at every load.
    /// </summary>
    public IReadOnlyList<string> Subscriptions { get; init; } = [];

    /// <summary>
    /// The schema an audience's definition satisfies for this plugin, as <c>audience_definition</c> declares it; null
    /// unless the manifest declares one, which it does exactly when it performs an operation that carries a definition.
    /// </summary>
    public AudienceDefinition? AudienceDefinition { get; init; }

    /// <summary>Whether this is a provider that reads its accounts' events, in a version of the events contract this build speaks.</summary>
    public bool ReadsEvents => Kind == PluginKind.Provider && Contracts.Events.Contains(EventsContract.CurrentVersion);
}

/// <summary>The ceilings of this installation, which a manifest may approach but never raise.</summary>
public sealed record ManifestBounds(int MaxTimeoutMs, int MaxMemoryMb);

/// <summary>
/// What one read made of a manifest. Every problem is reported, not just the first, so an author fixes the file
/// once; a manifest with any problem at all yields no manifest.
/// </summary>
public sealed record ManifestReadResult(PluginManifest? Manifest, IReadOnlyList<ManifestProblem> Problems)
{
    public bool IsValid => Manifest is not null && Problems.Count == 0;
}
