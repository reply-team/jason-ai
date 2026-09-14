using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Contracts.Execution;

/// <summary>
/// Everything an agent host is told about the attempt it is about to run, handed to it as one JSON object on
/// stdin. It is a public contract: anyone can write a host against it, so it never carries the capability token —
/// the host reads the descriptor file named in <see cref="Runtime"/> when it wants to call the Runtime API.
/// </summary>
public sealed record LaunchEnvelope(
    int EnvelopeVersion,
    string AttemptId,
    int AttemptNumber,
    string WorkItemId,
    string CampaignId,
    string? ContactId,
    WorkItemKind Kind,
    string? Role,
    string? ExecutionProfile,
    JsonObject Context,
    JsonNode? ResultFormat,
    int TimeoutSeconds,
    int HeartbeatSeconds,
    DateTimeOffset LockUntil,
    string WorkDir,
    RuntimeLocation Runtime)
{
    public const int CurrentVersion = 1;
}

/// <summary>Where the runtime is, as a file to read rather than a secret to carry.</summary>
public sealed record RuntimeLocation(string DescriptorFile, string ApiVersion);
