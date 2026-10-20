using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Contacts;

/// <summary>
/// What a channel name and a channel value may be, and the one canonical form each takes. Matching,
/// deduplication and suppression all compare normalized values, so "the same person typed differently" is
/// decided here once for the whole runtime rather than at every call site.
/// </summary>
public static partial class ChannelRules
{
    public const int MaxValueLength = 500;
    public const int MaxLabelLength = 100;
    public const int MaxDataBytes = 16 * 1024;

    public const string Email = "email";
    public const string Phone = "phone";
    public const string WhatsApp = "whatsapp";
    public const string LinkedIn = "linkedin";

    private static readonly HashSet<string> KnownChannels = new(StringComparer.Ordinal) { Email, Phone, WhatsApp, LinkedIn };

    /// <summary>The vocabulary is open: an unknown channel is accepted, it simply has no rule beyond "a non-empty string".</summary>
    public static bool IsKnown(string channel) => KnownChannels.Contains(channel);

    /// <summary>Lower-cases and validates a channel name; null when invalid, with the reason recorded on the field.</summary>
    public static string? NormalizeChannelName(string? raw, string field, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add(field, "required", $"{field} is required.");
            return null;
        }

        var name = raw.Trim().ToLowerInvariant();
        if (!ChannelName().IsMatch(name))
        {
            errors.Add(field, "invalid", "a channel name starts with a letter and continues with lower-case letters, digits or underscores, at most 32 characters.");
            return null;
        }

        return name;
    }

    /// <summary>Normalizes a value by its channel's rule; null when invalid, with the reason recorded on the field.</summary>
    public static string? NormalizeValue(string channel, string? raw, string field, ValidationErrors errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add(field, "required", $"{field} is required.");
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxValueLength)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {MaxValueLength} characters."));
            return null;
        }

        var normalized = channel switch
        {
            Email => NormalizeEmail(trimmed),
            Phone or WhatsApp => NormalizePhoneNumber(trimmed),
            LinkedIn => NormalizeUrl(trimmed),
            _ => trimmed,
        };

        if (normalized is null)
        {
            errors.Add(field, "invalid", RuleOf(channel));
            return null;
        }

        return normalized;
    }

    private static string RuleOf(string channel) => channel switch
    {
        Email => "an email value is a bare mailbox address with a dotted domain and no display name.",
        Phone or WhatsApp => "a phone value is in E.164 form: a plus sign, a non-zero country digit and up to fourteen further digits.",
        LinkedIn => "a linkedin value is an absolute http or https URL.",
        _ => "the value must be a non-empty string.",
    };

    /// <summary>A mailbox, not a header: a display name, a second address or any whitespace means the caller sent the wrong thing.</summary>
    private static string? NormalizeEmail(string value)
    {
        if (value.Any(char.IsWhiteSpace) || !MailAddress.TryCreate(value, out var address))
        {
            return null;
        }

        return address.Address.Equals(value, StringComparison.Ordinal) && address.Host.Contains('.', StringComparison.Ordinal)
            ? value.ToLowerInvariant()
            : null;
    }

    /// <summary>A phone number as its digits, the way a stored one is kept, when the text is one once its separators go; null otherwise.</summary>
    public static string? DigitsOfPhone(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var digits = PhoneSeparators().Replace(value.Trim(), string.Empty);
        return PhoneDigits().IsMatch(digits) ? digits : null;
    }

    private static string? NormalizePhoneNumber(string value)
    {
        var digits = PhoneSeparators().Replace(value, string.Empty);
        return E164().IsMatch(digits) ? digits : null;
    }

    /// <summary>
    /// Two profile links that differ only in the case of their scheme or host, a default port, a user name, one trailing
    /// slash or a fragment are the same profile. On linkedin.com and its subdomains a profile link names a person by its
    /// slug whatever its case, so the path is lowercased there; on any other host it keeps its case, and the query keeps
    /// its case everywhere. The path is the escaped one, so a letter written as a percent-escape keeps its case.
    /// </summary>
    private static string? NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        if (path.Length > 0 && path.EndsWith('/'))
        {
            path = path[..^1];
        }

        if (IsLinkedInHost(host))
        {
            path = path.ToLowerInvariant();
        }

        var port = uri.IsDefaultPort ? string.Empty : string.Create(CultureInfo.InvariantCulture, $":{uri.Port}");
        return string.Concat(uri.Scheme, "://", host, port, path, uri.Query);
    }

    /// <summary>linkedin.com itself, or a host under it; never a host that merely ends in the same letters.</summary>
    /// <summary>
    /// The profile path a pasted LinkedIn link names, lowercased and without its trailing slash, query or fragment, as a
    /// stored link keeps it; null when the text is not a LinkedIn link. A link typed without its scheme counts too.
    /// </summary>
    public static string? LinkedInProfilePath(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        var absolute = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) || !IsLinkedInHost(uri.Host.ToLowerInvariant()))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        return path.Length > 1 ? path : null;
    }

    private static bool IsLinkedInHost(string host) =>
        string.Equals(host, "linkedin.com", StringComparison.Ordinal) || host.EndsWith(".linkedin.com", StringComparison.Ordinal);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,31}\z")]
    private static partial Regex ChannelName();

    [GeneratedRegex(@"[\s().-]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^\+[1-9][0-9]{1,14}\z")]
    private static partial Regex E164();

    [GeneratedRegex(@"^\+?[0-9]{3,}\z")]
    private static partial Regex PhoneDigits();
}
