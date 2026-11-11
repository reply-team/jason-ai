namespace Jason.Contracts.Discovery;

/// <summary>
/// Published by the running runtime at <see cref="JasonPaths.DescriptorFile"/>; read by clients to find the
/// API and authenticate. A client must compare <see cref="InstanceId"/> with the runtime's answer and treat a
/// mismatch as a stale descriptor.
/// </summary>
/// <param name="InstalledImage">
/// The executable file this runtime runs as, when it is an installed single-file image
/// (<see cref="SelfExecutable.InstalledImage"/>); null, or absent from a file an earlier runtime wrote, when that is
/// not known. It is for a program that must know whether the runtime it found is the copy it would replace or remove,
/// and it is trusted only from a descriptor whose instance the runtime answering there confirms.
/// </param>
public sealed record RuntimeDescriptor(
    string ApiVersion,
    string RuntimeVersion,
    string InstanceId,
    int Pid,
    string BaseUrl,
    string Token,
    DateTimeOffset StartedAt,
    string? InstalledImage = null);
