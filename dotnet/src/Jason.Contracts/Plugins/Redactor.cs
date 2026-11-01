namespace Jason.Contracts.Plugins;

/// <summary>
/// Removes known secret values from text that is about to be written somewhere durable — a log line, a stderr
/// copy, an error trace. It masks values it was told about and nothing else: there is no pattern matching here,
/// because guessing what looks like a secret either misses or mangles ordinary text.
/// </summary>
public sealed class Redactor
{
    public const string Mask = "[redacted]";

    /// <summary>Below this a "secret" would match ordinary text, so a stub value redacts nothing.</summary>
    public const int MinimumSecretLength = 8;

    private readonly string[] _secrets;

    /// <summary>Longest first, so an overlapping pair masks the longer value whole rather than in halves.</summary>
    public Redactor(IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        _secrets = [.. secrets
            .Where(static secret => !string.IsNullOrWhiteSpace(secret) && secret.Length >= MinimumSecretLength)
            .Select(static secret => secret!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static secret => secret.Length)];
    }

    /// <summary>A redactor that knows no secrets and hands every text back unchanged.</summary>
    public static Redactor None { get; } = new([]);

    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_secrets.Length == 0 || text.Length == 0)
        {
            return text;
        }

        foreach (var secret in _secrets)
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }

        return text;
    }
}
