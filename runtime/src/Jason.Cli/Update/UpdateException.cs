namespace Jason.Cli.Update;

/// <summary>
/// The codes an update refuses with. One place, because the page prints them and a test reads them from here:
/// a code renamed in the code turns the page red rather than quietly disagreeing with it.
/// </summary>
public static class UpdateCodes
{
    /// <summary>What was downloaded is not what the manifest said it would be. The bytes are deleted.</summary>
    public const string ArtifactCorrupt = "update_artifact_corrupt";

    /// <summary>The download did not arrive, or the archive could not be read.</summary>
    public const string ArtifactUnreachable = "update_artifact_unreachable";

    /// <summary>The archive does not carry the one file this platform's release is supposed to contain.</summary>
    public const string ArtifactUnexpected = "update_artifact_unexpected";
}

/// <summary>
/// An update refused, with the code a person sees and a script can branch on. Everything an update can refuse
/// for has one; the message says which file or which version, because a code alone tells a person nothing they
/// can act on.
/// </summary>
public sealed class UpdateException : Exception
{
    public UpdateException()
        : this(UpdateCodes.ArtifactUnreachable, "The update could not be performed.")
    {
    }

    public UpdateException(string message)
        : this(UpdateCodes.ArtifactUnreachable, message)
    {
    }

    public UpdateException(string message, Exception? innerException)
        : this(UpdateCodes.ArtifactUnreachable, message, innerException)
    {
    }

    public UpdateException(string code, string message)
        : base(message) => Code = code;

    public UpdateException(string code, string message, Exception? innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Lowercase snake_case, as every error code in this product is.</summary>
    public string Code { get; } = UpdateCodes.ArtifactUnreachable;

    /// <summary>
    /// Whether trying again could work. A download that did not arrive is worth repeating; an archive that is
    /// not what it claimed to be is not, because the same bytes will fail the same way.
    /// </summary>
    public bool Retryable => Code == UpdateCodes.ArtifactUnreachable;
}
