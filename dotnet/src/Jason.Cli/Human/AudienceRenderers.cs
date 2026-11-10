using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the audience verbs. A renderer returns null when the body is not what it expects, and the
/// runner falls back to printing the response as it came.
/// </summary>
public static class AudienceRenderers
{
    private const int Label = 12;

    /// <summary>The provider's estimate, then the sample it gave, one person a row.</summary>
    public static string? Preview(string json)
    {
        var preview = RenderText.Read<AudiencePreviewResponse>(json);
        if (preview?.PluginId is null || preview.Sample is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Plugin:", preview.PluginId),
            Line("People:", Count(preview.Count)),
            Line("Companies:", Count(preview.Companies)),
            string.Empty,
        };

        var table = new HumanTable("NAME", "TITLE", "COMPANY", "LOCATION");
        foreach (var person in preview.Sample.OfType<JsonObject>())
        {
            table.Row(
                RenderText.Name((string?)person["first_name"], (string?)person["last_name"]),
                (string?)person["title"],
                (string?)person["company"]?["name"],
                (string?)person["location"]);
        }

        lines.Add(table.Render());
        return RenderText.Lines(lines);
    }

    /// <summary>The values, one a line, in the provider's order.</summary>
    public static string? FilterValues(string json)
    {
        var read = RenderText.Read<AudienceFilterValuesResponse>(json);
        if (read?.Values is null)
        {
            return null;
        }

        var table = new HumanTable("VALUE");
        foreach (var value in read.Values)
        {
            table.Row((string?)value);
        }

        return table.Render();
    }

    /// <summary>Whether the account may search now, why not, and what it has left where the provider says.</summary>
    public static string? Availability(string json)
    {
        var read = RenderText.Read<AudienceAvailabilityResponse>(json);
        if (read?.PluginId is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Plugin:", read.PluginId),
            Line("Available:", read.Available ? "yes" : "no"),
            Line("Reason:", read.Reason),
            Line("Detail:", read.Detail),
            Line("Credits:", Credits(read.Credits)),
            Line("Headroom:", Count(read.Headroom)),
        ]);
    }

    private static string? Credits(JsonObject? credits)
    {
        if (credits is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (credits["balance"] is JsonValue balance)
        {
            parts.Add($"{balance.ToJsonString()} {(string?)credits["currency"]}".Trim());
        }

        if (credits["price_per_person"] is JsonValue price)
        {
            parts.Add($"{price.ToJsonString()} a person");
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? Count(long? count) => count?.ToString(CultureInfo.InvariantCulture);

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));
}
