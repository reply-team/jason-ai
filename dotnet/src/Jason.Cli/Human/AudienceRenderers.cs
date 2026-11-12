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

    /// <summary>
    /// Where a campaign's audience stands: the reserve and the allowance as lines, then each audience with its definition
    /// and its searches as a table. A definition is laid out by <paramref name="described"/>, what the plugin the campaign's
    /// route for searches declares one to be: its properties in the order the schema declares them, each with the side of a
    /// person it filters on where the plugin says. Where that could not be read, the definition is shown as stored.
    /// </summary>
    public static string? CampaignAudience(string json, JsonObject? described)
    {
        var view = RenderText.Read<CampaignAudienceResponse>(json);
        if (view?.CampaignId is null || view.Reserve is null || view.Audiences is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Campaign:", view.CampaignId),
            Line("Untouched:", view.Reserve.Threshold is { } threshold
                ? string.Create(CultureInfo.InvariantCulture, $"{view.Reserve.Untouched}, threshold {threshold}")
                : string.Create(CultureInfo.InvariantCulture, $"{view.Reserve.Untouched}, no threshold")),
            Line("Allowance:", Allowance(view.Allowance)),
        };
        if (view.Allowance is { } allowance)
        {
            lines.Add(Line("Let go:", allowance.Released.ToString(CultureInfo.InvariantCulture)));
            lines.Add(Line("Set by:", $"{RenderText.Actor(allowance.SetBy)}, {RenderText.Moment(allowance.SetAt)}"));
        }

        foreach (var audience in view.Audiences)
        {
            lines.Add(string.Empty);
            lines.Add(RenderText.Shown(string.Create(
                CultureInfo.InvariantCulture,
                $"Audience {audience.Number}  {audience.Id}  made {RenderText.Moment(audience.CreatedAt)}  {audience.Landed} landed, {Searches(audience.SearchCount)}"))!);
            lines.AddRange(Definition(audience.Definition, described).Select(line => "  " + RenderText.Shown(line)));
            if (audience.Searches is not { Count: > 0 })
            {
                lines.Add("  No search taken.");
                continue;
            }

            var table = new HumanTable("SEARCH", "STATE", "ASKED", "FOUND", "LANDED", "BEGAN", "ENDED");
            foreach (var search in audience.Searches)
            {
                table.Row(
                    search.Search,
                    search.State,
                    search.Asked.ToString(CultureInfo.InvariantCulture),
                    search.Found?.ToString(CultureInfo.InvariantCulture),
                    search.Landed.ToString(CultureInfo.InvariantCulture),
                    RenderText.Moment(search.BeganAt),
                    search.EndedAs is { } endedAs ? $"{endedAs}, {RenderText.Moment(search.EndedAt)}" : null);
            }

            lines.AddRange(table.Render().Split(Environment.NewLine).Select(line => "  " + line));
            if (audience.SearchCount > audience.Searches.Count)
            {
                var earlier = audience.SearchCount - audience.Searches.Count;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {earlier} earlier {(earlier == 1 ? "search" : "searches")} not shown."));
            }
        }

        return RenderText.WithCursor(RenderText.Lines(lines), view.NextCursor);
    }

    private static string Searches(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "search" : "searches")}");

    /// <summary>The allowance in one line: its audience, the people asked for against its ceiling, its other limits, whether it still stands.</summary>
    private static string Allowance(StandingApprovalDto? allowance)
    {
        if (allowance is null)
        {
            return "none";
        }

        var parts = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"audience {allowance.Audience?.Number}"),
            string.Create(CultureInfo.InvariantCulture, $"{allowance.People ?? 0} of {allowance.MaxPeople} people"),
        };
        if (allowance.MaxPerSearch is { } perSearch)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{perSearch} per search"));
        }

        if (allowance.ExpiresAt is { } expires)
        {
            parts.Add("until " + RenderText.Moment(expires));
        }

        parts.Add(allowance.InForce ? "in force" : "not in force");
        return string.Join(", ", parts);
    }

    /// <summary>A definition's lines: by the plugin's declared schema where there is one, otherwise as it was stored.</summary>
    private static IEnumerable<string> Definition(JsonObject? definition, JsonObject? described)
    {
        if (definition is null)
        {
            return [];
        }

        if (described?["schema"]?["properties"] is not JsonObject properties)
        {
            return ["definition: " + RenderText.Compact(definition)];
        }

        var keys = described["keys"] as JsonObject;
        var declared = properties.Select(property => property.Key).Where(definition.ContainsKey);
        var undeclared = definition.Select(member => member.Key).Where(name => !properties.ContainsKey(name));
        return declared.Concat(undeclared).Select(name =>
            keys?[name]?["side"] is JsonValue side && side.TryGetValue<string>(out var said)
                ? $"{name} ({said}): {Value(definition[name])}"
                : $"{name}: {Value(definition[name])}");
    }

    /// <summary>
    /// A value of a definition as a person reads it: text as it is, a list item by item, an object member by member, a
    /// number or a flag as its JSON. Text is never escaped here, so a place's or a company's name reads as it was written;
    /// the line it ends up in is shown by <see cref="RenderText.Shown"/>.
    /// </summary>
    private static string Value(JsonNode? value) => value switch
    {
        JsonArray items => string.Join(", ", items.Select(Value)),
        JsonObject members => "{" + string.Join(", ", members.Select(member => $"{member.Key}: {Value(member.Value)}")) + "}",
        JsonValue text when text.TryGetValue<string>(out var said) => said,
        null => "null",
        _ => RenderText.Compact(value)!,
    };

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
