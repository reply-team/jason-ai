using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The copy a Reply test's runtime starts on is what a fresh start would have made: the same schema, the same migrations
/// recorded, and the same rows in every table — none left over from anything that ran before.
/// </summary>
public class MigratedDatabaseTests
{
    [Fact]
    public void The_copy_holds_what_a_fresh_migration_makes_and_nothing_more()
    {
        using var fresh = new TempDataDir();
        new DatabaseMigrator(fresh.Paths).Migrate();
        using var copied = new TempDataDir();
        MigratedDatabase.CopyTo(copied.Paths);

        var expected = Describe(fresh.Paths.DatabaseFile);
        var actual = Describe(copied.Paths.DatabaseFile);

        Assert.NotEmpty(expected.Schema);
        Assert.Equal(expected.Schema, actual.Schema);
        Assert.Equal(expected.Migrations, actual.Migrations);
        Assert.Equal(expected.Rows, actual.Rows);

        // And the start the copy is made for applies nothing: the migrator finds it current.
        var report = new DatabaseMigrator(copied.Paths).Migrate();
        Assert.Empty(report.NewlyApplied);
        Assert.Null(report.BackupFile);
    }

    private static (List<string> Schema, List<string> Migrations, List<string> Rows) Describe(string file)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();

        var schema = Strings(connection, "SELECT type || ' ' || name || ' ' || tbl_name || ' ' || coalesce(sql, '') FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name;");
        var migrations = Strings(connection, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";");
        var tables = Strings(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");
        var rows = tables.Select(table => table + " " + Strings(connection, $"SELECT count(*) FROM \"{table}\";").Single()).ToList();
        return (schema, migrations, rows);
    }

    private static List<string> Strings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return values;
    }
}
