using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace Jason.Contracts.Channels;

/// <summary>Why a channel name or a channel value has no canonical form.</summary>
public enum ChannelRefusal
{
    None,

    /// <summary>Nothing was given, or only white space.</summary>
    Required,

    /// <summary>Longer than a stored value may be.</summary>
    TooLong,

    /// <summary>Not a value of the channel's form; <see cref="ChannelRules.RuleOf"/> says what that form is.</summary>
    Invalid,
}

/// <summary>
/// What a channel name and a channel value may be, and the one canonical form each takes. Matching, deduplication and
/// suppression all compare normalized values, so "the same person typed differently" is decided here once — for the
/// runtime, and for a plugin through <c>host.channelKey</c>, which answers with this very function rather than a copy of it.
/// Pure string logic: nothing here reads a clock, a culture, a file or a database.
/// </summary>
public static partial class ChannelRules
{
    public const int MaxValueLength = 500;
    public const int MaxLabelLength = 100;
    public const int MaxDataBytes = 16 * 1024;

    /// <summary>The most code points a LinkedIn profile's name may have, after its one decoding.</summary>
    public const int MaxProfileNameLength = 100;

    public const string Email = "email";
    public const string Phone = "phone";
    public const string WhatsApp = "whatsapp";
    public const string LinkedIn = "linkedin";

    /// <summary>The form of a channel name, as a refusal says it.</summary>
    public const string ChannelNameRule = "a channel name starts with a letter and continues with lower-case letters, digits or underscores, at most 32 characters.";

    /// <summary>What every LinkedIn key starts with: the profile's name follows it, and nothing else does.</summary>
    private const string LinkedInProfile = "https://www.linkedin.com/in/";

    private static readonly HashSet<string> KnownChannels = new(StringComparer.Ordinal) { Email, Phone, WhatsApp, LinkedIn };

    /// <summary>A UTF-8 decoder that refuses what is no UTF-8, rather than writing a replacement character in its place.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The vocabulary is open: an unknown channel is accepted, it simply has no rule beyond "a non-empty string".</summary>
    public static bool IsKnown(string channel) => KnownChannels.Contains(channel);

