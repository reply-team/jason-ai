namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// What actually happened at the operating system: the command as it was started, the process it became, and
/// where it worked. The command carries no secret by construction — the whole argv is the mode word and four
/// public values — so it is safe to keep and safe to show.
/// </summary>
public sealed record InvocationLaunch(
    IReadOnlyList<string> Command,
    int? Pid,
    int? ExitCode,
    DateTimeOffset StartedAt,
    long DurationMs,
    string WorkDir);

/// <summary>One invocation, answered: what came back, where it came from, and how it was run.</summary>
/// <remarks><see cref="Launch"/> is null when the refusal happened before any process existed.</remarks>
public sealed record PluginInvocationResult(InvocationOutcome Outcome, InvocationProvenance Provenance, InvocationLaunch? Launch);
