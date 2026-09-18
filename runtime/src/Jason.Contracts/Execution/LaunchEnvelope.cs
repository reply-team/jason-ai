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
    /// <summary>
    /// 2 since <see cref="RuntimeLocation.CliCommand"/> was added. Every change here is additive, so a host
    /// written against version 1 keeps working: it reads the fields it knows and ignores the rest.
    /// </summary>
    public const int CurrentVersion = 2;
}

/// <summary>Where the runtime is, as a file to read rather than a secret to carry.</summary>
/// <param name="CliCommand">
/// The bare command word that reaches this runtime, so an agent never has to guess it. The launcher puts the
/// directory that word resolves in at the front of the child's search path, and where a host confines what the
/// agent may run, it is the same word that rule was built from. Absent in a version 1 envelope.
/// </param>
public sealed record RuntimeLocation(string DescriptorFile, string ApiVersion, string? CliCommand = null);
