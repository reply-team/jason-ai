namespace Jason.Runtime.Persistence;

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
/// next start then finds a part-migrated database and fails on it; so the sentence also says that the backups earlier
/// starts took are kept until a migration succeeds, and which of them to put back.
/// </para>
/// </remarks>
public sealed class MigrationFailedException : Exception
{
    private MigrationFailedException(string message, string migration, string backup, bool restored, Exception cause, bool created = false)
        : base(message, cause)
    {
        Migration = migration;
        Backup = backup;
        Restored = restored;
        Created = created;
    }

    /// <summary>Whether the database was one this start made from nothing, so that there was no copy to take or to put back.</summary>
    public bool Created { get; }

    /// <summary>The migration that failed, as the history names it.</summary>
    public string Migration { get; }

    /// <summary>The copy this start took before it began; empty when the database was made by this start (<see cref="Created"/>).</summary>
    public string Backup { get; }

    /// <summary>Whether the database was put back from <see cref="Backup"/>.</summary>
    public bool Restored { get; }

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
