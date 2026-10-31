using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;

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

    /// <summary>
    /// The project's dialect with property names matched exactly. The runtime keeps two keys of a context, a contact's
    /// custom fields or a channel's data that differ only in case as two, and a node read with the dialect's
    /// case-insensitive names would throw on listing them, after the runtime had already done what it was asked.
    /// </summary>
    private static readonly JsonSerializerOptions Answers = new(JasonJson.Options) { PropertyNameCaseInsensitive = false };

    /// <summary>A response body of the expected shape, or null when it is something else — the runner then prints the raw JSON.</summary>
    public static T? Read<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Answers);
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

    /// <summary>
    /// Text somebody else wrote, as a human view prints it: a character that changes how the line around it reads (see
    /// <see cref="PreviewTemplate.ChangesTheLine"/>: an escape sequence, a backspace, a right-to-left override, a line
    /// separator) is shown as U+FFFD, a mark that can be seen, rather than written; a tab, the joiners, the soft hyphen
    /// and the direction marks stay (see <see cref="IsKept"/>). It is the one rule the human views hold for a person's
    /// text, so a name, a reason, a subject and a message are printed the same way wherever they are: every table cell,
    /// every labelled line of every view, and the lines a view writes of its own around such text. A value printed
    /// whole as JSON needs no rule, because the serializer escapes such a character. A line break is such a character,
    /// so this is for a value that is one line; <see cref="ShownLines"/> is for one that is not. The rule reads
    /// characters, not bytes: what the console's code page makes of a glyph that is not marked is the writer's matter.
    /// </summary>
    public static string? Shown(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var shown = new StringBuilder(text.Length);
        foreach (var character in text.EnumerateRunes())
        {
            shown.Append(!IsKept(character) && PreviewTemplate.ChangesTheLine(character) ? "\uFFFD" : character.ToString());
        }

        return shown.ToString();
    }

    /// <summary>
    /// What <see cref="Shown"/> prints as it came although <see cref="PreviewTemplate.ChangesTheLine"/> is true or, for
    /// the tab, would be turned into a space by the summary's rule: a tab, which is whitespace a person reads as such,
    /// and the zero-width joiner, the zero-width non-joiner and the soft hyphen, which emoji sequences, Persian and
    /// Indic spelling and HTML-made text are written with, and the left-to-right, right-to-left and Arabic letter
    /// marks, which text mixing Arabic or Hebrew with Latin letters and digits is written with to read in the right
    /// order. None of them moves a character of its own or breaks a line, unlike the overrides, embeddings and
    /// isolates, which stay marked. The summary's own rule is a contract and keeps them marked.
    /// </summary>
    private static bool IsKept(Rune character) =>
        character.Value is 0x09 or 0x200C or 0x200D or 0x00AD or 0x200E or 0x200F or 0x061C;

    /// <summary>
    /// A message's text split at its own line breaks (CR LF, CR and LF alike, so that no carriage return reaches the
    /// terminal as one), each line shown by <see cref="Shown"/>. A caller with no text gives it an empty one, which is one
    /// empty line, and text that ends in a line break ends in an empty line.
    /// </summary>
    public static IEnumerable<string> ShownLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(line => Shown(line)!);

    public static string Lines(IEnumerable<string> lines) => string.Join(Environment.NewLine, lines);
}
