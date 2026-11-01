using System.Globalization;
using Jason.Contracts.Channels;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The channel rules of <see cref="ChannelRules"/>, with each refusal recorded on the field of the request it was given in:
/// the field and the rule are named, the submitted value never is.
/// </summary>
public static class ChannelValidation
{
    /// <summary>Lower-cases and validates a channel name; null when invalid, with the reason recorded on the field.</summary>
    public static string? Name(string? raw, string field, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var name = ChannelRules.NormalizeChannelName(raw, out var refusal);
        Record(refusal, field, ChannelRules.ChannelNameRule, errors);
        return name;
    }

    /// <summary>Normalizes a value by its channel's rule; null when invalid, with the reason recorded on the field.</summary>
    public static string? Value(string channel, string? raw, string field, ValidationErrors errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(errors);

        var value = ChannelRules.NormalizeValue(channel, raw, out var refusal);
        Record(refusal, field, ChannelRules.RuleOf(channel), errors);
        return value;
    }

    private static void Record(ChannelRefusal refusal, string field, string rule, ValidationErrors errors)
    {
        switch (refusal)
        {
            case ChannelRefusal.Required:
                errors.Add(field, "required", $"{field} is required.");
                break;
            case ChannelRefusal.TooLong:
                errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {ChannelRules.MaxValueLength} characters."));
                break;
            case ChannelRefusal.Invalid:
                errors.Add(field, "invalid", rule);
                break;
        }
    }
}
