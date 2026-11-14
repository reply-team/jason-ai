using System.Globalization;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>How the approvals' plan tests ask SQLite for the plan of a query the runtime really runs.</summary>
internal static class QueryPlans
{
    /// <summary>The query as EF Core writes it, its parameters bound as it binds them, asked for its plan.</summary>
    public static async Task<List<string>> PlanAsync(JasonDbContext db, string written, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var sql = new List<string>();
        foreach (var text in written.Split('\n'))
        {
            var row = text.TrimEnd('\r');
            if (row.StartsWith(".param set ", StringComparison.Ordinal))
            {
                var parts = row[".param set ".Length..].Split(' ', 2);
                var value = parts[1];
                command.Parameters.AddWithValue(
                    parts[0],
                    value.StartsWith('\'')
                        ? value[1..^1].Replace("''", "'", StringComparison.Ordinal)
                        : long.Parse(value, CultureInfo.InvariantCulture));
            }
            else
            {
                sql.Add(row);
            }
        }

        command.CommandText = "EXPLAIN QUERY PLAN " + string.Join('\n', sql).Trim();
        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            plan.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return plan;
    }

    /// <summary>
    /// The query as EF Core writes it, its parameters bound as it binds them, run to its end: the number of virtual machine
    /// steps it took and of steps a full scan took. It counts the rows a read visited without timing anything, so a test
    /// can say that rows of no concern to the read were never visited.
    /// </summary>
    public static async Task<(int Steps, int FullScanSteps)> StepsAsync(JasonDbContext db, string written, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        var sql = new List<string>();
        var parameters = new List<(string Name, string Value)>();
        foreach (var text in written.Split('\n'))
        {
            var row = text.TrimEnd('\r');
            if (row.StartsWith(".param set ", StringComparison.Ordinal))
            {
                var parts = row[".param set ".Length..].Split(' ', 2);
                parameters.Add((parts[0], parts[1]));
            }
            else
            {
                sql.Add(row);
            }
        }

        Assert.Equal(raw.SQLITE_OK, raw.sqlite3_prepare_v2(connection.Handle, string.Join('\n', sql).Trim(), out sqlite3_stmt statement));
        using (statement)
        {
            foreach (var (name, value) in parameters)
            {
                // EF Core lists a parameter by its bare name and writes it into the text with its prefix.
                var index = raw.sqlite3_bind_parameter_index(statement, "@" + name);
                Assert.True(index > 0, $"The query names no parameter {name}.");
                Assert.Equal(
                    raw.SQLITE_OK,
                    value.StartsWith('\'')
                        ? raw.sqlite3_bind_text(statement, index, value[1..^1].Replace("''", "'", StringComparison.Ordinal))
                        : raw.sqlite3_bind_int64(statement, index, long.Parse(value, CultureInfo.InvariantCulture)));
            }

            int step;
            while ((step = raw.sqlite3_step(statement)) == raw.SQLITE_ROW)
            {
            }

            Assert.Equal(raw.SQLITE_DONE, step);
            return (
                raw.sqlite3_stmt_status(statement, raw.SQLITE_STMTSTATUS_VM_STEP, 0),
                raw.sqlite3_stmt_status(statement, raw.SQLITE_STMTSTATUS_FULLSCAN_STEP, 0));
        }
    }
}
