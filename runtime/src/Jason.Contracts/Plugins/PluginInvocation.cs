using System.Text.Json.Nodes;

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
/// and the only place a caller can pass something of its own (the binding).
/// </summary>
public sealed record InvocationContext(
    JsonObject? Binding,
    string? AttemptId,
    int? AttemptNumber,
    string? WorkItemId,
    string? CampaignId,
    string RuntimeVersion);

/// <summary>One executable the plugin may start, named in the manifest and resolved to a path by the runtime.</summary>
public sealed record ExecutableGrant(string Name, string Path);

public sealed record ExecGrants(IReadOnlyList<ExecutableGrant> Executables);

public sealed record HttpGrants(IReadOnlyList<string> Hosts);

public sealed record EnvGrants(IReadOnlyList<string> Variables);

/// <summary>
/// The resolved policy — what the user granted out of what the manifest requested. The child enforces exactly
/// this and has no configuration of its own; a capability absent here is a capability it cannot use.
/// </summary>
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

/// <summary>
/// One invocation, written to the child's stdin as a single JSON object and then end of file. Nothing about an
/// invocation travels on argv beyond what a process listing may show, and no secret value travels here at all:
/// granted environment variables reach the child by name, through its own environment.
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
    InvocationLimits Limits);
