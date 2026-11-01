using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Work written before execution profiles existed still has to say where its profile comes from, and the
/// migration can only say what it can know. Work a person, a role or the runtime created is root work: nothing
/// caused it that could have had a profile, so it resolves to the global default exactly as it always would
/// have, and an upgrade never blocks somebody's backlog. Work an attempt created has ancestry that pinned no
/// profile — there was none to pin — so it is unresolved, and a claim refuses it rather than quietly choosing
/// an executor nobody asked for.
/// </summary>
public class LineageBackfillTests
{
    private const string PreviousMigration = "20260917203003_Reports";

    [Fact]
    public void Work_from_before_profiles_is_root_when_a_person_created_it_and_unresolved_when_a_run_did()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);

        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at)
            VALUES ('cmp_old', 'before profiles', 'active', '{}', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');

            INSERT INTO work_items (public_id, campaign_id, kind, role, status, priority, created_by_type, created_by_id,
                                    context_json, attempt_count, created_at, updated_at)
            VALUES ('wi_by_person', 1, 'ai_role', 'researcher', 'created', 0, 'human', NULL,
                    '{}', 0, '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z'),
                   ('wi_by_role', 1, 'ai_role', 'researcher', 'created', 0, 'role', 'planner',
                    '{}', 0, '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z'),
                   ('wi_by_run', 1, 'ai_role', 'researcher', 'created', 0, 'attempt', 'att_gone',
                    '{}', 0, '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        Assert.Equal("root", LineageOf(dir.Paths.DatabaseFile, "wi_by_person"));
        Assert.Equal("root", LineageOf(dir.Paths.DatabaseFile, "wi_by_role"));
        Assert.Equal("unresolved", LineageOf(dir.Paths.DatabaseFile, "wi_by_run"));
    }

    private static void Execute(string file, string sql)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(file));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? LineageOf(string file, string publicId)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(file));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT lineage_state FROM work_items WHERE public_id = $id";
        command.Parameters.AddWithValue("$id", publicId);
        return command.ExecuteScalar() as string;
    }
}
