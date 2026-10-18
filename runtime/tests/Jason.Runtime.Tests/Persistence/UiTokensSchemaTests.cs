using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>A paired browser is a row: the hash of its token and who paired when. Never the token.</summary>
public class UiTokensSchemaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_table_holds_a_hash_and_the_pairing_facts_and_nothing_else()
    {
        using var database = new TestDatabase();
        await using (var db = database.Open())
        {
            await db.Database.MigrateAsync(Ct);
        }

        using var connection = new SqliteConnection($"Data Source={database.File}");
        await connection.OpenAsync(Ct);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('ui_tokens') ORDER BY name";
        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(["id", "issued_at", "issued_to", "last_used_at", "public_id", "token_hash"], columns);
    }

    [Fact]
    public async Task Two_rows_cannot_share_a_hash_or_a_public_id()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await db.Database.MigrateAsync(Ct);
        db.UiTokens.Add(new UiToken { PublicId = "uit_one", TokenHash = new string('a', 64), IssuedTo = "ada", IssuedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);

        db.UiTokens.Add(new UiToken { PublicId = "uit_two", TokenHash = new string('a', 64), IssuedTo = "ada", IssuedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}
