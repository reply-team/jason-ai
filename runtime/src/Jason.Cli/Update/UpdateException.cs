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

    /// <summary>A different version is already in flight. The same one is resumed rather than refused.</summary>
    public const string InProgress = "update_in_progress";

    /// <summary>The feed offers nothing newer than what is installed.</summary>
    public const string UpToDate = "update_up_to_date";

    /// <summary>The release says it cannot be reached from this version in one step.</summary>
    public const string NotDirectlyApplicable = "update_not_directly_applicable";

    /// <summary>There is no single executable to replace: this build runs through the muxer.</summary>
    public const string NotUpdatable = "update_not_updatable";

    /// <summary>The staged file and the install path are on different volumes, and an update renames rather than copies.</summary>
    public const string CrossVolume = "update_cross_volume";

    /// <summary>The runtime would not do its part — drain, or stop — so nothing was replaced.</summary>
    public const string RuntimeUnreachable = "update_runtime_unreachable";

    /// <summary>The new version is in place but the runtime it starts is not the one this update installed.</summary>
    public const string NotHealthy = "update_not_healthy";

    /// <summary>There is no record of an update, or the executable it replaced is not where it was kept.</summary>
    public const string NothingToRollBack = "update_nothing_to_roll_back";

    /// <summary>
    /// The binary was put back, and the database was not: work has been recorded since the update, and restoring
    /// the backup would erase it.
    /// </summary>
    public const string RollbackUnsafe = "update_rollback_unsafe";

    /// <summary>Every code above, for the page's guard to read rather than for anything to iterate at runtime.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ArtifactCorrupt,
        ArtifactUnreachable,
        ArtifactUnexpected,
        InProgress,
        UpToDate,
        NotDirectlyApplicable,
        NotUpdatable,
        CrossVolume,
        RuntimeUnreachable,
        NotHealthy,
        NothingToRollBack,
        RollbackUnsafe,
    ];
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
