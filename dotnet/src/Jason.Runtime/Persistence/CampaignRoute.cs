using System.Text.Json.Nodes;

namespace Jason.Runtime.Persistence;

/// <summary>
/// Where one campaign sends its provider work: a plugin, and the binding that says which account of that plugin
/// to work through. A row with no operation is the campaign's default, and a campaign has at most one of those;
/// a row that names an operation overrides the default for that operation alone; and a row that names an operation and
/// a channel overrides the operation's own route for the calls on that channel alone, never standing in for it.
/// </summary>
public sealed class CampaignRoute
{
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>The canonical operation this route is for; null is the campaign's default route.</summary>
    public string? Operation { get; set; }

    /// <summary>
    /// The channel this route is for under its operation, one the operation's document lets a route name; null for the
    /// operation's own route, and always for the default, which names no operation for a channel to be under.
    /// </summary>
    public string? Channel { get; set; }

    public required string PluginId { get; set; }

    /// <summary>Which account, mailbox or workspace of the plugin the work runs against. Opaque to the runtime.</summary>
    public JsonObject? Binding { get; set; }

    public DateTime UpdatedAt { get; set; }
}
