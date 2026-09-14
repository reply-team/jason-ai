using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Contacts;

/// <summary>Validated, normalized contact fields ready to be written.</summary>
public sealed record ContactDraft(
    string? FirstName,
    string? LastName,
    string? Company,
    string? Title,
    string? TimeZone,
    IReadOnlyList<ContactChannel> Channels,
    JsonObject Custom);

/// <summary>
/// Turns a submitted payload into a draft or into the complete list of reasons it is not one. Every message
/// names the field and the rule; none of them repeats what was submitted, because a rejected import is the
/// easiest way for contact data to end up in a log file.
/// </summary>
public static class ContactValidation
{
    public const int MaxTextLength = 200;
    public const int MaxCustomBytes = 64 * 1024;

    /// <summary>Validates a whole payload. Aggregates every problem, then throws once.</summary>
    public static ContactDraft Validate(ContactInput input, string fieldPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fieldPrefix);

        var errors = new ValidationErrors();
        var draft = new ContactDraft(
            Text(input.FirstName, fieldPrefix + "first_name", errors),
            Text(input.LastName, fieldPrefix + "last_name", errors),
            Text(input.Company, fieldPrefix + "company", errors),
            Text(input.Title, fieldPrefix + "title", errors),
            TimeZoneRules.Normalize(input.TimeZone, fieldPrefix + "time_zone", errors),
            ValidateChannels(input.Channels, fieldPrefix, errors),
            ValidateCustom(input.Custom, fieldPrefix + "custom", errors));

        errors.ThrowIfAny();
        return draft;
    }

    /// <summary>Trimmed; blank becomes absent; length bounded by the column.</summary>
    public static string? Text(string? raw, string field, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (text.Length > MaxTextLength)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {MaxTextLength} characters."));
            return null;
        }

        return text;
    }

    /// <summary>Validates one channel list. The same pair twice, or a second primary on one channel, is the caller's mistake.</summary>
    public static List<ContactChannel> ValidateChannels(IReadOnlyList<ChannelInput>? channels, string fieldPrefix, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(fieldPrefix);

        var validated = new List<ContactChannel>();
        if (channels is null)
        {
            return validated;
        }

        var pairs = new HashSet<(string Channel, string Value)>();
        var primaries = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < channels.Count; index++)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"{fieldPrefix}channels[{index}]");
            var input = channels[index];
            if (input is null)
            {
                errors.Add(field, "invalid", "a channel entry must be an object.");
                continue;
            }

            var name = ChannelRules.NormalizeChannelName(input.Channel, field + ".channel", errors);
            var value = name is null ? null : ChannelRules.NormalizeValue(name, input.Value, field + ".value", errors);
            var label = Label(input.Label, field + ".label", errors);
            var data = Data(input.Data, field + ".data", errors);
            if (name is null || value is null)
            {
                continue;
            }

            if (!pairs.Add((name, value)))
            {
                errors.Add(field, "duplicate", "a contact carries each channel and value pair once.");
                continue;
            }

            if ((input.Primary ?? false) && !primaries.Add(name))
            {
                errors.Add(field + ".primary", "duplicate", "one channel name has at most one primary entry.");
                continue;
            }

            validated.Add(new ContactChannel
            {
                Channel = name,
                Value = value,
                Label = label,
                IsPrimary = input.Primary ?? false,
                Data = data,
            });
        }

        return validated;
    }

    /// <summary>The type already guarantees an object; what is left to check is that a role can still read it in one prompt.</summary>
    public static JsonObject ValidateCustom(JsonObject? custom, string field, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (custom is null)
        {
            return [];
        }

        if (Encoding.UTF8.GetByteCount(custom.ToJsonString(null)) > MaxCustomBytes)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must serialize to at most {MaxCustomBytes} bytes."));
            return [];
        }

        return custom;
    }

    private static string? Label(string? raw, string field, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var label = raw.Trim();
        if (label.Length > ChannelRules.MaxLabelLength)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {ChannelRules.MaxLabelLength} characters."));
            return null;
        }

        return label;
    }

    private static JsonObject? Data(JsonObject? data, string field, ValidationErrors errors)
    {
        if (data is null)
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(data.ToJsonString(null)) > ChannelRules.MaxDataBytes)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must serialize to at most {ChannelRules.MaxDataBytes} bytes."));
            return null;
        }

        return data;
    }
}
