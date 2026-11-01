using Jason.Contracts.Plugins;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Execution;

/// <summary>
/// Removes the capability token from anything the runtime is about to store or write to disk. An executor that
/// echoes its own environment should not be able to leave the token in an attempt's error trace or in the log
/// files of a work directory — the privacy rule covers state and artifacts, not only the runtime's own logs.
/// The masking itself is the shared <see cref="Redactor"/>; this type is the one secret the runtime always has,
/// plus every UI token it mints while it runs.
/// </summary>
public sealed class TokenRedactor
{
    public const string Mask = Redactor.Mask;

    /// <summary>Below this a "token" would match ordinary text, so a test or a stub token redacts nothing.</summary>
    public const int MinimumTokenLength = Redactor.MinimumSecretLength;

    private readonly Lock _gate = new();
    private readonly List<string> _secrets;
    private volatile Redactor _redactor;

    public TokenRedactor(RuntimeSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        _secrets = [secrets.CapabilityToken];
        _redactor = new Redactor([.. _secrets]);
    }

    /// <summary>
    /// A secret minted after start — a UI token — is masked from now on. Only tokens minted by this process are ever
    /// known in the clear; after a restart the store holds their hashes alone, which is the point of hashing them.
    /// </summary>
    public void Add(string secret)
    {
        lock (_gate)
        {
            _secrets.Add(secret);
            _redactor = new Redactor([.. _secrets]);
        }
    }

    public string Redact(string text) => _redactor.Redact(text);
}
