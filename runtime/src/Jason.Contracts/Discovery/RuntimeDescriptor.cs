namespace Jason.Contracts.Discovery;

/// <summary>
/// Published by the running runtime at <see cref="JasonPaths.DescriptorFile"/>; read by clients to find the
/// API and authenticate. A client must compare <see cref="InstanceId"/> with the runtime's answer and treat a
/// mismatch as a stale descriptor.
/// </summary>
public sealed record RuntimeDescriptor(
    string ApiVersion,
    string RuntimeVersion,
    string InstanceId,
    int Pid,
    string BaseUrl,
    string Token,
    DateTimeOffset StartedAt);
