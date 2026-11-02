using System.Globalization;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
}
