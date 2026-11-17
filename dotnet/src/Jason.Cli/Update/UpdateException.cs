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

    /// <summary>
    /// The archive does not carry the one file this platform's release is supposed to contain, the release publishes nothing
    /// for this platform, or the manifest read for a named version names another.
    /// </summary>
    public const string ArtifactUnexpected = "update_artifact_unexpected";

    /// <summary>
    /// A different version is already in flight. The same one is resumed rather than refused. And <c>jason runtime start</c>
    /// refuses with it while any update is in flight, since the apply starts the runtime itself as one of its steps.
    /// </summary>
    public const string InProgress = Contracts.Update.UnfinishedUpdate.InProgress;

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

    /// <summary>
    /// The new version is in place but the runtime it starts is not the one this update installed, or the new executable
    /// could not be run to finish the update.
    /// </summary>
    public const string NotHealthy = "update_not_healthy";

    /// <summary>
    /// A file an update has to move could not be moved: a directory that is not one, a permission, a lock. The
    /// message names the path and what to do, because this is the one family of failures that can happen inside
    /// the window where there is no executable at the install path.
    /// </summary>
    public const string FileRefused = "update_file_refused";

    /// <summary>There is no record of an update, or the executable it replaced is not where it was kept.</summary>
    public const string NothingToRollBack = "update_nothing_to_roll_back";

    /// <summary>
    /// The binary was put back, and the database was not: work has been recorded since the update, and restoring
    /// the backup would erase it.
    /// </summary>
    public const string RollbackUnsafe = "update_rollback_unsafe";

    /// <summary>
    /// A package manager installed this Jason and keeps its own record of the version, so its file is not this
    /// build's to replace: the manager upgrades it, with the runtime stopped first.
    /// </summary>
    public const string ManagedByPackageManager = "update_managed_by_package_manager";

    /// <summary>
    /// This jason is the one the desktop application carries, known by the record beside it, so its file is the
    /// application's, and no Jason Desktop is there to hand the update to: on Linux, or where the application is not
    /// where its carried jason says it is. Or the update recorded names such a file other than this one.
    /// </summary>
    public const string CarriedByDesktop = "update_carried_by_desktop";

    /// <summary>
    /// A carried jason handed the update to Jason Desktop, and the Desktop refused it: its reason and its sentence are the
    /// message. Nothing was installed, and the runtime is as it was before the drain.
    /// </summary>
    public const string RefusedByDesktop = "update_refused_by_desktop";

    /// <summary>
    /// A carried jason handed the update to Jason Desktop and had no answer: the start could not be made, ended with no
    /// request file naming an end, or a running window named none within the wait. The runtime is as it was before the drain.
    /// </summary>
    public const string DesktopUnanswered = "update_desktop_unanswered";

    /// <summary>
    /// Jason Desktop handed the update to its installer, and within ten minutes the application's <c>version.json</c> did
    /// not name the version asked for.
    /// </summary>
    public const string DesktopNotInstalled = "update_desktop_not_installed";

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
        FileRefused,
        NothingToRollBack,
        RollbackUnsafe,
        ManagedByPackageManager,
        CarriedByDesktop,
        RefusedByDesktop,
        DesktopUnanswered,
        DesktopNotInstalled,
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
        : this(code, message, null)
    {
    }

    public UpdateException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
        Retryable = code == UpdateCodes.ArtifactUnreachable;
    }

    /// <summary>The same, saying for itself whether trying again could work, where its code alone does not decide it.</summary>
    public UpdateException(string code, string message, Exception? innerException, bool retryable)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    /// <summary>Lowercase snake_case, as every error code in this product is.</summary>
    public string Code { get; } = UpdateCodes.ArtifactUnreachable;

    /// <summary>
    /// Whether trying again could work. A download that did not arrive is worth repeating; an archive that is
    /// not what it claimed to be is not, because the same bytes will fail the same way. A file refused because
    /// another program held it open is worth repeating too, once that program lets go.
    /// </summary>
    public bool Retryable { get; }
}
