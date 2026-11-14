using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A campaign's own send modes are four columns added to <c>campaigns</c>, each null: an installation upgraded from the
/// migration before keeps every campaign it had with no mode of its own, so each follows the installation, which nobody
/// has set yet, and every send still waits for a person. An update resets nothing and releases nothing.
/// </summary>
public class CampaignSendModesMigrationTests
{
    private const string PreviousMigration = "20261113021003_JournalByThread";

    [Fact]
    public void An_upgraded_campaign_holds_no_mode_of_its_own()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile) + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO campaigns (public_id, name, project_id, status, context_json, created_at, updated_at)
                SELECT 'cmp_01K7QN0000000000000000000U', 'Upgraded', id, 'active', '{}', '2026-11-01 09:00:00', '2026-11-01 09:00:00'
                FROM projects ORDER BY id LIMIT 1;
                """;
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        SqliteConnection.ClearAllPools();
        new DatabaseMigrator(dir.Paths).Migrate();

        using var after = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        var campaign = after.Campaigns.AsNoTracking().Single();
        Assert.Equal((null, null, null, null), (campaign.SendModeCold, campaign.SendModeColdSetBy, campaign.SendModeWarm, campaign.SendModeWarmSetBy));

        SqliteConnection.ClearAllPools();
    }
}
