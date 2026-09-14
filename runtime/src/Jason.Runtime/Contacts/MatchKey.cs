using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Contacts;

/// <summary>
/// What "the same person" means for one import: a channel the runtime normalizes, or a field of the caller's
/// own <c>custom</c> object. It is chosen per call because the answer is a property of the list being
/// imported — a CRM export knows its own ids — and not of the contact directory.
/// </summary>
public abstract partial record MatchKey
{
    private const string CustomPrefix = "custom:";

    private protected MatchKey()
    {
    }

    public sealed record ByChannel(string Channel) : MatchKey;

    public sealed record ByCustomField(string Field) : MatchKey;

    /// <summary>Null when absent; null plus an error recorded on <c>match_by</c> when malformed.</summary>
    public static MatchKey? Parse(string? matchBy, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(matchBy))
        {
            return null;
        }

        var raw = matchBy.Trim();
        if (raw.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var field = raw[CustomPrefix.Length..];
            if (!CustomFieldName().IsMatch(field))
            {
                errors.Add("match_by", "invalid", "a custom match key is 'custom:' followed by a field name of letters, digits and underscores, at most 64 characters.");
                return null;
            }

            return new ByCustomField(field);
        }

        var channel = ChannelRules.NormalizeChannelName(raw, "match_by", errors);
        return channel is null ? null : new ByChannel(channel);
    }

    /// <summary>The normalized key value of a payload item, or null when the item carries none.</summary>
    public string? ValueOf(ContactDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return this switch
        {
            ByChannel byChannel => draft.Channels.FirstOrDefault(channel => string.Equals(channel.Channel, byChannel.Channel, StringComparison.Ordinal))?.Value,
            ByCustomField byField => Scalar(draft.Custom, byField.Field),
            _ => null,
        };
    }

    /// <summary>Only a scalar identifies anybody: an object or an array under the key is not a key value.</summary>
    private static string? Scalar(JsonObject custom, string field)
    {
        if (!custom.TryGetPropertyValue(field, out var node) || node is not JsonValue value)
        {
            return null;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex CustomFieldName();
}
