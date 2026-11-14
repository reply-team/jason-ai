using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the sender verbs. A renderer returns null when the body is not what it expects, and the
/// runner falls back to printing the response as it came.
/// </summary>
public static class SenderRenderers
{
    private const int Label = 12;

    /// <summary>The plugin that answered and what became of each channel, then one account a row, in the provider's order.</summary>
    public static string? List(string json)
    {
        var page = RenderText.Read<SenderListResponse>(json);
        if (page?.PluginId is null || page.Items is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Plugin:", page.PluginId),
            Line("Email:", (string?)page.Channels?["email"]),
            Line("LinkedIn:", (string?)page.Channels?["linkedin"]),
            string.Empty,
        };

        var table = new HumanTable("CHANNEL", "STATUS", "OWNER", "NAME", "ADDRESS", "ID");
        foreach (var account in page.Items.OfType<JsonObject>())
        {
            table.Row(
                (string?)account["channel"],
                (string?)account["status"],
                (string?)account["owner"],
                (string?)account["name"],
                (string?)account["address"],
                (string?)account["external_id"]);
        }

        lines.Add(table.Render());
        if (page.HasMore)
        {
            lines.Add(string.Empty);
            lines.Add(Line("More:", "the provider holds more accounts after this page; ask again from the next offset."));
        }

        return RenderText.Lines(lines);
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));
}
