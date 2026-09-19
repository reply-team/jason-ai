namespace Jason.Contracts.Update;

/// <summary>
/// The feed could not be read, or what came back was not a manifest. It carries the code the CLI prints and the
/// runtime logs, so that a person reading either learns which of the three things went wrong rather than that
/// "the update check failed".
/// </summary>
public sealed class UpdateFeedException : Exception
{
    /// <summary>The document is not a manifest this build can read.</summary>
    public const string Invalid = "update_feed_invalid";

    /// <summary>Nobody answered, or what answered was not a success. Offline is the ordinary cause.</summary>
    public const string Unreachable = "update_feed_unreachable";

    /// <summary>The feed is not an address this build will fetch: only https, and loopback for a test's own stub.</summary>
    public const string Insecure = "update_feed_insecure";

    public UpdateFeedException(string code, string message)
        : base(message) => Code = code;

    public UpdateFeedException(string code, string message, Exception? innerException)
        : base(message, innerException) => Code = code;

    public UpdateFeedException()
        : this(Invalid, "The update feed could not be read.")
    {
    }

    public UpdateFeedException(string message)
        : this(Invalid, message)
    {
    }

    public UpdateFeedException(string message, Exception? innerException)
        : this(Invalid, message, innerException)
    {
    }

    /// <summary>One of the three constants above: lowercase snake_case, as every error code in this product is.</summary>
    public string Code { get; } = Invalid;
}
