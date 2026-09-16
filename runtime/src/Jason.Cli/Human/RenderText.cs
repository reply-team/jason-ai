using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Human;

/// <summary>
/// The small formatting decisions every <c>--human</c> renderer shares: how a response body is read, how a
/// moment is written, and how the API's snake_case vocabulary survives the trip through a CLR enum.
/// </summary>
internal static class RenderText
{
    private const string DigestPrefix = "sha256:";

    /// <summary>How much of a digest a person needs to tell two packages apart at a glance.</summary>
    private const int DigestShown = 12;

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

    /// <summary>The actor as the <c>--actor</c> option spells it, so what is read can be written back.</summary>
    public static string? Actor(ActorRef? actor) =>
        actor is null ? null : actor.Id is null ? Snake(actor.Type) : Snake(actor.Type) + ":" + actor.Id;

    /// <summary>A free-form JSON value as it travelled, compactly: there is no plainer way to show what was recorded.</summary>
    public static string? Compact(JsonNode? value) => value?.ToJsonString(JasonJson.Options);

    /// <summary>The keys of a free-form JSON object, listed rather than printed: the object itself is in the JSON output.</summary>
    public static string Keys(IEnumerable<KeyValuePair<string, JsonNode?>>? properties)
    {
        var keys = properties is null ? [] : properties.Select(property => property.Key).ToList();
        return keys.Count == 0 ? "none" : string.Join(", ", keys);
    }

    /// <summary>
    /// What providers call this entity, as a block only when there is one — most entities carry no pins, and a
    /// table of nothing would be noise. A pin that a later answer disagreed with shows both values and the attempt
    /// that disagreed, because that disagreement is exactly what a person is being asked to look at.
    /// </summary>
    public static IReadOnlyList<string> ExternalIds(IReadOnlyList<ExternalIdDto>? externalIds)
    {
        if (externalIds is null || externalIds.Count == 0)
        {
            return [];
        }

        var table = new HumanTable("PLUGIN", "KIND", "VALUE", "DISPUTED", "DISPUTED BY");
        foreach (var pin in externalIds)
        {
            table.Row(pin.PluginId, pin.Kind, pin.Value, pin.DivergedValue, pin.DivergedByAttemptId);
        }

        return [string.Empty, "EXTERNAL IDS", table.Render()];
    }

    /// <summary>
    /// The head of a content digest, which is what people compare at a glance; the whole value is in the JSON
    /// output. One spelling of the shortening, because two renderers showing the same digest differently would
    /// make a reader wonder which package they are looking at.
    /// </summary>
    public static string? Digest(string? digest)
    {
        if (string.IsNullOrEmpty(digest))
        {
            return null;
        }

        var hex = digest.StartsWith(DigestPrefix, StringComparison.Ordinal) ? digest[DigestPrefix.Length..] : digest;
        return hex.Length <= DigestShown ? hex : hex[..DigestShown];
    }

    /// <summary>Appends the cursor that continues a listing, when the page has one.</summary>
    public static string WithCursor(string rendered, string? nextCursor) =>
        nextCursor is null ? rendered : rendered + Environment.NewLine + "next cursor: " + nextCursor;

    public static string Lines(IEnumerable<string> lines) => string.Join(Environment.NewLine, lines);
}
