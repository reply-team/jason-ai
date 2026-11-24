using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A lift of the daily limits is a row: the local date it covers, the zone that date was taken in, who lifted it, why and
/// when. One per date of a zone, so a second lift of the same day has nowhere to go.
/// </summary>
public class LaunchLimitLiftsSchemaTests
{
    private static readonly DateTime Moment = new(2026, 10, 6, 12, 30, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_table_holds_the_day_its_zone_and_who_lifted_it_why_and_when()
    {
        using var database = new TestDatabase();
        await using (var db = database.Open())
        {
            await db.Database.MigrateAsync(Ct);
        }

        using var connection = new SqliteConnection($"Data Source={database.File}");
        await connection.OpenAsync(Ct);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('launch_limit_lifts') ORDER BY name";
        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(["actor_id", "actor_type", "created_at", "id", "local_date", "reason", "time_zone"], columns);
    }

    /// <summary>The same date in another zone is another day, and the same date of the same zone is refused.</summary>
    [Fact]
    public async Task One_row_per_date_of_a_zone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await db.Database.MigrateAsync(Ct);
        db.LaunchLimitLifts.Add(Lift("Asia/Tokyo"));
        db.LaunchLimitLifts.Add(Lift("Europe/Berlin"));
        await db.SaveChangesAsync(Ct);

        db.LaunchLimitLifts.Add(Lift("Asia/Tokyo"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private static LaunchLimitLift Lift(string zone) => new()
    {
        LocalDate = new DateOnly(2026, 10, 6),
        TimeZone = zone,
        ActorType = ActorType.Human,
        ActorId = "operator",
        CreatedAt = Moment,
    };
}
