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
    RuntimeLocation Runtime,
    RoleMemoryLocation? RoleMemory = null)
{
    /// <summary>
    /// 3 since <see cref="RoleMemory"/> was added, and 2 since <see cref="RuntimeLocation.CliCommand"/> was.
    /// Every change here is additive, so a host written against version 1 keeps working: it reads the fields it
    /// knows and ignores the rest.
    /// </summary>
    public const int CurrentVersion = 3;
}

/// <summary>Where the runtime is, as a file to read rather than a secret to carry.</summary>
/// <param name="CliCommand">
/// The bare command word that reaches this runtime, so an agent never has to guess it. The launcher puts the
/// directory that word resolves in at the front of the child's search path, and where a host confines what the
/// agent may run, it is the same word that rule was built from. Absent in a version 1 envelope.
/// </param>
public sealed record RuntimeLocation(string DescriptorFile, string ApiVersion, string? CliCommand = null);

/// <summary>
/// Where the role's own memory of this campaign is, as a thing to read rather than a snapshot to trust.
/// <para>
/// It is a reference and never the content, for the same reason the runtime's location is a file and not a
/// token. A note copied into the envelope would be what the role believed when the attempt was launched, and it
/// would arrive looking exactly like what is true now; read through the API at the moment it is wanted, it is
/// plainly a document with an age. Current runtime state is read the same way, and where the two disagree the
/// state is what is true.
/// </para>
/// </summary>
public sealed record RoleMemoryLocation(string CampaignId, string Role);
