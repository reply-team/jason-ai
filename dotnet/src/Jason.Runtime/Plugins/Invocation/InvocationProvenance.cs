namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// Which code answered, under which contract, and on whose behalf. Every invocation carries this back, whether
/// it ran or was refused before it started: an answer whose origin cannot be named is not evidence of anything.
/// </summary>
public sealed record InvocationProvenance(
    string PluginId,
    string? Version,
    string? Digest,
    int ProtocolVersion,
    int OperationContractVersion,
    string InvocationId,
    string CorrelationId,
    string SnapshotId);