    /// <summary>A channel name trimmed and lower-cased; null when there is none, or it is not of the form, and why.</summary>
    public static string? NormalizeChannelName(string? raw, out ChannelRefusal refusal)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            refusal = ChannelRefusal.Required;
            return null;
        }

        var name = raw.Trim().ToLowerInvariant();
        refusal = ChannelName().IsMatch(name) ? ChannelRefusal.None : ChannelRefusal.Invalid;
        return refusal == ChannelRefusal.None ? name : null;
    }

    /// <summary>A value as its channel's rule keeps it; null when there is none, or it is too long or not of the form, and why.</summary>
    public static string? NormalizeValue(string channel, string? raw, out ChannelRefusal refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        if (string.IsNullOrWhiteSpace(raw))
        {
            refusal = ChannelRefusal.Required;
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxValueLength)
        {
            refusal = ChannelRefusal.TooLong;
            return null;
        }

        var normalized = ByRule(channel, trimmed);
        refusal = normalized is null ? ChannelRefusal.Invalid : ChannelRefusal.None;
        return normalized;
    }

    /// <summary>
    /// The value the runtime's contacts, its do-not-contact register and its claim compare for a value given on a channel —
    /// the channel's name read as the runtime reads one — or null where the runtime would refuse to store it.
    /// </summary>
    public static string? KeyOf(string? channel, string? value) =>
        NormalizeChannelName(channel, out _) is { } name ? NormalizeValue(name, value, out _) : null;

    /// <summary>
    /// The key of a value this runtime stored before the key; the channel's own rule bounds it. The text is trimmed and
    /// given to the rule, and never held to the bound on a value given, because a rule before could store a value longer
    /// than it was given — a letter outside ASCII written as its escapes, a query kept — and that value still names its
    /// person. Two rules bound a key whatever the value's length: LinkedIn's, by the canonical start and at most 100 code
    /// points of the name, and the one phone and WhatsApp share, by E.164's sixteen characters. An email address, and a
    /// value on a channel without a rule, is keyed whole however long it is; only the LinkedIn re-key calls this today. Null
    /// where there is no channel name or no value, or the rule refuses the value.
    /// </summary>
    public static string? KeyOfStored(string? channel, string? value) =>
        NormalizeChannelName(channel, out _) is { } name && !string.IsNullOrWhiteSpace(value) ? ByRule(name, value.Trim()) : null;

    /// <summary>The form a channel's values take, as a refusal says it: never the value refused.</summary>
    public static string RuleOf(string channel) => channel switch
    {
        Email => "an email value is a bare mailbox address with a dotted domain and no display name.",
        Phone or WhatsApp => "a phone value is in E.164 form: a plus sign, a non-zero country digit and up to fourteen further digits.",
        LinkedIn => "a linkedin value is a link to a person's LinkedIn profile, linkedin.com/in/<name>, where <name> is 1 to 100 letters, digits, combining marks, hyphens or underscores, starts with a letter or a digit, and has a mark only after a letter, a digit or another mark.",
        _ => "the value must be a non-empty string.",
    };

    /// <summary>A phone number as its digits, the way a stored one is kept, when the text is one once its separators go; null otherwise.</summary>
    public static string? DigitsOfPhone(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var digits = PhoneSeparators().Replace(value.Trim(), string.Empty);
        return PhoneDigits().IsMatch(digits) ? digits : null;
    }

    /// <summary>
    /// The profile path a pasted LinkedIn link names — <c>/in/</c> and the profile's name, as its key ends — or null when the
    /// text names no LinkedIn profile.
    /// </summary>
    public static string? LinkedInProfilePath(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return KeyOf(LinkedIn, text) is { } key ? key["https://www.linkedin.com".Length..] : null;
    }

    /// <summary>A trimmed value as its channel's rule keeps it, or null where the rule refuses it; a channel without a rule keeps it as it is.</summary>
    private static string? ByRule(string channel, string trimmed) => channel switch
    {
        Email => NormalizeEmail(trimmed),
        Phone or WhatsApp => NormalizePhoneNumber(trimmed),
        LinkedIn => LinkedInKey(trimmed),
        _ => trimmed,
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

    private static string? NormalizePhoneNumber(string value)
    {
        var digits = PhoneSeparators().Replace(value, string.Empty);
        return E164().IsMatch(digits) ? digits : null;
    }

    /// <summary>
    /// A LinkedIn profile link as its key: <c>https://www.linkedin.com/in/</c> and the profile's name, decoded once,
    /// composed and lowercased. A link names a person by that name and nothing else, so its scheme, its host among
    /// linkedin.com's, a user name, the scheme's own port, every trailing or doubled slash, a path after the name, a query
    /// and a fragment never count. A spelling that a browser or .NET's <c>System.Uri</c> would read as a profile other
    /// than the one this rule keys is refused, never followed: a step to <c>.</c> or <c>..</c> in the path, written plainly
    /// or as escapes, which both follow; a backslash anywhere in the path, which both read as a slash; and a tab, a line
    /// feed or a carriage return anywhere in the value once its edges are trimmed, which a browser drops. Null for those,
    /// and for anything else that is not a link to a profile on linkedin.com: another host, or one that only ends in its
    /// letters; another port or scheme; a path that is not <c>/in/</c> and a name; a name that, once
    /// its escapes are decoded and it is composed, is not 1 to 100 letters, decimal digits, combining marks, hyphens or
    /// underscores, does not start with a letter or a digit, or has a mark after a hyphen or an underscore — a <c>%</c> that
    /// starts no escape and escapes that are no UTF-8 included. The name a key carries has no <c>%</c> left in it and is
    /// composed already, so a key is its own key.
    /// </summary>
    private static string? LinkedInKey(string value)
    {
        var text = TrimmedOfSpaceAndMarks(value);
        if (text.Length == 0 || text.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
        {
            return null;
        }

        // A scheme is what comes before the first "://", when that has the form of one; without one, it is https.
        var scheme = "https";
        var rest = text;
        var mark = text.IndexOf("://", StringComparison.Ordinal);
        if (mark > 0 && SchemeName().IsMatch(text.AsSpan(0, mark)))
        {
            scheme = text[..mark].ToLowerInvariant();
            rest = text[(mark + 3)..];
        }

        if (scheme is not ("http" or "https"))
        {
            return null;
        }

        var cut = rest.AsSpan().IndexOfAny('/', '?', '#');
        var authority = cut < 0 ? rest : rest[..cut];
        var tail = cut < 0 ? string.Empty : rest[cut..];

        // A user name is dropped; a port is the scheme's own or none.
        authority = authority[(authority.LastIndexOf('@') + 1)..];
        var colon = authority.LastIndexOf(':');
        var host = colon < 0 ? authority : authority[..colon];
        var port = colon < 0 ? string.Empty : authority[(colon + 1)..];
        if ((port.Length > 0 && port != (scheme == "https" ? "443" : "80")) || !IsLinkedInHost(host))
        {
            return null;
        }

        var end = tail.AsSpan().IndexOfAny('?', '#');
        var path = SlashRuns().Replace(end < 0 ? tail : tail[..end], "/");
        if (path.Length < 4 || path[0] != '/' || (path[1] | 0x20) != 'i' || (path[2] | 0x20) != 'n' || path[3] != '/'
            || path.Contains('\\', StringComparison.Ordinal) || path.Split('/').Any(IsDotStep))
        {
            return null;
        }

        var name = path[4..];
        var stop = name.AsSpan().IndexOfAny('/', '&');
        var decoded = DecodedOnce(stop < 0 ? name : name[..stop]);
        return decoded is null ? null : ProfileName(decoded) is { } key ? LinkedInProfile + key : null;
    }

    /// <summary>
    /// A step of a path to where it is, or back: <c>.</c> or <c>..</c>, written plainly or with its dots as escapes. A
    /// browser follows one, so a link that holds one opens a page other than the name after <c>/in/</c> says.
    /// </summary>
    private static bool IsDotStep(string step) =>
        step.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase) is "." or "..";

    private static string TrimmedOfSpaceAndMarks(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && (char.IsWhiteSpace(value[start]) || value[start] == '\uFEFF'))
        {
            start++;
        }

        while (end > start && (char.IsWhiteSpace(value[end - 1]) || value[end - 1] == '\uFEFF'))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>
    /// linkedin.com itself, or a host under it with no empty label; never a host that merely ends in the same letters, and
    /// never one written outside ASCII, which lowercasing could make look like it.
    /// </summary>
    private static bool IsLinkedInHost(string host)
    {
        if (!HostName().IsMatch(host))
        {
            return false;
        }

        var lowered = host.ToLowerInvariant();
        return lowered == "linkedin.com"
            || (lowered.EndsWith(".linkedin.com", StringComparison.Ordinal)
                && lowered[..^".linkedin.com".Length].Split('.').All(label => label.Length > 0));
    }

    /// <summary>
    /// The name with its escapes decoded, once: each run of <c>%XX</c> as strict UTF-8, every other character as itself.
    /// Null for a <c>%</c> that starts no escape, a run that is no UTF-8 and half of a surrogate pair.
    /// </summary>
    private static string? DecodedOnce(string name)
    {
        var decoded = new StringBuilder(name.Length);
        var bytes = new List<byte>();
        for (var at = 0; at < name.Length;)
        {
            if (name[at] != '%')
            {
                decoded.Append(name[at]);
                at++;
                continue;
            }

            bytes.Clear();
            while (at < name.Length && name[at] == '%')
            {
                if (at + 2 >= name.Length || !byte.TryParse(name.AsSpan(at + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var octet))
                {
                    return null;
                }

                bytes.Add(octet);
                at += 3;
            }

            try
            {
                decoded.Append(StrictUtf8.GetString([.. bytes]));
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        return decoded.ToString();
    }

    /// <summary>
    /// The decoded name as its key, or null. The name is composed (NFC) first, so an accent written precomposed and one
    /// written as a letter and a combining mark are one name. Composed, it must be 1 to 100 code points of letters, decimal
    /// digits, combining marks (Mn, Mc), hyphens and underscores; it must start with a letter or a digit; and a mark must
    /// follow a letter, a digit or another mark, never a hyphen, an underscore or the start. Then each code point is
    /// lowercased with the invariant culture — a mark lowercases to itself — and the result is composed again, because a
    /// capital that takes no mark can lowercase into a letter that does, and a key has to be its own key.
    /// </summary>
    private static string? ProfileName(string decoded)
    {
        if (Composed(decoded) is not { } composed)
        {
            return null;
        }

        var lowered = new StringBuilder(composed.Length);
        var points = 0;
        var marked = false;
        foreach (var rune in composed.EnumerateRunes())
        {
            var kind = Rune.GetUnicodeCategory(rune);
            var mark = kind is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
            var letterOrDigit = Rune.IsLetter(rune) || kind == UnicodeCategory.DecimalDigitNumber;
            var joiner = rune.Value is '-' or '_';
            if (++points > MaxProfileNameLength
                || !(letterOrDigit || mark || joiner)
                || (points == 1 && !letterOrDigit)
                || (mark && !marked))
            {
                return null;
            }

            // What a mark may follow: a letter, a digit or another mark.
            marked = letterOrDigit || mark;
            lowered.Append(Rune.ToLowerInvariant(rune).ToString());
        }

        return points == 0 ? null : lowered.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The text composed (NFC), or null where it cannot be: half of a surrogate pair on its own, or a code point Unicode
    /// keeps out of text, neither of which a name may hold.
    /// </summary>
    private static string? Composed(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,31}\z")]
    private static partial Regex ChannelName();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*\z")]
    private static partial Regex SchemeName();

    [GeneratedRegex(@"^[A-Za-z0-9.\-]+\z")]
    private static partial Regex HostName();

    [GeneratedRegex("/{2,}")]
    private static partial Regex SlashRuns();

    [GeneratedRegex(@"[\s().-]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^\+[1-9][0-9]{1,14}\z")]
    private static partial Regex E164();

    [GeneratedRegex(@"^\+?[0-9]{3,}\z")]
    private static partial Regex PhoneDigits();
}
