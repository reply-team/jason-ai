using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A database the runtime's own migrator has brought to the latest migration once in this test process, copied whole into
/// each Reply test's data directory before its runtime starts. Applying every migration to an empty file is most of what
/// a runtime's start costs, and a Reply test starts one per case to hold a plugin's behaviour, never the migrations: the
/// start still runs the migrator, which finds nothing pending, so the file a test's runtime opens is the one a fresh
/// start would have made — the same schema, triggers and seed rows — and the tests of the migrations themselves start
/// from an empty file as before.
/// </summary>
/// <remarks>
/// The copy is taken with <c>VACUUM INTO</c> rather than of the file on disk, so it carries nothing a write-ahead log
/// still held, and each test gets a copy of its own: nothing one test's runtime writes reaches another's.
/// </remarks>
internal static class MigratedDatabase
{
    private static readonly Lazy<string> Template = new(Make);

    /// <summary>Places a copy of the migrated database where a runtime on <paramref name="paths"/> opens its own.</summary>
    public static void CopyTo(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.StateDirectory);
        File.Copy(Template.Value, paths.DatabaseFile, overwrite: false);
    }

    private static string Make()
    {
        var made = new TempDataDir();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => made.Dispose();

        var report = new DatabaseMigrator(made.Paths).Migrate();
        if (report.NewlyApplied.Count == 0)
        {
            throw new InvalidOperationException("The migrator applied nothing to an empty database.");
        }

        var template = Path.Combine(made.Paths.Root, "template.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = made.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM INTO $template;";
            vacuum.Parameters.AddWithValue("$template", template);
            vacuum.ExecuteNonQuery();
        }

        return template;
    }
}
