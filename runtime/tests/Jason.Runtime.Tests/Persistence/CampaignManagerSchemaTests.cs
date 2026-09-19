using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What the database itself guarantees about the manager loop: one open check-in per campaign, and an upgraded
/// installation that starts level with its own chronicle instead of owing a review of everything that ever
/// happened in it.
/// </summary>
public class CampaignManagerSchemaTests
{
    private const string PreviousMigration = "20260918074712_RoleNotes";

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The rule that keeps a user who was away for a week from coming back to a pile of reviews. It is a
    /// database rule and not a check in the summon, because two writers deciding "is one already open?" by
    /// looking would both see no, and both insert.
    /// </summary>
    [Fact]
    public void One_open_check_in_per_campaign_is_a_database_rule()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Campaign(db);
        db.SaveChanges();

        db.WorkItems.Add(CheckIn(campaign, "wi_first"));
        db.SaveChanges();

        db.WorkItems.Add(CheckIn(campaign, "wi_second"));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    /// <summary>
    /// And the same rule the other way round: the filter is about what is <em>open</em>, so a campaign reviewed
    /// yesterday is reviewable again today. A unique index on the campaign alone would have made the first
    /// check-in the last one.
    /// </summary>
    [Fact]
    public void A_campaign_whose_check_in_is_over_can_have_another()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Campaign(db);
        var first = CheckIn(campaign, "wi_first");
        db.WorkItems.Add(first);
        db.SaveChanges();

        first.Status = WorkItemStatus.Succeeded;
        first.FinishedAt = Noon;
        db.SaveChanges();

        db.WorkItems.Add(CheckIn(campaign, "wi_second"));
        db.SaveChanges();

        Assert.Equal(2, db.WorkItems.Count());
    }

    /// <summary>
    /// The upgrade. Every campaign starts level with the chronicle as it stands, so the first thing a manager is
    /// ever asked about happened after the upgrade — and a campaign that is already live gets an anchor, or the
    /// cadence would measure from a moment that is not on record and the campaign would never be due at all.
    /// </summary>
    [Fact]
    public void An_upgraded_installation_owes_no_review_of_its_history()
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
            VALUES ('cmp_live', 'already running', 'active', '{}', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z'),
                   ('cmp_idle', 'never started', 'draft', '{}', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z');

            INSERT INTO journal (public_id, ts, actor_type, kind, campaign_id)
            VALUES ('jrn_1', '2026-09-01T00:00:00Z', 'human', 'campaign_created', 1),
                   ('jrn_2', '2026-09-01T00:00:01Z', 'human', 'campaign_started', 1),
                   ('jrn_3', '2026-09-01T00:00:02Z', 'human', 'workitem_failed', 1);
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        using (var db = new JasonDbContext(options))
        {
            var live = db.Campaigns.Single(c => c.PublicId == "cmp_live");
            var idle = db.Campaigns.Single(c => c.PublicId == "cmp_idle");

            // Level with the chronicle: the failure from before the upgrade is history, not an inbox.
            Assert.Equal(3, live.ManagerEventWatermark);
            Assert.Equal(3, idle.ManagerEventWatermark);

            // The live one is reviewable from now; the draft one has never been live and so has no anchor.
            Assert.NotNull(live.ManagerReviewAnchor);
            Assert.Null(idle.ManagerReviewAnchor);
        }
    }

    private static Campaign Campaign(JasonDbContext db)
    {
        var campaign = new Campaign
        {
            PublicId = "cmp_A",
            Name = "Work",
            Status = CampaignStatus.Active,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };
        db.Campaigns.Add(campaign);
        return campaign;
    }

    private static WorkItem CheckIn(Campaign campaign, string publicId) => new()
    {
        PublicId = publicId,
        Campaign = campaign,
        Kind = WorkItemKind.AiRole,
        Role = "manager",
        Status = WorkItemStatus.Created,
        CreatedByType = ActorType.System,
        CreatedById = "dispatcher",
        CreatedAt = Noon,
        UpdatedAt = Noon,
    };

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
