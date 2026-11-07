using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Plugins;

/// <summary>The module of a package and the function exported from it that one invocation calls.</summary>
public sealed record PluginEntry(string Module, string Function);

/// <summary>
/// Which package the child is about to run, as the runtime resolved it. The digest is the identity the child
/// re-computes before it runs a line of JavaScript; a hand-written envelope may leave it null, and the child
/// then says on stderr that it ran unverified code.
/// </summary>
public sealed record InvocationPlugin(string Id, string Version, PluginKind Kind, string Root, string? Digest, PluginEntry Entry);

/// <summary>
/// What the plugin is allowed to know about why it is running. Opaque to the runtime, non-secret by contract,
/// and the only place a caller can pass something of its own (the binding). <c>UnsettledSince</c> is when the
/// earliest earlier attempt of the work item that may have acted was claimed, in UTC; where none did it is null, and the
/// envelope carries no key for it. <c>UnsettledReference</c> is the provider's reference for the work item's send that the
/// latest such attempt to return one returned, null and without a key where none did: a provider identifier only.
/// </summary>
public sealed record InvocationContext(
    JsonObject? Binding,
    string? AttemptId,
    int? AttemptNumber,
    string? WorkItemId,
    string? CampaignId,
    string RuntimeVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? UnsettledSince = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnsettledReference = null);

/// <summary>
/// One executable the plugin may start, named in the manifest and resolved to a path by the runtime.
/// <c>Launch</c> is what the runtime resolved the name to beyond the program itself — absent for an ordinary
/// program, and the entry script for a Windows npm shim, where the program is the interpreter that runs it.
/// Those arguments go ahead of everything the plugin asks for; a plugin never sets them and cannot see them
/// as anything but the fact that this name is what it was granted.
/// </summary>
public sealed record ExecutableGrant(string Name, string Path, IReadOnlyList<string>? Launch = null);

public sealed record ExecGrants(IReadOnlyList<ExecutableGrant> Executables);

public sealed record HttpGrants(IReadOnlyList<string> Hosts);

public sealed record EnvGrants(IReadOnlyList<string> Variables);

/// <summary>
/// The resolved policy — what the user granted out of what the manifest requested. The child enforces exactly
/// this and takes no policy from anywhere else; a capability absent here is a capability it cannot use.
/// </summary>
public sealed record FileDirectoryGrant(string Written, string Real);

public sealed record InvocationGrants(ExecGrants? Exec, HttpGrants? Http, EnvGrants? Env);

public sealed record ExecLimits(int OutputBytes, int MaxCalls);

public sealed record HttpLimits(int ResponseBytes, int RequestBytes, int MaxCalls, int TimeoutMs);

public sealed record LogLimits(int LineBytes, int TotalBytes);

/// <summary>Everything the child bounds itself by: the engine's budget and the caps of each SDK function.</summary>
public sealed record InvocationLimits(
    int TimeoutMs,
    long MemoryBytes,
    int MaxStatements,
    int MaxRecursion,
    ExecLimits Exec,
    HttpLimits Http,
    LogLimits Log);

/// <summary>The bounds of one door: how many calls, how large an answer, and how long one call may take.</summary>
public sealed record DoorLimits(int MaxCalls, int ResponseBytes, int TimeoutMs)
{
    /// <summary>The protocol's fixed bounds, the ones every door is given.</summary>
    public static DoorLimits Standard { get; } = new(PluginProtocol.MaxDoorCalls, PluginProtocol.MaxDoorResponseBytes, PluginProtocol.DoorCallTimeoutMs);
}

/// <summary>
/// A handler's door to the Runtime API: where the runtime listens on loopback, the capability the runtime minted for
/// this one invocation, the journal line the invocation is about, and the operations the capability opens. The host
/// holds it and makes every call itself; nothing of it is reachable from the plugin's JavaScript. The capability is
/// a secret, so a door never prints it.
/// </summary>
public sealed record InvocationDoor(string BaseUrl, string Capability, string LineId, IReadOnlyList<string> Operations, DoorLimits Limits)
{
    public override string ToString() =>
        $"InvocationDoor {{ BaseUrl = {BaseUrl}, Capability = [redacted], LineId = {LineId}, Operations = {string.Join(' ', Operations)}, Limits = {Limits} }}";
}

/// <summary>
/// One invocation, written to the child's stdin as a single JSON object and then end of file. Nothing about an
/// invocation travels on argv beyond what a process listing may show. Two kinds of secret value travel here: a
/// handler's door capability, which the runtime revokes when the invocation ends, and the plugin's configuration, the
/// text the settings hold for it, which the plugin alone reads; the host masks both wherever it writes. Granted
/// environment variables reach the child by name, through its own environment.
/// </summary>
public sealed record PluginInvocation(
    int ProtocolVersion,
    string InvocationId,
    string CorrelationId,
    InvocationPlugin Plugin,
    string Operation,
    int OperationContractVersion,
    JsonObject Input,
    InvocationContext Context,
    InvocationGrants Grants,
    InvocationLimits Limits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InvocationDoor? Door = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(ConfigMapConverter))] IReadOnlyDictionary<string, string>? Config = null);
