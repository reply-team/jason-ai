using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The journal learns which person a line is about from this version on. Lines written before keep no person: the
/// migration adds the column and writes nothing to them, so the journal stays exactly as append-only as it was.
/// </summary>
public class JournalByPersonMigrationTests
{
    private const string PreviousMigration = "20261020050039_LinkedInOneKey";

    private const string ThisMigration = "JournalByPerson";

    [Fact]
    public void An_old_journal_keeps_its_lines_without_a_person_and_stays_append_only()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        Migrate(dir, PreviousMigration);
        using (var connection = Open(dir))
        {
            Execute(
                connection,
                """
                INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at)
                VALUES ('cmp_old', 'before people', 'active', '{}', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');
                INSERT INTO contacts (public_id, first_name, custom_json, created_at, updated_at)
                VALUES ('cnt_ada', 'Ada', '{}', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');
                INSERT INTO work_items (public_id, campaign_id, contact_id, kind, role, status, priority, created_by_type, created_by_id,
                                        context_json, attempt_count, created_at, updated_at)
                VALUES ('wi_ada', 1, 1, 'ai_role', 'researcher', 'succeeded', 0, 'human', NULL,
                        '{}', 0, '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');
                INSERT INTO journal (public_id, ts, actor_type, kind, key, campaign_id, work_item_id)
                VALUES ('jrn_1', '2026-09-01T00:00:00Z', 'human', 'contact_created', 'cnt_ada', NULL, NULL),
                       ('jrn_2', '2026-09-01T00:00:01Z', 'system', 'workitem_succeeded', NULL, 1, 'wi_ada');
                """);
        }

        Migrate(dir, Applied(dir, ThisMigration));

        using var after = Open(dir);
        Assert.Equal(2L, Scalar(after, "SELECT COUNT(*) FROM journal WHERE contact_id IS NULL"));
        Assert.Equal(
            2L,
            Scalar(after, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name IN ('journal_no_update', 'journal_no_delete')"));
        var refused = Assert.Throws<SqliteException>(() => Execute(after, "UPDATE journal SET reason = 'x'"));
        Assert.Contains("append-only", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The full name of the migration whose name ends with the one given, as the assembly lists it.</summary>
    private static string Applied(TempDataDir dir, string suffix)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        return Assert.Single(db.Database.GetMigrations(), name => name.EndsWith("_" + suffix, StringComparison.Ordinal));
    }

    private static void Migrate(TempDataDir dir, string migration)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(migration);
    }

    private static SqliteConnection Open(TempDataDir dir)
    {
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile));
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string statement)
    {
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string statement)
    {
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        return command.ExecuteScalar();
    }
}
