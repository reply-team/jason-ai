using System.Text.Encodings.Web;
using System.Text.Json;

namespace Jason.Contracts.Plugins;

/// <summary>
/// Removes known secret values from text that is about to be written somewhere durable — a log line, a stderr
/// copy, an error trace. It masks values it was told about and nothing else: there is no pattern matching here,
/// because guessing what looks like a secret either misses or mangles ordinary text. The host and the runtime write JSON
/// with <c>Utf8JsonWriter</c>, which spells <c>&amp;</c>, <c>+</c>, a quote and every non-ASCII character as an escape, so a
/// value is matched in the spelling that writer gives it as well as its own; the mask is plain ASCII, so a document it
/// touches stays parseable.
/// </summary>
public sealed class Redactor
{
    public const string Mask = "[redacted]";

    /// <summary>Below this a "secret" would match ordinary text, so a stub value redacts nothing.</summary>
    public const int MinimumSecretLength = 8;

    private readonly string[] _spellings;

    /// <summary>
    /// Longest first, so an overlapping pair masks the longer value whole rather than in halves. The length rule reads the
    /// value itself, so an escape never makes a short word long enough to mask.
    /// </summary>
    public Redactor(IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        _spellings = [.. secrets
            .Where(static secret => !string.IsNullOrWhiteSpace(secret) && secret.Length >= MinimumSecretLength)
            .SelectMany(static secret => Spellings(secret!))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static spelling => spelling.Length)];
    }

    /// <summary>A redactor that knows no secrets and hands every text back unchanged.</summary>
    public static Redactor None { get; } = new([]);

    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_spellings.Length == 0 || text.Length == 0)
        {
            return text;
        }

        foreach (var spelling in _spellings)
        {
            text = text.Replace(spelling, Mask, StringComparison.Ordinal);
        }

        return text;
    }

    private static string[] Spellings(string secret)
    {
        try
        {
            return
            [
                secret,
                JsonEncodedText.Encode(secret).ToString(),
                JsonEncodedText.Encode(secret, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
            ];
        }
        catch (ArgumentException)
        {
            // A value that is no valid UTF-16 has no JSON spelling: no writer can put it in a document.
            return [secret];
        }
    }
}
