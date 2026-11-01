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

public sealed record HttpRequestSpec(IReadOnlyList<string> Hosts);

public sealed record EnvRequestSpec(IReadOnlyList<string> Variables);

/// <summary>What the manifest asks for. Asking is not being given it: the grants decide that, at every reload.</summary>
public sealed record CapabilityRequests(ExecRequestSpec? Exec, HttpRequestSpec? Http, EnvRequestSpec? Env);

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
