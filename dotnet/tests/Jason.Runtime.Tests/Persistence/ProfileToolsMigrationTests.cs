using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The grant arrives as a column on revisions that already exist. A revision written before it granted nothing, and
/// says so; and the table is not rebuilt to add it, because a rebuild drops the old table and its two triggers with it,
/// leaving every revision open to a rewrite that nothing would refuse. Each case seeds a database at the migration before,
/// as an installation upgraded from it holds one.
/// </summary>
public class ProfileToolsMigrationTests
{
    private const string PreviousMigration = "20261025231527_ProviderBlockListingIndex";

    private const string ThisMigration = "20261026142432_ProfileTools";

    [Fact]
    public void A_revision_written_before_the_grant_existed_grants_nothing()
    {
        using var dir = Seeded();

        using var connection = Open(dir);
        Assert.Equal("[]", Scalar(connection, "SELECT tools_json FROM execution_profile_revisions WHERE number = 1"));

        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        Assert.Empty(db.ExecutionProfileRevisions.AsNoTracking().Single().Tools);
    }

    /// <summary>The triggers that freeze a revision are the ones it had before, and they refuse a rewrite of the new column too.</summary>
    [Fact]
    public void The_triggers_that_freeze_a_revision_survive_the_column_being_added()
    {
        using var dir = Seeded();
        using var connection = Open(dir);

        Assert.Equal(
            ["execution_profile_revisions_no_delete", "execution_profile_revisions_no_update"],
            Names(connection, "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'execution_profile_revisions'"));

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE execution_profile_revisions SET tools_json = '[\"web_search\"]'";
        var refused = Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
        Assert.Contains("immutable", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The other half of the freeze: a revision that existed before the column cannot be removed either.</summary>
    [Fact]
    public void A_revision_written_before_the_grant_cannot_be_deleted()
    {
        using var dir = Seeded();
        using var connection = Open(dir);

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM execution_profile_revisions WHERE number = 1";
        var refused = Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
        Assert.Contains("immutable", refused.Message, StringComparison.Ordinal);
        Assert.Equal("[]", Scalar(connection, "SELECT tools_json FROM execution_profile_revisions WHERE number = 1"));
    }

    /// <summary>The column holds JSON, like the two lists beside it, and its check says so by name.</summary>
    [Fact]
    public void A_grant_that_is_not_json_is_refused_by_its_check()
    {
        using var dir = Seeded();
        using var connection = Open(dir);

        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO execution_profile_revisions (profile_id, number, host, program, args_json, deny_json, tools_json, created_by_type, created_at) "
            + "VALUES (1, 2, 'claude_code', 'claude', '[]', '[]', 'web_search', 'human', '2026-01-02 09:00:00')";
        var refused = Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());
        Assert.Contains("ck_execution_profile_revisions_tools_json", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A database at the migration before this one, with one profile and its first revision, then this migration applied.</summary>
    private static TempDataDir Seeded()
    {
        var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        using (var connection = Open(dir))
        {
            using var seed = connection.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO execution_profiles (public_id, name, current_revision, created_at, updated_at)
                  VALUES ('prf_before', 'before-the-grant', 1, '2026-01-01 09:00:00', '2026-01-01 09:00:00');
                INSERT INTO execution_profile_revisions (profile_id, number, host, program, args_json, deny_json, created_by_type, created_at)
                  VALUES (1, 1, 'claude_code', 'claude', '[]', '["Write"]', 'human', '2026-01-01 09:00:00');
                """;
            seed.ExecuteNonQuery();
        }

        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
        }

        return dir;
    }

    private static SqliteConnection Open(TempDataDir dir)
    {
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile));
        connection.Open();
        return connection;
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    /// <summary>The first column of every row, sorted: SQLite leaves the order of a subquery's ORDER BY to itself.</summary>
    private static List<string> Names(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
