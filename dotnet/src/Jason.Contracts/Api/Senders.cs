using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// One page of the provider's accounts a send can go from, read now for the campaign <c>campaign_id</c> names through
/// that campaign's route for <c>sender.list</c>, on <c>sender_channel</c> where one is named. <c>owners</c> is
/// <c>own</c> or <c>team</c> and is required: a colleague's account is never offered by default. <c>limit</c> is 1 to
/// 100, and 20 when absent; <c>offset</c> is where the page starts in the provider's order, 0 when absent. Nothing is
/// recorded.
/// </summary>
public sealed record SenderListRequest(string? CampaignId, string? Owners, string? SenderChannel, int? Limit, int? Offset);

/// <summary>
/// The provider's page as it answered it, under the plugin that answered: <c>items</c> are the accounts, untouched,
/// <c>channels</c> says what became of each channel, and <c>vendor</c> the provider's order.
/// </summary>
public sealed record SenderListResponse(string PluginId, JsonArray Items, bool HasMore, JsonObject Channels, JsonObject Vendor);
