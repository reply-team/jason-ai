using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Human;

/// <summary>
/// The small formatting decisions every <c>--human</c> renderer shares: how a response body is read, how a
/// moment is written, and how the API's snake_case vocabulary survives the trip through a CLR enum.
/// </summary>
internal static class RenderText
{
    /// <summary>A response body of the expected shape, or null when it is something else — the runner then prints the raw JSON.</summary>
    public static T? Read<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The value as the API spells it, so people read the same words they would send back.</summary>
    public static string Snake<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static string Moment(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    public static string? Moment(DateTimeOffset? value) => value is null ? null : Moment(value.Value);

    /// <summary>A person's name from the parts a contact may or may not have.</summary>
    public static string? Name(string? firstName, string? lastName)
    {
        var name = string.Join(' ', new[] { firstName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return name.Length == 0 ? null : name;
    }

    /// <summary>The address a person would recognize the contact by: the primary email, or the first one.</summary>
    public static string? Email(IReadOnlyList<ChannelDto>? channels)
    {
        if (channels is null)
        {
            return null;
        }

        var emails = channels.Where(channel => string.Equals(channel.Channel, "email", StringComparison.Ordinal)).ToList();
        return (emails.Find(channel => channel.Primary) ?? emails.FirstOrDefault())?.Value;
    }

    /// <summary>The keys of a free-form JSON object, listed rather than printed: the object itself is in the JSON output.</summary>
    public static string Keys(IEnumerable<KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>>? properties)
    {
        var keys = properties is null ? [] : properties.Select(property => property.Key).ToList();
        return keys.Count == 0 ? "none" : string.Join(", ", keys);
    }

    /// <summary>Appends the cursor that continues a listing, when the page has one.</summary>
    public static string WithCursor(string rendered, string? nextCursor) =>
        nextCursor is null ? rendered : rendered + Environment.NewLine + "next cursor: " + nextCursor;

    public static string Lines(IEnumerable<string> lines) => string.Join(Environment.NewLine, lines);
}
