namespace Jason.Runtime.Configuration;

/// <summary>
/// What the user grants one plugin out of what its manifest requests: three lists naming executables, hosts and
/// environment variables. <c>*</c> grants everything that plugin requested for that capability; an absent list
/// grants nothing, which is also the default for a plugin nobody has decided about. Declaration is not
/// permission.
/// </summary>
public sealed class PluginGrant
{
    public List<string> Exec { get; set; } = [];

    public List<string> Http { get; set; } = [];

    public List<string> Env { get; set; } = [];
}

/// <summary>
/// The engine budget of an invocation. A manifest may raise the timeout and the memory of its own invocations up
/// to the ceilings here, never past them; the caller's budget only ever lowers the timeout further.
/// </summary>
public sealed class PluginLimitsOptions
{
    public int TimeoutMs { get; set; } = 60_000;

    public int MaxTimeoutMs { get; set; } = 3_600_000;

    public int MemoryMb { get; set; } = 64;

    public int MaxMemoryMb { get; set; } = 512;

    public int MaxStatements { get; set; } = 10_000_000;

    public int MaxRecursion { get; set; } = 64;
}

public sealed class PluginExecOptions
{
    public int OutputBytes { get; set; } = 4_194_304;

    public int MaxCalls { get; set; } = 64;
}

public sealed class PluginHttpOptions
{
    public int ResponseBytes { get; set; } = 4_194_304;

    public int RequestBytes { get; set; } = 1_048_576;

    public int MaxCalls { get; set; } = 64;

    public int TimeoutMs { get; set; } = 30_000;
}

/// <summary>What the runtime side of an invocation bounds: what it reads back, and how patiently it kills.</summary>
public sealed class PluginInvokerOptions
{
    public int OutcomeBytes { get; set; } = 2_097_152;

    public int StderrBytes { get; set; } = 4_194_304;

    public int KillGraceMs { get; set; } = 5_000;

    public int VersionCheckTimeoutMs { get; set; } = 5_000;

    public int LogLineBytes { get; set; } = 16_384;
}

/// <summary>
/// Everything about plugins a person may set: who is granted what, and the limits every invocation runs under.
/// Read through <c>IOptionsMonitor</c> — grants are resolved and frozen at each reload, limits are read for each
/// invocation.
/// </summary>
public sealed class PluginsOptions
{
    public const string Section = "Plugins";

    public Dictionary<string, PluginGrant> Grants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PluginLimitsOptions Limits { get; set; } = new();

    public PluginExecOptions Exec { get; set; } = new();

    public PluginHttpOptions Http { get; set; } = new();

    public PluginInvokerOptions Invoker { get; set; } = new();
}
