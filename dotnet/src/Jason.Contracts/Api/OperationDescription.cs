using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>What one canonical operation takes for one campaign, resolved against the routes active now.</summary>
public sealed record OperationDescribeRequest(string? CampaignId, string? Operation);

/// <summary>
/// The operation's own argument schema, and beside it what the routed plugin declares for the part of the input that is
/// the provider's: for an operation that carries a <c>definition</c>, the plugin's <c>audience_definition</c> as its
/// manifest declares it (the schema and its keys); null for one that carries none.
/// </summary>
public sealed record OperationDescriptionDto(
    string Operation,
    int Version,
    string PluginId,
    string PluginVersion,
    JsonObject Arguments,
    JsonObject? Definition);
