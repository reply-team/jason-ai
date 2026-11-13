using System.Text.Json.Nodes;

namespace Jason.Runtime.Persistence;

/// <summary>
/// Where one campaign sends the calls of one operation on one channel: a plugin, and the binding that says which account
/// of it to work through. It overrides the operation's own route (a <see cref="CampaignRoute"/>) for that channel alone
/// and never stands in for it. It is a table of its own rather than a column of <c>campaign_routes</c> so that an older
/// executable, which reads every row there as an operation's own route, never sees one.
/// </summary>
public sealed class CampaignChannelRoute
{
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>The canonical operation whose calls on <see cref="Channel"/> this route takes.</summary>
    public required string Operation { get; set; }

    /// <summary>The channel, one the operation's document lets a route name.</summary>
    public required string Channel { get; set; }

    public required string PluginId { get; set; }

    /// <summary>Which account, mailbox or workspace of the plugin the work runs against. Opaque to the runtime.</summary>
    public JsonObject? Binding { get; set; }

    public DateTime UpdatedAt { get; set; }
}
