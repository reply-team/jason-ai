using System.Data.Common;
using System.Globalization;
using System.Text;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// Every managed row in the database, folded into one string per table, so two censuses taken around an action
/// can be compared as wholes. The tables come from the model rather than from a list kept here: a table a later
/// wave adds is covered without anybody remembering to come back and name it.
/// </summary>
internal static class DatabaseCensus
{
    /// <summary>
    /// The two tables admitting a report is allowed to grow. Everything else is what the claim is about, so it
    /// is counted rather than exempted.
    /// </summary>
    private static readonly IReadOnlySet<string> Excluded =
        new HashSet<string>(StringComparer.Ordinal) { "reports", "journal" };

    public static async Task<IReadOnlyDictionary<string, string>> TakeAsync(JasonDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var tables = db.Model.GetEntityTypes()
            .Select(type => type.GetTableName())

            // An entity type mapped to no table has no rows to count. None exist today; the helper should not be
            // the thing that breaks on the wave that introduces one.
            .Where(name => name is not null && !Excluded.Contains(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var census = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                census[table] = await ReadAsync(db.Database.GetDbConnection(), table, cancellationToken);
            }

            return census;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Every column of every row, in rowid order. Values rather than a count, because an UPDATE that leaves the
    /// number of rows alone is exactly the kind of interference a census of counts would miss.
    /// </summary>
    private static async Task<string> ReadAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        // The names come from the model, so there is nothing here a caller could have shaped.
        command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new StringBuilder();
        while (await reader.ReadAsync(cancellationToken))
        {
            for (var column = 0; column < reader.FieldCount; column++)
            {
                rows.Append(reader.GetName(column)).Append('=').Append(Text(reader.GetValue(column))).Append('');
            }

            rows.Append('');
        }

        return rows.ToString();
    }

    private static string Text(object value) => value switch
    {
        DBNull => "<null>",
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
