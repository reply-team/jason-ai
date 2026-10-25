namespace Jason.Runtime.Persistence;

/// <summary>What a start whose migration failed did with the copy a start before it took, one stopped while it migrated.</summary>
public enum StoppedStart
{
    /// <summary>No start before this one was found stopped while it migrated.</summary>
    None,

    /// <summary>That start's copy was put back: the installation as it stood before that start began.</summary>
    PutBack,

    /// <summary>Putting that start's copy back was begun and failed.</summary>
    NotPutBack,

    /// <summary>Not put back: the chronicle has moved since that copy was taken, or could not be read, which counts the same.</summary>
    ChronicleMoved,

    /// <summary>Not put back: the copy is no longer where that start wrote down it was.</summary>
    BackupGone,
}

/// <summary>
/// A migration failed while the runtime was starting, so it has not started. The database was put back from the copy the
/// start took before it began, so it is exactly as the start found it — or, when putting it back failed too, the sentence
/// says which file to put back by hand.
/// </summary>
/// <remarks>
/// <para>
/// Its own type because of where it has to be heard: a migration runs before the runtime listens, and a runtime started in
/// the background has no console by then, so the reason it would not start reaches whoever started it only as a refusal.
/// </para>
/// <para>
/// A migration that SQLite cannot make in place is made in several transactions, and one that fails between them leaves
/// the database part migrated, which no later start can migrate. Putting the copy back is safe because nothing else has
/// written since it was taken: the start holds the installation's lock, and the runtime does not serve until its database
/// is ready. A start can still be stopped part way by something no code survives — a power cut, a process ended — and the
/// next start then finds a part-migrated database and fails on it; it puts back the copy the stopped start took while
/// nothing has been recorded since that copy was taken, and names that copy when something has.
/// </para>
/// </remarks>
public sealed class MigrationFailedException : Exception
{
    private MigrationFailedException(
        string message,
        string migration,
        string backup,
        bool restored,
        Exception cause,
        bool created = false,
        StoppedStart stoppedStart = StoppedStart.None,
        string? stoppedStartBackup = null)
        : base(message, cause)
    {
        Migration = migration;
        Backup = backup;
        Restored = restored;
        Created = created;
        StoppedStart = stoppedStart;
        StoppedStartBackup = stoppedStartBackup;
    }

    /// <summary>Whether the database was one this start made from nothing, so that there was no copy to take or to put back.</summary>
    public bool Created { get; }

    /// <summary>The migration that failed, as the history names it.</summary>
    public string Migration { get; }

    /// <summary>
    /// The copy the database was put back from, or is to be put back from by hand: the one this start took before it began,
    /// or the one a stopped start took where that is the one put back (<see cref="StoppedStart"/>); empty when the database
    /// was made by this start (<see cref="Created"/>).
    /// </summary>
    public string Backup { get; }

    /// <summary>Whether the database was put back from <see cref="Backup"/>.</summary>
    public bool Restored { get; }

    /// <summary>What became of the copy a start before this one took, when this start found that start stopped while it migrated.</summary>
    public StoppedStart StoppedStart { get; }

    /// <summary>The copy that stopped start took before it began; null when no such start was found.</summary>
    public string? StoppedStartBackup { get; }

    internal static MigrationFailedException PutBack(string migration, string backup, string backups, Exception cause) =>
        new(
            $"The runtime did not start: migrating its database to this version failed at {migration} ({cause.Message}). "
            + $"The database was put back from {backup}, the copy this start took before it began, so it is as it was before "
            + "this start, and the version that ran on it before still runs on it. "
            + $"The backups earlier starts took are kept in {backups} until a migration succeeds: if a start was stopped while "
            + "it migrated, the database it left was part migrated, and the backup that start took is the one to put back by hand.",
            migration,
            backup,
            restored: true,
            cause);

    internal static MigrationFailedException NotPutBack(string migration, string backup, string database, Exception cause, Exception putting) =>
        new(
            $"The runtime did not start: migrating its database to this version failed at {migration} ({cause.Message}), "
            + $"and putting back the copy this start took before it began failed too ({putting.Message}). "
            + $"The database may be part migrated. Put {backup} back by hand: with no runtime running, copy it over {database} "
            + $"and delete {database}-wal and {database}-shm.",
            migration,
            backup,
            restored: false,
            cause);

    internal static MigrationFailedException StoppedStartPutBack(string migration, string clean, Exception cause) =>
        new(
            "The runtime did not start: a start before this one was stopped while it migrated its database, and migrating it "
            + $"again failed at {migration} ({cause.Message}). The database was put back from {clean}, the copy that start took "
            + "before it began; nothing has been recorded since then. Start the runtime again: it migrates from there.",
            migration,
            clean,
            restored: true,
            cause,
            stoppedStart: StoppedStart.PutBack,
            stoppedStartBackup: clean);

    internal static MigrationFailedException StoppedStartNotPutBack(string migration, string clean, string database, Exception cause, Exception putting) =>
        new(
            "The runtime did not start: a start before this one was stopped while it migrated its database, and migrating it "
            + $"again failed at {migration} ({cause.Message}); putting back {clean}, the copy that start took before it began, "
            + $"failed too ({putting.Message}). The database may be part migrated. Put {clean} back by hand: with no runtime "
            + $"running, copy it over {database} and delete {database}-wal and {database}-shm.",
            migration,
            clean,
            restored: false,
            cause,
            stoppedStart: StoppedStart.NotPutBack,
            stoppedStartBackup: clean);

    /// <summary>
    /// The stopped start's copy refused, so the database is as this start found it — put back from this start's own copy,
    /// the one <see cref="Backup"/> names — and the sentence names the stopped start's for a person to decide on.
    /// </summary>
    internal static MigrationFailedException StoppedStartRefused(string migration, string backup, string clean, string database, string backups, StoppedStart why, Exception cause) =>
        new(
            "The runtime did not start: a start before this one was stopped while it migrated its database, and migrating it "
            + $"again failed at {migration} ({cause.Message}). The database is as this start found it. "
            + (why == StoppedStart.BackupGone
                ? $"Nothing was put back from {clean}, the copy that start took before it began: it is no longer there, so this "
                    + $"runtime cannot put back the installation as it stood before that start. The backups that are left are in {backups}."
                : $"Nothing was put back from {clean}, the copy that start took before it began: the chronicle has moved since "
                    + "it was taken, so putting it back would lose what was recorded. Only if you mean to lose that: with no "
                    + $"runtime running, copy it over {database} and delete {database}-wal and {database}-shm."),
            migration,
            backup,
            restored: true,
            cause,
            stoppedStart: why,
            stoppedStartBackup: clean);

    internal static MigrationFailedException MadeByThisStart(string migration, string database, bool removed, Exception cause) =>
        new(
            $"The runtime did not start: migrating its database to this version failed at {migration} ({cause.Message}). "
            + $"The database {database} was made by this start and held nothing yet, so no data is lost and there is no backup. "
            + (removed
                ? "This start deleted it; start the runtime again."
                : $"It could not be deleted: with no runtime running, delete {database}, {database}-wal and {database}-shm, then start the runtime again."),
            migration,
            string.Empty,
            restored: false,
            cause,
            created: true);
}
