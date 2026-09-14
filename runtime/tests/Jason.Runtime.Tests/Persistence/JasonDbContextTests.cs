using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

public class JasonDbContextTests
{
    [Fact]
    public void Schema_uses_snake_case_names()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.Database.Migrate();

        using var connection = new SqliteConnection($"Data Source={dir.Paths.DatabaseFile}");
        connection.Open();
        Assert.Contains("campaigns", Names(connection, "SELECT name FROM sqlite_master WHERE type = 'table'"));
        Assert.Contains("ix_campaigns_public_id", Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index'"));
        // Adding the context column together with its json_valid check rebuilds the table on SQLite, which
        // leaves the primary key first and the rest in name order. The names are what this test is about.
        Assert.Equal(["id", "archived_at", "context_json", "created_at", "name", "public_id", "status", "updated_at"], Names(connection, "SELECT name FROM pragma_table_info('campaigns')"));
    }

    [Fact]
    public void Campaigns_round_trip_with_text_status_and_utc_timestamps()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);
        var now = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
        var publicId = PublicId.New("cmp");

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
            db.Campaigns.Add(new Campaign { PublicId = publicId, Name = "Latin America launch", Status = CampaignStatus.Draft, CreatedAt = now, UpdatedAt = now });
            db.SaveChanges();
        }

        using (var db = new JasonDbContext(options))
        {
            var campaign = db.Campaigns.Single(c => c.PublicId == publicId);
            Assert.True(campaign.Id > 0);
            Assert.Equal(CampaignStatus.Draft, campaign.Status);
            Assert.Equal(now, campaign.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, campaign.CreatedAt.Kind);
            Assert.Null(campaign.ArchivedAt);
        }

        using var connection = new SqliteConnection($"Data Source={dir.Paths.DatabaseFile}");
        connection.Open();
        Assert.Equal(["draft"], Names(connection, "SELECT status FROM campaigns"));
    }

    [Fact]
    public void Public_ids_are_unique()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.Database.Migrate();
        var now = DateTime.UtcNow;
        db.Campaigns.Add(new Campaign { PublicId = "cmp_A", Name = "one", CreatedAt = now, UpdatedAt = now });
        db.Campaigns.Add(new Campaign { PublicId = "cmp_A", Name = "two", CreatedAt = now, UpdatedAt = now });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

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

        return names;
    }
}
