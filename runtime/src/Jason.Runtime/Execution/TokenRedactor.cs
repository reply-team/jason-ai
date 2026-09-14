using Jason.Runtime.Hosting;

namespace Jason.Runtime.Execution;

/// <summary>
/// Removes the capability token from anything the runtime is about to store or write to disk. An executor that
/// echoes its own environment should not be able to leave the token in an attempt's error trace or in the log
/// files of a work directory — the privacy rule covers state and artifacts, not only the runtime's own logs.
/// </summary>
public sealed class TokenRedactor(RuntimeSecrets secrets)
{
    public const string Mask = "[redacted]";

    /// <summary>Below this a "token" would match ordinary text, so a test or a stub token redacts nothing.</summary>
    public const int MinimumTokenLength = 8;

    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var token = secrets.CapabilityToken;
        return string.IsNullOrEmpty(token) || token.Length < MinimumTokenLength
            ? text
            : text.Replace(token, Mask, StringComparison.Ordinal);
    }
}
